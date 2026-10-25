using TabForge.Platform;

namespace TabForge.Services.Video;

/// <summary>A rectangle in screen pixels.</summary>
public sealed record ScreenRect(int X, int Y, int Width, int Height);

// Owns: copying a screen rectangle into a top-down BGRA buffer of a fixed size (scaled to fit, black bars for the aspect difference), through IScreenCapture.
// Does not own: choosing the rectangle, timing, encoding, the native calls (the platform adapter). Any thread may call Grab (one at a time); the frame belongs to the instance.
// Tests: LiveVideoRecordTests.
/// <summary>
/// One StretchBlt from the desktop surface per frame: the compositor already holds the picture, so the UI thread does no drawing work for
/// recording. What is captured is what is on screen (a window covered by another window records the cover).
/// </summary>
public sealed class ScreenRegionGrabber : IDisposable
{
    private readonly int _width, _height;
    private readonly Func<ScreenRect?> _region;
    private readonly IScreenCapture _capture;
    private IScreenFrame? _frame;

    public ScreenRegionGrabber(int width, int height, Func<ScreenRect?> region, IScreenCapture capture)
    {
        _width = width; _height = height; _region = region; _capture = capture;
    }

    /// <summary>Fills <paramref name="bgra"/> (width x height x 4); false when there is no region to take.</summary>
    public bool Grab(byte[] bgra)
    {
        if (_region() is not { Width: > 0, Height: > 0 } r) return false;
        _frame ??= _capture.OpenFrame(_width, _height);
        if (_frame is null) return false;
        var (x, y, w, h) = Fit(_width, _height, r.Width, r.Height);
        if (w < _width || h < _height) _frame.Clear();   // the black bars
        if (!_frame.StretchFromScreen(r, new ScreenRect(x, y, w, h))) return false;
        _frame.CopyPixelsTo(bgra);
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

    public void Dispose()
    {
        _frame?.Dispose();
        _frame = null;
    }
}
