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
        Content = _icon;
        this[!BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        HoverShade.Attach(this, hovered => Rest(hovered));
        Click += (_, _) => Flip();
    }

    /// <summary>Shapes the button for the legend's state: <paramref name="rect"/> is its place in panel coordinates, <paramref name="unit"/> the legend unit in real pixels.</summary>
    public void Fit(UiRect rect, bool minimised, double unit)
    {
        _minimised = minimised;
        Width = rect.Width;
        Height = rect.Height;
        CornerRadius = new CornerRadius(minimised ? rect.Width / 2 : 4 * unit);
        BorderThickness = minimised ? FretboardChromeMetrics.PillBorder : new Thickness(0);
        _glyph.Data = Geometry.Parse(minimised ? FretboardChromeMetrics.PillIconPath : ChevronPath);
        _glyph.StrokeThickness = FretboardChromeMetrics.PillStroke;
        _glyph.Width = _glyph.Height = minimised ? 24 : 14;
        _icon.Width = _icon.Height = rect.Width * (minimised ? 0.55 : 0.7);
        var label = minimised ? "Show the legend" : "Minimise the legend";
        ToolTip.SetTip(this, label);
        AutomationProperties.SetName(this, label);
        Rest(false);
    }

    private void Rest(bool hovered)
    {
        if (hovered) Background = HoverShade.Brush(this);
        else if (_minimised) this[!BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        else Background = AvaloniaPalette.TransparentBrush;
    }

    private void Flip()
    {
        if (_store() is not { } store || store.Settings() is not { } settings) return;
        settings.Editing.FretLegendMinimised = !settings.Editing.FretLegendMinimised;
        store.Save();
    }
}
