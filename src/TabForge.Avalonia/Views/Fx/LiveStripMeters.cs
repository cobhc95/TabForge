using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.LiveInput;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the Input and Output meters of the live input strip: two thin bars stacked in one drawn control (no text), the peak hold of the input, and the dB tooltip.
// Does not own: where the peaks come from (the engine), the dB maths (LiveStripRules), the hold rule (PeakHold) or the frame loop (LiveInputStrip).
// Tests: TabForge.Avalonia.Tests.LiveInputStripTests.
// Thread: UI thread. A frame only repaints (InvalidateVisual) and only when a bar moved; the tooltip text changes only while the pointer is over the meters, a few times a second.
/// <summary>Two stacked bars: Input (raw live input before the chain, with a peak-hold mark) above Output (after the chain, volume and pan).</summary>
public sealed class LiveStripMeters : Control
{
    private const double BarGap = 2;
    private readonly AvaloniaDrawContext _draw = new();
    private readonly PeakHold _hold = new();
    private double _inFraction, _outFraction, _holdFraction = double.NaN, _inDb = LiveStripRules.MeterFloorDb, _outDb = LiveStripRules.MeterFloorDb, _tipAt = double.NegativeInfinity;
    private MeterTone _inTone, _outTone;

    public LiveStripMeters()
    {
        Name = "LiveMeters";
        Width = ChromeMetrics.LiveStripMeterWidth;
        Height = ChromeMetrics.LiveStripMeterBar * 2 + BarGap;
        VerticalAlignment = VerticalAlignment.Center;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ToolTip.SetTip(this, Tip());
    }

    public double InputFraction => _inFraction;
    public double OutputFraction => _outFraction;
    public double InputHold => _holdFraction;
    public MeterTone InputTone => _inTone;
    public MeterTone OutputTone => _outTone;

    /// <summary>Shows one frame of levels (linear peaks, 0..1+) at <paramref name="nowMs"/>; allocates nothing and repaints only when a bar moved.</summary>
    public void Update(double inputPeak, double outputPeak, double nowMs)
    {
        _inDb = LiveStripRules.PeakToDb(inputPeak);
        var holdDb = LiveStripRules.PeakToDb(_hold.Update(inputPeak, nowMs));
        _outDb = LiveStripRules.PeakToDb(outputPeak);
        var inFraction = LiveStripRules.MeterFraction(_inDb);
        var outFraction = LiveStripRules.MeterFraction(_outDb);
        var hold = LiveStripRules.MeterFraction(holdDb);
        var inTone = LiveStripRules.ToneForDb(_inDb);
        var outTone = LiveStripRules.ToneForDb(_outDb);
        if (inFraction != _inFraction || outFraction != _outFraction || hold != _holdFraction || inTone != _inTone || outTone != _outTone)
        {
            _inFraction = inFraction; _outFraction = outFraction; _holdFraction = hold; _inTone = inTone; _outTone = outTone;
            InvalidateVisual();
        }
        if (IsPointerOver && nowMs - _tipAt >= ChromeMetrics.LiveStripTextIntervalMs) { _tipAt = nowMs; ToolTip.SetTip(this, Tip()); }
    }

    /// <summary>"Input -6 dB, Output -12 dB": the levels the bars show now.</summary>
    public string Tip() => $"Input {LiveStripRules.DbText(_inDb)}, Output {LiveStripRules.DbText(_outDb)}";

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width < 1) return;
        var ink = AvaloniaPalette.InkOf(ActualThemeVariant == ThemeVariant.Light);
        var ctx = _draw.Use(context);
        Bar(ctx, ink, 0, width, _inFraction, _inTone, _holdFraction);
        Bar(ctx, ink, ChromeMetrics.LiveStripMeterBar + BarGap, width, _outFraction, _outTone, double.NaN);
    }

    private static void Bar(IDrawContext ctx, System.Func<string, UiColor> ink, double y, double width, double fraction, MeterTone tone, double hold)
    {
        var bar = ChromeMetrics.LiveStripMeterBar;
        ctx.Rect(ink("TrackSliderTrack"), new UiStroke(ink("TrackSliderTrackBorder"), 1), 0, y, width, bar, 2);
        if (fraction > 0) ctx.Rect(ink(tone == MeterTone.Clip ? "Danger" : tone == MeterTone.Warm ? "Warning" : "Success"), null, 0, y, width * fraction, bar, 2);
        if (!double.IsNaN(hold)) ctx.Rect(ink("Danger"), null, System.Math.Min(width - 2, width * hold), y, 2, bar);
    }
}
