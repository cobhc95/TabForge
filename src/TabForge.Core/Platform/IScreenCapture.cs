using TabForge.Services.Video;

namespace TabForge.Platform;

// Owns: the screen pixel questions the app asks the operating system: open a frame of a fixed size, copy a screen rectangle into it, clear it, read its pixels.
// Does not own: choosing the region, fitting it into the frame (ScreenRegionGrabber.Fit), timing, encoding, or the native handles (the platform adapter).
// Tests: none dedicated(a cleared frame reads back black at the requested size), TestLiveVideoRecord.
/// <summary>The screen capture port that the GDI video grabber copies pixels through. The adapter is supplied by the host app.</summary>
public interface IScreenCapture
{
    /// <summary>Opens a top-down BGRA frame of <paramref name="width"/> x <paramref name="height"/>; null when the platform cannot open one.</summary>
    IScreenFrame? OpenFrame(int width, int height);
}

/// <summary>A BGRA frame that screen rectangles are copied into. One caller at a time; <see cref="IDisposable.Dispose"/> releases the frame.</summary>
public interface IScreenFrame : IDisposable
{
    /// <summary>Fills the whole frame with black.</summary>
    void Clear();

    /// <summary>Copies the screen rectangle <paramref name="source"/> into the frame rectangle <paramref name="destination"/>, stretched with a halftone filter. False when the copy fails.</summary>
    bool StretchFromScreen(ScreenRect source, ScreenRect destination);

    /// <summary>Copies the frame's pixels (width x height x 4, top-down BGRA) into <paramref name="bgra"/>.</summary>
    void CopyPixelsTo(byte[] bgra);
}
