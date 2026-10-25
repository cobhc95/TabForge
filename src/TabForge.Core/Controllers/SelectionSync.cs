using TabForge.Models;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>What <see cref="SelectionSync"/> needs from the score editor.</summary>
public interface ISelectionEditor
{
    bool HasSelection { get; }
    int SelectedTrackIndex { get; }
    TrackModel? Track { get; }
    (int StartMeasure, int StartCell, int EndMeasure, int EndCell) SelectionCellRange { get; }
    bool SelectionMatches(int startMeasure, int startCell, int endMeasure, int endCell);
    void SelectRange(int startMeasure, int startCell, int endMeasure, int endCell);
    void ClearSelection(bool notify = true);
    void ShowLoopTint((int StartMeasure, int StartCell, int EndMeasure, int EndCell)? range);
}

// Owns: keeping the score editor and the arrangement timeline in step with the shown document's SelectionModel (Bind follows a tab switch).
// Does not own: the selection data itself (SelectionModel) and the views' drawing.
// Needs from its host: ISelectionEditor (the score editor's selection), never a concrete view.
// Tests: SelectionModelTests.
/// <summary>
/// Binds the score editor and the arrangement timeline to one <see cref="SelectionModel"/>. The editor pushes
/// its selection with <see cref="PushFromEditor"/> (from its SelectionChanged); every other source writes the
/// model directly. On each model change the editor is updated (unless it made the change) and the timeline
/// callback runs. Applying to the editor raises its SelectionChanged again; that echo is ignored here, and a
/// value equal to the model's is a no-op in the model, so the two views can never loop.
/// </summary>
public sealed class SelectionSync : IDisposable
{
    private readonly ISelectionEditor _editor;
    private readonly Action<SelectionOrigin> _applyToTimeline;
    private readonly Func<SongProject?>? _project;
    private bool _applyingToEditor;

    /// <param name="project">The song the score shows, to light the notes under selected clips; null: clips light nothing.</param>
    public SelectionSync(SelectionModel model, ISelectionEditor editor, Action<SelectionOrigin> applyToTimeline, Func<SongProject?>? project = null)
    {
        _project = project;
        Model = model;
        _editor = editor;
        _applyToTimeline = applyToTimeline;
        model.Changed += OnModelChanged;
    }

    public SelectionModel Model { get; private set; }

    /// <summary>Stops following the model (the window closed).</summary>
    public void Dispose() => Model.Changed -= OnModelChanged;

    /// <summary>
    /// Follows <paramref name="model"/> (the shown document's selection) from now on. <paramref name="showDocument"/> puts the document in the score; the editor's
    /// selection changes it causes are ignored, and afterwards the score shows the model's range.
    /// </summary>
    public void Bind(SelectionModel model, Action showDocument)
    {
        Model.Changed -= OnModelChanged;
        Model = model;
        model.Changed += OnModelChanged;
        _applyingToEditor = true;
        try { showDocument(); }
        finally { _applyingToEditor = false; }
        ApplyToEditor();
    }

    /// <summary>True while the model is being written into the editor (its SelectionChanged is an echo).</summary>
    public bool IsApplyingToEditor => _applyingToEditor;

    private void OnModelChanged(object? sender, SelectionOrigin origin)
    {
        if (origin != SelectionOrigin.Editor) ApplyToEditor();
        else ApplyLoopTint();   // a note-made loop changes while the score's own selection stays
        _applyToTimeline(origin);
    }

    private void ApplyLoopTint() =>
        _editor.ShowLoopTint(Model.HasLoop ? (Model.LoopStartBar, Model.LoopStartCell, Model.LoopEndBar, Model.LoopEndCell) : null);

    /// <summary>Makes the score show the model's selection as highlighted notes (its score range, else the bars of the selected clips) and the loop region as a light tint only; a no-op when it already does.</summary>
    public void ApplyToEditor()
    {
        ApplyLoopTint();
        var shown = Shown();
        if (shown is { } r ? _editor.SelectionMatches(r.StartBar, r.StartCell, r.EndBar, r.EndCell) : !_editor.HasSelection) return;
        _applyingToEditor = true;
        try
        {
            if (shown is { } show) _editor.SelectRange(show.StartBar, show.StartCell, show.EndBar, show.EndCell);
            else _editor.ClearSelection();
        }
        finally { _applyingToEditor = false; }
    }

    // What the score lights: a score selection on its own track (or all tracks), else the selected clips' bars; null: nothing (the loop only tints, see ApplyToEditor).
    private (int StartBar, int StartCell, int EndBar, int EndCell)? Shown()
    {
        var s = Model;
        if (s.HasRange && (s.Scope == SelectionScope.AllTracks || s.TrackIndex == _editor.SelectedTrackIndex)) return (s.StartBar, s.StartCell, s.EndBar, s.EndCell);
        if (_project?.Invoke() is { } project && SelectionBars.ClipSpan(s, project) is { } clips) return (clips.Start, 0, clips.End, -1);
        return null;
    }

    /// <summary>The score's selection changed (mouse, Shift+arrows, Ctrl+A, an edit): write it to the model.</summary>
    public void PushFromEditor()
    {
        if (_applyingToEditor) return;
        if (_editor.HasSelection)
        {
            var range = _editor.SelectionCellRange;
            Model.SetRange(_editor.SelectedTrackIndex, range.StartMeasure, range.EndMeasure, SelectionOrigin.Editor,
                range.StartCell, range.EndCell);
        }
        else Model.Clear(SelectionOrigin.Editor);
    }

    /// <summary>After undo/redo, a track switch or an edit that changed the bar count: keep model and score valid and equal. A track switch clears a score selection and keeps a timeline range (the model decides; nothing moves to the new track).</summary>
    public void Reconcile(int barCount, int trackCount)
    {
        if (_applyingToEditor) return;
        Model.ClampTo(barCount, trackCount);
        if (Model.TrackIndex != _editor.SelectedTrackIndex && _editor.Track is not null)
            Model.SetTrack(_editor.SelectedTrackIndex);
        ApplyToEditor();
    }
}
