namespace TabForge.Services.Video;

// Owns: choosing the pixel source for a recording: Desktop Duplication when it works, otherwise the GDI grabber, switching once for good on the first DXGI failure.
// Does not own: either grabber's details, the region, timing. Any thread may call Grab (one at a time).
// Tests: TestLiveVideoRecord.
internal sealed class DesktopRegionGrabber : IDisposable
{
    private readonly ScreenRegionGrabber _gdi;
    private DxgiRegionGrabber? _dxgi;

    public DesktopRegionGrabber(int width, int height, Func<ScreenRect?> region)
    {
        _gdi = new ScreenRegionGrabber(width, height, region);
        _dxgi = new DxgiRegionGrabber(width, height, region);
    }

    /// <summary>"DXGI" or "GDI": the source in use now.</summary>
    public string Source => _dxgi is null ? "GDI" : "DXGI";

    /// <summary>
    /// True when <paramref name="bgra"/> holds a picture. Duplication reports nothing while the desktop is unchanged: the buffer then keeps the
    /// last frame (the caller reuses it), and before the first frame the GDI grabber takes one picture so a still screen never records black.
    /// </summary>
    public bool Grab(byte[] bgra)
    {
        if (_dxgi is { } d)
        {
            try { if (d.Grab(bgra)) return _have = true; }
            catch (Exception ex) when (ex is DxgiUnavailableException or DllNotFoundException or EntryPointNotFoundException) { d.Dispose(); _dxgi = null; }
            if (_dxgi is not null && _have) return true;
        }
        return _have = _gdi.Grab(bgra);
    }

    private bool _have;

    public void Dispose() { _dxgi?.Dispose(); _gdi.Dispose(); }
}
