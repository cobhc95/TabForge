using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// A small rotary knob. Drag up/right to increase, down/left to decrease (Shift for fine steps),
/// use the mouse wheel, Ctrl+click to return to <see cref="DefaultValue"/>, or double-click / right-click / F2 / Enter to type an exact value.
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
    /// <summary>Granularity of <see cref="Value"/> (1 = whole units, as before; 0.1 for tenths; 0 = none).</summary>
    public double Step { get; set; } = 1;
    /// <summary>Knob units per displayed unit (a knob that shows tenths of its value has 10), used to read a typed number.</summary>
    public double DisplayScale { get; set; } = 1;
    /// <summary>Optional: turns a typed displayed number into knob units (e.g. a 0-127 knob shown in percent). Default: number * <see cref="DisplayScale"/>.</summary>
    public Func<double, double>? FromDisplay { get; set; }
    /// <summary>Optional: reads the whole typed text into knob units, or null when it is not valid (e.g. pan "L 20"). Default: <see cref="KnobValueParser.TryParse"/>.</summary>
    public Func<string, double?>? Parse { get; set; }

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
        ContextMenuOpening += (_, _) => PrepareMenu();
        // The editor popup is not in the visual tree: close it (keeping the old value) when the knob goes away or is hidden.
        Unloaded += (_, _) => FinishEdit(false);
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) FinishEdit(false); };
        ToolTip = "";
        ToolTipOpening += (_, _) => ToolTip = ToolTipText();
    }

    private double Clamp(double v)
    {
        if (!double.IsFinite(v)) return double.IsPositiveInfinity(v) ? Maximum : Minimum;
        var c = Math.Clamp(v, Minimum, Maximum);
        return Step > 0 ? Math.Clamp(Math.Round(Math.Round(c / Step) * Step, 6), Minimum, Maximum) : c;
    }

    private void OnValueChanged(double oldValue, double newValue)
    {
        ToolTip = ToolTipText();
        ValueChanged?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(oldValue, newValue));
    }

    private string ToolTipText() => $"{Label}: {(Format?.Invoke(Value) ?? Value.ToString(CultureInfo.InvariantCulture))}" +
                                    "\nDrag or scroll to change · double-click or right-click to type a value · Ctrl+click to reset";

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _typeOnRelease = false;
        // Double-click: the editor opens on the release, so that release cannot land outside the (StaysOpen=false) popup and close it at once.
        if (e.ClickCount == 2) { _typeOnRelease = true; CaptureMouse(); e.Handled = true; return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Focus(); HandleLeftDown(1, true); e.Handled = true; return; }
        Focus();
        _dragStart = e.GetPosition(this);
        _dragStartValue = Value;
        _lastAngle = double.NaN;
        _dragging = CaptureMouse();
        if (_dragging) EditStarted?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>The click routes that are not a drag: double-click opens the value editor, Ctrl+click resets to the default.</summary>
    internal void HandleLeftDown(int clickCount, bool ctrl)
    {
        if (ctrl) SetAsEdit(DefaultValue);
        else if (clickCount == 2) BeginTypeValue();
    }

    private bool _typeOnRelease;

    /// <summary>Sets a value as one gesture (EditStarted, Value, EditEnded) like a drag, so undo, dirty flags and engine updates follow; no-op when unchanged.</summary>
    private bool SetAsEdit(double value)
    {
        var v = Clamp(value);
        if (v == Value) return false;
        EditStarted?.Invoke(this, EventArgs.Empty);
        Value = v;
        EditEnded?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Wheel / arrow step. A knob with a fine <see cref="Step"/> (FX Volume, 0.1 dB) keeps whole-unit coarse steps, as before.</summary>
    internal double CoarseStep(bool fine)
    {
        if (fine) return 1;
        var step = Math.Max(1, (Maximum - Minimum) / 32);
        return Step is > 0 and < 1 ? Math.Round(step) : step;
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
        if (_typeOnRelease)
        {
            _typeOnRelease = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            BeginTypeValue();
            e.Handled = true;
            return;
        }
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
        if (_editor is not null) return;
        var step = CoarseStep(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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
        if (_editor is not null) return;   // keys typed in the open editor are the editor's (a popup may route them through its placement target)
        var step = CoarseStep(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        switch (e.Key)
        {
            case Key.Up: case Key.Right: StepEdit(() => Value += step); e.Handled = true; break;
            case Key.Down: case Key.Left: StepEdit(() => Value -= step); e.Handled = true; break;
            case Key.Home: SetAsEdit(DefaultValue); e.Handled = true; break;
            case Key.F2: case Key.Enter: BeginTypeValue(); e.Handled = true; break;
        }
    }

    // ---------- type-in editor (created on demand only) ----------
    private Popup? _popup;
    private TextBox? _editor;
    private bool _editorDone;
    private const string TypeValueTag = "KnobTypeValue";

    /// <summary>True while the inline value editor is open.</summary>
    internal bool IsEditing => _editor is not null;
    internal TextBox? Editor => _editor;

    /// <summary>The current value as displayed (what the editor is pre-filled with).</summary>
    internal string DisplayText() => Format?.Invoke(Value) ?? Value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Reads typed text into a clamped, stepped knob value; false when it is not understood.</summary>
    internal bool TryParseInput(string text, out double knobValue)
    {
        knobValue = Value;
        double v;
        if (Parse is { } custom)
        {
            if (custom(text) is not { } r) return false;
            v = r;
        }
        else
        {
            if (!KnobValueParser.TryParse(text, out var shown)) return false;
            v = double.IsInfinity(shown) ? shown : FromDisplay is { } f ? f(shown) : shown * DisplayScale;
        }
        knobValue = Clamp(v);
        return true;
    }

    /// <summary>Applies typed text the way a drag does (EditStarted, Value, EditEnded), so undo and dirty flags follow.</summary>
    internal bool ApplyTyped(string text)
    {
        if (!TryParseInput(text, out var v)) return false;
        SetAsEdit(v);
        return true;
    }

    /// <summary>Opens the small inline editor on the knob: pre-filled with the displayed value, all selected, focused.</summary>
    public void BeginTypeValue()
    {
        if (!IsEnabled || _editor is not null) return;
        _editorDone = false;
        var box = new TextBox
        {
            Text = DisplayText(), MinWidth = 84, FontSize = 12, Padding = new Thickness(4, 2, 4, 2), BorderThickness = new Thickness(2),
            ToolTip = "Type a value, Enter to apply, Esc to cancel",
        };
        AutomationProperties.SetName(box, $"{(Label.Length > 0 ? Label : "Knob")} value");
        box.SetResourceReference(Control.BackgroundProperty, "Panel3Brush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        box.TextChanged += (_, _) => Mark(box);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; FinishEdit(true, keepOpenIfInvalid: true); }
            else if (e.Key == Key.Escape) { e.Handled = true; FinishEdit(false); }
        };
        box.LostKeyboardFocus += (_, _) => FinishEdit(true);
        _editor = box;
        _popup = new Popup { Child = box, PlacementTarget = this, Placement = PlacementMode.Center, StaysOpen = false, AllowsTransparency = false };
        _popup.Closed += (_, _) => FinishEdit(true);
        try { _popup.IsOpen = true; } catch (InvalidOperationException) { }
        Dispatcher.BeginInvoke(new Action(() => { if (_editor == box) { box.Focus(); Keyboard.Focus(box); box.SelectAll(); } }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Mark(TextBox box)
    {
        var ok = TryParseInput(box.Text, out _);
        if (ok) box.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        else box.BorderBrush = TryFindResource("DangerBrush") as Brush ?? Brushes.Red;
        box.ToolTip = ok ? "Type a value, Enter to apply, Esc to cancel" : "Not a valid value";
    }

    /// <summary>Closes the editor: apply (Enter or click away; an invalid entry keeps the old value) or cancel (Esc).</summary>
    internal void FinishEdit(bool apply, bool keepOpenIfInvalid = false)
    {
        if (_editor is not { } box || _editorDone) return;
        if (apply && keepOpenIfInvalid && !TryParseInput(box.Text, out _)) { Mark(box); return; }
        _editorDone = true;
        if (apply) ApplyTyped(box.Text);
        var popup = _popup;
        _editor = null; _popup = null;
        if (popup is { IsOpen: true }) popup.IsOpen = false;
        if (IsVisible) Focus();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (ContextMenu is not null) return;    // a knob with its own menu gets "Type value…" in it instead
        Focus();
        BeginTypeValue();
        e.Handled = true;
    }

    /// <summary>Adds "Type value…" as the first item of this knob's own context menu (once).</summary>
    internal void PrepareMenu()
    {
        if (ContextMenu is not { } menu || menu.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, TypeValueTag))) return;
        var item = new MenuItem { Header = "Type value…", Tag = TypeValueTag, InputGestureText = "Double-click" };
        if (TryFindResource(typeof(MenuItem)) is Style st) item.Style = st;
        item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(BeginTypeValue), System.Windows.Threading.DispatcherPriority.Input);
        menu.Items.Insert(0, item);
        menu.Items.Insert(1, new Separator { Style = TryFindResource(MenuItem.SeparatorStyleKey) as Style });
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
        try { RenderGuard.Inject("KnobControl"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "KnobControl", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
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

/// <summary>Reads typed knob values: plain numbers and the usual units ("2.1", "-6 dB", "75%", "2:1", "2.0:1", "100 ms"), comma or dot decimals.</summary>
public static class KnobValueParser
{
    private static readonly Regex Ratio = new(@"^([+-]?\d+(?:[.,]\d+)?)\s*:\s*(\d+(?:[.,]\d+)?)?$", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"^([+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+))\s*([a-z%]*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Infinity = new(@"^([+-]?)inf(?:inity)?(?:\s*db)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Units = new(StringComparer.OrdinalIgnoreCase) { "", "db", "%", "ms", "s", "x", "hz", "khz", "st", "bpm", "ct", "cents" };

    /// <summary>The displayed number (units dropped; a ratio a:b is a/b). "inf" and "-inf" give the infinities.</summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (text is null) return false;
        var t = text.Trim().Replace('−', '-');
        if (t.Length == 0) return false;
        var inf = Infinity.Match(t);
        if (inf.Success) { value = inf.Groups[1].Value == "-" ? double.NegativeInfinity : double.PositiveInfinity; return true; }
        var r = Ratio.Match(t);
        if (r.Success)
        {
            if (!D(r.Groups[1].Value, out var a)) return false;
            var b = 1.0;
            if (r.Groups[2].Success && (!D(r.Groups[2].Value, out b) || b == 0)) return false;
            value = a / b;
            return true;
        }
        var m = Number.Match(t);
        return m.Success && Units.Contains(m.Groups[2].Value) && D(m.Groups[1].Value, out value);
    }

    private static bool D(string s, out double v) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);

    /// <summary>Pan text to a 0-127 knob value (64 = centre): "Centre", "L 20", "R 5", or a signed offset such as -20 / +5.</summary>
    public static double? ParsePan(string? text)
    {
        if (text is null) return null;
        var t = text.Trim();
        if (t.Equals("centre", StringComparison.OrdinalIgnoreCase) || t.Equals("center", StringComparison.OrdinalIgnoreCase) || t.Equals("c", StringComparison.OrdinalIgnoreCase)) return 64;
        if (t.Length > 1 && t[0] is 'L' or 'l' or 'R' or 'r')
            return TryParse(t[1..], out var n) && n >= 0 && double.IsFinite(n) ? 64 + (t[0] is 'L' or 'l' ? -n : n) : null;
        return TryParse(t, out var o) && double.IsFinite(o) ? 64 + o : null;
    }
}
