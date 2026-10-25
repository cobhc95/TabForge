using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform.Controls;

/// <summary>
/// A small rotary dial, the twin of the WPF KnobControl: drag around the dial (clockwise raises; Shift for fine), the mouse wheel
/// and arrow keys step, Ctrl+click or Home returns to <see cref="DefaultValue"/>, double-click, right-click, F2 or Enter type an exact value.
/// The arc runs from <see cref="Origin"/> to the value, so a pan knob fills out from the centre.
/// Owns: pointer and key handling, the drawing (IDrawContext, repainted only when the value, hover, drag or focus changes) and the type-in flyout.
/// Does not own: the arithmetic (KnobMath), the typed text rules (KnobValueParser), what the value means (the window that hosts it).
/// Tests: TabForge.Avalonia.Tests.KnobControlTests.
/// </summary>
public sealed class KnobControl : Control
{
    /// <summary>Default edge length in DIPs (the WPF knob's FX bar size).</summary>
    public const double DefaultSize = 30;
    private const double ArcWidth = 2.6;
    private const double RingInset = 2.5;
    private const double FaceInset = 3.2;
    private const double PointerInset = 4.6;
    private const double TypeBoxMinWidth = 84;

    private readonly AvaloniaDrawContext _draw = new();
    private double _value, _dragValue, _lastAngle = double.NaN;
    private Point _lastPoint;
    private bool _dragging, _hover, _editorDone;
    private TextBox? _editor;
    private Flyout? _flyout;

    public KnobControl()
    {
        Width = Height = DefaultSize;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 127;
    public double DefaultValue { get; set; }
    /// <summary>Value the coloured arc starts from (Minimum for volume, the centre for pan).</summary>
    public double Origin { get; set; }
    /// <summary>Granularity of <see cref="Value"/> (1 = whole units, 0.1 = tenths, 0 = none).</summary>
    public double Step { get; set; } = 1;
    public string Label { get; set; } = "";
    /// <summary>Turns a value into its displayed text (tooltip and the type-in start).</summary>
    public Func<double, string>? Format { get; set; }
    /// <summary>Reads the whole typed text into knob units, or null when it is not valid (pan "L 20"). Default: KnobValueParser.TryParse.</summary>
    public Func<string, double?>? Parse { get; set; }
    /// <summary>Turns a typed displayed number into knob units (a 0-127 knob shown in percent). Default: the number itself.</summary>
    public Func<double, double>? FromDisplay { get; set; }

    public event EventHandler<double>? ValueChanged;
    /// <summary>Raised when a drag, step or typed value starts / ends so callers can group undo.</summary>
    public event EventHandler? EditStarted;
    public event EventHandler? EditEnded;

    public double Value
    {
        get => _value;
        set
        {
            var v = KnobMath.Clamp(value, Minimum, Maximum, Step);
            if (v == _value) return;
            _value = v;
            ToolTip.SetTip(this, TipText());
            InvalidateVisual();
            ValueChanged?.Invoke(this, v);
        }
    }

    /// <summary>The editor box while the type-in is open (tests type into it).</summary>
    public TextBox? Editor => _editor;

    /// <summary>The value as displayed (what the type-in is filled with).</summary>
    public string DisplayText() => Format?.Invoke(_value) ?? _value.ToString("0.##", CultureInfo.InvariantCulture);

    private string TipText() => $"{Label}: {DisplayText()}\nDrag or scroll to change · double-click or right-click to type a value · Ctrl+click to reset";

    /// <summary>Reads typed text into a clamped, stepped value; false when it is not understood.</summary>
    public bool TryParseInput(string text, out double knobValue)
    {
        knobValue = _value;
        double v;
        if (Parse is { } custom)
        {
            if (custom(text) is not { } r) return false;
            v = r;
        }
        else
        {
            if (!KnobValueParser.TryParse(text, out var shown)) return false;
            v = double.IsInfinity(shown) ? shown : FromDisplay is { } f ? f(shown) : shown;
        }
        knobValue = KnobMath.Clamp(v, Minimum, Maximum, Step);
        return true;
    }

    /// <summary>Sets a value as one gesture (EditStarted, Value, EditEnded) like a drag; no-op when unchanged.</summary>
    private void SetAsEdit(double value)
    {
        var v = KnobMath.Clamp(value, Minimum, Maximum, Step);
        if (v == _value) return;
        EditStarted?.Invoke(this, EventArgs.Empty);
        Value = v;
        EditEnded?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }
    protected override void OnGotFocus(GotFocusEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(RoutedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!IsEnabled || _editor is not null) return;
        Focus();
        if (point.Properties.IsRightButtonPressed) { e.Handled = true; return; }
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount == 2) { BeginTypeValue(); return; }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { SetAsEdit(DefaultValue); return; }
        _dragValue = _value;
        _lastPoint = point.Position;
        _lastAngle = double.NaN;
        _dragging = true;
        e.Pointer.Capture(this);
        EditStarted?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y)) >= KnobMath.AngleDragMinRadius)
        {
            var angle = Math.Atan2(p.Y - c.Y, p.X - c.X);
            if (double.IsNaN(_lastAngle)) _lastAngle = angle;
            var d = angle - _lastAngle;
            if (d > Math.PI) d -= 2 * Math.PI; else if (d < -Math.PI) d += 2 * Math.PI;
            _lastAngle = angle;
            _dragValue = KnobMath.DragByAngle(_dragValue, d, Minimum, Maximum, fine);
        }
        else
        {
            _lastAngle = double.NaN;
            _dragValue = KnobMath.DragByTravel(_dragValue, (_lastPoint.Y - p.Y) + (p.X - _lastPoint.X), Minimum, Maximum, fine);
        }
        _lastPoint = p;
        Value = _dragValue;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right && IsEnabled && _editor is null)
        {
            e.Handled = true;
            BeginTypeValue();
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        EditEnded?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled || _editor is not null) return;
        SetAsEdit(_value + Math.Sign(e.Delta.Y) * KnobMath.CoarseStep(Minimum, Maximum, Step, e.KeyModifiers.HasFlag(KeyModifiers.Shift)));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled || _editor is not null) return;
        var step = KnobMath.CoarseStep(Minimum, Maximum, Step, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        switch (e.Key)
        {
            case Key.Up: case Key.Right: SetAsEdit(_value + step); break;
            case Key.Down: case Key.Left: SetAsEdit(_value - step); break;
            case Key.Home: SetAsEdit(DefaultValue); break;
            case Key.F2: case Key.Enter: BeginTypeValue(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Opens the small inline editor over the knob: filled with the displayed value, all selected.</summary>
    public void BeginTypeValue()
    {
        if (!IsEnabled || _editor is not null) return;
        _editorDone = false;
        var box = new TextBox { Text = DisplayText(), MinWidth = TypeBoxMinWidth };
        global::Avalonia.Automation.AutomationProperties.SetName(box, $"{(Label.Length > 0 ? Label : "Knob")} value");
        ToolTip.SetTip(box, "Type a value, Enter to apply, Esc to cancel");
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; FinishEdit(true, keepOpenIfInvalid: true); }
            else if (e.Key == Key.Escape) { e.Handled = true; FinishEdit(false); }
        };
        _editor = box;
        _flyout = new Flyout { Content = box, Placement = PlacementMode.Center, ShowMode = FlyoutShowMode.Transient };
        _flyout.Closed += (_, _) => FinishEdit(true);
        _flyout.ShowAt(this);
        Dispatcher.UIThread.Post(() => { if (_editor == box) { box.Focus(); box.SelectAll(); } }, DispatcherPriority.Input);
    }

    /// <summary>Closes the editor: apply (Enter or click away; an invalid entry keeps the old value) or cancel (Esc).</summary>
    public void FinishEdit(bool apply, bool keepOpenIfInvalid = false)
    {
        if (_editor is not { } box || _editorDone) return;
        if (apply && keepOpenIfInvalid && !TryParseInput(box.Text ?? "", out _)) return;
        _editorDone = true;
        if (apply && TryParseInput(box.Text ?? "", out var v)) SetAsEdit(v);
        var flyout = _flyout;
        _editor = null;
        _flyout = null;
        flyout?.Hide();
        Focus();
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 4) return;
        var d = _draw.Use(context);
        var track = Colours.Get("BorderBrush");
        var accent = Colours.Get("AccentBrush");
        var face = Colours.Get("Panel3Brush");
        var ink = Colours.Get("TextBrush");
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = size / 2 - RingInset;
        if (_dragging || _hover)
            d.Ellipse(new UiColor((byte)(_dragging ? 110 : 60), accent.R, accent.G, accent.B), null, c.X, c.Y, r + 3, r + 3);
        d.Path(null, new UiStroke(track, ArcWidth, RoundCaps: true), Arc(d, c, r, -KnobMath.SweepDegrees / 2, KnobMath.SweepDegrees / 2));
        var from = KnobMath.AngleOf(Math.Clamp(Origin, Minimum, Maximum), Minimum, Maximum);
        var to = KnobMath.AngleOf(_value, Minimum, Maximum);
        if (Math.Abs(to - from) > 0.5)
            d.Path(null, new UiStroke(accent, ArcWidth, RoundCaps: true), Arc(d, c, r, from, to));
        d.Ellipse(face, new UiStroke(IsFocused ? accent : track, 1), c.X, c.Y, r - FaceInset, r - FaceInset);
        var tip = OnCircle(c, (r - FaceInset) * 0.25, to);
        var end = OnCircle(c, r - PointerInset, to);
        d.Line(new UiStroke(ink, 1.8, RoundCaps: true), tip.X, tip.Y, end.X, end.Y);
    }

    private ThemeColourCache? _theme;
    private ThemeColourCache Colours => _theme ??= new(this);

    private static Point OnCircle(Point c, double r, double degrees)
    {
        var rad = (degrees - 90) * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }

    private static IUiPath Arc(IDrawContext d, Point c, double r, double fromDeg, double toDeg)
    {
        if (toDeg < fromDeg) (fromDeg, toDeg) = (toDeg, fromDeg);
        var start = OnCircle(c, r, fromDeg);
        var end = OnCircle(c, r, toDeg);
        return d.Paths.BuildPath(s =>
        {
            s.BeginFigure(new UiPoint(start.X, start.Y), false, false);
            s.ArcTo(new UiPoint(end.X, end.Y), r, r, 0, toDeg - fromDeg > 180, true);
        });
    }
}
