using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's selection and tool-drag gestures: the Select tool's (Pointer) left-drag box marquee and Alt-drag
//       freehand lasso (Shift adds, Ctrl toggles), the Split line drag (every crossed note cut at the snap), the Glue
//       drag (touched notes merged) and the Mute paint (each touched note flipped once). Each commits one undo step on
//       release; a drag only hit-tests and invalidates.
// Does not own: starting the gesture (PianoRollSurface.Input) or drawing the overlays (PianoRollSurface.Drawing).
// Tests: TabForge.Avalonia.Tests.PianoRollSelectTests.
public sealed partial class PianoRollSurface
{
    private readonly List<Point> _lasso = new();
    private readonly List<Point> _splitPath = new();
    private readonly HashSet<int> _glueTouched = new();
    private readonly HashSet<int> _muteTouched = new();
    private List<int> _splitHits = new();

    private void ClearSelectState()
    {
        _lasso.Clear();
        _splitPath.Clear();
        _glueTouched.Clear();
        _muteTouched.Clear();
        _splitHits = new List<int>();
    }

    /// <summary>The lasso's pixel path (the overlay draws it).</summary>
    internal IReadOnlyList<Point> LassoPoints => _lasso;

    /// <summary>The split line's pixel path (the overlay draws it).</summary>
    internal IReadOnlyList<Point> SplitPoints => _splitPath;

    private bool StartSelectBox(Point p, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            _gesture = Gesture.Lasso;
            _lasso.Clear();
            _lasso.Add(p);
            return true;
        }
        _gesture = Gesture.Marquee;
        _rightBox = false;
        return true;
    }

    private void DragSelectBox(Point p)
    {
        if (_gesture != Gesture.Lasso) return;
        if (_lasso.Count == 0 || System.Math.Abs(_lasso[_lasso.Count - 1].X - p.X) + System.Math.Abs(_lasso[_lasso.Count - 1].Y - p.Y) > 2) _lasso.Add(p);
    }

    private List<(double Beat, double Pitch)> LassoBeats() =>
        _lasso.Select(q => (BeatAt(q.X), (double)PitchAt(q.Y))).ToList();

    private void ReleaseSelectBox(KeyModifiers modifiers)
    {
        List<int> picked;
        if (_gesture == Gesture.Lasso && _lasso.Count >= 3)
            picked = PianoRollSelection.InPolygon(Presenter.Notes, LassoBeats());
        else
        {
            var box = MarqueeRect();
            picked = PianoRollEdits.InBox(Presenter.Notes, BeatAt(box.Left), BeatAt(box.Right), PitchAt(box.Bottom), PitchAt(box.Top));
        }
        Presenter.Select(Combine(picked, modifiers | _dragModifiers));
        SelectionChanged?.Invoke();
    }

    private bool StartSplit(Point p, int index)
    {
        _gesture = Gesture.SplitLine;
        _splitPath.Clear();
        _splitPath.Add(p);
        _pressNote = index;
        _splitHits = index >= 0 ? new List<int> { index } : new List<int>();
        return true;
    }

    private void DragSplit(Point p)
    {
        _splitPath.Add(p);
        _splitHits = CrossedNotes();   // hit test only; the cut commits on release
    }

    private List<int> CrossedNotes()
    {
        var path = _splitPath.Select(q => (BeatAt(q.X), (double)PitchAt(q.Y))).ToList();
        var hits = new List<int>();
        if (path.Count == 0) return hits;
        var notes = Presenter.Notes;
        for (var i = 0; i < notes.Count; i++)
            for (var s = 0; s < path.Count; s++)
            {
                var a = s == 0 ? path[0] : path[s - 1];
                if (!double.IsNaN(PianoRollSelection.CrossBeat(notes[i], a, path[s]))) { hits.Add(i); break; }
            }
        return hits;
    }

    private void ReleaseSplit()
    {
        if (!_moved)
        {
            if (_pressNote >= 0) Run(Presenter.CommitAsync(PianoRollEdits.Split(Presenter.Notes, _pressNote, SplitBeat(_pressBeat)), PianoRollEditKind.Notes, "Split note"));
            return;
        }
        var path = _splitPath.Select(q => (BeatAt(q.X), (double)PitchAt(q.Y))).ToList();
        var cuts = new List<(int Index, double At)>();
        foreach (var i in CrossedNotes())
        {
            double best = double.NaN;
            for (var s = 0; s < path.Count; s++)
            {
                var beat = PianoRollSelection.CrossBeat(Presenter.Notes[i], s == 0 ? path[0] : path[s - 1], path[s]);
                if (!double.IsNaN(beat)) best = beat;
            }
            if (!double.IsNaN(best)) cuts.Add((i, SnapBeat(best, new[] { i }, _alt)));
        }
        if (cuts.Count > 0) Run(Presenter.CommitAsync(PianoRollEdits.SplitMany(Presenter.Notes, cuts), PianoRollEditKind.Notes, "Split notes"));
    }

    private bool StartGlue(Point p, int index)
    {
        _gesture = Gesture.GluePaint;
        _glueTouched.Clear();
        if (index >= 0) _glueTouched.Add(index);
        return true;
    }

    private void DragGlue(Point p)
    {
        var (index, _) = HitNote(p);
        if (index >= 0) _glueTouched.Add(index);   // hit test only; the merge commits on release
    }

    private void ReleaseGlue()
    {
        if (!_moved)
        {
            if (_pressNote >= 0) Run(Presenter.GlueAtAsync(_pressNote));
            return;
        }
        if (_glueTouched.Count > 1) Run(Presenter.CommitAsync(PianoRollEdits.Glue(Presenter.Notes, _glueTouched.ToList()), PianoRollEditKind.Notes, "Glue notes"));
        else if (_glueTouched.Count == 1) Run(Presenter.GlueAtAsync(_glueTouched.First()));
    }

    /// <summary>Where a click would cut with the Split tool (the snapped beat under the pointer over a note); null otherwise (a test reads it).</summary>
    internal double? SplitPreviewBeat => Presenter.Tool == PianoRollTool.Split && _hover is var (beat, _, index) && index >= 0 ? beat : null;

    /// <summary>The split key (S): cuts every selected note the cut position crosses, once, as one undo step. The cut is the pointer's grid position
    /// snapped (Alt bypasses), else the playhead / edit cursor when the pointer is off the grid.</summary>
    internal void SplitSelection()
    {
        if (Presenter.Selected.Count == 0) { Report?.Invoke("Select notes to split first"); return; }
        var at = CutBeat();   // the same position the dashed cut marker shows
        var (result, cuts) = PianoRollEdits.SplitSelectedAt(Presenter.Notes, Presenter.Selected, at);
        if (cuts == 0) { Report?.Invoke("The cut position does not cross a selected note"); return; }
        Run(Presenter.CommitAsync(result, PianoRollEditKind.Notes, "Split notes"));
    }

    /// <summary>The glue key (Ctrl+Shift+G) and the Glue button: joins the selected notes of one pitch that touch, overlap or lie within one snap step.</summary>
    internal void GlueSelection()
    {
        if (Presenter.Selected.Count < 2) { Report?.Invoke("Select two or more notes to glue"); return; }
        var first = Presenter.Selected.Min(i => Presenter.Notes[i].Start);
        var gap = Snap() is { Enabled: true } ? StepAt(first) : 0;
        var (result, pairs) = PianoRollEdits.GlueTouching(Presenter.Notes, Presenter.Selected, gap);
        if (pairs == 0) { Report?.Invoke("Nothing to glue"); return; }
        Report?.Invoke(pairs == 1 ? "Glued 1 pair" : $"Glued {pairs} pairs");
        Run(Presenter.CommitAsync(result, PianoRollEditKind.Notes, "Glue notes"));
    }

    private bool StartMute(Point p, int index)
    {
        _gesture = Gesture.MutePaint;
        _muteTouched.Clear();
        if (index >= 0) _muteTouched.Add(index);
        return true;
    }

    private void DragMute(Point p)
    {
        var (index, _) = HitNote(p);
        if (index >= 0) _muteTouched.Add(index);   // each note once (a set); the flip commits on release
    }

    private void ReleaseMute()
    {
        var picked = (_moved ? _muteTouched : _muteTouched.Count > 0 ? _muteTouched : new HashSet<int>(_pressNote >= 0 ? new[] { _pressNote } : System.Array.Empty<int>())).ToList();
        if (picked.Count > 0) Run(Presenter.CommitAsync(PianoRollEdits.FlipMute(Presenter.Notes, picked), PianoRollEditKind.Expression, "Mute notes"));
    }
}
