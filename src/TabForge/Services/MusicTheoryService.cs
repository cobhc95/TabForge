using System.Linq;
using TabForge.Models;

namespace TabForge.Services;

// standard theory helpers: scales, chords, transposition. No external deps.
// Owns: note names, intervals and pitch arithmetic shared by editing and display.
// Does not own: the notation layout and playback.
// Tests: TestNoteNames, TestTransposeAllVoices.
public static class MusicTheoryService
{
    public static readonly string[] NoteNames = Audio.Contracts.NoteNames.SharpPitchClasses();

    public static readonly Dictionary<string, int[]> Scales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Major"] = new[] { 0, 2, 4, 5, 7, 9, 11 },
        ["Natural Minor"] = new[] { 0, 2, 3, 5, 7, 8, 10 },
        ["Harmonic Minor"] = new[] { 0, 2, 3, 5, 7, 8, 11 },
        ["Melodic Minor"] = new[] { 0, 2, 3, 5, 7, 9, 11 },
        ["Major Pentatonic"] = new[] { 0, 2, 4, 7, 9 },
        ["Minor Pentatonic"] = new[] { 0, 3, 5, 7, 10 },
        ["Blues"] = new[] { 0, 3, 5, 6, 7, 10 },
        ["Dorian"] = new[] { 0, 2, 3, 5, 7, 9, 10 },
        ["Phrygian"] = new[] { 0, 1, 3, 5, 7, 8, 10 },
        ["Lydian"] = new[] { 0, 2, 4, 6, 7, 9, 11 },
        ["Mixolydian"] = new[] { 0, 2, 4, 5, 7, 9, 10 },
        ["Locrian"] = new[] { 0, 1, 3, 5, 6, 8, 10 },
        ["Diminished"] = new[] { 0, 2, 3, 5, 6, 8, 9, 11 },
        ["Whole Tone"] = new[] { 0, 2, 4, 6, 8, 10 },
    };

    public static readonly Dictionary<string, int[]> Chords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Maj"] = new[] { 0, 4, 7 },
        ["min"] = new[] { 0, 3, 7 },
        ["7"] = new[] { 0, 4, 7, 10 },
        ["maj7"] = new[] { 0, 4, 7, 11 },
        ["min7"] = new[] { 0, 3, 7, 10 },
        ["dim"] = new[] { 0, 3, 6 },
        ["aug"] = new[] { 0, 4, 8 },
        ["sus2"] = new[] { 0, 2, 7 },
        ["sus4"] = new[] { 0, 5, 7 },
        ["5"] = new[] { 0, 7 },
        ["9"] = new[] { 0, 4, 7, 10, 14 },
        ["add9"] = new[] { 0, 4, 7, 14 },
    };

    public static List<string> ScaleNotes(string root, string scaleName)
    {
        var rootIdx = Array.FindIndex(NoteNames, n => n.Equals(root, StringComparison.OrdinalIgnoreCase));
        if (rootIdx < 0) rootIdx = 0;
        if (!Scales.TryGetValue(scaleName, out var iv)) iv = Scales["Major"];
        return iv.Select(i => NoteNames[(rootIdx + i) % 12]).ToList();
    }

    /// <summary>Note name with octave (60 is "C4"), the one spelling every view and dialog shows.</summary>
    public static string NoteName(int midi) => Audio.Contracts.NoteNames.Name(midi);

    // Suggest a fret/string position for a midi pitch on a tuning (high->low list).
    public static (int stringIndex, int fret) SuggestPosition(IList<int> tuningHighToLow, int midi)
    {
        var best = (s: 0, f: midi - tuningHighToLow[0]);
        var bestScore = int.MaxValue;
        for (var s = 0; s < tuningHighToLow.Count; s++)
        {
            var f = midi - tuningHighToLow[s];
            if (f < 0 || f > 24) continue;
            var score = f + Math.Abs(s - tuningHighToLow.Count / 2);
            if (score < bestScore) { bestScore = score; best = (s, f); }
        }
        return best;
    }

    /// <summary>
    /// Transposes the notes of both voices by <paramref name="semitones"/>. Drum tracks are skipped (their note numbers are
    /// instruments, not pitches). <paramref name="range"/> limits it to a cell range (inclusive, EndCell -1 = end of the
    /// bar); null = the whole track. A note whose new fret would leave 0..NumberOfFrets moves to another free string of
    /// its beat where it fits (lowest fret); if none fits, its pitch still moves but its fret stays (counted in Unplaced).
    /// </summary>
    public static (int Moved, int Unplaced) TransposeTrack(TrackModel track, int semitones, (int StartMeasure, int StartCell, int EndMeasure, int EndCell)? range = null)
    {
        if (track.Kind == TrackKind.Drums || track.MidiChannel == 9) return (0, 0);
        var moved = 0;
        var unplaced = 0;
        for (var mi = 0; mi < track.Measures.Count; mi++)
        {
            if (range is { } r && (mi < r.StartMeasure || mi > r.EndMeasure)) continue;
            var m = track.Measures[mi];
            foreach (var cells in new[] { m.Cells, m.Voice2Cells })
                for (var ci = 0; ci < cells.Count; ci++)
                {
                    if (range is { } rr)
                    {
                        if (mi == rr.StartMeasure && ci < rr.StartCell) continue;
                        if (mi == rr.EndMeasure && rr.EndCell >= 0 && ci > rr.EndCell) continue;
                    }
                    var notes = cells[ci].Notes;
                    foreach (var n in notes)
                    {
                        // The stored sounding pitch moves by the interval (a harmonic keeps its offset over the fret).
                        var onString = n.StringIndex >= 0 && n.StringIndex < track.StringTunings.Count;
                        var newMidi = Math.Clamp((n.MidiValue > 0 || !onString ? n.MidiValue : track.PitchOf(n.StringIndex, n.Fret)) + semitones, 0, 127);
                        var nf = n.Fret + semitones;
                        if (onString && nf >= 0 && nf <= track.NumberOfFrets) n.Fret = nf;
                        else if (onString && FreeStringFor(track, notes, n, track.PitchOf(n.StringIndex, n.Fret) + semitones) is var (s, f) && s >= 0)
                        {
                            // Off the neck on its own string: re-finger it on a free string of the same beat.
                            n.StringIndex = s;
                            n.Fret = f;
                        }
                        else unplaced++;
                        n.MidiValue = newMidi;
                        if (n.SlideTargetMidi > 0) n.SlideTargetMidi = Math.Clamp(n.SlideTargetMidi + semitones, 0, 127);
                        if (n.TrillTargetMidi > 0) n.TrillTargetMidi = Math.Clamp(n.TrillTargetMidi + semitones, 0, 127);
                        moved++;
                    }
                }
        }
        return (moved, unplaced);
    }

    /// <summary>The free string of the beat (lowest fret) where <paramref name="pitch"/> fits on the neck; (-1, 0) if none.</summary>
    private static (int String, int Fret) FreeStringFor(TrackModel track, List<TabNote> beat, TabNote note, int pitch)
    {
        var best = (String: -1, Fret: 0);
        for (var s = 0; s < track.StringTunings.Count; s++)
        {
            if (beat.Any(o => !ReferenceEquals(o, note) && o.StringIndex == s)) continue;
            var f = track.FretOf(s, pitch);
            if (f >= 0 && f <= track.NumberOfFrets && (best.String < 0 || f < best.Fret)) best = (s, f);
        }
        return best;
    }
}
