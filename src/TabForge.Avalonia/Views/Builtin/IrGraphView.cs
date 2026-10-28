using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.AudioEngine.Builtin.IrLoader;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the IR Loader window's picture: the impulse response's waveform (top) and its frequency-response shape from 20 Hz to 20 kHz (bottom, the loudest
//   frequency at 0 dB down to -54 dB, with the 0 dB line and the 100 Hz / 1 kHz / 10 kHz marks), or the drop hint while there is no IR. Geometry is rebuilt only when the data or the size changes.
// Does not own: computing the data (IrPictures, on a worker in IrLoaderEditor), the window around it.
// Tests: BuiltinEffectTests (shots).
public sealed class IrGraphView : Control
{
    private const double TopDb = 6, BottomDb = -54;
    private float[] _peaks = Array.Empty<float>();
    private float[] _curve = Array.Empty<float>();
    private string _hint = "Drop a .wav impulse response here, or choose one above";
    private Size _built;
    private StreamGeometry? _wave, _response;

    /// <summary>Shows a new IR (waveform peaks and response curve in dB); empty arrays show <paramref name="hint"/> instead.</summary>
    public void SetData(float[] peaks, float[] curve, string hint)
    {
        (_peaks, _curve, _hint) = (peaks, curve, hint);
        _built = default;
        InvalidateVisual();
    }

    public bool HasData => _peaks.Length > 0;

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : AvaloniaPalette.TransparentBrush;

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 4 || size.Height < 4) return;
        context.FillRectangle(Brush("WindowBackground"), new Rect(size));
        var grid = new Pen(Brush("BorderSoftBrush"), DialogMetrics.IrGridStroke);
        context.DrawRectangle(grid, new Rect(size).Deflate(DialogMetrics.IrGridStroke / 2));
        if (_peaks.Length == 0)
        {
            var text = Label(_hint, Brush("TextMuted"));
            context.DrawText(text, new Point((size.Width - text.Width) / 2, (size.Height - text.Height) / 2));
            return;
        }
        if (_built != size) Build(size);
        var half = size.Height * 0.4;
        context.DrawLine(grid, new Point(0, half), new Point(size.Width, half));
        var zero = half + (size.Height - half) * (TopDb / (TopDb - BottomDb));
        context.DrawLine(grid, new Point(0, zero), new Point(size.Width, zero));
        foreach (var hz in new[] { 100.0, 1000.0, 10000.0 })
        {
            var x = Math.Log(hz / IrPictures.LowHz) / Math.Log(IrPictures.HighHz / IrPictures.LowHz) * size.Width;
            context.DrawLine(grid, new Point(x, half), new Point(x, size.Height));
            context.DrawText(Label(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", Brush("TextMuted")), new Point(x + 3, size.Height - DialogMetrics.FontSize - 4));
        }
        context.DrawText(Label("0 dB", Brush("TextMuted")), new Point(3, zero + 1));
        if (_wave is not null) context.DrawGeometry(Brush("AccentSoftBrush"), new Pen(Brush("AccentBrush"), DialogMetrics.IrGridStroke), _wave);
        if (_response is not null) context.DrawGeometry(null, new Pen(Brush("AccentBrush"), DialogMetrics.IrCurveStroke), _response);
    }

    private void Build(Size size)
    {
        _built = size;
        var half = size.Height * 0.4;
        var mid = half / 2;
        var peak = 0f;
        foreach (var p in _peaks) peak = Math.Max(peak, p);
        var scale = peak > 0 ? (mid - 4) / peak : 0;
        _wave = new StreamGeometry();
        using (var g = _wave.Open())
        {
            g.BeginFigure(new Point(0, mid), true);
            for (var i = 0; i < _peaks.Length; i++) g.LineTo(new Point(X(i, _peaks.Length, size.Width), mid - _peaks[i] * scale));
            for (var i = _peaks.Length - 1; i >= 0; i--) g.LineTo(new Point(X(i, _peaks.Length, size.Width), mid + _peaks[i] * scale));
            g.EndFigure(true);
        }
        _response = null;
        if (_curve.Length < 2) return;
        _response = new StreamGeometry();
        using var r = _response.Open();
        for (var i = 0; i < _curve.Length; i++)
        {
            var db = Math.Clamp(_curve[i], BottomDb, TopDb);
            var y = half + (size.Height - half) * ((TopDb - db) / (TopDb - BottomDb));
            var point = new Point(X(i, _curve.Length, size.Width), y);
            if (i == 0) r.BeginFigure(point, false); else r.LineTo(point);
        }
        r.EndFigure(false);
    }

    private static double X(int i, int count, double width) => count <= 1 ? 0 : i * width / (count - 1);

    private FormattedText Label(string text, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, DialogMetrics.FontSize - 2, brush);
}
