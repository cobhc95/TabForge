using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// A small rotary knob. Drag up/right to increase, down/left to decrease (Shift for fine steps),
/// use the mouse wheel, or double-click to return to <see cref="DefaultValue"/>.
/// The arc is drawn from <see cref="Origin"/>, so a pan knob fills out from the centre.
/// </summary>
public sealed class KnobControl : FrameworkElement
{
    private const double SweepDegrees = 270;
    private Point _dragStart;
    private double _dragStartValue, _lastAngle = double.NaN;
    private bool _dragging;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(KnobControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((KnobControl)d).OnValueChanged((double)e.OldValue, (double)e.NewValue),
            (d, v) => ((KnobControl)d).Clamp((double)v)));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 127;
    public double DefaultValue { get; set; }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer, System.Windows.Automation.Provider.IRangeValueProvider
    {
        private readonly KnobControl _k;
        public Peer(KnobControl k) : base(k) { _k = k; }
        protected override string GetClassNameCore() => "KnobControl";
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Slider;
        public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface p) => p == System.Windows.Automation.Peers.PatternInterface.RangeValue ? this : base.GetPattern(p);
        public double Value => _k.Value;
        public bool IsReadOnly => !_k.IsEnabled;
        public double Maximum => _k.Maximum;
        public double Minimum => _k.Minimum;
        public double LargeChange => Math.Max(1, (_k.Maximum - _k.Minimum) / 10);
        public double SmallChange => 1;
        public void SetValue(double value) { if (_k.IsEnabled) _k.Value = value; }
    }
    /// <summary>Value the coloured arc starts from (Minimum for volume, centre for pan).</summary>
    public double Origin { get; set; }
    /// <summary>Formats the tooltip value text.</summary>
    public Func<double, string>? Format { get; set; }
    public string Label { get; set; } = "";

    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;
    /// <summary>Raised when a drag gesture starts / ends so callers can group undo.</summary>
    public event EventHandler? EditStarted;
    public event EventHandler? EditEnded;

    public KnobControl()
    {
        Focusable = true;
        Width = 24; Height = 24;
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTip = "";
        ToolTipOpening += (_, _) => ToolTip = ToolTipText();
    }

    private double Clamp(double v) => double.IsFinite(v) ? Math.Clamp(Math.Round(v), Minimum, Maximum) : Minimum;

    private void OnValueChanged(double oldValue, double newValue)
    {
        ToolTip = ToolTipText();
        ValueChanged?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(oldValue, newValue));
    }

    private string ToolTipText() => $"{Label}: {(Format?.Invoke(Value) ?? Value.ToString(CultureInfo.InvariantCulture))}" +
                                    "\nDrag or scroll to change · double-click to reset · right-click for options";

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { Value = DefaultValue; e.Handled = true; return; }
        Focus();
        _dragStart = e.GetPosition(this);
        _dragStartValue = Value;
        _lastAngle = double.NaN;
        _dragging = CaptureMouse();
        if (_dragging) EditStarted?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y));
        if (radius >= 8)
        {
            // Circular drag: clockwise around the knob raises the value (screen Y points down, so atan2 grows clockwise).
            var angle = Math.Atan2(p.Y - c.Y, p.X - c.X);
            if (double.IsNaN(_lastAngle)) _lastAngle = angle;
            var d = angle - _lastAngle;
            if (d > Math.PI) d -= 2 * Math.PI; else if (d < -Math.PI) d += 2 * Math.PI;
            _lastAngle = angle;
            _dragStartValue += d / (Math.PI * 1.5) * (Maximum - Minimum) * (fine ? 0.25 : 1);
            _dragStartValue = Math.Clamp(_dragStartValue, Minimum, Maximum);
            _dragStart = p;
        }
        else
        {
            // Near the centre the angle is unstable: drag up / right raises the value.
            _lastAngle = double.NaN;
            var travel = (_dragStart.Y - p.Y) + (p.X - _dragStart.X);
            _dragStartValue += travel * (Maximum - Minimum) / (fine ? 600 : 150);
            _dragStartValue = Math.Clamp(_dragStartValue, Minimum, Maximum);
            _dragStart = p;
        }
        Value = _dragStartValue;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        EditEnded?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : Math.Max(1, (Maximum - Minimum) / 32);
        StepEdit(() => Value += Math.Sign(e.Delta) * step);
        e.Handled = true;
    }

    private System.Windows.Threading.DispatcherTimer? _stepEditTimer;

    /// <summary>Groups a burst of wheel / key steps into one EditStarted..EditEnded gesture, like a drag.</summary>
    private void StepEdit(Action change)
    {
        if (_dragging) { change(); return; }
        if (_stepEditTimer is null)
        {
            _stepEditTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _stepEditTimer.Tick += (_, _) => { _stepEditTimer!.Stop(); EditEnded?.Invoke(this, EventArgs.Empty); };
        }
        if (!_stepEditTimer.IsEnabled) EditStarted?.Invoke(this, EventArgs.Empty);
        change();
        _stepEditTimer.Stop();
        _stepEditTimer.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : Math.Max(1, (Maximum - Minimum) / 32);
        switch (e.Key)
        {
            case Key.Up: case Key.Right: StepEdit(() => Value += step); e.Handled = true; break;
            case Key.Down: case Key.Left: StepEdit(() => Value -= step); e.Handled = true; break;
            case Key.Home: Value = DefaultValue; e.Handled = true; break;
        }
    }

    private double AngleOf(double value)
    {
        var t = Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0;
        return -SweepDegrees / 2 + t * SweepDegrees; // 0° = straight up
    }

    private static Point OnCircle(Point c, double r, double degrees)
    {
        var rad = (degrees - 90) * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }

    private static Geometry Arc(Point c, double r, double fromDeg, double toDeg)
    {
        if (toDeg < fromDeg) (fromDeg, toDeg) = (toDeg, fromDeg);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(OnCircle(c, r, fromDeg), false, false);
            ctx.ArcTo(OnCircle(c, r, toDeg), new Size(r, r), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 4) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = size / 2 - 2.5;
        var track = TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray;
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var face = TryFindResource("Panel3Brush") as Brush ?? Brushes.Black;
        var ink = TryFindResource("TextBrush") as Brush ?? Brushes.White;

        // Soft accent glow while hovered, stronger while turning.
        if ((_dragging || IsMouseOver) && accent is SolidColorBrush glowSource)
        {
            var g = glowSource.Color;
            var glow = new RadialGradientBrush(Color.FromArgb((byte)(_dragging ? 110 : 60), g.R, g.G, g.B), Color.FromArgb(0, g.R, g.G, g.B));
            dc.DrawEllipse(glow, null, c, r + 3, r + 3);
        }
        dc.DrawGeometry(null, new Pen(track, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            Arc(c, r, -SweepDegrees / 2, SweepDegrees / 2));
        var from = AngleOf(Math.Clamp(Origin, Minimum, Maximum));
        var to = AngleOf(Value);
        if (Math.Abs(to - from) > 0.5)
            dc.DrawGeometry(null, new Pen(accent, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Arc(c, r, from, to));
        dc.DrawEllipse(face, new Pen(IsKeyboardFocused ? accent : track, 1), c, r - 3.2, r - 3.2);
        dc.DrawLine(new Pen(ink, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            OnCircle(c, (r - 3.2) * 0.25, to), OnCircle(c, r - 4.6, to));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
}
