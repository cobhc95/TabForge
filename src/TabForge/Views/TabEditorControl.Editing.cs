using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Views;

// TabEditorControl: the editor's command API. The commands live in ScoreEditCommands; this is the control's public surface and the context they run in.
public sealed partial class TabEditorControl : IScoreEditContext
{
    private ScoreEditCommands? _editsOwned;
    private ScoreEditCommands _edits => _editsOwned ??= new ScoreEditCommands(this);

    /// <summary>The commands the note-effect editor dialogs use (selection and one-undo-step edits).</summary>
    public ScoreEditCommands Effects => _edits;


    public event EventHandler<string>? StatusMessage;

    SongProject? IScoreEditContext.Project => _project;
    TrackModel? IScoreEditContext.Track => Track;
    int IScoreEditContext.SelectedTrackIndex => SelectedTrackIndex;
    int IScoreEditContext.ActiveVoiceIndex => _activeVoiceIndex;
    int IScoreEditContext.SelectedMeasure { get => SelectedMeasure; set => SelectedMeasure = value; }
    int IScoreEditContext.SelectedCell { get => SelectedCell; set => SelectedCell = value; }
    int IScoreEditContext.SelectedString { get => SelectedString; set => SelectedString = value; }
    bool IScoreEditContext.HasSelection => HasSelection;
    int IScoreEditContext.CurrentDurationDenominator { get => CurrentDurationDenominator; set => CurrentDurationDenominator = value; }
    int IScoreEditContext.CurrentDots { get => CurrentDots; set => CurrentDots = value; }
    bool IScoreEditContext.CurrentTriplet { get => CurrentTriplet; set => CurrentTriplet = value; }
    int IScoreEditContext.CurrentTupletNumerator { get => CurrentTupletNumerator; set => CurrentTupletNumerator = value; }
    int IScoreEditContext.CurrentTupletDenominator { get => CurrentTupletDenominator; set => CurrentTupletDenominator = value; }
    int IScoreEditContext.CurrentVelocity { get => CurrentVelocity; set => CurrentVelocity = value; }
    List<TabCell> IScoreEditContext.CellsFor(MeasureModel measure, bool create) => CellsFor(measure, create);
    int IScoreEditContext.SlotsFor(int measure) => SlotsFor(measure);
    bool IScoreEditContext.RunEdit(Func<bool> mutate, bool markTimeline, bool continuesLastStep) => RunEdit(mutate, markTimeline, continuesLastStep);
    void IScoreEditContext.PreviewNote(TabNote note) => PreviewNote(note);
    // A tool-state change is not a cursor move: the toolbars re-read it, playback never seeks to the cursor.
    void IScoreEditContext.NotifyState()
    {
        _selectionShouldSeekPlayback = false;
        try { SelectionChanged?.Invoke(this, EventArgs.Empty); }
        finally { _selectionShouldSeekPlayback = true; }
    }
    void IScoreEditContext.Redraw() => InvalidateVisual();
    void IScoreEditContext.Say(string text) => StatusMessage?.Invoke(this, text);
    bool IScoreEditContext.AutoAdvanceAfterEntry => AutoAdvanceAfterEntry;
    bool IScoreEditContext.PreventBarOverflow => PreventBarOverflow;
    bool IScoreEditContext.FillBarsWithRests => FillBarsWithRests;
    bool IScoreEditContext.MergeRestsOnDelete => MergeRestsOnDelete && FillBarsWithRests;
    void IScoreEditContext.SelectRange(int startMeasure, int startCell, int endMeasure, int endCell) => SelectRange(startMeasure, startCell, endMeasure, endCell);
    void IScoreEditContext.ClearSelection() => ClearSelection();
    (int m1, int c1, int m2, int c2) IScoreEditContext.SelectionRange() => SelectionRange();
    void IScoreEditContext.SetPosition(int measure, int cell, int stringIndex) => SetPosition(measure, cell, stringIndex);
    void IScoreEditContext.MoveForwardBeat(TrackModel track) => MoveForwardBeat(track, seekPlayback: false);
}
