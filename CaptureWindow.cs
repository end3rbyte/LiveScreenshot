using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using WinRT;

namespace LiveScreenshot;

// Identifies a selected desktop window and maps its captured pixels to the desktop.
internal sealed partial record CaptureWindow(nint Handle, uint ProcessId, string Title)
{
    private const uint ExtendedFrameBounds = 9;
    private const uint Cloaked = 14;

    // Lists visible, titled desktop windows outside LiveScreenshot.
    internal static IReadOnlyList<CaptureWindow> List()
    {
        List<nint> handles = [];
        if (!EnumWindows((handle, _) =>
        {
            handles.Add(handle);
            return true;
        }, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return handles.Select(ReadWindow).OfType<CaptureWindow>().ToArray();
    }

    // Creates the capture item from the same handle used to place the ping.
    internal unsafe GraphicsCaptureItem CreateItem()
    {
        EnsureAvailable();
        Guid interopId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        Guid itemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        using IObjectReference factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem", interopId);
        nint* table = *(nint**)factory.ThisPtr;
        delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int> create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)table[3];
        nint item = 0;
        Marshal.ThrowExceptionForHR(create(factory.ThisPtr, Handle, &itemId, &item));
        try
        {
            return GraphicsCaptureItem.FromAbi(item) ?? throw new InvalidOperationException("Windows did not return a capture item.");
        }
        finally
        {
            MarshalInspectable<GraphicsCaptureItem>.DisposeAbi(item);
        }
    }

    // Returns the screen pixel under the preview click when the frame still matches.
    internal Point GetScreenPoint(BitmapSource image, Point previewPoint, Size previewSize)
    {
        if (previewPoint.X < 0 || previewPoint.Y < 0 ||
            previewPoint.X >= previewSize.Width || previewPoint.Y >= previewSize.Height)
        {
            throw new InvalidOperationException("The ping coordinates are outside the captured image.");
        }

        EnsureAvailable();
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(Handle, ExtendedFrameBounds, out NativeRect bounds,
            (uint)Marshal.SizeOf<NativeRect>()));
        if (bounds.Right - bounds.Left != image.PixelWidth || bounds.Bottom - bounds.Top != image.PixelHeight)
        {
            throw new InvalidOperationException("The preview size does not match the window. Wait for a new frame before pinging.");
        }

        return new Point(
            bounds.Left + (int)(previewPoint.X * image.PixelWidth / previewSize.Width),
            bounds.Top + (int)(previewPoint.Y * image.PixelHeight / previewSize.Height));
    }

    // Rejects windows that closed, minimized, or left the current desktop.
    private void EnsureAvailable()
    {
        if (GetWindowThreadProcessId(Handle, out uint processId) == 0 || processId != ProcessId ||
            !IsWindowVisible(Handle) || IsIconic(Handle) || IsCloaked(Handle))
        {
            throw new InvalidOperationException("The selected window is no longer visible.");
        }
    }

    // Reads the title and process identity of a window eligible for capture.
    private static CaptureWindow? ReadWindow(nint handle)
    {
        if (!IsWindowVisible(handle) || IsIconic(handle) ||
            GetWindowThreadProcessId(handle, out uint processId) == 0 || processId == Environment.ProcessId)
        {
            return null;
        }

        int length = GetWindowTextLength(handle);
        if (length == 0 || IsCloaked(handle))
        {
            return null;
        }

        StringBuilder title = new(length + 1);
        return GetWindowText(handle, title, title.Capacity) == 0 ? null : new CaptureWindow(handle, processId, title.ToString());
    }

    // Checks whether DWM has hidden the window from the desktop.
    private static bool IsCloaked(nint handle)
    {
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(handle, Cloaked, out uint cloaked, sizeof(uint)));
        return cloaked != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowCallback(nint window, nint data);

    // Enumerates top-level desktop windows.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, nint data);

    // Reads the current visibility style.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    // Checks whether a window is minimized.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    // Retrieves the owning process and thread.
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    // Measures the window title in UTF-16 code units.
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(nint window);

    // Copies the window title.
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int capacity);

    // Reads the DWM cloaking flags.
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, out uint result, uint size);

    // Reads the DWM frame bounds in physical desktop pixels.
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, out NativeRect result, uint size);

}
