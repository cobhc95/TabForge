using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.ScoreEditing;

/// <summary>What the edit commands read and write on the editor: the song, the cursor, the pending entry state, and the host's edit and redraw hooks.</summary>
public interface IScoreEditContext
{
    SongProject? Project { get; }
    TrackModel? Track { get; }
    int SelectedTrackIndex { get; }
    int ActiveVoiceIndex { get; }
    int SelectedMeasure { get; set; }
    int SelectedCell { get; set; }
    int SelectedString { get; set; }
    bool HasSelection { get; }
    /// <summary>The duration, velocity and entry flags new notes use (Core ScoreEditToolState).</summary>
    Presenters.ScoreEditToolState Tools { get; }
    List<TabCell> CellsFor(MeasureModel measure, bool create = false);
    int SlotsFor(int measure);
    /// <summary>Runs one command as one edit (see <see cref="IScoreEditHost.Run"/>), then coerces the cursor, raises Edited and redraws; returns whether the song changed.</summary>
    bool RunEdit(Func<bool> mutate, bool markTimeline = true, bool continuesLastStep = false);
    void PreviewNote(TabNote note);
    /// <summary>The tool state changed without a song edit: toolbars re-read it.</summary>
    void NotifyState();
    void Redraw();
    void Say(string text);
    void SelectRange(int startMeasure, int startCell, int endMeasure, int endCell);
    /// <summary>Ends the selection (a command that consumed it, Undo / Redo); the cursor stays.</summary>
    void ClearSelection();
    (int m1, int c1, int m2, int c2) SelectionRange();
    void SetPosition(int measure, int cell, int stringIndex);
    void MoveForwardBeat(TrackModel track);
}

// Owns: the score editor's note commands (entry and edits of notes, durations, ties, techniques, beat and bar operations) and the tool-state queries the toolbars read.
// Does not own: the editor window and its key routing (the host behind IScoreEditContext), the undo transaction (RunEdit), or the pure note rules (EditCommands).
// Tests: no dedicated test class.

/// <summary>
/// The score editor's note commands (duration, dots, tuplets, ties, techniques, accents, beat and bar operations, fret entry) and the tool-state
/// queries the toolbars read. Every change to the song goes through <see cref="IScoreEditContext.RunEdit"/>.
/// </summary>
public sealed partial class ScoreEditCommands
{
    /// <summary>Status shown when a note command finds no note under the cursor; the host clears it once an edit succeeds.</summary>
    public const string NoNoteMessage = "No note on this string";

    private readonly IScoreEditContext _c;

    public ScoreEditCommands(IScoreEditContext context) => _c = context;

    private SongProject? _project => _c.Project;
    private TrackModel? Track => _c.Track;
    private int SelectedTrackIndex => _c.SelectedTrackIndex;
    private int ActiveVoiceIndex => _c.ActiveVoiceIndex;
    private int _activeVoiceIndex => _c.ActiveVoiceIndex;
    private int SelectedMeasure { get => _c.SelectedMeasure; set => _c.SelectedMeasure = value; }
    private int SelectedCell { get => _c.SelectedCell; set => _c.SelectedCell = value; }
    private int SelectedString { get => _c.SelectedString; set => _c.SelectedString = value; }
    private bool HasSelection => _c.HasSelection;
    private int CurrentDurationDenominator { get => _c.Tools.DurationDenominator; set => _c.Tools.DurationDenominator = value; }
    private int CurrentDots { get => _c.Tools.Dots; set => _c.Tools.Dots = value; }
    private bool CurrentTriplet { get => _c.Tools.Triplet; set => _c.Tools.Triplet = value; }
    private int CurrentTupletNumerator { get => _c.Tools.TupletNumerator; set => _c.Tools.TupletNumerator = value; }
    private int CurrentTupletDenominator { get => _c.Tools.TupletDenominator; set => _c.Tools.TupletDenominator = value; }
    private int CurrentVelocity { get => _c.Tools.Velocity; set => _c.Tools.Velocity = value; }
    // The quick two-digit fret entry window: the last digit typed and where.
    private DateTime _lastDigit = DateTime.MinValue;
    private int _lastDigitMeasure = -1, _lastDigitCell = -1, _lastDigitString = -1;
    private List<TabCell> CellsFor(MeasureModel measure, bool create = false) => _c.CellsFor(measure, create);
    private int SlotsFor(int measure) => _c.SlotsFor(measure);
    private bool RunEdit(Func<bool> mutate, bool markTimeline = true, bool continuesLastStep = false) { ForgetWritingMarks(); return _c.RunEdit(mutate, markTimeline, continuesLastStep); }
    private void PreviewNote(TabNote note) => _c.PreviewNote(note);
    private void InvalidateVisual() => _c.Redraw();
    private bool AutoAdvanceAfterEntry => _c.Tools.AutoAdvanceAfterEntry;
    private bool PreventBarOverflow => _c.Tools.PreventBarOverflow;
    private bool FillBars => _c.Tools.FillBarsWithRests;
    private (int m1, int c1, int m2, int c2) SelectionRange() => _c.SelectionRange();
    private void SetPosition(int measure, int cell, int stringIndex) => _c.SetPosition(measure, cell, stringIndex);
    private void MoveForwardBeat(TrackModel track) => _c.MoveForwardBeat(track);
    // ---------- note editing ----------

    public TabCell? CurrentCell(bool create = false)
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return null;
        var m = track.Measures[SelectedMeasure];
        var cells = CellsFor(m, create: create && _activeVoiceIndex == 1);
        return SelectedCell >= 0 && SelectedCell < cells.Count ? cells[SelectedCell] : null;
    }

    public MeasureModel? CurrentMeasure()
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return null;
        return track.Measures[SelectedMeasure];
    }

    /// <summary>Enter a fret on the selected string (TAB entry).</summary>
    /// <summary>
    /// Runs a catalogued note/beat command (see <see cref="Services.HotkeyCatalog"/>) on the selection.
    /// Returns false for ids the editor does not own; the window handles those.
    /// </summary>
    public bool TryRunNoteCommand(string id)
    {
        switch (id)
        {
            case "Edit.InsertBeat": InsertBeat(); return true;
            case "Edit.DeleteBeats": DeleteBeats(); return true;
            case "Note.Longer": Longer(); return true;
            case "Note.Shorter": Shorter(); return true;
            case "Note.MoveStringUp": MoveNotesToAdjacentString(-1); return true;
            case "Note.MoveStringDown": MoveNotesToAdjacentString(1); return true;
            case "Note.PitchUp": ShiftPitch(1); return true;
            case "Note.PitchDown": ShiftPitch(-1); return true;
            case "Note.RepeatBeat": CopyLastBeat(); return true;
            case "Note.Rest": ToggleRest(); return true;
            case "Note.Tie": ToggleTie(); return true;
            case "Note.Fermata": ToggleFermata(); return true;
            case "Note.Accent": SetAccent(1); return true;
            case "Note.Staccato": ToggleStaccato(); return true;
            case "Note.Tenuto": ToggleTenuto(); return true;
            case "Note.HammerPull": ToggleTechnique(TechniqueNames.Hopo); return true;
            case "Note.Vibrato": ToggleTechnique(TechniqueNames.Vibrato); return true;
            case "Note.Slide": ToggleTechnique(TechniqueNames.LegatoSlide); return true;
            case "Note.LetRing": ToggleTechnique(TechniqueNames.LetRing); return true;
            case "Note.Dead": ToggleDead(); return true;
            case "Note.Ghost": ToggleGhost(); return true;
            case "Note.PalmMute": ToggleTechnique(TechniqueNames.PalmMute); return true;
            case "Note.FadeIn": ToggleTechnique(TechniqueNames.FadeIn); return true;
            case "Note.FadeOut": ToggleTechnique(TechniqueNames.FadeOut); return true;
            case "Note.Bend": ToggleTechnique(TechniqueNames.Bend); return true;
            case "Note.ShiftSlide": ToggleTechnique(TechniqueNames.ShiftSlide); return true;
            case "Note.WideVibrato": ToggleTechnique(TechniqueNames.WideVibrato); return true;
            case "Note.TremoloBar": ToggleTechnique(TechniqueNames.TremoloBar); return true;
            case "Note.TremoloPicking": ToggleTechnique(TechniqueNames.TremoloPick); return true;
            case "Note.ArtificialHarmonic": ToggleTechnique(TechniqueNames.ArtificialHarmonic); return true;
            case "Note.Tapping": ToggleTechnique(TechniqueNames.Tapping); return true;
            case "Note.Slap": ToggleTechnique(TechniqueNames.Slap); return true;
            case "Note.Pop": ToggleTechnique(TechniqueNames.Pop); return true;
            case "Note.WahOpen": ToggleTechnique(TechniqueNames.WahOpen); return true;
            case "Note.WahClose": ToggleTechnique(TechniqueNames.WahClose); return true;
            case "Note.BrushDown": ToggleTechnique(TechniqueNames.BrushDown); return true;
            case "Note.BrushUp": ToggleTechnique(TechniqueNames.BrushUp); return true;
            case "Note.ArpeggioDown": ToggleTechnique(TechniqueNames.ArpeggioDown); return true;
            case "Note.ArpeggioUp": ToggleTechnique(TechniqueNames.ArpeggioUp); return true;
            case "Note.Dot": ToggleDot(); return true;
            case "Note.DoubleDot": SetDots(2); return true;
            case "Note.Triplet": ToggleTriplet(); return true;
            case "Bar.RepeatOpen": ToggleRepeatOpen(); return true;
            case "Bar.RepeatClose": ToggleRepeatClose(); return true;
        }
        return false;
    }

    /// <summary>Highest typed/clicked number: the track's last fret, or on a drum track the GM percussion note itself (up to 127).</summary>
    private static int MaxEntryNumber(TrackModel track) =>
        track.IsDrumTrack ? 127 : track.NumberOfFrets > 0 ? track.NumberOfFrets : 24;

    public void EnterFret(int digit, bool autoAdvance = true)
    {
        var track = Track;
        var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        var editingExistingNote = false;
        var prior = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        var append = prior is not null && (DateTime.UtcNow - _lastDigit).TotalMilliseconds < 700   // the second digit of a two-digit fret: one undo step with the first
                     && _lastDigitMeasure == SelectedMeasure && _lastDigitCell == SelectedCell
                     && _lastDigitString == SelectedString && prior.Fret < 10 && prior.Fret > 0;
        RunEdit(() =>
        {
            ApplyPendingDuration(cell);
            SplitBeatTie(cell, SelectedString);

            var existing = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
            editingExistingNote = existing is not null;
            var fret = Math.Clamp(append && existing is not null && existing.Fret * 10 + digit <= MaxEntryNumber(track) ? existing.Fret * 10 + digit : digit, 0, MaxEntryNumber(track));   // two digits past the last fret: the second replaces the first
            if (existing is null)
            {
                existing = new TabNote { StringIndex = SelectedString, Velocity = CurrentVelocity };
                cell.Notes.Add(existing);
            }
            existing.Fret = fret;
            existing.Dead = false;
            existing.MidiValue = MidiOf(track, SelectedString, fret);
            _lastDigit = DateTime.UtcNow; _lastDigitMeasure = SelectedMeasure; _lastDigitCell = SelectedCell; _lastDigitString = SelectedString;

            PreviewNote(existing);
            FillBarAfterWrite();
            return true;
        }, continuesLastStep: append);
        // Auto-advance is useful for entering a passage, but changing an existing
        // note should leave the edit cursor on that note for further adjustments.
        if (!editingExistingNote && autoAdvance && AutoAdvanceAfterEntry) MoveForwardBeat(track);
    }

    /// <summary>Toggle the fretboard's note on the selected beat/string using the editor edit lifecycle.</summary>
    public bool ToggleFretAtPosition(int stringIndex, int fret)
    {
        var track = Track;
        var cell = CurrentCell(create: true);
        if (track is null || cell is null || track.StringTunings.Count == 0 ||
            stringIndex < 0 || stringIndex >= track.StringTunings.Count || fret < 0 || fret > MaxEntryNumber(track))
            return false;

        RunEdit(() =>
        {
            var sameString = cell.Notes.Select((note, index) => (note, index))
                .Where(item => item.note.StringIndex == stringIndex).ToList();
            var existing = sameString.FirstOrDefault().note;
            var remove = sameString.Count > 0 && !existing.Dead && existing.Fret == fret;
            if (remove)
            {
                cell.Notes.RemoveAll(note => note.StringIndex == stringIndex);
            }
            else
            {
                ApplyPendingDuration(cell);
                SplitBeatTie(cell, stringIndex);
                cell.IsRest = false;
                var midi = MidiOf(track, stringIndex, fret);
                if (sameString.Count == 0)
                {
                    existing = new TabNote { StringIndex = stringIndex, Velocity = CurrentVelocity };
                    cell.Notes.Add(existing);
                }
                else
                {
                    existing.Fret = fret;
                    existing.Dead = false;
                    existing.MidiValue = midi;
                    for (var i = sameString.Count - 1; i >= 1; i--)
                        cell.Notes.Remove(sameString[i].note);
                }
                existing.Fret = fret;
                existing.Dead = false;
                existing.MidiValue = midi;
                PreviewNote(existing);
                FillBarAfterWrite();
            }

            _lastDigitMeasure = _lastDigitCell = _lastDigitString = -1;
            SetPosition(SelectedMeasure, SelectedCell, stringIndex);
            return true;
        });
        return true;
    }

    /// <summary>Notation entry: digits pick the string, the fret is chosen automatically.</summary>
    public void EnterStringOnStaff(int stringNumber)
    {
        var track = Track;
        var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        var target = stringNumber == 0 ? BestStringFor(track, cell) : Math.Clamp(stringNumber - 1, 0, track.StringTunings.Count - 1);
        RunEdit(() =>
        {
            ApplyPendingDuration(cell);
            var note = cell.Notes.FirstOrDefault(n => n.StringIndex == target) ?? new TabNote { StringIndex = target, Velocity = CurrentVelocity };
            if (!cell.Notes.Contains(note)) cell.Notes.Add(note);
            note.Fret = note.Fret == 0 ? 5 : note.Fret;
            note.MidiValue = MidiOf(track, target, note.Fret);
            PreviewNote(note);
            FillBarAfterWrite();
            return true;
        });
    }

    private int BestStringFor(TrackModel track, TabCell cell)
    {
        var used = cell.Notes.Select(n => n.StringIndex).ToHashSet();
        for (var s = 0; s < track.StringTunings.Count; s++) if (!used.Contains(s)) return s;
        return 0;
    }

    // Drum tracks: the number typed is the GM percussion note itself (as in the reference); otherwise tuning + capo + fret.
    internal static int MidiOf(TrackModel track, int stringIndex, int fret) => Services.EditCommands.NoteMidi(track, stringIndex, fret);

}
