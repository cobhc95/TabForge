using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// MainWindow, shared selection: the score editor and the arrangement timeline both mirror one SelectionModel.
// Every selection change (score mouse/keys, timeline drag or click, Esc, area commands, track switch, undo,
// bar insert/delete, tab switch) is written to _selection; SelectionSync applies it to the editor and
// SelectionLoopController to the timeline highlight and the selected (loop) area.
public partial class MainWindow : ISelectionLoopHost
{
    private readonly SelectionModel _selection = new();
    private SelectionSync? _selectionSync;
    private readonly SelectionLoopController _selLoop;

    private void InitSelectionModel()
    {
        _selectionSync = new SelectionSync(_selection, Editor, origin => _selLoop.ApplySelection(_selection, origin));
    }

    /// <summary>The score's selection changed: write it to the model (its own echo is ignored).</summary>
    private void PushEditorSelectionToModel() => _selectionSync?.PushFromEditor();

    /// <summary>After undo/redo or any edit that can change the bar count: keep model and score valid and equal.</summary>
    private void ReconcileSelection()
    {
        _selectionSync?.Reconcile(MaxMeasures(), _project.Tracks.Count);
        _selLoop.ShowScore(_selection);
    }

    // ISelectionLoopHost
    DocumentSession ISelectionLoopHost.ActiveDocument => Doc;
    bool ISelectionLoopHost.LoopOn => _loop;
    void ISelectionLoopHost.SetLoopActive(bool loop) => SetLoopActive(loop);
    void ISelectionLoopHost.SyncAreaVisuals() => SyncAreaVisuals();
    void ISelectionLoopHost.ShowScoreSelection(SelectionModel s) => Arrangement.SetScoreSelection(s.BarRange, s.ScopeTrack);

    void ISelectionLoopHost.ShowArea(int start, int end, int startCell, int endCell)
    {
        Arrangement.SetLoopRange(start, end);
        SyncAreaVisuals();
        _midi.SetLoopRange(start, end, _selLoop.StartCell, _selLoop.EndCell);
        Arrangement.SetLoopEnabled(ShowLoopOnTimeline);
        StatusText.Text = _loop
            ? $"Looping bars {start + 1}-{end + 1}"
            : BarRangePromptText.Tip(start, end, MenuKey);
    }
}
