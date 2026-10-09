using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views.EffectEditors;

// Owns: drawing one EffectCurve on a time/pitch grid and turning mouse and key input into curve edits.
// Does not own: the point rules (EffectCurve), presets, or applying the curve to notes.
// Tests: TestEffectCurveMath (geometry and edits), checked in the running app for looks.
/// <summary>
/// Time runs across, pitch up the side in quarter-tone steps (a solid line per semitone, labelled per whole tone). Drag a point to move it,
/// click empty grid to add one, right-click or double-click a point to remove it. Arrow keys move the selected point, Delete removes it.
/// It draws only when the curve changes; there is no per-frame work.
/// </summary>
internal sealed class EffectCurveEditor : FrameworkElement
{
    private const double HitRadius = 9;
    private const double LeftMargin = 40, OtherMargin = 12;
    private int _selected = -1;
    private bool _dragging;

    public EffectCurveEditor(EffectCurve curve)
    {
        Curve = curve;
        Focusable = true;
        Width = 560;
        Height = curve.MaxValue - curve.MinValue > 8 ? 360 : 240;
        System.Windows.Automation.AutomationProperties.SetName(this, "Effect curve editor");
        ToolTip = "Drag a point to move it. Click to add a point. Right-click or double-click a point to remove it.";
    }

    public EffectCurve Curve { get; }
    public event EventHandler? CurveChanged;

    /// <summary>The grid rectangle inside the control.</summary>
    internal Rect PlotRect(Size size) => new(LeftMargin, OtherMargin, Math.Max(1, size.Width - LeftMargin - OtherMargin), Math.Max(1, size.Height - 2 * OtherMargin));

    internal Point ToPixel(Rect plot, double offset, double value) => new(
        plot.Left + offset / EffectCurve.Span * plot.Width,
        plot.Bottom - (value - Curve.MinValue) / (Curve.MaxValue - Curve.MinValue) * plot.Height);

    internal (double Offset, double Value) FromPixel(Rect plot, Point p) => (
        (p.X - plot.Left) / plot.Width * EffectCurve.Span,
        Curve.MinValue + (plot.Bottom - p.Y) / plot.Height * (Curve.MaxValue - Curve.MinValue));

    /// <summary>The index of the point under a pixel position, or -1.</summary>
    internal int HitPoint(Rect plot, Point p)
    {
        var best = -1;
        var bestDistance = HitRadius;
        for (var i = 0; i < Curve.Points.Count; i++)
        {
            var d = (ToPixel(plot, Curve.Points[i].Offset, Curve.Points[i].Value) - p).Length;
            if (d <= bestDistance) { best = i; bestDistance = d; }
        }
        return best;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var plot = PlotRect(new Size(ActualWidth, ActualHeight));
        var text = (Brush)FindResource("TextBrush");
        var muted = (Brush)FindResource("MutedBrush");
        var accent = (Brush)FindResource("AccentBrush");
        var soft = new Pen((Brush)FindResource("BorderSoftBrush"), 1) { DashStyle = DashStyles.Dot };
        var strong = new Pen((Brush)FindResource("BorderBrush"), 1);
        dc.DrawRectangle((Brush)FindResource("Panel2Brush"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var v = Curve.MinValue; v <= Curve.MaxValue; v++)
        {
            var y = ToPixel(plot, 0, v).Y;
            var semitone = v % 2 == 0;
            dc.DrawLine(semitone ? strong : soft, new Point(plot.Left, y), new Point(plot.Right, y));
            if (semitone)
                dc.DrawText(new FormattedText(Label(v), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, v == 0 ? text : muted, dpi),
                    new Point(plot.Left - 6 - 28, y - 7));
        }
        for (var c = 0; c <= EffectCurve.Columns; c++)
        {
            var x = plot.Left + c * plot.Width / EffectCurve.Columns;
            dc.DrawLine(c % 3 == 0 ? strong : soft, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        var pts = Curve.Points;
        var line = new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round };
        for (var i = 1; i < pts.Count; i++) dc.DrawLine(line, ToPixel(plot, pts[i - 1].Offset, pts[i - 1].Value), ToPixel(plot, pts[i].Offset, pts[i].Value));
        for (var i = 0; i < pts.Count; i++)
        {
            var centre = ToPixel(plot, pts[i].Offset, pts[i].Value);
            dc.DrawRectangle(i == _selected ? accent : text, strong, new Rect(centre.X - 5, centre.Y - 5, 10, 10));
        }
        if (IsKeyboardFocused) dc.DrawRectangle(null, new Pen(accent, 1) { DashStyle = DashStyles.Dash }, new Rect(1, 1, ActualWidth - 2, ActualHeight - 2));
    }

    /// <summary>Axis text in semitones (a quarter-tone unit is half a semitone): +1, 0, -2...</summary>
    internal static string Label(double value)
    {
        var semitones = value / 2;
        return semitones > 0 ? "+" + semitones.ToString("0.#", CultureInfo.InvariantCulture) : semitones.ToString("0.#", CultureInfo.InvariantCulture);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var plot = PlotRect(new Size(ActualWidth, ActualHeight));
        var position = e.GetPosition(this);
        var hit = HitPoint(plot, position);
        if (hit >= 0 && e.ClickCount == 2) { Remove(hit); e.Handled = true; return; }
        if (hit < 0)
        {
            if (!plot.Contains(position)) return;
            var (offset, value) = FromPixel(plot, position);
            hit = Curve.Add(offset, value);
            Changed();
        }
        _selected = hit;
        _dragging = true;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || _selected < 0) return;
        var (offset, value) = FromPixel(PlotRect(new Size(ActualWidth, ActualHeight)), e.GetPosition(this));
        Curve.Move(_selected, offset, value);
        Changed();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var hit = HitPoint(PlotRect(new Size(ActualWidth, ActualHeight)), e.GetPosition(this));
        if (hit >= 0) Remove(hit);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_selected < 0 || _selected >= Curve.Points.Count) { if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right) _selected = 0; else return; }
        var p = Curve.Points[_selected];
        switch (e.Key)
        {
            case Key.Up: Curve.Move(_selected, p.Offset, p.Value + 1); break;
            case Key.Down: Curve.Move(_selected, p.Offset, p.Value - 1); break;
            case Key.Left: Curve.Move(_selected, p.Offset - EffectCurve.TimeStep, p.Value); break;
            case Key.Right: Curve.Move(_selected, p.Offset + EffectCurve.TimeStep, p.Value); break;
            case Key.Delete: Remove(_selected); e.Handled = true; return;
            default: return;
        }
        e.Handled = true;
        Changed();
    }

    private void Remove(int index)
    {
        if (!Curve.RemoveAt(index)) return;
        _selected = -1;
        _dragging = false;
        Changed();
    }

    /// <summary>The curve was replaced from outside (a preset was picked).</summary>
    public void Refresh()
    {
        _selected = -1;
        InvalidateVisual();
    }

    private void Changed()
    {
        InvalidateVisual();
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
}
