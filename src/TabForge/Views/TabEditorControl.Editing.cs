using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Views;

// TabEditorControl: the editor's command API. The commands live in ScoreEditCommands; this is the control's public surface and the context they run in.
public sealed partial class TabEditorControl : IScoreEditContext
{
    private ScoreEditCommands? _editsOwned;
    private ScoreEditCommands _edits => _editsOwned ??= new ScoreEditCommands(this);

    public TabCell? CurrentCell(bool create = false) => _edits.CurrentCell(create);
    public bool? GetToolState(string toolId) => _edits.GetToolState(toolId);
    public void SetDynamicVelocity(int velocity) => _edits.SetDynamicVelocity(velocity);
    public void SetTuplet(int numerator, int denominator) => _edits.SetTuplet(numerator, denominator);
    public void SetTupletEntryState(int numerator, int denominator) => _edits.SetTupletEntryState(numerator, denominator);
    public MeasureModel? CurrentMeasure() => _edits.CurrentMeasure();
    public int CurrentDurationSlots() => _edits.CurrentDurationSlots();
    public bool TryRunNoteCommand(string id) => _edits.TryRunNoteCommand(id);
    public void EnterFret(int digit, bool autoAdvance = true) => _edits.EnterFret(digit, autoAdvance);
    public bool ToggleFretAtPosition(int stringIndex, int fret) => _edits.ToggleFretAtPosition(stringIndex, fret);
    public void EnterStringOnStaff(int stringNumber) => _edits.EnterStringOnStaff(stringNumber);
    public void SetDuration(int denominator) => _edits.SetDuration(denominator);
    public void Longer() => _edits.Longer();
    public void Shorter() => _edits.Shorter();
    public void ToggleDot() => _edits.ToggleDot();
    public void SetDots(int dots) => _edits.SetDots(dots);
    public void ToggleTriplet() => _edits.ToggleTriplet();
    public bool CanSetDuration(int denominator) => _edits.CanSetDuration(denominator);
    public bool CanSetDots(int dots) => _edits.CanSetDots(dots);
    public bool CanSetTuplet((int Numerator, int Denominator) ratio) => _edits.CanSetTuplet(ratio);
    public bool HasEditableNotes => _edits.HasEditableNotes;
    public bool HasSecondaryBeamEligibleNotes => _edits.HasSecondaryBeamEligibleNotes;
    public bool? GetNoteCellToolState(Func<TabCell, bool> state) => _edits.GetNoteCellToolState(state);
    public void SetSoundDurationPercent(int percent) => _edits.SetSoundDurationPercent(percent);
    public void SetOctaveShift(int semitones) => _edits.SetOctaveShift(semitones);
    public void SetBeamMode(BeamMode mode) => _edits.SetBeamMode(mode);
    public void ResetBeaming() => _edits.ResetBeaming();
    public void SetSecondaryBeamBreak(bool value) => _edits.SetSecondaryBeamBreak(value);
    public void SetStemDirection(StemDirection direction) => _edits.SetStemDirection(direction);
    public bool CanTieSelectedNote() => _edits.CanTieSelectedNote();
    public bool CanTieSelectedBeat() => _edits.CanTieSelectedBeat();
    public void TieSelectedNote() => _edits.TieSelectedNote();
    public void TieSelectedBeat() => _edits.TieSelectedBeat();
    public bool CanSetDurationForTool(string key) => _edits.CanSetDurationForTool(key);
    public void ToggleRest() => _edits.ToggleRest();
    public void ToggleTie() => _edits.ToggleTie();
    public void ToggleFermata() => _edits.ToggleFermata();
    public void CycleAccent() => _edits.CycleAccent();
    public void SetAccent(int accent) => _edits.SetAccent(accent);
    public void ToggleStaccato() => _edits.ToggleStaccato();
    public void ToggleTenuto() => _edits.ToggleTenuto();
    public void ToggleTechnique(string technique) => _edits.ToggleTechnique(technique);
    public void ToggleDead() => _edits.ToggleDead();
    public void ToggleGhost() => _edits.ToggleGhost();
    public void DeleteNote() => _edits.DeleteNote();
    public void DeleteBeat() => _edits.DeleteBeat();
    public void InsertBeat() => _edits.InsertBeat();
    public void DeleteBeats() => _edits.DeleteBeats();
    public void CopyLastBeat() => _edits.CopyLastBeat();
    public void EmptyBar() => _edits.EmptyBar();

    public void ToggleRepeatOpen() => _edits.ToggleRepeatOpen();
    public void ToggleRepeatClose(int? count = null) => _edits.ToggleRepeatClose(count);
    public bool MoveNotesToAdjacentString(int delta) => _edits.MoveNotesToAdjacentString(delta);
    public void ShiftPitch(int semitones) => _edits.ShiftPitch(semitones);

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
    DateTime IScoreEditContext.LastDigitTime { get => _lastDigit; set => _lastDigit = value; }
    int IScoreEditContext.LastDigitMeasure { get => _lastDigitMeasure; set => _lastDigitMeasure = value; }
    int IScoreEditContext.LastDigitCell { get => _lastDigitCell; set => _lastDigitCell = value; }
    int IScoreEditContext.LastDigitString { get => _lastDigitString; set => _lastDigitString = value; }
    List<TabCell> IScoreEditContext.CellsFor(MeasureModel measure, bool create) => CellsFor(measure, create);
    int IScoreEditContext.SlotsFor(int measure) => SlotsFor(measure);
    bool IScoreEditContext.RunEdit(Func<bool> mutate, bool markTimeline) => RunEdit(mutate, markTimeline);
    void IScoreEditContext.PreviewNote(TabNote note) => PreviewNote(note);
    void IScoreEditContext.NotifyState() => SelectionChanged?.Invoke(this, EventArgs.Empty);
    void IScoreEditContext.Redraw() => InvalidateVisual();
    void IScoreEditContext.Say(string text) => StatusMessage?.Invoke(this, text);
    bool IScoreEditContext.AutoAdvanceAfterEntry => AutoAdvanceAfterEntry;
    bool IScoreEditContext.PreventBarOverflow => PreventBarOverflow;
    bool IScoreEditContext.FillBarsWithRests => FillBarsWithRests;
    bool IScoreEditContext.MergeRestsOnDelete => MergeRestsOnDelete && FillBarsWithRests;
    void IScoreEditContext.SelectRange(int startMeasure, int startCell, int endMeasure, int endCell) => SelectRange(startMeasure, startCell, endMeasure, endCell);
    (int m1, int c1, int m2, int c2) IScoreEditContext.SelectionRange() => SelectionRange();
    void IScoreEditContext.SetPosition(int measure, int cell, int stringIndex) => SetPosition(measure, cell, stringIndex);
    void IScoreEditContext.MoveForwardBeat(TrackModel track) => MoveForwardBeat(track);
}
