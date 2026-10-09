using TabForge.Models;

namespace TabForge.Views.EffectEditors;

/// <summary>The effects that have an editor dialog. A follow-up editor adds its member here.</summary>
public enum EffectEditorKind { Bend, TremoloBar, Trill, Grace, Harmonic }

// Owns: the editable point list of a bend or tremolo-bar curve and its snapping, ordering and limits (pure; no WPF).
// Does not own: drawing and mouse handling (EffectCurveEditor), presets (EffectPresetStore), writing into notes (EffectEdits).
// Tests: TestEffectCurveMath.
/// <summary>
/// Points are in the model's units: offset 0..60 across the note, value in quarter-tones (2 = one semitone). The first point is always at
/// offset 0 and the last at <see cref="Span"/>; those two only move up and down, so a curve always covers the whole note.
/// </summary>
public sealed class EffectCurve
{
    public const double Span = 60;
    /// <summary>Time grid columns across the note.</summary>
    public const int Columns = 12;
    public const double TimeStep = Span / Columns;

    private readonly List<BendPointModel> _points = new();

    public EffectCurve(double minValue, double maxValue)
    {
        MinValue = minValue;
        MaxValue = maxValue;
        SetPoints(new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = Span, Value = 0 } });
    }

    public double MinValue { get; }
    public double MaxValue { get; }
    public IReadOnlyList<BendPointModel> Points => _points;

    /// <summary>Replaces the curve: points are snapped, sorted, limited and given end points at 0 and <see cref="Span"/>.</summary>
    public void SetPoints(IEnumerable<BendPointModel> points)
    {
        var snapped = points
            .Select(p => (Offset: SnapTime(p.Offset <= 1.0 && p.Offset > 0 ? p.Offset * Span : p.Offset), Value: SnapValue(p.Value)))
            .OrderBy(p => p.Offset).ToList();
        _points.Clear();
        foreach (var p in snapped)
        {
            if (_points.Count > 0 && _points[^1].Offset == p.Offset) _points[^1].Value = p.Value;   // one value per time
            else _points.Add(new BendPointModel { Offset = p.Offset, Value = p.Value });
        }
        if (_points.Count == 0) _points.Add(new BendPointModel { Offset = 0, Value = 0 });
        if (_points[0].Offset > 0) _points.Insert(0, new BendPointModel { Offset = 0, Value = _points[0].Value });
        if (_points[^1].Offset < Span) _points.Add(new BendPointModel { Offset = Span, Value = _points[^1].Value });
    }

    public double SnapTime(double offset) => Math.Clamp(Math.Round(offset / TimeStep) * TimeStep, 0, Span);
    public double SnapValue(double value) => Math.Clamp(Math.Round(value), MinValue, MaxValue);

    /// <summary>Adds a point (or sets the value of the point already at that time); returns its index.</summary>
    public int Add(double offset, double value)
    {
        offset = SnapTime(offset);
        value = SnapValue(value);
        var existing = _points.FindIndex(p => p.Offset == offset);
        if (existing >= 0) { _points[existing].Value = value; return existing; }
        var index = _points.FindIndex(p => p.Offset > offset);
        _points.Insert(index, new BendPointModel { Offset = offset, Value = value });
        return index;
    }

    /// <summary>Moves a point; it stays between its neighbours in time (the end points keep their time).</summary>
    public void Move(int index, double offset, double value)
    {
        if (index < 0 || index >= _points.Count) return;
        var p = _points[index];
        p.Value = SnapValue(value);
        if (index > 0 && index < _points.Count - 1)
            p.Offset = Math.Clamp(SnapTime(offset), _points[index - 1].Offset + TimeStep, _points[index + 1].Offset - TimeStep);
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
