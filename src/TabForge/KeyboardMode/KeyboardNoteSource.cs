using TabForge.Models;
using TabForge.Playback;
using TabForge.Visualization;

namespace TabForge.KeyboardMode;

// Owns: one keyboard track of a compiled timeline as Keyboard mode notes (with each note's hand from KeyboardHands and the finger written in the score): one lane per key of a range window (the song's lowest to highest key, widened to whole octaves, at most the 88 keys),
//   and the same notes as expected presses for the judge, with a loop unrolled the way the guitar view does.
// Does not own: the timeline (tempo, speed, repeats are already in its times), which notes are in view (KeyboardModeWindow), the clock (KeyboardModeClock), judging (KeyboardModeJudge) or drawing.
// Tests: TestKeyboardModeKeyboardSource.
public sealed class KeyboardNoteSource : IKeyboardModeNoteSource
{
    /// <summary>The lowest and highest key of an 88-key piano.</summary>
    public const int LowestKey = 21, HighestKey = 108;
    private const double MinDurationMs = 30;
    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private KeyboardNoteSource(int lowest, int highest, IReadOnlyList<KeyboardModeNote> notes, int skipped = 0)
    {
        Note = skipped > 0 ? $"{skipped} note{(skipped == 1 ? "" : "s")} outside the 88 keys left out" : "";
        Lowest = lowest;
        Highest = highest;
        Notes = notes;
        MaxDurationMs = notes.Count == 0 ? 0 : notes.Max(n => n.DurationMs);
        LaneLabels = Enumerable.Range(lowest, highest - lowest + 1).Select(LabelOf).ToArray();
    }

    /// <summary>MIDI number of lane 0 (the lowest key; lanes run from the low keys to the high ones, left to right).</summary>
    public int Lowest { get; }
    public int Highest { get; }
    /// <summary>What the header says about the notes (keys outside the 88 left out), empty when every note fits.</summary>
    public string Note { get; }
    public int LaneCount => Highest - Lowest + 1;
    /// <summary>"C4" on every C, empty on the other keys.</summary>
    public IReadOnlyList<string> LaneLabels { get; }
    /// <summary>The notes in onset order; <see cref="KeyboardModeNote.StringIndex"/> is the lane (key minus <see cref="Lowest"/>) and <see cref="KeyboardModeNote.Fret"/> is the MIDI key.</summary>
    public IReadOnlyList<KeyboardModeNote> Notes { get; }
    public double MaxDurationMs { get; }

    /// <summary>A source with no notes: the 61-key range of the keyboard view.</summary>
    public static KeyboardNoteSource Empty() { var (lo, hi) = KeyboardPaneSizing.RangeFor(61); return new(lo, hi, Array.Empty<KeyboardModeNote>()); }

    /// <summary>True for a track with notation: a keyboard track, or any other one shown as a keyboard by the player's choice (its sounding pitches).</summary>
    public static bool Suits(TrackModel? track) => track is { HasNotation: true };

    /// <summary>The lane of a MIDI key, or -1 when it is outside the window.</summary>
    public int LaneOf(int midi) => midi < Lowest || midi > Highest ? -1 : midi - Lowest;

    public static bool IsBlackKey(int midi) => ((midi % 12) + 12) % 12 is 1 or 3 or 6 or 8 or 10;

    /// <summary>The notes of track <paramref name="trackIndex"/>. <paramref name="project"/> is read, never changed. Keys outside the 88 are left out; a key played twice at one time is one note.</summary>
    public static KeyboardNoteSource Build(ScoreTimeline timeline, SongProject project, int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= project.Tracks.Count || !Suits(project.Tracks[trackIndex])) return Empty();
        var all = timeline.NotesFor(trackIndex);
        var events = all.Where(e => e.Midi is >= LowestKey and <= HighestKey).ToArray();
        if (events.Length == 0) return Empty();
        var low = events.Min(e => e.Midi);
        var high = events.Max(e => e.Midi);
        var lowest = Math.Max(LowestKey, low - low % 12);
        var highest = Math.Min(HighestKey, high - high % 12 + 11);
        var seen = new HashSet<(int, long)>();
        var kept = new List<NoteEvent>(events.Length);
        foreach (var e in events.OrderBy(e => e.OnsetMs).ThenBy(e => e.Midi))
            if (seen.Add((e.Midi, (long)Math.Round(e.OnsetMs)))) kept.Add(e);
        var left = KeyboardHands.Assign(kept.Select(e => (e.Midi, e.OnsetMs, Math.Max(MinDurationMs, e.DurationMs))).ToArray());
        var track = project.Tracks[trackIndex];
        var notes = kept.Select((e, i) => new KeyboardModeNote
        {
            OnsetMs = e.OnsetMs, DurationMs = Math.Max(MinDurationMs, e.DurationMs), StringIndex = e.Midi - lowest, Fret = e.Midi, LeftHand = left[i], Finger = FingerOf(track, e),
        }).ToList();
        return new KeyboardNoteSource(lowest, highest, notes, all.Length - events.Length);
    }

    /// <summary>The finger written on the score note of an event, 1 (thumb) to 5 (little); 0 when none is written or the note is not found.</summary>
    private static int FingerOf(TrackModel track, NoteEvent e)
    {
        if (e.Bar < 0 || e.Bar >= track.Measures.Count) return 0;
        var cells = track.Measures[e.Bar].CellsForVoice(e.VoiceIndex);
        if (e.Cell < 0 || e.Cell >= cells.Count) return 0;
        foreach (var n in cells[e.Cell].Notes)
            if (n.StringIndex == e.StringIndex && (n.LeftHandFinger ?? n.RightHandFinger) is { } f and >= 0 and <= 4) return f + 1;
        return 0;
    }

    /// <summary>True when any note has a finger written (the finger numbers toggle has something to show).</summary>
    public bool HasFingers => Notes.Any(n => n.Finger > 0);

    /// <summary>The presses the player should make for the notes that start in the virtual stretch [fromV, toV) (virtual time as <see cref="KeyboardModeClock"/> keeps it, a loop unrolled),
    /// in song seconds, ready for <see cref="KeyboardModeJudge"/>. Without a loop virtual time is song time.</summary>
    public List<KeyboardModeExpected> ExpectedIn(double fromV, double toV, KeyboardModeLoop? loop)
    {
        var placed = new List<KeyboardModePlaced>();
        KeyboardModeWindow.Collect(this, fromV, toV, loop, placed);
        return placed.Where(p => p.VirtualMs >= fromV && p.VirtualMs < toV)
            .Select(p => new KeyboardModeExpected(p.Note.Fret, p.VirtualMs / 1000, p.Note.DurationMs / 1000)).ToList();
    }

    /// <summary>All the presses of the song once through (no loop), only the practised hand's with a hands filter.</summary>
    public List<KeyboardModeExpected> Expected(KeyboardHandsFilter hands = KeyboardHandsFilter.Both) =>
        Notes.Where(n => KeyboardHands.Practises(hands, n.LeftHand)).Select(n => new KeyboardModeExpected(n.Fret, n.OnsetMs / 1000, n.DurationMs / 1000)).ToList();

    /// <summary>"F#3" for MIDI 54 (sharps, octave from C-1 = 0).</summary>
    public static string NameOf(int midi) => NoteNames[((midi % 12) + 12) % 12] + (midi / 12 - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string LabelOf(int midi) => midi % 12 == 0 ? NoteNames[0] + (midi / 12 - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
}
