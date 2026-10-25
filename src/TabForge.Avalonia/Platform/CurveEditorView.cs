using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The bend and tremolo-bar curve editor: the twin of the WPF EffectCurveEditor. Time runs across, pitch up the side in quarter-tone steps
/// (a solid line per semitone, labelled per whole tone). Drag a point to move it, click empty grid to add one, right-click or double-click a
/// point to remove it, Arrow keys move the selected point and Delete removes it. The point under the pointer is outlined.
/// Owns: the drawing of the grid, curve and handles through <see cref="AvaloniaDrawContext"/> in theme colours, and the pointer and key input
/// as curve edits. Does not own: the point rules (<see cref="EffectCurve"/>), presets, or applying the curve to notes (the dialog and EffectCurveFlow).
/// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// </summary>
public sealed class CurveEditorView : Control
{
    /// <summary>Pointer distance in DIP within which a press hits a point.</summary>
    internal const double HitRadius = 9;
    /// <summary>Half the side of a drawn point handle in DIP.</summary>
    internal const double HandleHalf = 5;
    internal const double LeftMargin = 40, OtherMargin = 12;
    internal const double EditorWidth = 560, ShortEditorHeight = 240, TallEditorHeight = 360;
    /// <summary>A value range wider than this many quarter-tones gets the taller grid.</summary>
    internal const double TallRangeAbove = 8;
    private const double LabelSize = 11, LabelWidth = 28, LabelGap = 6, LabelHalfHeight = 7;
    private const double StrongLine = 1, SoftLine = 1, CurveLine = 2.5, SelectedRing = 2, FocusRing = 1;
    private const double DotDash = 2, DotGap = 2, FocusDash = 4, FocusGap = 4;
    private const string LabelFamily = "Segoe UI";
    private const int MinorColumnsPerMajor = 3;

    private readonly AvaloniaDrawContext _draw = new();
    private ThemeColourCache? _theme;
    private ThemeColourCache Colours => _theme ??= new(this);
    private int _selected = -1;
    private int _hover = -1;
    private bool _dragging;

    public CurveEditorView(EffectCurve curve)
    {
        Curve = curve;
        Focusable = true;
        Width = EditorWidth;
        Height = curve.MaxValue - curve.MinValue > TallRangeAbove ? TallEditorHeight : ShortEditorHeight;
        AutomationProperties.SetName(this, "Effect curve editor");
        ToolTip.SetTip(this, "Drag a point to move it. Click to add a point. Right-click or double-click a point to remove it.");
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public EffectCurve Curve { get; }

    /// <summary>Raised after every change of the curve made by the pointer or the keys.</summary>
    public event EventHandler? CurveChanged;

    /// <summary>The index of the selected point, or -1.</summary>
    internal int Selected => _selected;
    /// <summary>The index of the point under the pointer, or -1.</summary>
    internal int Hovered => _hover;

    /// <summary>The grid rectangle inside the control.</summary>
    internal Rect PlotRect(Size size) => new(LeftMargin, OtherMargin, Math.Max(1, size.Width - LeftMargin - OtherMargin), Math.Max(1, size.Height - 2 * OtherMargin));

    internal Point ToPixel(Rect plot, double offset, double value) => new(
        plot.Left + offset / EffectCurve.Span * plot.Width,
        plot.Bottom - (value - Curve.MinValue) / (Curve.MaxValue - Curve.MinValue) * plot.Height);

    internal (double Offset, double Value) FromPixel(Rect plot, Point p) => (
        (p.X - plot.Left) / plot.Width * EffectCurve.Span,
        Curve.MinValue + (plot.Bottom - p.Y) / plot.Height * (Curve.MaxValue - Curve.MinValue));

    /// <summary>The index of the point under a pixel position, or -1. The nearer of two in reach wins.</summary>
    internal int HitPoint(Rect plot, Point p)
    {
        var best = -1;
        var bestDistance = HitRadius;
        for (var i = 0; i < Curve.Points.Count; i++)
        {
            var d = Distance(ToPixel(plot, Curve.Points[i].Offset, Curve.Points[i].Value), p);
            if (d <= bestDistance) { best = i; bestDistance = d; }
        }
        return best;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // The axis texts, made once per value: a repaint never formats a string. Ceiling: one per grid line of the curve's range. Thread: UI thread only.
    private readonly Dictionary<double, string> AxisLabels = new();

    private string LabelOf(double value)
    {
        if (!AxisLabels.TryGetValue(value, out var text)) AxisLabels[value] = text = Label(value);
        return text;
    }

    /// <summary>Axis text in semitones (a quarter-tone unit is half a semitone): +1, 0, -2...</summary>
    internal static string Label(double value)
    {
        var semitones = value / 2;
        return semitones > 0 ? "+" + semitones.ToString("0.#", CultureInfo.InvariantCulture) : semitones.ToString("0.#", CultureInfo.InvariantCulture);
    }

    public override void Render(DrawingContext context)
    {
        var d = _draw.Use(context);
        var size = new Size(Bounds.Width, Bounds.Height);
        var plot = PlotRect(size);
        var text = Colours.Get("TextBrush");
        var muted = Colours.Get("MutedBrush");
        var accent = Colours.Get("AccentBrush");
        var strong = new UiStroke(Colours.Get("BorderBrush"), StrongLine);
        var soft = new UiStroke(Colours.Get("BorderSoftBrush"), SoftLine, Dash: DotDash, Gap: DotGap);
        d.Rect(Colours.Get("Panel2Brush"), null, 0, 0, size.Width, size.Height);
        for (var v = Curve.MinValue; v <= Curve.MaxValue; v++)
        {
            var y = ToPixel(plot, 0, v).Y;
            var semitone = v % 2 == 0;
            d.Line(semitone ? strong : soft, plot.Left, y, plot.Right, y);
            if (semitone)
            {
                var label = d.Text(LabelOf(v), LabelFamily, LabelSize, v == 0 ? text : muted, d.PixelsPerDip);
                d.DrawText(label, plot.Left - LabelGap - LabelWidth, y - LabelHalfHeight);
            }
        }
        for (var c = 0; c <= EffectCurve.Columns; c++)
        {
            var x = plot.Left + c * plot.Width / EffectCurve.Columns;
            d.Line(c % MinorColumnsPerMajor == 0 ? strong : soft, x, plot.Top, x, plot.Bottom);
        }
        var pts = Curve.Points;
        var line = new UiStroke(accent, CurveLine, RoundJoins: true);
        for (var i = 1; i < pts.Count; i++)
        {
            var from = ToPixel(plot, pts[i - 1].Offset, pts[i - 1].Value);
            var to = ToPixel(plot, pts[i].Offset, pts[i].Value);
            d.Line(line, from.X, from.Y, to.X, to.Y);
        }
        for (var i = 0; i < pts.Count; i++)
        {
            var centre = ToPixel(plot, pts[i].Offset, pts[i].Value);
            var edge = i == _hover || i == _selected ? new UiStroke(accent, SelectedRing) : strong;
            d.Rect(i == _selected ? accent : text, edge, centre.X - HandleHalf, centre.Y - HandleHalf, 2 * HandleHalf, 2 * HandleHalf);
        }
        if (IsFocused) d.Rect(null, new UiStroke(accent, FocusRing, Dash: FocusDash, Gap: FocusGap), FocusRing, FocusRing, size.Width - 2 * FocusRing, size.Height - 2 * FocusRing);
    }

    private Rect Plot() => PlotRect(new Size(Bounds.Width, Bounds.Height));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed) { PressRight(point.Position); e.Handled = true; return; }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (PressLeft(point.Position, e.ClickCount)) { e.Pointer.Capture(this); e.Handled = true; }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_dragging) { DragTo(position); return; }
        HoverAt(position);
    }

    /// <summary>Outlines the point under <paramref name="position"/> (none when the pointer is off every point).</summary>
    internal void HoverAt(Point position)
    {
        var hover = HitPoint(Plot(), position);
        if (hover == _hover) return;
        _hover = hover;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        Release();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_dragging || _hover < 0) return;
        _hover = -1;
        InvalidateVisual();
    }

    /// <summary>A left press at <paramref name="position"/>: a double click removes the point, a press on empty grid adds one, and a press on a point starts a drag. False when the press is outside the grid.</summary>
    internal bool PressLeft(Point position, int clickCount)
    {
        var plot = Plot();
        var hit = HitPoint(plot, position);
        if (hit >= 0 && clickCount == 2) { Remove(hit); return true; }
        if (hit < 0)
        {
            if (!plot.Contains(position)) return false;
            var (offset, value) = FromPixel(plot, position);
            hit = Curve.Add(offset, value);
            Changed();
        }
        _selected = hit;
        _dragging = true;
        InvalidateVisual();
        return true;
    }

    /// <summary>A right press removes the point under <paramref name="position"/>, if any.</summary>
    internal void PressRight(Point position)
    {
        var hit = HitPoint(Plot(), position);
        if (hit >= 0) Remove(hit);
    }

    /// <summary>Moves the dragged point to <paramref name="position"/>; a press that is not a drag does nothing.</summary>
    internal void DragTo(Point position)
    {
        if (!_dragging || _selected < 0) return;
        var (offset, value) = FromPixel(Plot(), position);
        Curve.Move(_selected, offset, value);
        Changed();
    }

    internal void Release() => _dragging = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_selected < 0 || _selected >= Curve.Points.Count)
        {
            if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right) _selected = 0;
            else return;
        }
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
}
