using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the fretboard legend's minimise and restore button: a small chevron beside the legend's title while it is open, the round palette button that stands for the legend while it is minimised, their
//   look (icon at the legend's unit, hover shade, no pointer change) and the click, which flips EditingSettings.FretLegendMinimised and saves, so every observer re-reads the look.
// Does not own: where it goes or the legend's drawing (InstrumentPanelView.LegendToggleBounds, DrawLegend), the stored setting (EditingSettings) or the settings store (the host's callback).
// Tests: TabForge.Avalonia.Tests.FretboardLegendRowsTests.
// Threads: UI thread only.
internal sealed class FretboardLegendToggle : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    private const string ChevronPath = "M2,4 L7,9 L12,4";
    private readonly Func<(Func<AppSettings?> Settings, Action Save)?> _store;
    private readonly Path _glyph = new() { StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
    private readonly Viewbox _icon = new() { Stretch = Stretch.Uniform };
    // The visible chip (the chevron, or the round palette button); the button around it is larger so it is easy to hit.
    private readonly Border _chip = new() { IsHitTestVisible = false, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
    private bool _minimised;

    public FretboardLegendToggle(Func<(Func<AppSettings?> Settings, Action Save)?> store)
    {
        _store = store;
        Name = "FretboardLegendToggle";
        IsVisible = false;
        Focusable = false;
        Padding = new Thickness(0);
        MinWidth = MinHeight = 0;
        HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        _glyph[!Shape.StrokeProperty] = new DynamicResourceExtension("IconActiveBrush");
        _icon.Child = _glyph;
        _chip.Child = _icon;
        Content = _chip;
        BorderThickness = new Thickness(0);
        Background = AvaloniaPalette.TransparentBrush;   // a brush, so the whole area around the chip takes the click
        HoverShade.Attach(this, hovered => Rest(hovered));
        Click += (_, _) => Flip();
    }

    /// <summary>
    /// Shapes the button for the legend's state: <paramref name="rect"/> is the chip's place in panel coordinates, <paramref name="unit"/> the legend unit in real pixels. The clickable
    /// area is larger than the chip all round; returns how far it reaches beyond the chip, so the caller places the button that much up and left.
    /// </summary>
    public double Fit(UiRect rect, bool minimised, double unit)
    {
        _minimised = minimised;
        var pad = Math.Max(5, 7 * unit);
        Width = rect.Width + 2 * pad;
        Height = rect.Height + 2 * pad;
        _chip.Width = rect.Width;
        _chip.Height = rect.Height;
        _chip.CornerRadius = new CornerRadius(minimised ? rect.Width / 2 : 4 * unit);
        _chip.BorderThickness = minimised ? FretboardChromeMetrics.PillBorder : new Thickness(1);
        _glyph.Data = Geometry.Parse(minimised ? FretboardChromeMetrics.PillIconPath : ChevronPath);
        _glyph.StrokeThickness = FretboardChromeMetrics.PillStroke;
        _glyph.Width = _glyph.Height = minimised ? 24 : 14;
        _icon.Width = _icon.Height = rect.Width * (minimised ? 0.55 : 0.7);
        var label = minimised ? "Show the legend" : "Minimise the legend";
        ToolTip.SetTip(this, label);
        AutomationProperties.SetName(this, label);
        Rest(false);
        return pad;
    }

    // Rest: the round button keeps its panel fill and edge, the chevron has none. Hover: the chip takes the hover shade and an edge in both states.
    private void Rest(bool hovered)
    {
        if (hovered) { _chip.Background = HoverShade.Brush(this); _chip[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush"); return; }
        if (_minimised) { _chip[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush"); _chip[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"); }
        else { _chip.Background = AvaloniaPalette.TransparentBrush; _chip.BorderBrush = AvaloniaPalette.TransparentBrush; }
    }

    private void Flip()
    {
        if (_store() is not { } store || store.Settings() is not { } settings) return;
        settings.Editing.FretLegendMinimised = !settings.Editing.FretLegendMinimised;
        store.Save();
    }
}
