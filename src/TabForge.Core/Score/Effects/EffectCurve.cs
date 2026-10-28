using TabForge.Models;

namespace TabForge.Views.EffectEditors;

/// <summary>The effects that have an editor dialog. A follow-up editor adds its member here.</summary>
public enum EffectEditorKind { Bend, TremoloBar, Trill, Grace, Harmonic }

// Owns: the editable point list of a bend or tremolo-bar curve: its ordering, its value axis (widened to show any imported value) and the optional snap of placed points (pure; no UI toolkit).
// Does not own: drawing and mouse handling (CurveEditorView), presets (EffectPresetStore), the ease between points (EffectCurveShape), writing into notes (EffectEdits).
// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// <summary>
/// Points are in the model's units: offset 0..60 across the note, value in quarter tones (4 = a whole tone, "Full"). The first point is always at
/// offset 0 and the last at <see cref="Span"/>; those two only move up and down, so a curve always covers the whole note. Imported points are kept as they
/// are (no snapping, no value limit): the axis widens to hold them. Points the person places snap to a twelfth of the note unless asked not to.
/// </summary>
public sealed class EffectCurve
{
    public const double Span = 60;
    /// <summary>Time grid columns across the note.</summary>
    public const int Columns = 12;
    public const double TimeStep = Span / Columns;
    /// <summary>The closest two points may be in time, and the distance from an end within which an imported point is that end.</summary>
    public const double MinGap = 1;
    /// <summary>Bend curves run 0 to +<see cref="BendMaxValue"/> quarter tones (0..3 tones).</summary>
    public const double BendMaxValue = 12;
    /// <summary>Tremolo-bar curves run <see cref="TremoloMinValue"/> to <see cref="TremoloMaxValue"/> quarter tones (-6..+4 tones).</summary>
    public const double TremoloMinValue = -24, TremoloMaxValue = 16;

    private readonly List<BendPointModel> _points = new();

    public EffectCurve(double minValue, double maxValue)
    {
        MinValue = minValue;
        MaxValue = maxValue;
        SetPoints(new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = Span, Value = 0 } });
    }

    /// <summary>The lowest and highest value of the axis; they grow in whole tones to show an imported point, never shrink.</summary>
    public double MinValue { get; private set; }
    public double MaxValue { get; private set; }
    public IReadOnlyList<BendPointModel> Points => _points;

    /// <summary>Replaces the curve: points are sorted and given end points at 0 and <see cref="Span"/>; times and values are kept (the axis widens to show them).</summary>
    public void SetPoints(IEnumerable<BendPointModel> points)
    {
        var given = points.ToList();
        var scale = Span / BendPointModel.SpanOf(given);
        var sorted = given
            .Select(p => (Offset: NearEnd(Math.Clamp(p.Offset * scale, 0, Span)), p.Value))
            .OrderBy(p => p.Offset).ToList();
        _points.Clear();
        foreach (var p in sorted)
        {
            if (_points.Count > 0 && _points[^1].Offset == p.Offset) _points[^1].Value = p.Value;   // one value per time
            else _points.Add(new BendPointModel { Offset = p.Offset, Value = p.Value });
            Widen(p.Value);
        }
        if (_points.Count == 0) _points.Add(new BendPointModel { Offset = 0, Value = 0 });
        if (_points[0].Offset > 0) _points.Insert(0, new BendPointModel { Offset = 0, Value = _points[0].Value });
        if (_points[^1].Offset < Span) _points.Add(new BendPointModel { Offset = Span, Value = _points[^1].Value });
    }

    private static double NearEnd(double offset) => offset < MinGap ? 0 : offset > Span - MinGap ? Span : offset;

    private void Widen(double value)
    {
        var tone = EffectCurveShape.ValuesPerTone;
        if (value < MinValue) MinValue = Math.Floor(value / tone) * tone;
        if (value > MaxValue) MaxValue = Math.Ceiling(value / tone) * tone;
    }

    /// <summary>A time rounded to a twelfth of the note.</summary>
    public static double SnapTime(double offset) => Math.Clamp(Math.Round(offset / TimeStep) * TimeStep, 0, Span);
    /// <summary>A value rounded to a quarter tone and kept on the axis.</summary>
    public double SnapValue(double value) => Math.Clamp(Math.Round(value), MinValue, MaxValue);

    /// <summary>Adds a point (or sets the value of the point already at that time); returns its index. <paramref name="snap"/>: round the time to a twelfth of the note.</summary>
    public int Add(double offset, double value, bool snap = true)
    {
        offset = snap ? SnapTime(offset) : Math.Clamp(offset, 0, Span);
        value = SnapValue(value);
        var existing = _points.FindIndex(p => Math.Abs(p.Offset - offset) < MinGap);
        if (existing >= 0) { _points[existing].Value = value; return existing; }
        var index = _points.FindIndex(p => p.Offset > offset);
        _points.Insert(index, new BendPointModel { Offset = offset, Value = value });
        return index;
    }

    /// <summary>Moves a point; it stays between its neighbours in time (the end points keep their time).</summary>
    public void Move(int index, double offset, double value, bool snap = true)
    {
        if (index < 0 || index >= _points.Count) return;
        var p = _points[index];
        p.Value = SnapValue(value);
        if (index == 0 || index == _points.Count - 1) return;
        var low = _points[index - 1].Offset + MinGap;
        var high = _points[index + 1].Offset - MinGap;
        var wanted = snap ? SnapTime(offset) : offset;
        p.Offset = high < low ? (low + high) / 2 : Math.Clamp(wanted, low, high);
    }

    /// <summary>Removes a point; false for the first and last point (they stay).</summary>
    public bool RemoveAt(int index)
    {
        if (index <= 0 || index >= _points.Count - 1) return false;
        _points.RemoveAt(index);
        return true;
    }

    public List<BendPointModel> Snapshot() => _points.Select(p => p.Clone()).ToList();
}
