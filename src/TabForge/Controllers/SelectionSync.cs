using TabForge.Models;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>
/// Binds the score editor and the arrangement timeline to one <see cref="SelectionModel"/>. The editor pushes
/// its selection with <see cref="PushFromEditor"/> (from its SelectionChanged); every other source writes the
/// model directly. On each model change the editor is updated (unless it made the change) and the timeline
/// callback runs. Applying to the editor raises its SelectionChanged again; that echo is ignored here, and a
/// value equal to the model's is a no-op in the model, so the two views can never loop.
/// </summary>
public sealed class SelectionSync
{
    private readonly TabEditorControl _editor;
    private readonly Action<SelectionOrigin> _applyToTimeline;
    private bool _applyingToEditor;

    public SelectionSync(SelectionModel model, TabEditorControl editor, Action<SelectionOrigin> applyToTimeline)
    {
        Model = model;
        _editor = editor;
        _applyToTimeline = applyToTimeline;
        model.Changed += OnModelChanged;
    }

    public SelectionModel Model { get; }

    /// <summary>True while the model is being written into the editor (its SelectionChanged is an echo).</summary>
    public bool IsApplyingToEditor => _applyingToEditor;

    private void OnModelChanged(object? sender, SelectionOrigin origin)
    {
        if (origin != SelectionOrigin.Editor) ApplyToEditor();
        _applyToTimeline(origin);
    }

    /// <summary>Makes the score show the model's range; a no-op when it already does.</summary>
    public void ApplyToEditor()
    {
        var s = Model;
        if (s.HasRange ? _editor.SelectionMatches(s.StartBar, s.StartCell, s.EndBar, s.EndCell) : !_editor.HasSelection) return;
        _applyingToEditor = true;
        try
        {
            if (s.HasRange) _editor.SelectRange(s.StartBar, s.StartCell, s.EndBar, s.EndCell);
            else _editor.ClearSelection();
        }
        finally { _applyingToEditor = false; }
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

    /// <summary>After undo/redo, a track switch or an edit that changed the bar count: keep model and score valid and equal.</summary>
    public void Reconcile(int barCount, int trackCount)
    {
        Model.ClampTo(barCount, trackCount);
        if (Model.TrackIndex != _editor.SelectedTrackIndex && _editor.Track is not null)
            Model.SetTrack(_editor.SelectedTrackIndex);
        ApplyToEditor();
    }
}
