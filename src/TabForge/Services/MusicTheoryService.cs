using System.Linq;
using TabForge.Models;

namespace TabForge.Services;

// standard theory helpers: scales, chords, transposition. No external deps.
public static class MusicTheoryService
{
    public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

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

    public static string NoteName(int midi) => NoteNames[((midi % 12) + 12) % 12] + (midi / 12 - 1);

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

    public static void TransposeTrack(TrackModel track, int semitones)
    {
        foreach (var m in track.Measures)
            foreach (var c in m.Cells)
                foreach (var n in c.Notes)
                {
                    // Move fret when possible, else shift midi.
                    var nf = n.Fret + semitones;
                    if (nf >= 0 && nf <= track.NumberOfFrets)
                    {
                        n.Fret = nf;
                        if (n.StringIndex >= 0 && n.StringIndex < track.StringTunings.Count)
                            n.MidiValue = track.PitchOf(n.StringIndex, nf);
                    }
                    else n.MidiValue = Math.Clamp(n.MidiValue + semitones, 0, 127);
                }
    }
}
