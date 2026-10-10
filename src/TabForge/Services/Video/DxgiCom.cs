using System.Runtime.InteropServices;

namespace TabForge.Services.Video;


// Owns: the raw COM plumbing for Desktop Duplication: virtual-table calls on DXGI and Direct3D 11 interfaces, interface ids, the two native entry points.
// Does not own: what is captured or how (DxgiRegionGrabber). No NuGet or SDK types; slot numbers follow the published interface order.
// Tests: TestLiveVideoRecord.
internal static unsafe class DxgiCom
{
    public const int WaitTimeout = unchecked((int)0x887A0027), AccessLost = unchecked((int)0x887A0026);
    public static readonly Guid Factory1 = new("770aae78-f26f-4dba-a829-253c83d1b387"), Output1 = new("00cddea8-939b-4b83-a340-a685226666cc"), Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    /// <summary>The function pointer in virtual-table slot <paramref name="index"/> of a COM object.</summary>
    public static void* Slot(void* obj, int index) => (*(void***)obj)[index];

    public static void Release(void* obj) { if (obj != null) ((delegate* unmanaged[Stdcall]<void*, uint>)Slot(obj, 2))(obj); }

    public static int QueryInterface(void* obj, Guid iid, out void* result)
    {
        void* r = null;
        var hr = ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)Slot(obj, 0))(obj, &iid, &r);
        result = r;
        return hr;
    }

    [DllImport("dxgi.dll")] public static extern int CreateDXGIFactory1(Guid* riid, void** factory);
    [DllImport("d3d11.dll")] public static extern int D3D11CreateDevice(void* adapter, int driverType, IntPtr software, uint flags, int* levels, uint count, uint sdkVersion, void** device, int* level, void** context);
}

// Owns: the one error DxgiRegionGrabber raises when duplication cannot be set up.
// Does not own: the fallback that follows (DesktopRegionGrabber).
// Tests: TestLiveVideoRecord.
/// <summary>DXGI or Direct3D cannot supply desktop frames here (remote session, no duplication support, unusual rotation).</summary>
internal sealed class DxgiUnavailableException : Exception
{
    public DxgiUnavailableException(string message) : base(message) { }
}
