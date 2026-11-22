using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LiveScreenshot;

// Bridges the Windows capture APIs and Direct3D 11.
internal static partial class CaptureInterop
{
    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);

    // Wraps the Direct3D device for use by the capture frame pool.
    internal static IDirect3DDevice WrapDevice(ID3D11Device device)
    {
        using IDXGIDevice dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out nint pointer));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    // Acquires the native texture backing a captured surface.
    internal static unsafe ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        using ObjectReference<WinRT.Interop.IUnknownVftbl> access = ((IWinRTObject)surface).NativeObject.As<WinRT.Interop.IUnknownVftbl>(
            new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1"));
        nint self = access.ThisPtr;
        nint* methods = *(nint**)self;
        delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int> get = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)methods[3];
        Guid textureId = typeof(ID3D11Texture2D).GUID;
        nint texture = 0;
        Marshal.ThrowExceptionForHR(get(self, &textureId, &texture));
        return new ID3D11Texture2D(texture);
    }
}
