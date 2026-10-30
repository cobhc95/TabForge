using System;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Meters;

// Owns: the drawn bars every level meter shares: the track, the green part up to the warm level, the yellow part above it, the clip strip and the peak-hold line.
// Does not own: the numbers (MasterMeterModel), the frame around the bars (MasterMeterView draws the transport button, TrackMeterColumn the row gap).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests, TrackMeterTests.
internal static class MeterBarPainter
{
    // The master meter's design geometry, in transport-button DIP; the master scales it with its width, the track meter gives its own box.
    internal const double BarWidth = 8, BarGap = 3, BarTop = 8, SideInset = 4, ClipLightHeight = 2.5, ClipLightTop = 4, NumberHeight = 9, NumberTextSize = 8.5,
        HoldHeight = 1.5, BarRadius = 1.5;

    /// <summary>The hover fill of a meter cell: the same stronger icon hover the transport buttons use.</summary>
    internal static UiColor HoverFill(bool light) => AvaloniaPalette.Get(HoverShade.IconPaletteKey, light);

    /// <summary>The pressed fill of a meter cell: the same press surface the other track cells blend toward.</summary>
    internal static UiColor PressedFill(bool light) => AvaloniaPalette.Get(HoverShade.PressPaletteKey, light);

    /// <summary>Draws the two bars and their hold lines in a box; <paramref name="scale"/> keeps the corner and hold tick in proportion.</summary>
    internal static void DrawBars(IDrawContext d, Func<string, UiColor> ink, bool light, MasterMeterModel model, double left, double top,
        double barWidth, double gap, double barHeight, double radius, double holdHeight, double scale)
    {
        DrawBar(d, ink, light, model, left, top, barWidth, barHeight, model.LeftDb, model.HoldLeftDb, radius, holdHeight, scale);
        DrawBar(d, ink, light, model, left + barWidth + gap, top, barWidth, barHeight, model.RightDb, model.HoldRightDb, radius, holdHeight, scale);
    }

    /// <summary>The clip strip above the bars: danger when lit, the slider track colour when clear.</summary>
    internal static void DrawClipStrip(IDrawContext d, Func<string, UiColor> ink, bool light, bool lit, double left, double top, double width, double height, double radius) =>
        d.Rect(lit ? ink("Danger") : ink("TrackSliderTrack"), null, left, top, width, height, radius);

    /// <summary>Draws the two bars side by side as horizontal rows (left channel on top), filling left to right, with the hold tick and a clip end mark on the right.</summary>
    internal static void DrawBarsHorizontal(IDrawContext d, Func<string, UiColor> ink, bool light, MasterMeterModel model, double left, double top,
        double barLength, double barHeight, double gap, double radius, double holdWidth, double clipMark)
    {
        DrawBarH(d, ink, model, left, top, barLength, barHeight, model.LeftDb, model.HoldLeftDb, radius, holdWidth, clipMark);
        DrawBarH(d, ink, model, left, top + barHeight + gap, barLength, barHeight, model.RightDb, model.HoldRightDb, radius, holdWidth, clipMark);
    }

    private static void DrawBarH(IDrawContext d, Func<string, UiColor> ink, MasterMeterModel model, double x, double y, double length, double height,
        double db, double holdDb, double radius, double holdWidth, double clipMark)
    {
        d.Rect(ink("TrackSliderTrack"), null, x, y, length, height, radius);
        var fraction = MasterMeterModel.Fraction(db);
        var warm = MasterMeterModel.Fraction(MasterMeterModel.WarmDb);
        if (fraction > 0)
        {
            d.Rect(ink("Meter"), null, x, y, Math.Min(fraction, warm) * length, height);
            if (fraction > warm) d.Rect(ink("MeterHot"), null, x + warm * length, y, (fraction - warm) * length, height);
        }
        if (model.HoldShown && holdDb > MasterMeterModel.FloorDb)
        {
            var at = Math.Min(length - holdWidth, length * MasterMeterModel.Fraction(holdDb));
            d.Rect(ink("TextPrimary"), null, x + at, y, holdWidth, height);
        }
        if (model.ClipLit) d.Rect(ink("Danger"), null, x + length - clipMark, y, clipMark, height);
    }

    private static void DrawBar(IDrawContext d, Func<string, UiColor> ink, bool light, MasterMeterModel model, double x, double y, double width, double height,
        double db, double holdDb, double radius, double holdHeight, double scale)
    {
        d.Rect(ink("TrackSliderTrack"), null, x, y, width, height, radius);
        var fraction = MasterMeterModel.Fraction(db);
        var warm = MasterMeterModel.Fraction(MasterMeterModel.WarmDb);
        if (fraction > 0)
        {
            var green = Math.Min(fraction, warm) * height;
            d.Rect(ink("Meter"), null, x, y + height - green, width, green);
            if (fraction > warm) d.Rect(ink("MeterHot"), null, x, y + height * (1 - fraction), width, (fraction - warm) * height);
        }
        if (model.HoldShown && holdDb > MasterMeterModel.FloorDb)
        {
            var at = Math.Min(height - holdHeight, height * (1 - MasterMeterModel.Fraction(holdDb)));
            d.Rect(ink("TextPrimary"), null, x, y + at, width, holdHeight * scale);
        }
    }
}
