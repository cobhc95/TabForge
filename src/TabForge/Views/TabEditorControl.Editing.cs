using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

// TabEditorControl: note editing.
public sealed partial class TabEditorControl
{
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
            var populated = cells.Where(cell => cell.Notes.Count > 0 || cell.IsRest).ToList();
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

    private List<TabCell> ToolCells(bool createVoice = false)
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
        EditStarting?.Invoke(this, EventArgs.Empty);
        edit(cells);
        EditedNow();
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
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var note in notes) note.Velocity = CurrentVelocity;
        EditedNow();
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
        EditStarting?.Invoke(this, EventArgs.Empty);
        current.TupletNumerator = numerator;
        current.TupletDenominator = denominator;
        current.IsTriplet = CurrentTriplet;
        EditedNow();
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
        EditStarting?.Invoke(this, EventArgs.Empty);
        ApplyPendingDuration(cell);

        var existing = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        var editingExistingNote = existing is not null;
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
        EditedNow();
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

        EditStarting?.Invoke(this, EventArgs.Empty);
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
        EditedNow();
        return true;
    }

    /// <summary>Notation entry: digits pick the string, the fret is chosen automatically.</summary>
    public void EnterStringOnStaff(int stringNumber)
    {
        var track = Track;
        var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        var target = stringNumber == 0 ? BestStringFor(track, cell) : Math.Clamp(stringNumber - 1, 0, track.StringTunings.Count - 1);
        EditStarting?.Invoke(this, EventArgs.Empty);
        ApplyPendingDuration(cell);
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == target) ?? new TabNote { StringIndex = target, Velocity = CurrentVelocity };
        if (!cell.Notes.Contains(note)) cell.Notes.Add(note);
        note.Fret = note.Fret == 0 ? 5 : note.Fret;
        note.MidiValue = MidiOf(track, target, note.Fret);
        PreviewNote(note);
        EditedNow();
    }

    private int BestStringFor(TrackModel track, TabCell cell)
    {
        var used = cell.Notes.Select(n => n.StringIndex).ToHashSet();
        for (var s = 0; s < track.StringTunings.Count; s++) if (!used.Contains(s)) return s;
        return 0;
    }

    private static int MidiOf(TrackModel track, int stringIndex, int fret)
    {
        // Drum tracks: the number typed is the GM percussion note itself (as in the reference).
        if (track.StringTunings.Count == 0 || track.MidiChannel == 9 || track.Kind == TrackKind.Drums) return fret;
        return track.PitchOf(stringIndex, fret);   // tuning + capo + fret
    }

    private void ApplyPendingDuration(TabCell cell)
    {
        // Typing on an existing beat (notes or a rest) keeps that beat's rhythm; only a new,
        // empty beat takes the toolbar duration. Otherwise a fret change could overfill the bar.
        if (cell.Notes.Count > 0 || cell.IsRest)
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
        if (ApplyToolSelection(cells => Services.EditCommands.ApplyDuration(cells, denominator))) return;
        var cell = CurrentCell();
        if (cell is not null && (cell.Notes.Count > 0 || cell.IsRest))
        {
            EditStarting?.Invoke(this, EventArgs.Empty);
            cell.DurationDenominator = denominator;
            EditedNow();
        }
        else
        {
            // Nothing under the cursor yet: the value still applies to the next note,
            // so tell the host to refresh its readouts.
            NotifyState();
            InvalidateVisual();
        }
    }

    private void NotifyState() => SelectionChanged?.Invoke(this, EventArgs.Empty);

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
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.Dots = CurrentDots;
        EditedNow();
    }

    public void SetDots(int dots)
    {
        dots = Math.Clamp(dots, 0, 2);
        if (!CanSetDots(dots)) return;
        CurrentDots = dots;
        if (ApplyToolSelection(cells => Services.EditCommands.ApplyDots(cells, CurrentDots))) return;
        var cell = CurrentCell();
        if (cell is null) { NotifyState(); InvalidateVisual(); return; }
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.Dots = CurrentDots;
        EditedNow();
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
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.IsTriplet = CurrentTriplet;
        cell.TupletNumerator = CurrentTriplet ? 3 : 0;
        cell.TupletDenominator = CurrentTriplet ? 2 : 0;
        EditedNow();
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

    private static double BeatStart(TabCell cell, int cellIndex)
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

    public bool HasEditableNotes
    {
        get
        {
            var cells = ToolCells();
            return cells.SelectMany(cell => cell.Notes).Any(note => HasSelection || note.StringIndex == SelectedString);
        }
    }

    public bool HasSecondaryBeamEligibleNotes =>
        (HasSelection ? ToolCells() : CurrentCell() is { } cell ? new List<TabCell> { cell } : new List<TabCell>())
        .Any(cell => cell.Notes.Count > 0 && cell.DurationDenominator >= 16);

    public bool? GetNoteCellToolState(Func<TabCell, bool> state)
    {
        var cells = HasSelection ? ToolCells() : CurrentCell() is { } cell ? new List<TabCell> { cell } : new List<TabCell>();
        var notes = cells.Where(cell => cell.Notes.Count > 0).ToList();
        return notes.Count == 0 ? null : Uniform(notes.Select(state));
    }

    private List<TabCell> EditableCells() => HasSelection ? ToolCells(createVoice: true) :
        CurrentCell() is { } cell ? new List<TabCell> { cell } : new List<TabCell>();

    public void SetSoundDurationPercent(int percent)
    {
        percent = Math.Clamp(percent, 1, 200);
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var cell in cells) cell.SoundDurationPercent = percent;
        EditedNow();
    }

    public void SetOctaveShift(int semitones)
    {
        if (semitones is not (-24 or -12 or 0 or 12 or 24)) return;
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var cell in cells) cell.OctaveShiftSemitones = semitones;
        EditedNow();
    }

    public void SetBeamMode(BeamMode mode)
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var cell in cells) cell.BeamMode = mode;
        EditedNow();
    }

    public void ResetBeaming()
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var cell in cells)
        {
            cell.BeamMode = BeamMode.Auto;
            cell.BreakSecondaryBeamBefore = false;
        }
        EditedNow();
    }

    public void SetSecondaryBeamBreak(bool value)
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0 && cell.DurationDenominator >= 16).ToList();
        if (cells.Count == 0) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var cell in cells) cell.BreakSecondaryBeamBefore = value;
        EditedNow();
    }

    public void SetStemDirection(StemDirection direction)
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var cell in cells) cell.StemDirection = direction;
        EditedNow();
    }

    public bool CanTieSelectedNote()
    {
        if (HasSelection) return false;
        var cell = CurrentCell();
        var note = cell?.Notes.FirstOrDefault(candidate => candidate.StringIndex == SelectedString);
        return note is not null && FindPreviousCompatibleNote(note) is not null;
    }

    public bool CanTieSelectedBeat()
    {
        if (HasSelection) return false;
        var cell = CurrentCell();
        return cell is not null && cell.Notes.Any(note => FindPreviousCompatibleNote(note) is not null);
    }

    public void TieSelectedNote()
    {
        if (!CanTieSelectedNote()) return;
        var note = CurrentCell()!.Notes.First(candidate => candidate.StringIndex == SelectedString);
        EditStarting?.Invoke(this, EventArgs.Empty);
        note.Tied = true;
        EditedNow();
    }

    public void TieSelectedBeat()
    {
        if (!CanTieSelectedBeat()) return;
        var notes = CurrentCell()!.Notes.Where(note => FindPreviousCompatibleNote(note) is not null).ToList();
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var note in notes) note.Tied = true;
        EditedNow();
    }

    private TabNote? FindPreviousCompatibleNote(TabNote destination)
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return null;
        var currentCells = CellsFor(track.Measures[SelectedMeasure]);
        if (SelectedCell < 0 || SelectedCell >= currentCells.Count) return null;
        var currentStart = AbsoluteCellStart(track, SelectedMeasure, SelectedCell, currentCells[SelectedCell]);
        for (var measureIndex = SelectedMeasure; measureIndex >= 0; measureIndex--)
        {
            var cells = CellsFor(track.Measures[measureIndex]);
            var firstCell = measureIndex == SelectedMeasure ? SelectedCell - 1 : cells.Count - 1;
            for (var cellIndex = firstCell; cellIndex >= 0; cellIndex--)
            {
                var cell = cells[cellIndex];
                var previous = cell.Notes.FirstOrDefault(candidate => candidate.StringIndex == destination.StringIndex);
                if (previous is null) continue;
                if (NotePitch(track, previous) != NotePitch(track, destination)) return null;
                var end = AbsoluteCellStart(track, measureIndex, cellIndex, cell) + MusicTime.CellSlots(cell);
                return Math.Abs(end - currentStart) <= 0.51 ? previous : null;
            }
        }
        return null;
    }

    private static int NotePitch(TrackModel track, TabNote note) => note.MidiValue > 0 ? note.MidiValue
        : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
            ? track.PitchOf(note.StringIndex, note.Fret) : note.Fret;

    private double AbsoluteCellStart(TrackModel track, int measureIndex, int cellIndex, TabCell cell)
    {
        var start = 0.0;
        for (var measure = 0; measure < measureIndex; measure++)
            start += _project is null ? 16 : MusicTime.BarSlots(_project, measure);
        return start + BeatStart(cell, cellIndex);
    }

    public bool CanSetDurationForTool(string key) => key switch
    {
        "whole" => CanSetDuration(1), "half" => CanSetDuration(2), "quarter" => CanSetDuration(4),
        "eighth" => CanSetDuration(8), "sixteenth" => CanSetDuration(16),
        "thirtysecond" => CanSetDuration(32), "sixtyfourth" => CanSetDuration(64),
        "dotted" => CanSetDots(1), "double-dotted" => CanSetDots(2),
        "tuplet" => CanSetTuplet(GetToolState("duration:tuplet") == true ? (0, 0) : (3, 2)),
        _ => true
    };

    public void ToggleRest()
    {
        if (ApplyToolSelection(cells =>
            {
                var makeRest = !cells.All(cell => cell.IsRest);
                foreach (var selected in cells)
                {
                    selected.IsRest = makeRest;
                    if (makeRest) selected.Notes.Clear();
                }
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.IsRest = !cell.IsRest;
        if (cell.IsRest) cell.Notes.Clear();
        EditedNow();
    }

    public void ToggleTie()
    {
        if (ApplyToolSelection(cells =>
            {
                var makeTied = !cells.All(cell => cell.IsTied || cell.Notes.Any(note => note.Tied));
                foreach (var selected in cells)
                {
                    selected.IsTied = makeTied;
                    foreach (var note in selected.Notes) note.Tied = makeTied;
                }
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.IsTied = !cell.IsTied;
        EditedNow();
    }

    public void ToggleFermata()
    {
        var cell = CurrentCell(create: true); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.Fermata = !cell.Fermata;
        EditedNow();
    }

    public void CycleAccent()
    {
        if (ApplyToolSelection(cells =>
            {
                var current = cells.Select(cell => cell.Accent).Distinct().Count() == 1 ? cells[0].Accent : 0;
                var target = (current + 1) % 3;
                foreach (var selected in cells) selected.Accent = target;
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.Accent = (cell.Accent + 1) % 3;
        EditedNow();
    }

    public void SetAccent(int accent)
    {
        accent = Math.Clamp(accent, 0, 2);
        if (ApplyToolSelection(cells =>
            {
                foreach (var selected in cells) selected.Accent = accent;
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.Accent = accent;
        EditedNow();
    }

    public void ToggleStaccato()
    {
        if (ApplyToolSelection(Services.EditCommands.ToggleStaccato)) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty); cell.Staccato = !cell.Staccato; EditedNow();
    }

    public void ToggleTenuto()
    {
        var cell = CurrentCell(create: true); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty); cell.Tenuto = !cell.Tenuto; EditedNow();
    }

    public void ToggleTechnique(string technique)
    {
        if (HasSelection)
        {
            var notes = ToolCells(createVoice: true).SelectMany(cell => cell.Notes).ToList();
            if (notes.Count == 0) return;
            var add = notes.Any(note => !note.Techniques.Contains(technique));
            EditStarting?.Invoke(this, EventArgs.Empty);
            foreach (var selectedNote in notes)
            {
                if (add) selectedNote.Techniques.Add(technique);
                else selectedNote.Techniques.Remove(technique);
            }
            EditedNow();
            return;
        }
        var track = Track; var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        if (note is null)
        {
            ApplyPendingDuration(cell);
            note = new TabNote { StringIndex = SelectedString, Fret = 0, MidiValue = MidiOf(track, SelectedString, 0), Velocity = CurrentVelocity };
            cell.Notes.Add(note);
        }
        if (!note.Techniques.Add(technique)) note.Techniques.Remove(technique);
        EditedNow();
    }

    public void ToggleDead()
    {
        if (HasSelection)
        {
            var notes = ToolCells(createVoice: true).SelectMany(cell => cell.Notes).ToList();
            if (notes.Count == 0) return;
            var value = notes.Any(note => !note.Dead);
            EditStarting?.Invoke(this, EventArgs.Empty);
            foreach (var selectedNote in notes) selectedNote.Dead = value;
            EditedNow();
            return;
        }
        var track = Track; var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        if (note is null) { ApplyPendingDuration(cell); note = new TabNote { StringIndex = SelectedString, Dead = true, Fret = 0, MidiValue = MidiOf(track, SelectedString, 0), Velocity = CurrentVelocity }; cell.Notes.Add(note); }
        else note.Dead = !note.Dead;
        EditedNow();
    }

    public void ToggleGhost()
    {
        if (HasSelection)
        {
            var notes = ToolCells(createVoice: true).SelectMany(cell => cell.Notes).ToList();
            if (notes.Count == 0) return;
            var value = notes.Any(note => !note.Ghost);
            EditStarting?.Invoke(this, EventArgs.Empty);
            foreach (var selectedNote in notes) selectedNote.Ghost = value;
            EditedNow();
            return;
        }
        var track = Track; var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        if (note is null) { ApplyPendingDuration(cell); note = new TabNote { StringIndex = SelectedString, Ghost = true, Fret = 5, MidiValue = MidiOf(track, SelectedString, 5), Velocity = CurrentVelocity }; cell.Notes.Add(note); }
        else note.Ghost = !note.Ghost;
        EditedNow();
    }

    /// <summary>Backspace deletes the note under the cursor.</summary>
    public void DeleteNote()
    {
        if (ApplyToolSelection(cells =>
            {
                foreach (var selected in cells) selected.Notes.Clear();
            })) return;
        var track = Track; var cell = CurrentCell();
        if (track is null || cell is null) return;
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        EditStarting?.Invoke(this, EventArgs.Empty);
        if (note is not null) cell.Notes.Remove(note);
        EditedNow();
    }

    public void DeleteBeat()
    {
        var cell = CurrentCell(); if (cell is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        cell.Notes.Clear();
        cell.IsRest = false;
        EditedNow();
    }

    /// <summary>Insert a beat at the cursor, shifting the rest of the bar right.</summary>
    public void InsertBeat()
    {
        var measure = CurrentMeasure(); if (measure is null) return;
        var cells = CellsFor(measure, create: true);
        var slots = SlotsFor(SelectedMeasure);
        var at = Math.Clamp(SelectedCell, 0, Math.Max(0, slots - 1));
        EditStarting?.Invoke(this, EventArgs.Empty);
        for (var i = at; i < cells.Count; i++)
            if (cells[i].RhythmicPosition is { } position)
                cells[i].RhythmicPosition = position + 1;
            cells.Insert(Math.Min(at, cells.Count), new TabCell
            {
                DurationDenominator = CurrentDurationDenominator, Dots = CurrentDots, IsTriplet = CurrentTriplet,
                TupletNumerator = CurrentTupletNumerator, TupletDenominator = CurrentTupletDenominator
            });
        while (cells.Count > slots) cells.RemoveAt(cells.Count - 1);
        EditedNow();
        RefreshBarCells(cells, slots);
    }

    /// <summary>Delete the beat(s) at the cursor, shifting the rest of the bar left.</summary>
    public void DeleteBeats()
    {
        var measure = CurrentMeasure(); if (measure is null) return;
        var cells = CellsFor(measure, create: true);
        var slots = SlotsFor(SelectedMeasure);
        EditStarting?.Invoke(this, EventArgs.Empty);
        var cell = SelectedCell < cells.Count ? cells[SelectedCell] : null;
        var count = cell is null ? 1 : Math.Max(1, MusicTime.CellSlotsRounded(cell));
        for (var i = Math.Min(SelectedCell + count, cells.Count); i < cells.Count; i++)
            if (cells[i].RhythmicPosition is { } position)
                cells[i].RhythmicPosition = Math.Max(0, position - count);
        for (var i = 0; i < count && cells.Count > 0; i++)
            if (SelectedCell < cells.Count) cells.RemoveAt(SelectedCell);
        while (cells.Count < slots) cells.Add(new TabCell());
        EditedNow();
    }

    private static void RefreshBarCells(List<TabCell> cells, int slots)
    {
        while (cells.Count < slots) cells.Add(new TabCell());
        while (cells.Count > slots) cells.RemoveAt(cells.Count - 1);
    }

    /// <summary>Repeat beat (C, and Edit > Copy last beat): the previous beat of the active voice onto the cursor, then step forward.</summary>
    public void CopyLastBeat()
    {
        var track = Track; var measure = CurrentMeasure();
        if (track is null || measure is null) return;
        var cells = CellsFor(measure);
        if (!Services.EditCommands.CanCopyLastBeat(cells, SelectedCell)) { StatusMessage?.Invoke(this, "No previous beat to copy"); return; }
        EditStarting?.Invoke(this, EventArgs.Empty);
        Services.EditCommands.CopyLastBeat(cells, SelectedCell);
        EditedNow();
        MoveForwardBeat(track);
    }

    /// <summary>Empties the active voice of the selected bar (one undo step; nothing is captured when it is already empty).</summary>
    public void EmptyBar()
    {
        var measure = CurrentMeasure(); if (measure is null) return;
        var cells = CellsFor(measure);
        if (!cells.Any(c => c.Notes.Count > 0 || c.IsRest || c.IsTied)) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        Services.EditCommands.EmptyBar(cells);
        EditedNow();
    }

    public event EventHandler<string>? StatusMessage;

    internal static TabCell CloneCell(TabCell src) => src.Clone();
}
