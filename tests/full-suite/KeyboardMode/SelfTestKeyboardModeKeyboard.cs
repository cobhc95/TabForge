using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

// Owns: the keyboard note source check: lanes per key and the range window, chords, tempo, speed 50/150%, repeats, rests, a loop unrolled for the judge, and the empty cases. Headless.
// Does not own: the judge and score (SelfTestKeyboardModeJudge.cs) or the note stream timing (SelfTestKeyboardModeNoteStream.cs).
// Tests: TestKeyboardModeKeyboardSource.
public static partial class SelfTest
{
    private static SongProject KeysSong(int bars = 2, int bpm = 120)
    {
        var p = SingleTrack(bars, bpm);
        p.Tracks[0].Kind = TrackKind.Keys;
        return p;
    }

    private static KeyboardNoteSource KeysSourceOf(SongProject p, double speed = 1, bool repeats = true) =>
        KeyboardNoteSource.Build(MidiTimelineBuilder.Build(p, new PlaybackOptions { Speed = speed, RepeatExpansion = repeats }), p, 0);

    private static void TestKeyboardModeKeyboardSource()
    {
        // 120 bpm in 4/4: a bar is 2000 ms, a quarter 500 ms; cell 8 of a bar of sixteenths starts at 1000 ms.
        var song = KeysSong();
        Beat(song, 0, 0, 0, 4, 60);
        Beat(song, 0, 0, 4, 4, 64);
        var chord = Beat(song, 0, 0, 8, 4, 60, 1, 0);
        chord.Notes.Add(new TabNote { StringIndex = 2, Fret = 0, MidiValue = 67 });
        Beat(song, 0, 1, 0, 4, 71);
        var src = KeysSourceOf(song);
        Check("keys: onsets follow the timeline, a chord shares one", src.Notes.Select(n => (int)n.OnsetMs).SequenceEqual(new[] { 0, 500, 1000, 1000, 2000 }), string.Join(",", src.Notes.Select(n => n.OnsetMs)));
        Check("keys: the range is the song's lowest to highest key widened to whole octaves (C4..B4)", src.Lowest == 60 && src.Highest == 71 && src.LaneCount == 12, $"{src.Lowest}..{src.Highest}");
        Check("keys: a note's lane is its key minus the lowest, and Fret carries the MIDI key", src.Notes[1].StringIndex == 4 && src.Notes[1].Fret == 64 && src.LaneOf(67) == 7 && src.LaneOf(59) == -1 && src.LaneOf(72) == -1);
        Check("keys: lane labels name the C keys only", src.LaneLabels.Count == 12 && src.LaneLabels[0] == "C4" && src.LaneLabels.Count(l => l.Length > 0) == 1);
        Check("keys: black keys are told apart", KeyboardNoteSource.IsBlackKey(61) && !KeyboardNoteSource.IsBlackKey(60) && KeyboardNoteSource.IsBlackKey(70) && !KeyboardNoteSource.IsBlackKey(71));
        Check("keys: a note has a length", src.Notes.All(n => n.DurationMs >= 30) && src.MaxDurationMs >= 400);
        var exp = src.Expected();
        Check("keys: expected presses are the notes in seconds", exp.Count == 5 && exp[1] == new KeyboardModeExpected(64, 0.5, src.Notes[1].DurationMs / 1000));

        var half = KeysSourceOf(song, 0.5);
        var fast = KeysSourceOf(song, 1.5);
        Check("keys: speed 50% doubles the times", Math.Abs(half.Notes[4].OnsetMs - 4000) < 1, half.Notes[4].OnsetMs.ToString());
        Check("keys: speed 150% shortens the times by a third", Math.Abs(fast.Notes[4].OnsetMs - 2000 / 1.5) < 1, fast.Notes[4].OnsetMs.ToString());

        var slow = KeysSong(2, 60);
        Beat(slow, 0, 1, 0, 4, 60);
        Check("keys: 60 bpm doubles the bar length", Math.Abs(KeysSourceOf(slow).Notes[0].OnsetMs - 4000) < 1);

        var repeated = OrderExampleProject(new("L1", "repeat", 2, "0 :|", Array.Empty<int>()));
        repeated.Tracks[0].Kind = TrackKind.Keys;
        Beat(repeated, 0, 0, 0, 4, 60);
        Beat(repeated, 0, 0, 8, 4, 62);
        repeated.Tracks[0].Measures[0].Cells[4].IsRest = true;
        Check("keys: a repeated bar plays its notes again and a rest gives none", KeysSourceOf(repeated).Notes.Select(n => (int)n.OnsetMs).SequenceEqual(new[] { 0, 1000, 2000, 3000 }), string.Join(",", KeysSourceOf(repeated).Notes.Select(n => n.OnsetMs)));
        Check("keys: without repeat expansion the bar plays once", KeysSourceOf(repeated, 1, false).Notes.Count == 2);

        // A loop of the first bar unrolls: the third pass starts at virtual 4000 ms and the judge gets virtual seconds.
        var loop = new KeyboardModeLoop(0, 2000);
        var firstPass = src.ExpectedIn(0, 2000, loop);
        var wrapped = src.ExpectedIn(2000, 6000, loop);
        Check("keys: without a loop ExpectedIn is the notes that start in the stretch", src.ExpectedIn(400, 1100, null).Select(e => e.Midi).SequenceEqual(new[] { 64, 60, 67 }) && firstPass.Count == 4);
        Check("keys: a loop gives the loop's notes again, later by one loop length each pass", wrapped.Count == 8 && wrapped.Where(e => e.Midi == 64).Select(e => e.OnsetSec).SequenceEqual(new[] { 2.5, 4.5 }), string.Join(",", wrapped.Select(e => $"{e.Midi}@{e.OnsetSec}")));
        Check("keys: a loop shorter than 50 ms is no loop", src.ExpectedIn(0, 2000, new KeyboardModeLoop(0, 10)).Count == 4);

        // Edges: keys beyond the 88 are left out, the range clamps to them, a key doubled at one time is one note, wrong or empty tracks give the 61-key range.
        var wide = KeysSong(1);
        Beat(wide, 0, 0, 0, 4, 20);
        Beat(wide, 0, 0, 4, 4, 22);
        Beat(wide, 0, 0, 8, 4, 108);
        var w = KeysSourceOf(wide);
        Check("keys: a key below the piano is left out and the range clamps to 21..108", w.Notes.Count == 2 && w.Lowest == 21 && w.Highest == 108 && w.LaneCount == 88 && w.LaneOf(108) == 87, $"{w.Lowest}..{w.Highest} {w.Notes.Count}");
        var twice = KeysSong(1);
        Beat(twice, 0, 0, 0, 4, 60, 0, 0);
        twice.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 0, MidiValue = 60 });
        Check("keys: one key twice at one time is one note", KeysSourceOf(twice).Notes.Count == 1);
        var emptyKeys = KeysSourceOf(KeysSong(1));
        Check("keys: an empty keys track gives the 61-key range and no notes", emptyKeys.Notes.Count == 0 && emptyKeys.Lowest == 36 && emptyKeys.Highest == 96 && emptyKeys.ExpectedIn(0, 1000, null).Count == 0);
        Check("keys: a bad index gives an empty source, and a guitar track shown as keys has its pitches", KeyboardNoteSource.Build(MidiTimelineBuilder.Build(SingleTrack(1), new PlaybackOptions()), SingleTrack(1), 0).Notes.Count == 0 && KeyboardNoteSource.Build(new ScoreTimeline(), song, 5).Notes.Count == 0 && !KeyboardNoteSource.Suits(null));
        KeyboardModeAnyTrackChecks();
    }

    /// <summary>Any track with notes shows as falling keys at its sounding pitches: a guitar with capo and transpose, and the imported demo's pad.</summary>
    private static void KeyboardModeAnyTrackChecks()
    {
        var guitar = SingleTrack(2);
        guitar.Tracks[0].Capo = 2;
        guitar.Tracks[0].Transpose = -12;
        Beat(guitar, 0, 0, 0, 4, 0, 0, 3);
        Beat(guitar, 0, 0, 4, 4, 0, 3, 5);
        var timeline = MidiTimelineBuilder.Build(guitar, new PlaybackOptions());
        var keys = KeyboardNoteSource.Build(timeline, guitar, 0);
        var played = timeline.NotesFor(0).Select(e => e.Midi).OrderBy(m => m).ToArray();
        Check("keys any track: a guitar track has the playback's pitches", played.Length == 2 && keys.Notes.Select(n => n.Fret).OrderBy(m => m).SequenceEqual(played), string.Join(",", keys.Notes.Select(n => n.Fret)));
        Check("keys any track: those pitches are tuning + capo + fret + transpose", keys.Notes.Any(n => n.Fret == 64 + 2 + 3 - 12) && keys.Notes.Any(n => n.Fret == 50 + 2 + 5 - 12));
        Check("keys any track: the expected presses for the judge carry the same pitches", keys.Expected().Select(e => e.Midi).OrderBy(m => m).SequenceEqual(played));
        var demo = TabForge.Presets.FullDemoSongFactory.Create();
        var demoTimeline = MidiTimelineBuilder.Build(demo, new PlaybackOptions());
        foreach (var name in new[] { "Pad", "Piano", "Gtr" })
        {
            var index = demo.Tracks.FindIndex(t => t.Name.Contains(name));
            var source = index < 0 ? KeyboardNoteSource.Empty() : KeyboardNoteSource.Build(demoTimeline, demo, index);
            Check($"keys any track: the demo's {name} track has falling notes in range", index >= 0 && source.Notes.Count > 0 && source.Lowest >= 21 && source.Highest <= 108 && source.Notes.All(n => n.Fret >= source.Lowest && n.Fret <= source.Highest),
                $"track {index}: {source.Notes.Count} notes, {source.Lowest}..{source.Highest}");
        }
    }
}
