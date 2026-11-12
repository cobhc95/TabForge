using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;
using TabForge.Commands;

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
    void SelectRange(int startMeasure, int startCell, int endMeasure, int endCell);
    /// <summary>Ends the selection (a command that consumed it, Undo / Redo); the cursor stays.</summary>
    void ClearSelection();
    (int m1, int c1, int m2, int c2) SelectionRange();
    void SetPosition(int measure, int cell, int stringIndex);
    void MoveForwardBeat(TrackModel track);
}

// Owns: the score editor's note commands (entry and edits of notes, durations, ties, techniques, beat and bar operations) and the tool-state queries the toolbars read.
// Does not own: the editor window and its key routing (the host behind IScoreEditContext), the undo transaction (RunEdit), or the pure note rules (EditCommands).
// Tests: ScoreNoteEditTests, ScoreEditKeyBehaviourTests, ScoreEntryBeamTests (the commands read the context through _c directly; the context has no
//   message or redraw hooks: the view repaints after every handled key, and the old Say/NotifyState/Redraw hooks were empty and are gone).

/// <summary>
/// The score editor's note commands (duration, dots, tuplets, ties, techniques, accents, beat and bar operations, fret entry) and the tool-state
/// queries the toolbars read. Every change to the song goes through <see cref="IScoreEditContext.RunEdit"/>.
/// </summary>
public sealed partial class ScoreEditCommands
{
    private readonly IScoreEditContext _c;

    public ScoreEditCommands(IScoreEditContext context) => _c = context;

    // The quick two-digit fret entry window: the last digit typed and where.
    private DateTime _lastDigit = DateTime.MinValue;
    private int _lastDigitMeasure = -1, _lastDigitCell = -1, _lastDigitString = -1;
    private bool RunEdit(Func<bool> mutate, bool markTimeline = true, bool continuesLastStep = false) { ForgetWritingMarks(); return _c.RunEdit(mutate, markTimeline, continuesLastStep); }
    // ---------- note editing ----------

    public TabCell? CurrentCell(bool create = false)
    {
        var track = _c.Track;
        if (track is null || _c.SelectedMeasure < 0 || _c.SelectedMeasure >= track.Measures.Count) return null;
        var m = track.Measures[_c.SelectedMeasure];
        var cells = _c.CellsFor(m, create: create && _c.ActiveVoiceIndex == 1);
        return _c.SelectedCell >= 0 && _c.SelectedCell < cells.Count ? cells[_c.SelectedCell] : null;
    }

    public MeasureModel? CurrentMeasure()
    {
        var track = _c.Track;
        if (track is null || _c.SelectedMeasure < 0 || _c.SelectedMeasure >= track.Measures.Count) return null;
        return track.Measures[_c.SelectedMeasure];
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
            case CommandIds.Note.MoveStringUp: MoveNotesToAdjacentString(-1); return true;
            case CommandIds.Note.MoveStringDown: MoveNotesToAdjacentString(1); return true;
            case CommandIds.Note.PitchUp: ShiftPitch(1); return true;
            case CommandIds.Note.PitchDown: ShiftPitch(-1); return true;
            case CommandIds.Note.RepeatBeat: CopyLastBeat(); return true;
            case "Note.Rest": ToggleRest(); return true;
            case CommandIds.Note.Tie: ToggleTie(); return true;
            case CommandIds.Note.Fermata: ToggleFermata(); return true;
            case CommandIds.Note.Accent: SetAccent(1); return true;
            case CommandIds.Note.Staccato: ToggleStaccato(); return true;
            case "Note.Tenuto": ToggleTenuto(); return true;
            case CommandIds.Note.HammerPull: ToggleTechnique(TechniqueNames.Hopo); return true;
            case CommandIds.Note.Vibrato: ToggleTechnique(TechniqueNames.Vibrato); return true;
            case CommandIds.Note.Slide: ToggleTechnique(TechniqueNames.LegatoSlide); return true;
            case CommandIds.Note.LetRing: ToggleTechnique(TechniqueNames.LetRing); return true;
            case CommandIds.Note.Dead: ToggleDead(); return true;
            case CommandIds.Note.Ghost: ToggleGhost(); return true;
            case CommandIds.Note.PalmMute: ToggleTechnique(TechniqueNames.PalmMute); return true;
            case CommandIds.Note.FadeIn: ToggleTechnique(TechniqueNames.FadeIn); return true;
            case "Note.FadeOut": ToggleTechnique(TechniqueNames.FadeOut); return true;
            case CommandIds.Note.Bend: ToggleTechnique(TechniqueNames.Bend); return true;
            case "Note.ShiftSlide": ToggleTechnique(TechniqueNames.ShiftSlide); return true;
            case "Note.WideVibrato": ToggleTechnique(TechniqueNames.WideVibrato); return true;
            case CommandIds.Note.TremoloBar: ToggleTechnique(TechniqueNames.TremoloBar); return true;
            case CommandIds.Note.TremoloPicking: ToggleTechnique(TechniqueNames.TremoloPick); return true;
            case "Note.ArtificialHarmonic": ToggleTechnique(TechniqueNames.ArtificialHarmonic); return true;
            case CommandIds.Note.Tapping: ToggleTechnique(TechniqueNames.Tapping); return true;
            case CommandIds.Note.Slap: ToggleTechnique(TechniqueNames.Slap); return true;
            case CommandIds.Note.Pop: ToggleTechnique(TechniqueNames.Pop); return true;
            case "Note.WahOpen": ToggleTechnique(TechniqueNames.WahOpen); return true;
            case "Note.WahClose": ToggleTechnique(TechniqueNames.WahClose); return true;
            case CommandIds.Note.BrushDown: ToggleTechnique(TechniqueNames.BrushDown); return true;
            case CommandIds.Note.BrushUp: ToggleTechnique(TechniqueNames.BrushUp); return true;
            case "Note.ArpeggioDown": ToggleTechnique(TechniqueNames.ArpeggioDown); return true;
            case "Note.ArpeggioUp": ToggleTechnique(TechniqueNames.ArpeggioUp); return true;
            case CommandIds.Note.Dot: ToggleDot(); return true;
            case CommandIds.Note.DoubleDot: SetDots(2); return true;
            case CommandIds.Note.Triplet: ToggleTriplet(); return true;
            case CommandIds.Bar.RepeatOpen: ToggleRepeatOpen(); return true;
            case CommandIds.Bar.RepeatClose: ToggleRepeatClose(); return true;
        }
        return false;
    }

    /// <summary>Highest typed/clicked number: the track's last fret, or on a drum track the GM percussion note itself (up to 127).</summary>
    private static int MaxEntryNumber(TrackModel track) =>
        track.IsDrumTrack ? 127 : track.NumberOfFrets > 0 ? track.NumberOfFrets : 24;

    public void EnterFret(int digit, bool autoAdvance = true)
    {
        var track = _c.Track;
        var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        var editingExistingNote = false;
        var prior = cell.Notes.FirstOrDefault(n => n.StringIndex == _c.SelectedString);
        var append = prior is not null && (DateTime.UtcNow - _lastDigit).TotalMilliseconds < 700   // the second digit of a two-digit fret: one undo step with the first
                     && _lastDigitMeasure == _c.SelectedMeasure && _lastDigitCell == _c.SelectedCell
                     && _lastDigitString == _c.SelectedString && prior.Fret < 10 && prior.Fret > 0;
        RunEdit(() =>
        {
            ApplyPendingDuration(cell);
            SplitBeatTie(cell, _c.SelectedString);

            var existing = cell.Notes.FirstOrDefault(n => n.StringIndex == _c.SelectedString);
            editingExistingNote = existing is not null;
            var fret = Math.Clamp(append && existing is not null && existing.Fret * 10 + digit <= MaxEntryNumber(track) ? existing.Fret * 10 + digit : digit, 0, MaxEntryNumber(track));   // two digits past the last fret: the second replaces the first
            if (existing is null)
            {
                existing = new TabNote { StringIndex = _c.SelectedString, Velocity = _c.Tools.Velocity };
                cell.Notes.Add(existing);
            }
            existing.Fret = fret;
            existing.Dead = false;
            existing.MidiValue = MidiOf(track, _c.SelectedString, fret);
            _lastDigit = DateTime.UtcNow; _lastDigitMeasure = _c.SelectedMeasure; _lastDigitCell = _c.SelectedCell; _lastDigitString = _c.SelectedString;

            _c.PreviewNote(existing);
            FillBarAfterWrite();
            return true;
        }, continuesLastStep: append);
        // Auto-advance is useful for entering a passage, but changing an existing
        // note should leave the edit cursor on that note for further adjustments.
        if (!editingExistingNote && autoAdvance && _c.Tools.AutoAdvanceAfterEntry) _c.MoveForwardBeat(track);
    }

    /// <summary>Toggle the fretboard's note on the selected beat/string using the editor edit lifecycle.</summary>
    public bool ToggleFretAtPosition(int stringIndex, int fret)
    {
        var track = _c.Track;
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
                    existing = new TabNote { StringIndex = stringIndex, Velocity = _c.Tools.Velocity };
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
                _c.PreviewNote(existing);
                FillBarAfterWrite();
            }

            _lastDigitMeasure = _lastDigitCell = _lastDigitString = -1;
            _c.SetPosition(_c.SelectedMeasure, _c.SelectedCell, stringIndex);
            return true;
        });
        return true;
    }

    // Drum tracks: the number typed is the GM percussion note itself (as in the reference); otherwise tuning + capo + fret.
    internal static int MidiOf(TrackModel track, int stringIndex, int fret) => Services.EditCommands.NoteMidi(track, stringIndex, fret);

}
