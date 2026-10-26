using TabForge.Models;

namespace TabForge.Documents;

// Owns: retuning every pitched track of a document by a per-string change (six-string reference, high to low) as one undo step, the document's
//   per-string shift readout, and the status line that describes the tuning after a retune.
// Does not own: the tuning window and its presets (GlobalTuningPresenter, TuningPresets), the menu row and command (the Avalonia Global tuning handler),
//   the undo stacks and the dirty mark (DocumentEdits).
// Tests: none dedicated(full suite; the same retune rule in the app).

/// <summary>The global tuning retune of one open document.</summary>
public static class GlobalRetune
{
    /// <summary>The number of strings in the tuning reference (six-string, high to low).</summary>
    private const int ReferenceStringCount = 6;

    /// <summary>
    /// Applies <paramref name="delta"/> (semitones per string, high to low) to every pitched track of the document, as one undo step. Returns the status
    /// line for the new tuning, or null when the delta changes nothing (nothing is stored then).
    /// </summary>
    public static string? Apply(IEditableDocument document, int[] delta)
    {
        if (delta.All(d => d == 0)) return null;
        DocumentEdits.Run(document, project =>
        {
            ApplyDelta(project, delta);
            for (var i = 0; i < ReferenceStringCount; i++) document.TuningShift[i] += delta[i];   // the readout is kept beside the undo snapshot, not inside it
            return true;
        });
        return StatusOf(document.TuningShift);
    }

    /// <summary>The status line for a per-string shift: the original tuning, one shift for every string, or a custom per-string tuning.</summary>
    public static string StatusOf(int[] offsets)
    {
        if (offsets.All(o => o == 0)) return "Original tuning";
        if (!offsets.All(o => o == offsets[0])) return "All tracks retuned (custom per-string tuning)";
        var uniform = offsets[0];
        return $"All tracks retuned {(uniform > 0 ? "+" : "")}{uniform} semitones";
    }

    /// <summary>
    /// Moves string tunings and sounding pitches together, so fret numbers stay the same, like physically retuning the instrument. Strings map from the
    /// lowest string up: a bass follows the four lowest strings and extra low strings on 7- and 8-string instruments follow the lowest. Drum tracks (MIDI channel 10) keep their pitches.
    /// </summary>
    private static void ApplyDelta(SongProject project, int[] delta)
    {
        foreach (var track in project.Tracks.Where(t => t.MidiChannel != 9))
        {
            var count = track.StringTunings.Count;
            int DeltaOf(int stringIndex) => delta[Math.Clamp(count <= ReferenceStringCount ? stringIndex + (ReferenceStringCount - count) : Math.Min(stringIndex, ReferenceStringCount - 1), 0, ReferenceStringCount - 1)];
            track.StringTunings = track.StringTunings.Select((p, s) => Math.Clamp(p + DeltaOf(s), 0, MidiLimits.Max)).ToList();
            foreach (var measure in track.Measures)
                foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                    foreach (var note in cell.Notes)
                    {
                        var d = DeltaOf(note.StringIndex);
                        note.MidiValue = Math.Clamp(note.MidiValue + d, 0, MidiLimits.Max);
                        if (note.SlideTargetMidi > 0) note.SlideTargetMidi = Math.Clamp(note.SlideTargetMidi + d, 0, MidiLimits.Max);
                        if (note.TrillTargetMidi > 0) note.TrillTargetMidi = Math.Clamp(note.TrillTargetMidi + d, 0, MidiLimits.Max);
                    }
        }
    }
}
