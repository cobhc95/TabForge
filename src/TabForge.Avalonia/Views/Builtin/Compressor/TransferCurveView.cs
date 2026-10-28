using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabForge.AudioEngine.Builtin.Compressor;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the compressor transfer curve: output level against input level from -60 to 0 dB (the grid every 10 dB, the 1:1 line, the threshold mark, the curve from
//   CompressorCurve, the same function as the sound) and a dot at the input level the compressor hears now. It repaints only when the settings changed or the dot
//   moved by half a dB.
// Does not own: polling the engine (CompressorEditor), the gain computer (CompressorCurve).
// Tests: BuiltinEditorWindowTests.
public sealed class TransferCurveView : Control
{
    private const double FloorDb = -60, DotStepDb = 0.5, GridStepDb = 10, CurveStepDb = 1;
    private double _threshold = -18, _ratio = 4, _knee = 6, _inputDb = double.NegativeInfinity;
    private StreamGeometry? _curve;
    private Size _built;

    public TransferCurveView()
    {
        Name = "CompressorCurve";
        MinWidth = MinHeight = DialogMetrics.FxTransferMinSize;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Shows the curve of the current settings; repaints only when one of them changed.</summary>
    public void SetCurve(double threshold, double ratio, double knee)
    {
        if (threshold == _threshold && ratio == _ratio && knee == _knee) return;
        (_threshold, _ratio, _knee) = (threshold, ratio, knee);
        _built = default;
        InvalidateVisual();
    }

    /// <summary>Shows where the input level is now (below -60 dB: no dot).</summary>
    public void SetLevel(double inputDb)
    {
        var shown = inputDb <= FloorDb ? double.NegativeInfinity : Math.Round(inputDb / DotStepDb) * DotStepDb;
        if (shown.Equals(_inputDb)) return;
        _inputDb = shown;
        InvalidateVisual();
    }

    private Rect Plot
    {
        get
        {
            var side = Math.Max(1, Math.Min(Bounds.Width - DialogMetrics.FxPlotLeft - DialogMetrics.FxPlotRight, Bounds.Height - DialogMetrics.FxPlotTop - DialogMetrics.FxPlotBottom));
            return new Rect(DialogMetrics.FxPlotLeft, DialogMetrics.FxPlotTop, side, side);
        }
    }

    private Point At(double inDb, double outDb)
    {
        var plot = Plot;
        return new Point(plot.X + plot.Width * (Math.Clamp(inDb, FloorDb, 0) - FloorDb) / -FloorDb, plot.Y + plot.Height * (0 - Math.Clamp(outDb, FloorDb, 0)) / -FloorDb);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 40 || Bounds.Height < 40) return;
        var plot = Plot;
        var grid = new Pen(BuiltinPaint.Theme(this, "BorderSoftBrush"), DialogMetrics.FxGridStroke);
        var muted = BuiltinPaint.Theme(this, "TextMuted");
        context.FillRectangle(BuiltinPaint.Theme(this, "WindowBackground"), plot);
        for (var db = FloorDb; db <= 0; db += GridStepDb)
        {
            context.DrawLine(grid, At(db, FloorDb), At(db, 0));
            context.DrawLine(grid, At(FloorDb, db), At(0, db));
            var text = BuiltinPaint.Label(db.ToString("0", CultureInfo.InvariantCulture), muted);
            context.DrawText(text, new Point(At(db, FloorDb).X - text.Width / 2, plot.Bottom + DialogMetrics.FxLabelInset / 2));
            context.DrawText(text, new Point(plot.Left - text.Width - DialogMetrics.FxLabelInset, At(FloorDb, db).Y - text.Height / 2));
        }
        context.DrawRectangle(grid, plot);
        var unity = new Pen(muted, DialogMetrics.FxGridStroke);
        var mark = new Pen(BuiltinPaint.Palette(this, "Warning"), DialogMetrics.FxMarkerStroke);
        context.DrawLine(unity, At(FloorDb, FloorDb), At(0, 0));
        context.DrawLine(mark, At(_threshold, FloorDb), At(_threshold, 0));
        if (_built != Bounds.Size) Build();
        if (_curve is not null) context.DrawGeometry(null, new Pen(BuiltinPaint.Theme(this, "AccentBrush"), DialogMetrics.FxCurveStroke), _curve);
        if (!double.IsNegativeInfinity(_inputDb))
        {
            var dot = At(_inputDb, CompressorCurve.OutputDb(_inputDb, _threshold, _ratio, _knee));
            context.DrawEllipse(BuiltinPaint.Palette(this, "Success"), new Pen(BuiltinPaint.Theme(this, "TextPrimary"), DialogMetrics.FxGridStroke), dot, DialogMetrics.FxDotRadius, DialogMetrics.FxDotRadius);
        }
    }

    private void Build()
    {
        _built = Bounds.Size;
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            for (var db = FloorDb; db <= 0; db += CurveStepDb)
            {
                var point = At(db, CompressorCurve.OutputDb(db, _threshold, _ratio, _knee));
                if (db == FloorDb) g.BeginFigure(point, false); else g.LineTo(point);
            }
            g.EndFigure(false);
        }
        _curve = curve;
    }
}
