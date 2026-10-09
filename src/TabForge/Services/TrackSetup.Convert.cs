using TabForge.Models;

namespace TabForge.Services;

// Owns: moving a track between the drum family and the pitched family (kind, strings, notes).
// Does not own: the channel, program or rig (the caller sets them), the dialogs, undo.
// Tests: TestDrumToPitchedConversion.
public static partial class TrackSetup
{
    private static readonly int[] DrumLines = { 49, 46, 42, 38, 36 };

    public static bool IsDrumFamily(TrackModel track) => track.Kind == TrackKind.Drums || track.MidiChannel == 9;

    /// <summary>True when switching to <paramref name="toDrums"/> changes family and the track has notes to re-map.</summary>
    public static bool ConversionRemapsNotes(TrackModel track, bool toDrums) =>
        !track.IsAudio && IsDrumFamily(track) != toDrums && HasNotes(track);

    /// <summary>The kind a pitched instrument gets when it comes from a drum track: fretted unless it is a keys or bass sound.</summary>
    public static TrackKind PitchedKindFor(string instrument) => KindOf(instrument, -1, TrackKind.Guitar) is var k && k != TrackKind.Drums ? k : TrackKind.Guitar;

    /// <summary>
    /// Moves the track to the other family. Drums to pitched: default strings of <paramref name="pitchedKind"/> (or <paramref name="tunings"/>),
    /// every note keeps its MIDI pitch and takes the lowest fret that plays it. Pitched to drums: drum lines, each note keeps its pitch as the drum key.
    /// </summary>
    public static void ConvertFamily(TrackModel track, bool toDrums, TrackKind pitchedKind, IReadOnlyList<int>? tunings = null)
    {
        if (track.IsAudio) return;
        track.Capo = 0;
        if (toDrums)
        {
            track.Kind = TrackKind.Drums;
            track.StringTunings = DrumLines.ToList();
            Remap(track, (note, lines) => { note.StringIndex = Nearest(lines, note.MidiValue); note.Fret = 0; });
            return;
        }
        track.Kind = pitchedKind == TrackKind.Drums ? TrackKind.Guitar : pitchedKind;
        track.CustomDrumMap = null;
        var strings = (tunings is { Count: > 0 } ? tunings.ToList() : DefaultStrings(track.Kind) ?? GuitarStrings.ToList());
        track.StringTunings = strings;
        var frets = track.NumberOfFrets > 0 ? track.NumberOfFrets : 24;
        Remap(track, (note, lines) =>
        {
            var best = -1;
            for (var s = 0; s < lines.Count; s++)
            {
                var fret = note.MidiValue - lines[s];
                if (fret < 0 || fret > frets) continue;
                if (best < 0 || fret < note.MidiValue - lines[best]) best = s;
            }
            if (best < 0) best = Nearest(lines, note.MidiValue);   // out of range: the closest string, fret clamped
            note.StringIndex = best;
            note.Fret = Math.Clamp(note.MidiValue - lines[best], 0, frets);
        });
    }

    private static int Nearest(IReadOnlyList<int> lines, int midi)
    {
        var best = 0;
        for (var s = 1; s < lines.Count; s++) if (Math.Abs(lines[s] - midi) < Math.Abs(lines[best] - midi)) best = s;
        return best;
    }

    private static void Remap(TrackModel track, Action<TabNote, IReadOnlyList<int>> place)
    {
        foreach (var measure in track.Measures)
            foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                foreach (var note in cell.Notes) place(note, track.StringTunings);
    }
}
