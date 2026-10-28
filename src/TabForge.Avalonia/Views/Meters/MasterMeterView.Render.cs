using System;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Meters;

// Owns: the drawing of the master meter through IDrawContext: the transport button look, the clip light, two bars with their hold lines, the number.
// Does not own: the numbers (MasterMeterModel), the clicks and the tooltip (MasterMeterView.cs).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
public sealed partial class MasterMeterView
{
    // The drawing in 34 DIP units; every number scales with the control width, so a smaller (compact) bar shrinks it with the buttons.
    private const double DesignSize = UiMetrics.TransportButtonSize;
    private const double BarWidth = 8, BarGap = 3, BarTop = 8, SideInset = 4, ClipLightHeight = 2.5, ClipLightTop = 4, NumberHeight = 9, NumberTextSize = 8.5, HoldHeight = 1.5, BarRadius = 1.5;

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width; var height = Bounds.Height;
        if (width < 1 || height < 1) return;
        var light = ActualThemeVariant == ThemeVariant.Light;
        var d = _draw.Use(context);
        var fill = _hovered ? AvaloniaPalette.Get(HoverShade.IconPaletteKey, light) : TransportBarView.TransportButton.Fill("PlaybarSurface", light);
        if (light && !_hovered) fill = ThemeColours.Blend(fill, AvaloniaPalette.Get("PanelBackground", light), TransportGlowRules.LightFillLift);
        var border = TransportBarView.TransportButton.BorderWidth;
        d.Rect(fill, new UiStroke(Palette.Get("PlaybarBorder", light), border), border / 2, border / 2, width - border, height - border, TransportBarView.TransportButton.ButtonCornerRadius);
        var s = width / DesignSize;
        var settings = _settings();
        var barsWidth = (2 * BarWidth + BarGap) * s;
        var left = (width - barsWidth) / 2;
        var top = BarTop * s;
        var bottom = height - SideInset * s - (settings.ShowNumber ? (NumberHeight + 1) * s : 0);
        var barHeight = Math.Max(2, bottom - top);
        d.Rect(Model.ClipLit ? Palette.Get("Danger", light) : Palette.Get("TrackSliderTrack", light), null, left, ClipLightTop * s, barsWidth, ClipLightHeight * s, BarRadius * s);
        DrawBar(d, light, left, top, BarWidth * s, barHeight, Model.LeftDb, Model.HoldLeftDb, s);
        DrawBar(d, light, left + (BarWidth + BarGap) * s, top, BarWidth * s, barHeight, Model.RightDb, Model.HoldRightDb, s);
        if (!settings.ShowNumber) return;
        var text = d.Text(Model.NumberText, ChromeMetrics.UiFontFamily, NumberTextSize * s, Palette.Get("TextPrimary", light), d.PixelsPerDip);
        d.DrawText(text, (width - text.Width) / 2, bottom + s);
    }

    // One bar: the track, the green part up to the warm level and the yellow part above it, then the hold line.
    private void DrawBar(IDrawContext d, bool light, double x, double y, double width, double height, double db, double holdDb, double s)
    {
        d.Rect(Palette.Get("TrackSliderTrack", light), null, x, y, width, height, BarRadius * s);
        var fraction = MasterMeterModel.Fraction(db);
        var warm = MasterMeterModel.Fraction(MasterMeterModel.WarmDb);
        if (fraction > 0)
        {
            var green = Math.Min(fraction, warm) * height;
            d.Rect(Palette.Get("Meter", light), null, x, y + height - green, width, green);
            if (fraction > warm) d.Rect(Palette.Get("MeterHot", light), null, x, y + height * (1 - fraction), width, (fraction - warm) * height);
        }
        if (Model.HoldShown && holdDb > MasterMeterModel.FloorDb)
        {
            var at = Math.Min(height - HoldHeight * s, height * (1 - MasterMeterModel.Fraction(holdDb)));
            d.Rect(Palette.Get("TextPrimary", light), null, x, y + at, width, HoldHeight * s);
        }
    }
}
