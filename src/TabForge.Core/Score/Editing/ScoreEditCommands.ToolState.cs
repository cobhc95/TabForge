using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.ScoreEditing;

// Owns: the toolbox state queries and tool-selection edits of the score editor: uniform tool states, dynamics and tuplet entry state (partial of ScoreEditCommands).
// Tests: no dedicated test class.

public sealed partial class ScoreEditCommands
{
    /// <summary>Returns a single active state for the toolbox, or null when it is absent/mixed.</summary>
    public bool? GetToolState(string toolId)
    {
        var cells = ToolCells();
        if (cells.Count == 0) return null;
        var measure = CurrentMeasure();
        var notes = ToolNotes(cells);

        if (toolId == "edit:pointer") return !HasSelection;
        if (toolId.StartsWith("duration:", StringComparison.Ordinal)) return DurationToolState(toolId[9..], cells);
        if (toolId.StartsWith("dynamic:", StringComparison.Ordinal))
        {
            var velocity = DynamicVelocity(toolId[8..]);
            if (notes.Count == 0) return HasSelection ? null : CurrentVelocity == velocity;
            return Uniform(notes.Select(note => NearestDynamicVelocity(note.Velocity) == velocity));
        }
        if (toolId.StartsWith("effect:", StringComparison.Ordinal)) return EffectToolState(toolId[7..], cells, notes);
        if (toolId.StartsWith("beat:", StringComparison.Ordinal) || toolId.StartsWith("composition:", StringComparison.Ordinal))
        {
            var key = toolId[(toolId.IndexOf(':') + 1)..];
            return BeatToolState(key, measure);
        }
        return null;
    }

    /// <summary>The state of a duration tool: the value shared by the populated cells (or the entry value when there are none).</summary>
    private bool? DurationToolState(string key, List<TabCell> cells)
    {
        var populated = cells.Where(cell => (cell.Notes.Count > 0 || cell.IsRest) && !(!HasSelection && Placeholder(cell))).ToList();
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

    /// <summary>The state of an effect tool: accent, staccato, grace, dead or ghost note, chord, text, or a technique shared by the notes.</summary>
    private static bool? EffectToolState(string key, List<TabCell> cells, List<TabNote> notes)
    {
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

    private static bool? BeatToolState(string key, MeasureModel? measure)
    {
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
        CurrentVelocity = Math.Clamp(velocity, 1, MidiLimits.Max);
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
        var targets = ToolCells().Where(MusicTime.IsBeat).ToList();
        if (targets.Count > 0 && targets.All(c => c.Tuplet == (numerator, denominator))) { numerator = 0; denominator = 0; }   // the lit ratio again clears it
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
}
