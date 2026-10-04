using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Controllers;

// Owns: deleting bars that hold no notes (the Delete key on selected empty bars, and the timeline's "Delete empty bars"), with the question and the clip warning.
// Does not own: the bar removal (ArrangementController, EmptyBars) or the dialog (the host).
// Tests: TestBarDeleteGuards.
internal sealed class EmptyBarFlow
{
    private readonly ISectionEditHost _host;
    private readonly ArrangementController _arrangement;

    public EmptyBarFlow(ISectionEditHost host, ArrangementController arrangement)
    {
        _host = host;
        _arrangement = arrangement;
    }

    /// <summary>
    /// The Delete key in the score: when the selection is whole bars that hold no notes on any track, asks "Delete N empty bars?" and removes them (true: the
    /// key is handled, whatever the answer). False when the selection is something else, so the key clears beats as usual.
    /// </summary>
    public bool TryDeleteSelectedEmpty(DocumentSession doc, int trackIndex, int m1, int c1, int m2, int c2)
    {
        if (!_host.IsShown(doc) || (m1 == m2 && c1 == c2)) return false;
        var project = doc.Project;
        if (!CoversWholeBars(project, trackIndex, m1, c1, m2, c2)) return false;
        var bars = EmptyBars.InRange(project, m1, m2);
        if (bars.Count != m2 - m1 + 1) return false;   // a bar with notes (or the only bar of the song): Delete clears beats
        if (!_host.Ask("Delete empty bars", $"Delete {bars.Count} empty bar{(bars.Count == 1 ? "" : "s")}?", "Delete", withUndoHint: true)) return true;
        Remove(doc, bars);
        return true;
    }

    /// <summary>"Delete empty bars" on a bar range: removes only the bars that are empty on every track.</summary>
    public void DeleteEmptyInRange(DocumentSession doc, int start, int end)
    {
        if (!_host.IsShown(doc)) return;
        var bars = EmptyBars.InRange(doc.Project, start, end);
        if (bars.Count == 0) { _host.SetStatus("No empty bars in the selection"); return; }
        Remove(doc, bars);
    }

    private void Remove(DocumentSession doc, List<int> bars)
    {
        if (ClipDeleteImpact.Find(doc.Project, bars.Select(b => (b, b)).ToList()) is { } impact
            && !_host.Ask("Delete bars", ClipDeleteImpact.Describe(impact), "Continue", withUndoHint: false)) return;
        if (_arrangement.DeleteEmptyBars(doc, bars).Value is not { } map) return;
        _host.EndLoop();
        _host.ClearSelection();
        doc.SkipRanges.Clear();
        _host.SyncAreaVisuals();
        var count = BarRangeEditor.MaxMeasures(doc.Project);
        var at = Math.Clamp(bars[0], 0, Math.Max(0, count - 1));
        _host.SetCaret(at, 0, _host.Caret.String);
        _host.FinishStructureEdit($"Deleted {bars.Count} empty bar{(bars.Count == 1 ? "" : "s")}", map, at);
    }

    /// <summary>True when the cell range starts at or before the first beat of bar m1 and ends at or after the last beat of bar m2 on the track.</summary>
    private static bool CoversWholeBars(SongProject project, int trackIndex, int m1, int c1, int m2, int c2)
    {
        if (trackIndex < 0 || trackIndex >= project.Tracks.Count) return false;
        var measures = project.Tracks[trackIndex].Measures;
        if (m1 < 0 || m2 >= measures.Count || m2 < m1) return false;
        var firstBeat = CursorPositions.Allowed(measures[m1].Cells)[0];
        var beats = measures[m2].Cells.Select((cell, i) => (cell, i)).Where(x => CursorPositions.IsBeatCell(x.cell)).Select(x => x.i).ToList();
        var lastBeat = beats.Count == 0 ? 0 : beats[^1];
        return c1 <= firstBeat && c2 >= lastBeat;
    }
}
