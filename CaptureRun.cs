using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LiveScreenshot;

// Owns a capture session and publishes sampled frames.
internal sealed class CaptureRun : IDisposable
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private readonly object gate = new();
    private readonly GraphicsCaptureItem item;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Action<Exception> failed;
    private readonly Action<Exception?> outputStatus;
    private readonly Action closed;
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDirect3DDevice? runtimeDevice;
    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;
    private ID3D11Texture2D? staging;
    private SizeInt32 size;
    private TimeSpan nextWrite;
    private long lastFrame = -1;
    private bool stopping;

    // Records the selected item and its lifecycle callbacks.
    internal CaptureRun(GraphicsCaptureItem item, Action closed, Action<Exception> failed, Action<Exception?> outputStatus)
    {
        this.item = item;
        this.closed = closed;
        this.failed = failed;
        this.outputStatus = outputStatus;
    }

    internal string Name => item.DisplayName;
    internal bool ReceivingFrames
    {
        get
        {
            long timestamp = Interlocked.Read(ref lastFrame);
            return timestamp >= 0 && clock.ElapsedMilliseconds - timestamp < Interval.TotalMilliseconds;
        }
    }

    // Starts frame delivery with the native capture border enabled.
    internal void Start()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException("Windows capture is unavailable.");
        }

        if (!Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
            "Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            throw new InvalidOperationException("This version of Windows cannot require the native capture border.");
        }

        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0], out device, out context).CheckError();
        runtimeDevice = CaptureInterop.WrapDevice(device!);
        size = item.Size;
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(runtimeDevice, PixelFormat, 2, size);
        session = pool.CreateCaptureSession(item);
        session.IsBorderRequired = true;
        item.Closed += OnClosed;
        pool.FrameArrived += OnFrame;
        session.StartCapture();
    }

    // Notifies the UI that the selected source was closed.
    private void OnClosed(GraphicsCaptureItem sender, object args)
    {
        closed();
    }

    // Consumes frames and writes at most one complete PNG per second.
    private void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        lock (gate)
        {
            if (stopping)
            {
                return;
            }

            try
            {
                ConsumeFrame(sender);
            }
            catch (Exception error)
            {
                stopping = true;
                failed(error);
            }
        }
    }

    // Drops old-size frames and recreates the pool after a resize.
    private void ConsumeFrame(Direct3D11CaptureFramePool sender)
    {
        SizeInt32 updatedSize;
        using (Direct3D11CaptureFrame? frame = sender.TryGetNextFrame())
        {
            if (frame is null)
            {
                return;
            }

            updatedSize = frame.ContentSize;
            if (updatedSize.Width <= 0 || updatedSize.Height <= 0)
            {
                return;
            }

            if (updatedSize.Width == size.Width && updatedSize.Height == size.Height)
            {
                Interlocked.Exchange(ref lastFrame, clock.ElapsedMilliseconds);
                TimeSpan now = clock.Elapsed;
                if (now >= nextWrite)
                {
                    nextWrite = now + Interval;
                    Publish(ReadPixels(frame));
                }

                return;
            }
        }

        staging?.Dispose();
        staging = null;
        sender.Recreate(runtimeDevice!, PixelFormat, 2, updatedSize);
        size = updatedSize;
    }

    // Reports output errors while keeping the session and its last good image.
    private void Publish(BitmapSource image)
    {
        try
        {
            SnapshotFile.Publish(image);
            outputStatus(null);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            outputStatus(error);
        }
    }

    // Copies the GPU surface to a CPU-readable texture and makes a WPF bitmap.
    private BitmapSource ReadPixels(Direct3D11CaptureFrame frame)
    {
        using ID3D11Texture2D texture = CaptureInterop.GetTexture(frame.Surface);
        Texture2DDescription description = texture.Description;
        description.BindFlags = BindFlags.None;
        description.MiscFlags = ResourceOptionFlags.None;
        description.Usage = ResourceUsage.Staging;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        staging ??= device!.CreateTexture2D(description);
        context!.CopyResource(staging, texture);
        MappedSubresource mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int stride = checked((int)mapped.RowPitch);
            return BitmapSource.Create(size.Width, size.Height, 96, 96, PixelFormats.Bgr32,
                null, mapped.DataPointer, checked(stride * size.Height), stride);
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    // Quiesces frame writes before releasing capture and graphics resources.
    public void Dispose()
    {
        lock (gate)
        {
            stopping = true;
        }

        item.Closed -= OnClosed;
        if (pool is not null)
        {
            pool.FrameArrived -= OnFrame;
        }

        session?.Dispose();
        pool?.Dispose();
        staging?.Dispose();
        runtimeDevice?.Dispose();
        context?.Dispose();
        device?.Dispose();
    }
}
