using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.LiveInput;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the cents scale of the live input strip: a rail with ticks at -50/-25/0/+25/+50, the labels -50 and +50 and the needle at the cents value, green within
//   the in-tune band of the strip rules, else the warning colour.
// Does not own: the cents value (the strip tuner sets it) or the in-tune band (LiveStripRules).
// Tests: TabForge.Avalonia.Tests.LiveStripSliderTests, LiveTunerUiTests.
// Thread: UI thread. A new value only repaints; the size never changes and a repaint allocates nothing new (text is shaped once and cached by the draw context).
/// <summary>A -50..+50 cents scale. <see cref="Cents"/> is NaN when nothing is heard (no needle).</summary>
public sealed class LiveCentsBar : Control
{
    private const double NeedleWidth = 3, NeedleHeight = 9, RailY = 10, TickShort = 3, TickLong = 6, LabelY = 12, LabelSize = 8;
    private readonly AvaloniaDrawContext _draw = new();
    private double _cents = double.NaN;

    public LiveCentsBar() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public double Cents
    {
        get => _cents;
        set
        {
            if (value == _cents || double.IsNaN(value) && double.IsNaN(_cents)) return;
            _cents = value;
            InvalidateVisual();
        }
    }

    /// <summary>The palette token of the needle and the cents text for a value: green in tune, warning otherwise.</summary>
    public static string ToneToken(double cents) => LiveStripRules.IsInTune(cents) ? "Success" : "Warning";

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width < NeedleWidth * 2) return;
        var ink = AvaloniaPalette.InkOf(ActualThemeVariant == ThemeVariant.Light);
        var d = _draw.Use(context);
        var muted = ink("TextMuted");
        var line = new UiStroke(muted, 1);
        d.Line(line, TunerPresenter.NeedleX(-50, width), RailY, TunerPresenter.NeedleX(50, width), RailY);
        for (var tick = -50.0; tick <= 50; tick += 25)
        {
            var x = TunerPresenter.NeedleX(tick, width);
            d.Line(tick == 0 ? new UiStroke(ink("TextPrimary"), 1) : line, x, RailY, x, RailY + (tick == 0 ? TickLong : TickShort));
        }
        DrawLabel(d, "-50", muted, TunerPresenter.NeedleX(-50, width));
        DrawLabel(d, "+50", muted, TunerPresenter.NeedleX(50, width));
        if (double.IsNaN(_cents)) return;
        d.Rect(ink(ToneToken(_cents)), null, TunerPresenter.NeedleX(_cents, width) - NeedleWidth / 2, 0, NeedleWidth, NeedleHeight, 1);
    }

    private void DrawLabel(AvaloniaDrawContext d, string text, UiColor colour, double centreX)
    {
        var label = d.Text(text, ChromeMetrics.UiFontFamily, LabelSize, colour, d.PixelsPerDip);
        d.DrawText(label, Math.Clamp(centreX - label.Width / 2, 0, Math.Max(0, Bounds.Width - label.Width)), LabelY);
    }
}
