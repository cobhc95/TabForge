using System.Runtime.InteropServices;

namespace TabForge.Services.Video;

/// <summary>A rectangle in screen pixels.</summary>
public sealed record ScreenRect(int X, int Y, int Width, int Height);

// Owns: copying a screen rectangle into a top-down BGRA buffer of a fixed size (scaled to fit, black bars for the aspect difference), with GDI.
// Does not own: choosing the rectangle, timing, encoding. Any thread may call Grab (one at a time); the GDI objects belong to the instance.
// Tests: TestLiveVideoRecord.
/// <summary>
/// One StretchBlt from the desktop surface per frame: the compositor already holds the picture, so the UI thread does no drawing work for
/// recording. What is captured is what is on screen (a window covered by another window records the cover).
/// </summary>
public sealed class ScreenRegionGrabber : IDisposable
{
    private const int SrcCopy = 0x00CC0020, Halftone = 4;

    private readonly int _width, _height;
    private readonly Func<ScreenRect?> _region;
    private IntPtr _screen, _memory, _bitmap, _old, _bits;

    public ScreenRegionGrabber(int width, int height, Func<ScreenRect?> region) { _width = width; _height = height; _region = region; }

    /// <summary>Fills <paramref name="bgra"/> (width x height x 4); false when there is no region to take.</summary>
    public bool Grab(byte[] bgra)
    {
        if (_region() is not { Width: > 0, Height: > 0 } r) return false;
        if (_bits == IntPtr.Zero && !Create()) return false;
        var (x, y, w, h) = Fit(_width, _height, r.Width, r.Height);
        if (w < _width || h < _height) PatBlt(_memory, 0, 0, _width, _height, 0x00000042);   // BLACKNESS
        SetStretchBltMode(_memory, Halftone);
        if (!StretchBlt(_memory, x, y, w, h, _screen, r.X, r.Y, r.Width, r.Height, SrcCopy)) return false;
        Marshal.Copy(_bits, bgra, 0, _width * _height * 4);
        return true;
    }

    /// <summary>
    /// Where a whole source of <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> goes in the frame: scaled to fit, aspect kept,
    /// centred; the rest stays black. Every grabber uses this, so a region is never cropped.
    /// </summary>
    public static ScreenRect Fit(int frameWidth, int frameHeight, int sourceWidth, int sourceHeight)
    {
        var scale = Math.Min(frameWidth / (double)sourceWidth, frameHeight / (double)sourceHeight);
        int w = Math.Clamp((int)Math.Round(sourceWidth * scale), 1, frameWidth), h = Math.Clamp((int)Math.Round(sourceHeight * scale), 1, frameHeight);
        return new ScreenRect((frameWidth - w) / 2, (frameHeight - h) / 2, w, h);
    }

    private bool Create()
    {
        _screen = GetDC(IntPtr.Zero);
        _memory = CreateCompatibleDC(_screen);
        var info = new BitmapInfo { Size = 40, Width = _width, Height = -_height, Planes = 1, BitCount = 32 };   // negative height: top-down
        _bitmap = CreateDIBSection(_screen, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero) { Dispose(); return false; }
        _old = SelectObject(_memory, _bitmap);
        return true;
    }

    public void Dispose()
    {
        if (_old != IntPtr.Zero) SelectObject(_memory, _old);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_memory != IntPtr.Zero) DeleteDC(_memory);
        if (_screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screen);
        _old = _bitmap = _memory = _screen = _bits = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, Used, Important; }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool PatBlt(IntPtr dc, int x, int y, int w, int h, int rop);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
}
