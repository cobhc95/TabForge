using System;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: how a drag-select marquee draws (timeline lanes and score): a translucent accent fill with a thin 1 px border, from the theme tokens
//   MarqueeFill and MarqueeBorder (light and dark).
// Does not own: where the box is (TimelineInputController.Box, ScorePreviewInput) or what it selects.
// Tests: no dedicated test class.
internal static class MarqueePainter
{
    public const string FillKey = "MarqueeFill";
    public const string BorderKey = "MarqueeBorder";
    public const double BorderWidth = 1;

    public static void Draw(IDrawContext d, Func<string, UiColor> ink, double x, double y, double width, double height)
    {
        if (width <= 0 && height <= 0) return;
        d.Rect(ink(FillKey), new UiStroke(ink(BorderKey), BorderWidth), x + 0.5, y + 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1));
    }
}
