namespace TabForge.Presenters;

/// <summary>
/// The arithmetic of fitting a docked plug-in editor into the FX window: the window rectangle that shows the whole editor, which scroll bars
/// the editor needs in an area and how far they scroll, and where the plug-in's view goes for a scroll offset.
/// Owns: those rules. Does not own: the window, the bars, or the engine's editor window (the Avalonia PluginEditorFit moves them).
/// Tests: no dedicated test class.
/// </summary>
public static class PluginEditorFitMath
{
    /// <summary>Margin kept free of the work area when the window grows (DIPs, total per axis).</summary>
    public const double WorkAreaMargin = 40;
    /// <summary>Rounding between pixels and DIPs never shows a bar.</summary>
    public const double BarSlack = 0.5;

    public readonly record struct Rectangle(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// <summary>The visible part of the editor and how far it can scroll (same unit as the inputs).</summary>
    public readonly record struct ScrollLayout(double ViewWidth, double ViewHeight, double RangeX, double RangeY);

    /// <summary>Editor pixels to DIPs at a DPI scale (1.5 = 150%).</summary>
    public static (double Width, double Height) ToDip(int widthPx, int heightPx, double scaleX, double scaleY) => (widthPx / scaleX, heightPx / scaleY);

    /// <summary>
    /// The window rectangle that shows the whole editor: grows only (never shrinks), at most the work area less
    /// <paramref name="margin"/>, top-left kept unless the grown window would leave the work area.
    /// <paramref name="chrome"/> is everything of the window that is not the editor area.
    /// </summary>
    public static Rectangle FitWindow(Rectangle window, (double Width, double Height) chrome, (double Width, double Height) editor, Rectangle work, double margin = WorkAreaMargin)
    {
        var w = Math.Max(window.Width, Math.Min(chrome.Width + editor.Width, work.Width - margin));
        var h = Math.Max(window.Height, Math.Min(chrome.Height + editor.Height, work.Height - margin));
        if (w <= window.Width && h <= window.Height) return window;
        var x = Math.Max(work.Left, Math.Min(window.Left, work.Right - w));
        var y = Math.Max(work.Top, Math.Min(window.Top, work.Bottom - h));
        return new Rectangle(x, y, w, h);
    }

    /// <summary>Which bars an editor needs in an area (a bar takes <paramref name="bar"/> from the other axis) and the scroll ranges (the overflow).</summary>
    public static ScrollLayout Scroll(double areaWidth, double areaHeight, double editorWidth, double editorHeight, double bar)
    {
        bool h = editorWidth > areaWidth + BarSlack, v = editorHeight > areaHeight + BarSlack;
        if (h && !v) v = editorHeight > areaHeight - bar + BarSlack;
        if (v && !h) h = editorWidth > areaWidth - bar + BarSlack;
        double viewW = Math.Max(0, areaWidth - (v ? bar : 0)), viewH = Math.Max(0, areaHeight - (h ? bar : 0));
        return new ScrollLayout(Math.Min(editorWidth, viewW), Math.Min(editorHeight, viewH), h ? editorWidth - viewW : 0, v ? editorHeight - viewH : 0);
    }

    /// <summary>Where the plug-in's view goes inside the editor window for a scroll offset in DIPs (pixels, zero or negative).</summary>
    public static (int X, int Y) ViewOrigin(double offsetX, double offsetY, double scaleX, double scaleY) =>
        (-(int)Math.Round(offsetX * scaleX), -(int)Math.Round(offsetY * scaleY));
}
