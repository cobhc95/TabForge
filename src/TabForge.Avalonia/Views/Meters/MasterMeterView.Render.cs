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
    private const double BarWidth = MeterBarPainter.BarWidth, BarGap = MeterBarPainter.BarGap, BarTop = MeterBarPainter.BarTop, SideInset = MeterBarPainter.SideInset,
        ClipLightHeight = MeterBarPainter.ClipLightHeight, ClipLightTop = MeterBarPainter.ClipLightTop, NumberHeight = MeterBarPainter.NumberHeight,
        NumberTextSize = MeterBarPainter.NumberTextSize, HoldHeight = MeterBarPainter.HoldHeight, BarRadius = MeterBarPainter.BarRadius;

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width; var height = Bounds.Height;
        if (width < 1 || height < 1) return;
        var light = ActualThemeVariant == ThemeVariant.Light;
        var d = _draw.Use(context);
        var fill = _hovered ? MeterBarPainter.HoverFill(light) : TransportBarView.TransportButton.Fill("PlaybarSurface", light);
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
        MeterBarPainter.DrawClipStrip(d, key => Palette.Get(key, light), light, Model.ClipLit, left, ClipLightTop * s, barsWidth, ClipLightHeight * s, BarRadius * s);
        MeterBarPainter.DrawBars(d, key => Palette.Get(key, light), light, Model, left, top, BarWidth * s, BarGap * s, barHeight, BarRadius * s, HoldHeight * s, s);
        if (!settings.ShowNumber) return;
        var text = d.Text(Model.NumberText, ChromeMetrics.UiFontFamily, NumberTextSize * s, Palette.Get("TextPrimary", light), d.PixelsPerDip);
        d.DrawText(text, (width - text.Width) / 2, bottom + s);
    }

}
