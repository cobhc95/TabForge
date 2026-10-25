using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Services.Video;

namespace TabForge.Avalonia.Video;

// Owns: what a Record video region choice (LiveVideoChoices) means in the window: a rectangle in the window content's device pixels,
//   taken from the score, the Band view, the instrument panel or the whole content. A hidden element falls back to the score.
// Does not own: the capture (AvaloniaWindowFrameCapture), the stored choice (LiveVideoSettings) or the layout of the views.
// Threads: UI thread only (it reads the visual tree).
// Tests: TabForge.Avalonia.Tests.LiveVideoRecordTests.
internal sealed class LiveVideoRegions
{
    private readonly Func<Visual?> _content;
    private readonly Control _score;
    private readonly Func<Control?> _band, _instrument;

    /// <param name="content">The window's content; the rectangles are in its device pixels.</param>
    /// <param name="score">The score's scroll view (its visible part is the score area).</param>
    /// <param name="band">The Band view, or null while it does not exist.</param>
    /// <param name="instrument">The instrument panel, or null while it does not exist.</param>
    public LiveVideoRegions(Func<Visual?> content, Control score, Func<Control?> band, Func<Control?> instrument)
    {
        _content = content; _score = score; _band = band; _instrument = instrument;
    }

    /// <summary>The window content, which the frame capture renders.</summary>
    public Visual? Content => _content();

    /// <summary>The rectangle of the region in the content's device pixels; null while it is not on screen.</summary>
    public ScreenRect? RectOf(string region) => region switch
    {
        LiveVideoChoices.Window => ContentRect(),
        LiveVideoChoices.Band => ElementRect(_band()) ?? ScoreRect(),
        LiveVideoChoices.ScoreAndInstrument => Union(ScoreRect(), ElementRect(_instrument())),
        _ => ScoreRect(),
    };

    private ScreenRect? ScoreRect() => ElementRect(_score);

    private ScreenRect? ContentRect()
    {
        var content = _content();
        return content is null ? null : ToDevicePixels(content, new Rect(content.Bounds.Size));
    }

    /// <summary>The element's rectangle in the content's device pixels; null when it is hidden, empty or outside the content.</summary>
    private ScreenRect? ElementRect(Control? element)
    {
        var content = _content();
        if (content is null || element is not { IsEffectivelyVisible: true } || element.Bounds.Width < 1 || element.Bounds.Height < 1) return null;
        var origin = element.TranslatePoint(new Point(0, 0), content);
        return origin is { } at ? ToDevicePixels(content, new Rect(at, element.Bounds.Size)) : null;
    }

    private static ScreenRect ToDevicePixels(Visual content, Rect dips)
    {
        var scaling = TopLevel.GetTopLevel(content)?.RenderScaling ?? 1.0;
        return new ScreenRect((int)Math.Round(dips.X * scaling), (int)Math.Round(dips.Y * scaling),
            (int)Math.Round(dips.Width * scaling), (int)Math.Round(dips.Height * scaling));
    }

    private static ScreenRect? Union(ScreenRect? a, ScreenRect? b)
    {
        if (a is null || b is null) return a ?? b;
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new ScreenRect(x, y, Math.Max(a.X + a.Width, b.X + b.Width) - x, Math.Max(a.Y + a.Height, b.Y + b.Height) - y);
    }
}
