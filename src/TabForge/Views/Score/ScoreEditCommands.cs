using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

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
    int CurrentDurationDenominator { get; set; }
    int CurrentDots { get; set; }
    bool CurrentTriplet { get; set; }
    int CurrentTupletNumerator { get; set; }
    int CurrentTupletDenominator { get; set; }
    int CurrentVelocity { get; set; }
    DateTime LastDigitTime { get; set; }
    int LastDigitMeasure { get; set; }
    int LastDigitCell { get; set; }
    int LastDigitString { get; set; }
    List<TabCell> CellsFor(MeasureModel measure, bool create = false);
    int SlotsFor(int measure);
    /// <summary>Runs one command as one edit (see <see cref="IScoreEditHost.Run"/>), then coerces the cursor, raises Edited and redraws; returns whether the song changed.</summary>
    bool RunEdit(Func<bool> mutate, bool markTimeline = true);
    void PreviewNote(TabNote note);
    /// <summary>The tool state changed without a song edit: toolbars re-read it.</summary>
    void NotifyState();
    void Redraw();
    void Say(string text);
    bool AutoAdvanceAfterEntry { get; }
    bool PreventBarOverflow { get; }
    bool FillBarsWithRests { get; }
    bool MergeRestsOnDelete { get; }
    void SelectRange(int startMeasure, int startCell, int endMeasure, int endCell);
    (int m1, int c1, int m2, int c2) SelectionRange();
    void SetPosition(int measure, int cell, int stringIndex);
    void MoveForwardBeat(TrackModel track);
}

/// <summary>
/// The score editor's note commands (duration, dots, tuplets, ties, techniques, accents, beat and bar operations, fret entry) and the tool-state
/// queries the toolbars read. Every change to the song goes through <see cref="IScoreEditContext.RunEdit"/>.
/// </summary>
public sealed partial class ScoreEditCommands
{
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
    private int CurrentDurationDenominator { get => _c.CurrentDurationDenominator; set => _c.CurrentDurationDenominator = value; }
    private int CurrentDots { get => _c.CurrentDots; set => _c.CurrentDots = value; }
    private bool CurrentTriplet { get => _c.CurrentTriplet; set => _c.CurrentTriplet = value; }
    private int CurrentTupletNumerator { get => _c.CurrentTupletNumerator; set => _c.CurrentTupletNumerator = value; }
    private int CurrentTupletDenominator { get => _c.CurrentTupletDenominator; set => _c.CurrentTupletDenominator = value; }
    private int CurrentVelocity { get => _c.CurrentVelocity; set => _c.CurrentVelocity = value; }
    private DateTime _lastDigit { get => _c.LastDigitTime; set => _c.LastDigitTime = value; }
    private int _lastDigitMeasure { get => _c.LastDigitMeasure; set => _c.LastDigitMeasure = value; }
    private int _lastDigitCell { get => _c.LastDigitCell; set => _c.LastDigitCell = value; }
    private int _lastDigitString { get => _c.LastDigitString; set => _c.LastDigitString = value; }
    private List<TabCell> CellsFor(MeasureModel measure, bool create = false) => _c.CellsFor(measure, create);
    private int SlotsFor(int measure) => _c.SlotsFor(measure);
    private bool RunEdit(Func<bool> mutate, bool markTimeline = true) => _c.RunEdit(mutate, markTimeline);
    private void PreviewNote(TabNote note) => _c.PreviewNote(note);
    private void InvalidateVisual() => _c.Redraw();
    private bool AutoAdvanceAfterEntry => _c.AutoAdvanceAfterEntry;
    private bool PreventBarOverflow => _c.PreventBarOverflow;
    private bool FillBars => _c.FillBarsWithRests;
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

    /// <summary>Returns a single active state for the toolbox, or null when it is absent/mixed.</summary>
    public bool? GetToolState(string toolId)
    {
        var cells = ToolCells();
        if (cells.Count == 0) return null;
        var measure = CurrentMeasure();
        var notes = ToolNotes(cells);

        if (toolId == "edit:pointer") return !HasSelection;
        if (toolId.StartsWith("duration:", StringComparison.Ordinal))
        {
            var key = toolId[9..];
            var populated = cells.Where(cell => (cell.Notes.Count > 0 || cell.IsRest) && !WritingDuration.ToolsShowWriting(cell, HasSelection)).ToList();
            return key switch
            {
                "whole" or "half" or "quarter" or "eighth" or "sixteenth" or "thirtysecond" or "sixtyfourth" =>
                    Uniform((populated.Count > 0 ? populated.Select(cell => cell.DurationDenominator) : new[] { CurrentDurationDenominator })
                        .Select(value => value == DurationForKey(key))),
                "dotted" => Uniform((populated.Count > 0 ? populated.Select(cell => cell.Dots) : new[] { CurrentDots }).Select(value => value == 1)),
                "double-dotted" => Uniform((populated.Count > 0 ? populated.Select(cell => cell.Dots) : new[] { CurrentDots }).Select(value => value == 2)),
                "tuplet" => Uniform((populated.Count > 0 ? populated.Select(cell => cell.IsTriplet || cell.TupletNumerator > 0) : new[] { CurrentTupletNumerator > 0 || CurrentTriplet }).Select(value => value)),
                "tie" => Uniform(cells.SelectMany(cell => cell.Notes.Count > 0
                    ? cell.Notes.Select(note => cell.IsTied || note.Tied)
                    : new[] { cell.IsTied })),
                _ => null
            };
        }
        if (toolId.StartsWith("dynamic:", StringComparison.Ordinal))
        {
            var velocity = DynamicVelocity(toolId[8..]);
            if (notes.Count == 0) return HasSelection ? null : CurrentVelocity == velocity;
            return Uniform(notes.Select(note => NearestDynamicVelocity(note.Velocity) == velocity));
        }
        if (toolId.StartsWith("effect:", StringComparison.Ordinal))
        {
            var key = toolId[7..];
            if (key is "accent" or "heavy_accent")
                return Uniform(cells.Select(cell => cell.Accent == (key == "accent" ? 1 : 2)));
            if (key == "staccato") return Uniform(cells.Select(cell => cell.Staccato));
            if (key == "grace_note") return Uniform(cells.SelectMany(cell => cell.Notes.Count > 0
                ? cell.Notes.Select(note => cell.IsGrace || note.Techniques.Contains("GraceBefore") ||
                    note.Techniques.Contains("GraceOnBeat") || note.Techniques.Contains("GraceBend"))
                : new[] { cell.IsGrace }));
            if (key == "dead_note") return Uniform(notes.Select(note => note.Dead));
            if (key == "ghost_note") return Uniform(notes.Select(note => note.Ghost));
            var techniques = key switch
            {
                "slides" => new[] { "Slide", "LegatoSlide", "ShiftSlide", "SlideInBelow", "SlideInAbove",
                    "SlideOutUp", "SlideOutDown", "PickSlideUp", "PickSlideDown" },
                "hammer_on_pull_off" => new[] { "HOPO", "HOPOOrigin", "HOPODestination" },
                "natural_harmonic" => new[] { "Harmonic" },
                "tremolo_bar" => new[] { "TremBar", "TremBarWide" },
                "tremolo_picking" => new[] { "TremoloPick" },
                "stroke_down" => new[] { "BrushDown" },
                "stroke_up" => new[] { "BrushUp" },
                "pickstroke_down" => new[] { "PickDown" },
                "pickstroke_up" => new[] { "PickUp" },
                "chord" or "chord_menu" => Array.Empty<string>(),
                "text" => Array.Empty<string>(),
                _ => new[] { key switch
                {
                    "palm_mute" => "PalmMute", "let_ring" => "LetRing", "bend" => "Bend",
                    "vibrato" => "Vibrato", "trill" => "Trill", "tapping" => "Tapping",
                    "slapping" => "Slap", "popping" => "Pop", "fade_in" => "FadeIn",
                    _ => key
                } }
            };
            if (key is "chord" or "chord_menu") return Uniform(cells.Select(cell => !string.IsNullOrWhiteSpace(cell.ChordName)));
            if (key == "text") return Uniform(cells.Select(cell => !string.IsNullOrWhiteSpace(cell.Text)));
            return Uniform(notes.Select(note => techniques.Any(note.Techniques.Contains)));
        }
        if (toolId.StartsWith("beat:", StringComparison.Ordinal) || toolId.StartsWith("composition:", StringComparison.Ordinal))
        {
            var key = toolId[(toolId.IndexOf(':') + 1)..];
            return key switch
            {
                "alternate_ending" => measure is null ? null : measure.AlternateEnding > 0,
                "time_signature" => measure is null ? null : measure.TimeSigNum.HasValue || measure.TimeSigDenom.HasValue,
                "tempo" => measure?.TempoChange.HasValue,
                "repeat_open" => measure?.RepeatStart,
                "repeat_close" => measure?.RepeatEnd,
                _ => null
            };
        }
        return null;
    }

    internal List<TabCell> ToolCells(bool createVoice = false)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return new List<TabCell>();
        if (!HasSelection)
        {
            var current = CurrentCell();
            return current is null ? new List<TabCell>() : new List<TabCell> { current };
        }

        var (m1, c1, m2, c2) = SelectionRange();
        if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
        var selected = new List<TabCell>();
        for (var m = Math.Max(0, m1); m <= Math.Min(m2, track.Measures.Count - 1); m++)
        {
            var measure = track.Measures[m];
            var first = m == m1 ? c1 : 0;
            var cells = CellsFor(measure, create: createVoice && _activeVoiceIndex == 1);
            var last = m == m2 ? c2 : cells.Count - 1;
            for (var c = Math.Max(0, first); c <= Math.Min(last, cells.Count - 1); c++) selected.Add(cells[c]);
        }
        return selected;
    }

    private List<TabNote> ToolNotes(IReadOnlyList<TabCell> cells)
    {
        if (HasSelection) return cells.SelectMany(cell => cell.Notes).ToList();
        return cells[0].Notes.Where(note => note.StringIndex == SelectedString).ToList();
    }

    private static bool? Uniform(IEnumerable<bool> values)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext()) return null;
        var first = enumerator.Current;
        while (enumerator.MoveNext()) if (enumerator.Current != first) return null;
        return first;
    }

    private static int DurationForKey(string key) => key switch
    {
        "whole" => 1, "half" => 2, "quarter" => 4, "eighth" => 8,
        "sixteenth" => 16, "thirtysecond" => 32, "sixtyfourth" => 64, _ => 0
    };

    private static int DynamicVelocity(string key) => Dynamics.VelocityFor(key);

    private static int NearestDynamicVelocity(int velocity) => Dynamics.Velocities[Dynamics.NearestIndex(velocity)];

    private bool ApplyToolSelection(Action<IReadOnlyList<TabCell>> edit)
    {
        if (!HasSelection) return false;
        var cells = ToolCells(createVoice: true);
        if (cells.Count == 0) return true;
        RunEdit(() => { edit(cells); return true; });
        return true;
    }

    /// <summary>Set the dynamic on selected notes, or the pending entry dynamic at an empty cursor.</summary>
    public void SetDynamicVelocity(int velocity)
    {
        CurrentVelocity = Math.Clamp(velocity, 1, 127);
        var notes = HasSelection
            ? ToolCells(createVoice: true).SelectMany(cell => cell.Notes).ToList()
            : CurrentCell()?.Notes.Where(note => note.StringIndex == SelectedString).ToList() ?? new List<TabNote>();
        if (notes.Count == 0)
        {
            NotifyState();
            InvalidateVisual();
            return;
        }
        RunEdit(() => { foreach (var note in notes) note.Velocity = CurrentVelocity; return true; });
    }

    /// <summary>Apply an explicit tuplet ratio to the selected beats or the current beat.</summary>
    public void SetTuplet(int numerator, int denominator)
    {
        if (numerator < 2 || denominator < 1 || numerator > 13 || denominator > 12) return;
        if (!CanSetTuplet((numerator, denominator))) return;
        CurrentTriplet = numerator == 3 && denominator == 2;
        CurrentTupletNumerator = numerator;
        CurrentTupletDenominator = denominator;
        if (ApplyToolSelection(cells =>
            {
                foreach (var cell in cells)
                {
                    cell.TupletNumerator = numerator;
                    cell.TupletDenominator = denominator;
                    cell.IsTriplet = CurrentTriplet;
                }
            })) return;
        var current = CurrentCell(create: true);
        if (current is null) { NotifyState(); return; }
        RunEdit(() =>
        {
            current.TupletNumerator = numerator;
            current.TupletDenominator = denominator;
            current.IsTriplet = CurrentTriplet;
            return true;
        });
    }

    public void SetTupletEntryState(int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0)
        {
            CurrentTupletNumerator = 0;
            CurrentTupletDenominator = 0;
            return;
        }
        CurrentTupletNumerator = Math.Clamp(numerator, 2, 13);
        CurrentTupletDenominator = Math.Clamp(denominator, 1, 12);
        CurrentTriplet = CurrentTupletNumerator == 3 && CurrentTupletDenominator == 2;
    }

    public MeasureModel? CurrentMeasure()
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return null;
        return track.Measures[SelectedMeasure];
    }

    public int CurrentDurationSlots() => MusicTime.CellSlotsRounded(new TabCell
    {
        DurationDenominator = CurrentDurationDenominator, Dots = CurrentDots, IsTriplet = CurrentTriplet,
        TupletNumerator = CurrentTupletNumerator, TupletDenominator = CurrentTupletDenominator
    });

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
            case "Note.Accent": CycleAccent(); return true;
            case "Note.Staccato": ToggleStaccato(); return true;
            case "Note.Tenuto": ToggleTenuto(); return true;
            case "Note.Bend": ToggleTechnique(TechniqueNames.Bend); return true;
            case "Note.HammerPull": ToggleTechnique(TechniqueNames.Hopo); return true;
            case "Note.Vibrato": ToggleTechnique(TechniqueNames.Vibrato); return true;
            case "Note.Slide": ToggleTechnique(TechniqueNames.LegatoSlide); return true;
            case "Note.LetRing": ToggleTechnique(TechniqueNames.LetRing); return true;
            case "Note.Dead": ToggleDead(); return true;
            case "Note.Ghost": ToggleGhost(); return true;
            case "Note.Harmonic": ToggleTechnique(TechniqueNames.Harmonic); return true;
            case "Note.Trill": ToggleTechnique(TechniqueNames.Trill); return true;
            case "Note.TremoloBar": ToggleTechnique(TechniqueNames.TremoloBar); return true;
            case "Note.Grace": ToggleTechnique("GraceBefore"); return true;
            case "Note.PalmMute": ToggleTechnique(TechniqueNames.PalmMute); return true;
            case "Note.FadeIn": ToggleTechnique(TechniqueNames.FadeIn); return true;
            case "Note.FadeOut": ToggleTechnique(TechniqueNames.FadeOut); return true;
            case "Note.Dot": ToggleDot(); return true;
            case "Note.DoubleDot": SetDots(2); return true;
            case "Note.Triplet": ToggleTriplet(); return true;
            case "Bar.RepeatOpen": ToggleRepeatOpen(); return true;
            case "Bar.RepeatClose": ToggleRepeatClose(); return true;
        }
        return false;
    }

    /// <summary>Highest typed/clicked number: a fret (36), or on a drum track the GM percussion note itself (up to 127).</summary>
    private static int MaxEntryNumber(TrackModel track) =>
        track.MidiChannel == 9 || track.Kind == TrackKind.Drums ? 127 : 36;

    public void EnterFret(int digit, bool autoAdvance = true)
    {
        var track = Track;
        var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        var editingExistingNote = false;
        RunEdit(() =>
        {
            ApplyPendingDuration(cell);

            var existing = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
            editingExistingNote = existing is not null;
            var append = existing is not null && (DateTime.UtcNow - _lastDigit).TotalMilliseconds < 700
                         && _lastDigitMeasure == SelectedMeasure && _lastDigitCell == SelectedCell
                         && _lastDigitString == SelectedString && existing.Fret < 10 && existing.Fret > 0;
            var fret = Math.Clamp(append ? existing!.Fret * 10 + digit : digit, 0, MaxEntryNumber(track));
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
            return true;
        });
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

    private void ApplyPendingDuration(TabCell cell)
    {
        // A note keeps its own length; a rest or empty slot takes the remembered writing duration (the rest fill, when on, refills the remainder).
        if (!WritingDuration.Applies(cell))
        {
            cell.IsRest = false;
            return;
        }
        cell.DurationDenominator = CurrentDurationDenominator;
        cell.Dots = CurrentDots;
        cell.IsTriplet = CurrentTriplet;
        cell.TupletNumerator = CurrentTupletNumerator;
        cell.TupletDenominator = CurrentTupletDenominator;
        cell.IsRest = false;
    }

    public void SetDuration(int denominator) => SetDuration(denominator, force: false);

    // force: +/- step the selected beat, even when the bar then over/underfills
    // (the bar is flagged instead of the key silently doing nothing).
    private void SetDuration(int denominator, bool force)
    {
        if (Array.IndexOf(MusicTime.AllDenominators, denominator) < 0) return;
        if ((!force || PreventBarOverflow) && !CanSetDuration(denominator)) return;
        CurrentDurationDenominator = denominator;
        if (ApplyToolSelection(cells => { if (RefillSelectedRests(cells, denominator)) return; Services.EditCommands.ApplyDuration(cells, denominator); NormaliseRests(cells, resize: true); })) { RestoreRestSelection(); return; }
        var cell = CurrentCell();
        if (cell is not null && (cell.Notes.Count > 0 || cell.IsRest))
        {
            RunEdit(() => { cell.DurationDenominator = denominator; NormaliseRests(new[] { cell }, resize: true); return true; });
        }
        else
        {
            // Nothing under the cursor yet: the value still applies to the next note,
            // so tell the host to refresh its readouts.
            NotifyState();
            InvalidateVisual();
        }
    }

    private void NotifyState() => _c.NotifyState();

    public void Longer() => SetDuration(MusicTime.Longer(StepBaseDuration()), force: true);
    public void Shorter() => SetDuration(MusicTime.Shorter(StepBaseDuration()), force: true);

    // +/- step from the selected beat's own value (not a stale toolbar value).
    private int StepBaseDuration()
    {
        var selected = ToolCells().FirstOrDefault(c => c.Notes.Count > 0 || c.IsRest);
        if (selected is not null) return selected.DurationDenominator;
        var cell = CurrentCell();
        return cell is not null && (cell.Notes.Count > 0 || cell.IsRest) ? cell.DurationDenominator : CurrentDurationDenominator;
    }

    public void ToggleDot()
    {
        var dots = (CurrentDots + 1) % 3;
        if (!CanSetDots(dots)) return;
        CurrentDots = dots;
        if (ApplyToolSelection(cells => Services.EditCommands.ApplyDots(cells, CurrentDots))) return;
        var cell = CurrentCell();
        if (cell is null) { NotifyState(); InvalidateVisual(); return; }
        RunEdit(() => { cell.Dots = CurrentDots; return true; });
    }

    public void SetDots(int dots)
    {
        dots = Math.Clamp(dots, 0, 2);
        if (!CanSetDots(dots)) return;
        CurrentDots = dots;
        if (ApplyToolSelection(cells => Services.EditCommands.ApplyDots(cells, CurrentDots))) return;
        var cell = CurrentCell();
        if (cell is null) { NotifyState(); InvalidateVisual(); return; }
        RunEdit(() => { cell.Dots = CurrentDots; return true; });
    }

    public void ToggleTriplet()
    {
        var selectionCells = HasSelection ? ToolCells() : new List<TabCell>();
        var triplet = selectionCells.Count > 0
            ? Services.EditCommands.NextTriplet(selectionCells)
            : !CurrentTriplet;
        if (!CanSetTuplet(triplet ? (3, 2) : (0, 0))) return;
        CurrentTriplet = triplet;
        CurrentTupletNumerator = triplet ? 3 : 0;
        CurrentTupletDenominator = triplet ? 2 : 0;
        if (ApplyToolSelection(cells =>
            {
                foreach (var selected in cells) Services.EditCommands.ApplyTriplet(selected, CurrentTriplet);
            })) return;
        var cell = CurrentCell();
        if (cell is null) { NotifyState(); InvalidateVisual(); return; }
        RunEdit(() =>
        {
            cell.IsTriplet = CurrentTriplet;
            cell.TupletNumerator = CurrentTriplet ? 3 : 0;
            cell.TupletDenominator = CurrentTriplet ? 2 : 0;
            return true;
        });
    }

    /// <summary>Whether the candidate written duration fits every selected beat in its own bar.</summary>
    public bool CanSetDuration(int denominator)
        => CanApplyRhythmicValue(denominator, null, null);

    public bool CanSetDots(int dots)
        => CanApplyRhythmicValue(null, Math.Clamp(dots, 0, 2), null);

    public bool CanSetTuplet((int Numerator, int Denominator) ratio)
        => CanApplyRhythmicValue(null, null, ratio);

    private bool CanApplyRhythmicValue(int? requestedDenominator, int? requestedDots,
        (int Numerator, int Denominator)? requestedTuplet)
    {
        if (requestedDenominator.HasValue && Array.IndexOf(MusicTime.AllDenominators, requestedDenominator.Value) < 0) return false;
        // Default: any rhythm may be entered; a bar that no longer adds up is flagged red.
        if (!PreventBarOverflow) return Track is not null && _project is not null;
        var track = Track;
        if (track is null || _project is null) return false;
        var selected = ToolCells();
        var selectedSet = selected.ToHashSet();
        var checkedTarget = false;
        for (var measureIndex = 0; measureIndex < track.Measures.Count; measureIndex++)
        {
            var measure = track.Measures[measureIndex];
            var cells = CellsFor(measure, create: _activeVoiceIndex == 1);
            var barSlots = MusicTime.BarSlots(_project!, measureIndex);
            var beats = cells.Select((cell, index) => (cell, index))
                .Where(item => item.cell.Notes.Count > 0 || item.cell.IsRest || item.cell.HasAnnotation)
                .Select(item => (item.cell, item.index, Start: BeatStart(item.cell, item.index)))
                .ToList();

            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (!selectedSet.Contains(cell)) continue;
                checkedTarget = true;
                var start = BeatStart(cell, index);
                var hasBeat = cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation;
                var denominator = requestedDenominator ?? (hasBeat ? cell.DurationDenominator : CurrentDurationDenominator);
                var dots = requestedDots ?? (hasBeat ? cell.Dots : CurrentDots);
                var tuplet = requestedTuplet ?? (hasBeat ? cell.Tuplet : CurrentTupletNumerator > 0
                    ? (CurrentTupletNumerator, CurrentTupletDenominator) : CurrentTriplet ? (3, 2) : (0, 0));
                var duration = DurationSlots(denominator, dots, tuplet);
                var end = start + duration;
                if (start < 0 || end > barSlots + 0.001) return false;
                foreach (var other in beats)
                {
                    if (ReferenceEquals(other.cell, cell) || Math.Abs(other.Start - start) <= 0.001) continue;
                    var otherDuration = MusicTime.CellSlots(other.cell);
                    if (selectedSet.Contains(other.cell))
                    {
                        var otherTuplet = requestedTuplet ?? other.cell.Tuplet;
                        var otherDenominator = requestedDenominator ?? other.cell.DurationDenominator;
                        var otherDots = requestedDots ?? other.cell.Dots;
                        otherDuration = DurationSlots(otherDenominator, otherDots, otherTuplet);
                    }
                    if (start < other.Start + otherDuration - 0.001 && other.Start < end - 0.001)
                        return false;
                }
            }
        }
        // ToolCells normally returns the current cell even when it is empty. Its pending duration
        // still has to fit between the cursor and the next real beat (or the end of this bar).
        if (checkedTarget) return true;
        var currentMeasure = CurrentMeasure();
        if (currentMeasure is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return false;
        var currentCells = CellsFor(currentMeasure);
        if (currentCells.Count == 0)
        {
            var pendingTupletOnly = requestedTuplet ?? (CurrentTupletNumerator > 0
                ? (CurrentTupletNumerator, CurrentTupletDenominator) : CurrentTriplet ? (3, 2) : (0, 0));
            var pendingOnlyDuration = DurationSlots(requestedDenominator ?? CurrentDurationDenominator,
                requestedDots ?? CurrentDots, pendingTupletOnly);
            return pendingOnlyDuration <= MusicTime.BarSlots(_project, SelectedMeasure) + 0.001;
        }
        var cursorCell = Math.Clamp(SelectedCell, 0, Math.Max(0, currentCells.Count - 1));
        var cursor = currentCells[cursorCell];
        var cursorStart = BeatStart(cursor, cursorCell);
        var pendingDenominator = requestedDenominator ?? CurrentDurationDenominator;
        var pendingDots = requestedDots ?? CurrentDots;
        var pendingTuplet = requestedTuplet ?? (CurrentTupletNumerator > 0
            ? (CurrentTupletNumerator, CurrentTupletDenominator) : CurrentTriplet ? (3, 2) : (0, 0));
        var pendingDuration = DurationSlots(pendingDenominator, pendingDots, pendingTuplet);
        var pendingBarSlots = MusicTime.BarSlots(_project, SelectedMeasure);
        var beatsAtCursorBar = currentCells.Select((cell, index) => (cell, index))
            .Where(item => item.cell.Notes.Count > 0 || item.cell.IsRest || item.cell.HasAnnotation)
            .Select(item => (item.cell, Start: BeatStart(item.cell, item.index), Duration: MusicTime.CellSlots(item.cell)))
            .ToList();
        var cursorEnd = cursorStart + pendingDuration;
        if (cursorStart < 0 || cursorEnd > pendingBarSlots + 0.001) return false;
        return beatsAtCursorBar.All(beat => Math.Abs(beat.Start - cursorStart) <= 0.001 ||
            !(cursorStart < beat.Start + beat.Duration - 0.001 && beat.Start < cursorEnd - 0.001));
    }

    internal static double BeatStart(TabCell cell, int cellIndex)
        => cell.RhythmicPosition is { } exact && double.IsFinite(exact) ? Math.Max(0, exact) : cellIndex;

    private static double DurationSlots(int denominator, int dots, (int Numerator, int Denominator) tuplet)
    {
        denominator = Math.Clamp(denominator <= 0 ? 16 : denominator, 1, 64);
        dots = Math.Clamp(dots, 0, 2);
        var slots = 16.0 / denominator;
        if (dots == 1) slots *= 1.5;
        else if (dots >= 2) slots *= 1.75;
        if (tuplet.Numerator > 0) slots *= tuplet.Denominator / (double)tuplet.Numerator;
        return Math.Clamp(slots, 0.25, 64);
    }
}
