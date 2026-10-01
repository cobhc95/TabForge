using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// Horizontal input level meter for an armed track (it shows the input level on armed tracks even
/// when not recording). Peak with a short hold; drawn in code, repainted only when the value changes.
/// Colours come from the theme (MeterBrush, MeterHotBrush, DangerBrush).
/// </summary>
public sealed class InputMeter : FrameworkElement
{
    private double _level, _hold;
    private long _holdUntil;
    private bool _midi;

    public InputMeter()
    {
        Height = 8; Width = 120;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>Peak 0..1 (MIDI: velocity / 127). Cheap to call every frame; repaints only on a visible change.</summary>
    public void Show(double peak, bool midi)
    {
        var now = Environment.TickCount64;
        var level = Math.Clamp(peak, 0, 1.2);
        // Fast attack, ~20 dB/s fall, as a DAW meter.
        var fallen = Math.Max(level, _level * 0.82);
        if (level >= _hold || now > _holdUntil) { _hold = level; _holdUntil = now + 900; }
        if (Math.Abs(fallen - _level) < 0.004 && midi == _midi && now <= _holdUntil) return;
        _level = fallen < 0.002 ? 0 : fallen;
        _midi = midi;
        InvalidateVisual();
    }

    /// <summary>Meter position of a linear peak: dB mapped over -60..+6 dB.</summary>
    private static double Position(double peak) =>
        peak <= 0.001 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 66, 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("InputMeter"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "InputMeter", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var back = TryFindResource("Panel3Brush") as Brush ?? Brushes.Black;
        var green = TryFindResource("MeterBrush") as Brush ?? Brushes.LimeGreen;
        var amber = TryFindResource("MeterHotBrush") as Brush ?? Brushes.Gold;
        var red = TryFindResource("DangerBrush") as Brush ?? Brushes.Red;
        dc.DrawRoundedRectangle(back, null, new Rect(0, 0, w, h), 2, 2);
        var x = Position(_level) * w;
        var hot = Position(0.5) * w;      // -6 dB
        var clip = Position(1.0) * w;     // 0 dB
        if (x > 0) dc.DrawRectangle(green, null, new Rect(0, 1, Math.Min(x, hot), h - 2));
        if (x > hot) dc.DrawRectangle(amber, null, new Rect(hot, 1, Math.Min(x, clip) - hot, h - 2));
        if (x > clip) dc.DrawRectangle(red, null, new Rect(clip, 1, x - clip, h - 2));
        var hx = Position(_hold) * w;
        if (hx > 1) dc.DrawRectangle(_hold >= 1 ? red : _midi ? amber : green, null, new Rect(Math.Min(hx, w - 2), 0, 2, h));
    }
}

/// <summary>
/// Live-monitoring switch of an armed track (record monitoring): lit = you hear the input through the track
/// while recording. On by default. Drawn in code with theme colours.
/// </summary>
public sealed class MonitorButton : FrameworkElement
{
    private bool _hover;
    private bool _on = true;
    public bool On { get => _on; set { _on = value; InvalidateVisual(); } }
    public event EventHandler? Toggled;

    public MonitorButton()
    {
        Width = 24; Height = 22;
        ToolTip = "Monitor: hear the input live through this track while it is armed (on by default). Off: it is still recorded and metered, just not played.";
        System.Windows.Automation.AutomationProperties.SetName(this, "Live monitoring");
    }

    protected override void OnMouseEnter(MouseEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = false; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = true;
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { e.Handled = true; Toggled?.Invoke(this, EventArgs.Empty); }

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("MonitorButton"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "MonitorButton", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
    {
        var accent =TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var border = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        var muted = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var strong = TryFindResource("TextStrongBrush") as Brush ?? Brushes.White;
        var box = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1);
        dc.DrawRoundedRectangle(_on ? accent : Brushes.Transparent, new Pen(_hover ? accent : border, 1), box, 4, 4);
        var ink = _on ? strong : muted;
        var pen = new Pen(ink, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var cx = box.X + box.Width / 2 - 2; var cy = box.Y + box.Height / 2;
        // Speaker: a small box and cone, with sound waves when on and a slash when off.
        var cone = new StreamGeometry();
        using (var c = cone.Open())
        {
            c.BeginFigure(new Point(cx - 6, cy - 2.5), true, true);
            c.LineTo(new Point(cx - 3, cy - 2.5), true, false);
            c.LineTo(new Point(cx + 1, cy - 6), true, false);
            c.LineTo(new Point(cx + 1, cy + 6), true, false);
            c.LineTo(new Point(cx - 3, cy + 2.5), true, false);
            c.LineTo(new Point(cx - 6, cy + 2.5), true, false);
        }
        cone.Freeze();
        dc.DrawGeometry(ink, null, cone);
        if (_on)
        {
            dc.DrawArc(pen, cx + 3, cy, 3);
            dc.DrawArc(pen, cx + 3, cy, 6);
        }
        else dc.DrawLine(new Pen(ink, 1.6), new Point(cx - 6, cy + 7), new Point(cx + 8, cy - 7));
    }
}

internal static class DrawingExtensions
{
    /// <summary>A right-facing arc (sound wave) of the given radius around (cx, cy).</summary>
    public static void DrawArc(this DrawingContext dc, Pen pen, double cx, double cy, double radius)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx + radius * 0.5, cy - radius * 0.87), false, false);
            c.ArcTo(new Point(cx + radius * 0.5, cy + radius * 0.87), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
