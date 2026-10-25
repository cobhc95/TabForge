using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Platform;
using TabForge.Services.Video;

namespace TabForge.Avalonia.Video;

// Owns: the pixels of a live recording taken from the Avalonia window. A UI-thread timer renders the window content into a bitmap at the
//   recording rate (LiveVideoChoices.CaptureFps, 30 or 60 fps) and reads it back into a managed buffer. Grab (on the session's
//   capture thread) copies that buffer, crops the region and fits it into the frame, so only the render and the read-back run on the UI thread.
//   While the capture thread has not taken the last frame, the timer skips the render instead of piling UI work up behind a slow encoder.
// Does not own: the region choice (LiveVideoRegions), the frame timing (LiveVideoSession) or the encoder (VideoEncoder).
// Keys and ceilings: one content buffer and one scratch buffer, both the size of the window content in device pixels, shrunk so the region is at most the frame size (sized again when
//   the window resizes); the content buffer is written on the UI thread under _gate, the scratch buffer is used only by Grab.
// Threads: the constructor, the timer, RenderNow and the first half of Dispose run on the UI thread; Grab runs on the capture thread.
// Tests: TabForge.Avalonia.Tests.LiveVideoRecordTests (a headless window reads back its own colour, the rate is capped, an untaken frame is not re-rendered).
internal sealed class AvaloniaWindowFrameCapture : IVideoCapture
{
    private const double DotsPerInch = 96;   // Avalonia's DIP to inch mapping; the bitmap's dpi is this times the render scaling

    private readonly int _frameWidth, _frameHeight;
    private readonly Func<Visual?> _content;
    private readonly Func<ScreenRect?> _region;
    private readonly IUiThread _ui;
    private readonly IUiTimer _timer;
    private readonly object _gate = new();
    private RenderTargetBitmap? _target;         // UI thread only
    private WriteableBitmap? _readback;          // UI thread only: the render's pixels are copied out of this
    private byte[] _contentPixels = Array.Empty<byte>();    // written under _gate
    private int _contentWidth, _contentHeight;   // written under _gate
    private double _contentShrink = 1;           // written under _gate: the content pixels over the region's pixels
    private byte[] _scratch = Array.Empty<byte>();          // used by Grab only
    private int _renderedVersion;                // written under _gate (UI thread): bumped after each fresh render
    private int _grabbedVersion;                 // written under _gate (capture thread): the last render it took
    private volatile bool _disposed;
    private int _blackRenders;                   // UI thread: consecutive renders that showed no colour at all

    /// <param name="frameWidth">The frame size (1920 x 1080 or 3840 x 2160, landscape or portrait).</param>
    /// <param name="frameRate">Frames per second: at most the live capture rate, on a Render-priority timer so the frames keep their pace.</param>
    /// <param name="content">The visual whose content is recorded (the window's content); null while there is no window.</param>
    /// <param name="region">The rectangle of the content to record, in its device pixels; null while it is not on screen.</param>
    /// <param name="ui">The UI thread the timer and the render run on.</param>
    public AvaloniaWindowFrameCapture(int frameWidth, int frameHeight, int frameRate, Func<Visual?> content, Func<ScreenRect?> region, IUiThread ui)
    {
        _frameWidth = frameWidth; _frameHeight = frameHeight; _content = content; _region = region; _ui = ui;
        FrameRate = Math.Clamp(frameRate, 1, LiveVideoChoices.MaxCaptureFps);
        _timer = ui.CreateTimer(TimeSpan.FromSeconds(1.0 / FrameRate), UiPriority.Render);
        _timer.Tick += (_, _) => RenderNow();
        _timer.Start();
    }

    /// <summary>The capped render rate (frames per second).</summary>
    internal int FrameRate { get; private set; }

    /// <summary>UI thread: slows the render timer (the session fell back to 30 fps).</summary>
    public void SetFrameRate(int fps)
    {
        FrameRate = Math.Clamp(fps, 1, LiveVideoChoices.MaxCaptureFps);
        _timer.Interval = TimeSpan.FromSeconds(1.0 / FrameRate);
    }

    /// <summary>How many fresh renders ran (tests: an untaken frame is skipped).</summary>
    internal int Renders { get; private set; }

    /// <summary>UI thread: renders the content once and reads it back (the timer calls this at the capped rate). Skipped while the
    /// capture thread still holds the last frame, so a slow encoder throttles the render instead of the UI thread queueing behind it.</summary>
    internal void RenderNow()
    {
        if (_disposed) return;
        lock (_gate) { if (_renderedVersion != _grabbedVersion) return; }
        if (_content() is not { } root || !root.IsEffectivelyVisible) return;
        if (_region() is not { } region || region.Width < 1 || region.Height < 1) return;
        // Render at the size the frame needs, not the window's device pixels: the region is shown at most 1:1 in the frame.
        var fit = ScreenRegionGrabber.Fit(_frameWidth, _frameHeight, region.Width, region.Height);
        var shrink = Math.Min(1.0, (double)fit.Width / region.Width);
        var scaling = (TopLevel.GetTopLevel(root)?.RenderScaling ?? 1.0) * shrink;
        var width = (int)Math.Round(root.Bounds.Width * scaling);
        var height = (int)Math.Round(root.Bounds.Height * scaling);
        if (width < 1 || height < 1) return;
        EnsureBitmaps(width, height, scaling);
        _target!.Render(root);
        using (var locked = _readback!.Lock())
        {
            _target.CopyPixels(locked, AlphaFormat.Premul);
            if (IsBlack(locked, width, height) && ++_blackRenders <= 30) return;   // a render that came out black (window not drawn yet): try again, a real black window passes after 30
            _blackRenders = 0;
            lock (_gate)
            {
                EnsureContentBuffer(width, height);
                if (locked.RowBytes == width * 4) Marshal.Copy(locked.Address, _contentPixels, 0, width * height * 4);
                else
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(locked.Address + y * locked.RowBytes, _contentPixels, y * width * 4, width * 4);
                _contentWidth = width; _contentHeight = height; _contentShrink = shrink;
                _renderedVersion++;
            }
            Renders++;
        }
    }

    private static bool IsBlack(ILockedFramebuffer fb, int width, int height)
    {
        for (var i = 0; i < 64; i++)
        {
            var x = (int)((long)(i % 8 * 2 + 1) * width / 16); var y = (int)((long)(i / 8 * 2 + 1) * height / 16);
            if (Marshal.ReadInt32(fb.Address + y * fb.RowBytes + x * 4) != unchecked((int)0xFF000000) && (Marshal.ReadInt32(fb.Address + y * fb.RowBytes + x * 4) & 0xFFFFFF) != 0) return false;
        }
        return true;
    }

    /// <summary>Capture thread: the latest render, cropped to the region and fitted into the frame (black around it). False when there is no picture yet.</summary>
    public bool Grab(byte[] bgra)
    {
        if (_region() is not { } region) return false;
        int width, height;
        double shrink;
        lock (_gate)
        {
            if (_contentWidth == 0) return false;
            width = _contentWidth; height = _contentHeight; shrink = _contentShrink;
            if (_scratch.Length != _contentPixels.Length) _scratch = new byte[_contentPixels.Length];
            Buffer.BlockCopy(_contentPixels, 0, _scratch, 0, _contentPixels.Length);
            _grabbedVersion = _renderedVersion;   // the frame is taken: the next render may run
        }
        if (shrink < 1)
        {
            int x = (int)Math.Round(region.X * shrink), y = (int)Math.Round(region.Y * shrink);
            region = new ScreenRect(x, y, Math.Max(1, (int)Math.Round(region.Width * shrink)), Math.Max(1, (int)Math.Round(region.Height * shrink)));
        }
        var crop = Crop(region, width, height);
        if (crop is null) return false;
        FitInto(bgra, _frameWidth, _frameHeight, _scratch, width, crop);
        return true;
    }

    /// <summary>Stops the timer and frees the bitmaps. Any thread: the UI-thread part is posted.</summary>
    public void Dispose()
    {
        _disposed = true;
        lock (_gate) { _contentPixels = Array.Empty<byte>(); _contentWidth = _contentHeight = 0; }
        _ui.Post(() =>
        {
            _timer.Stop();
            _target?.Dispose();
            _readback?.Dispose();
            _target = null;
            _readback = null;
        });
    }

    private void EnsureBitmaps(int width, int height, double scaling)
    {
        if (_target is { } target && target.PixelSize.Width == width && target.PixelSize.Height == height) return;
        _target?.Dispose();
        _readback?.Dispose();
        var dpi = new Vector(DotsPerInch * scaling, DotsPerInch * scaling);
        _target = new RenderTargetBitmap(new PixelSize(width, height), dpi);
        _readback = new WriteableBitmap(new PixelSize(width, height), dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
    }

    // Called under _gate.
    private void EnsureContentBuffer(int width, int height)
    {
        if (_contentPixels.Length != width * height * 4) _contentPixels = new byte[width * height * 4];
    }

    /// <summary>The region clipped to the content; null when nothing of it is inside.</summary>
    private static ScreenRect? Crop(ScreenRect region, int width, int height)
    {
        int x = Math.Max(0, region.X), y = Math.Max(0, region.Y);
        int right = Math.Min(width, region.X + region.Width), bottom = Math.Min(height, region.Y + region.Height);
        return right > x && bottom > y ? new ScreenRect(x, y, right - x, bottom - y) : null;
    }

    /// <summary>Nearest-neighbour scale of the crop into the frame, centred with black around it (the rule of ScreenRegionGrabber.Fit).</summary>
    private static void FitInto(byte[] frame, int frameWidth, int frameHeight, byte[] source, int sourceWidth, ScreenRect crop)
    {
        Array.Clear(frame);
        var dest = ScreenRegionGrabber.Fit(frameWidth, frameHeight, crop.Width, crop.Height);
        for (var dy = 0; dy < dest.Height; dy++)
        {
            var sourceRow = (crop.Y + (int)((long)dy * crop.Height / dest.Height)) * sourceWidth;
            var frameRow = (dest.Y + dy) * frameWidth + dest.X;
            for (var dx = 0; dx < dest.Width; dx++)
            {
                var sourceIndex = (sourceRow + crop.X + (int)((long)dx * crop.Width / dest.Width)) * 4;
                var frameIndex = (frameRow + dx) * 4;
                frame[frameIndex] = source[sourceIndex];
                frame[frameIndex + 1] = source[sourceIndex + 1];
                frame[frameIndex + 2] = source[sourceIndex + 2];
                frame[frameIndex + 3] = source[sourceIndex + 3];
            }
        }
    }
}
