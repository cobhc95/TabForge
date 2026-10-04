using TabForge.Controllers;
using TabForge.Models;

namespace TabForge;

// MainWindow, shared selection: the score editor and the arrangement timeline both mirror one SelectionModel.
// Every selection change (score mouse/keys, timeline drag or click, Esc, area commands, track switch, undo,
// bar insert/delete, tab switch) is written to _selection; SelectionSync applies it to the editor and
// ApplySelectionToTimeline to the timeline highlight and the selected (loop) area.
public partial class MainWindow
{
    private readonly SelectionModel _selection = new();
    private SelectionSync? _selectionSync;

    private void InitSelectionModel() => _selectionSync = new SelectionSync(_selection, Editor, ApplySelectionToTimeline);

    /// <summary>The timeline's score-selection highlight and its selected (loop) area both show the model's range.</summary>
    private void ApplySelectionToTimeline(SelectionOrigin origin)
    {
        var s = _selection;
        Arrangement.SetScoreSelection(s.BarRange, s.ScopeTrack);
        if (s.HasRange)
        {
            ApplyLoopArea(s.StartBar, s.EndBar, s.StartCell, s.EndCell);
            return;
        }
        if (!_loopHasArea) return;
        _loopHasArea = false;
        // Clearing the area while looping falls back to the no-area loop (the whole song), as Esc always did.
        // A tab switch only drops the area: the new song's loop is set up by the switch itself.
        if (_loop && origin != SelectionOrigin.Document) SetLoopActive(true);
        else SyncAreaVisuals();
    }

    /// <summary>The score's selection changed: write it to the model (its own echo is ignored).</summary>
    private void PushEditorSelectionToModel() => _selectionSync?.PushFromEditor();

    /// <summary>After undo/redo or any edit that can change the bar count: keep model and score valid and equal.</summary>
    private void ReconcileSelection()
    {
        _selectionSync?.Reconcile(MaxMeasures(), _project.Tracks.Count);
        Arrangement.SetScoreSelection(_selection.BarRange, _selection.ScopeTrack);
    }
}
