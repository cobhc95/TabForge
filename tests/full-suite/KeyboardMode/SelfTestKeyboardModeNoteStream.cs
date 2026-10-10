using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

// Owns: the Keyboard mode note stream of any track (chords as columns, tempo, speed, repeats, rests, sounding pitches), the loop window and clock, and the palette's text contrast.
// Does not own: the dock row (TestDockPaneTable), the menu and hotkey rows (TestFeatureModuleContributions) or the capture (docs/CAPTURE_SCRIPT.md, step learn-playing).
// Tests: TestKeyboardModeNoteStream.
public static partial class SelfTest
{
    private static KeyboardNoteSource KeyboardModeSourceOf(SongProject p, double speed = 1, bool repeats = true) =>
        KeyboardNoteSource.Build(MidiTimelineBuilder.Build(p, new PlaybackOptions { Speed = speed, RepeatExpansion = repeats }), p, 0);

    private static void TestKeyboardModeNoteStream()
    {
        // Timing at 120 bpm in 4/4: a bar is 2000 ms, a cell (sixteenth) 125 ms.
        var song = SingleTrack(3);
        Beat(song, 0, 0, 0, 4, 64, 0, 0);
        Beat(song, 0, 0, 4, 4, 59, 1, 0);
        Beat(song, 0, 1, 0, 4, 55, 2, 2);
        var one = KeyboardModeSourceOf(song);
        Check("learn: onsets follow the timeline (0, 500, 2000 ms)", one.Notes.Select(n => (int)n.OnsetMs).SequenceEqual(new[] { 0, 500, 2000 }), string.Join(",", one.Notes.Select(n => n.OnsetMs)));
        Check("learn: a note has the sounding pitch of its string and fret, and a length", one.Notes[2].Fret == 55 && one.Notes[2].DurationMs > 400 && one.Notes[0].Fret == 64, string.Join(",", one.Notes.Select(n => n.Fret)));
        var half = KeyboardModeSourceOf(song, 0.5);
        var fast = KeyboardModeSourceOf(song, 1.5);
        Check("learn: speed 50% doubles the times", Math.Abs(half.Notes[2].OnsetMs - 4000) < 1, half.Notes[2].OnsetMs.ToString());
        Check("learn: speed 150% shortens the times by a third", Math.Abs(fast.Notes[2].OnsetMs - 2000 / 1.5) < 1, fast.Notes[2].OnsetMs.ToString());

        // A chord is one group, also when it is strummed.
        var chord = SingleTrack(1);
        foreach (var s in new[] { 0, 1, 2, 3 }) Beat(chord, 0, 0, 0, 4, 64 - s * 5, s, 3).Notes.Last().Techniques.Add("BrushDown");
        var column = KeyboardModeSourceOf(chord).Notes;
        Check("learn: the notes of one chord start together (within the judge's 40 ms chord window), even strummed", column.Count == 4 && column.Max(n => n.OnsetMs) - column.Min(n => n.OnsetMs) <= 40, string.Join(",", column.Select(n => n.OnsetMs)));

        // A tempo change in the song moves later notes.
        var tempo = SingleTrack(2, 60);
        Beat(tempo, 0, 1, 0, 4, 64, 0, 0);
        Check("learn: 60 bpm doubles the bar length (second bar at 4000 ms)", Math.Abs(KeyboardModeSourceOf(tempo).Notes[0].OnsetMs - 4000) < 1);

        // A repeat plays its bar twice; a rest makes no note.
        var repeated = OrderExampleProject(new("L1", "repeat", 2, "0 :|", Array.Empty<int>()));
        Beat(repeated, 0, 0, 0, 4, 64, 0, 1);
        Beat(repeated, 0, 0, 8, 4, 59, 1, 1);
        repeated.Tracks[0].Measures[0].Cells[4].IsRest = true;
        var played = KeyboardModeSourceOf(repeated).Notes.Select(n => (int)n.OnsetMs).ToArray();
        Check("learn: a repeated bar gives its notes again after the bar (rest cells give none)", played.SequenceEqual(new[] { 0, 1000, 2000, 3000 }), string.Join(",", played));

        Check("learn: a drum track shows as its MIDI pitches (an empty timeline gives no notes)", KeyboardNoteSource.Build(new ScoreTimeline(), new SongProject { Tracks = { new TrackModel { Kind = TrackKind.Drums } } }, 0).Notes.Count == 0);

        KeyboardModeLoopChecks(one);
        KeyboardModePaletteChecks();
    }

    private static void KeyboardModeLoopChecks(KeyboardNoteSource basis)
    {
        var song = SingleTrack(4);
        Beat(song, 0, 1, 4, 4, 64, 0, 1);   // 2500 ms
        Beat(song, 0, 2, 4, 4, 64, 0, 2);   // 4500 ms
        var source = KeyboardModeSourceOf(song);
        var loop = new KeyboardModeLoop(2000, 6000);
        var into = new List<KeyboardModePlaced>();
        KeyboardModeWindow.Collect(source, 5100, 8000, loop, into);
        Check("learn loop: after the loop end the loop's first notes come again (4500 at 4500 is behind, 2500 returns at 6500)",
            into.Select(p => (int)p.VirtualMs).SequenceEqual(new[] { 6500 }), string.Join(",", into.Select(p => p.VirtualMs)));
        into.Clear();
        KeyboardModeWindow.Collect(source, 3100, 11000, loop, into);
        Check("learn loop: two passes later the notes keep coming in order", into.Select(p => (int)p.VirtualMs).SequenceEqual(new[] { 4500, 6500, 8500, 10500 }), string.Join(",", into.Select(p => p.VirtualMs)));
        Check("learn loop: a virtual time maps back to song time", KeyboardModeWindow.RealOf(6500, loop) == 2500 && KeyboardModeWindow.RealOf(4500, loop) == 4500 && KeyboardModeWindow.RealOf(2000, null) == 2000);
        into.Clear();
        KeyboardModeWindow.Collect(source, 0, 7000, null, into);
        Check("learn loop: without a loop the window is plain song time", into.Select(p => (int)p.VirtualMs).SequenceEqual(new[] { 2500, 4500 }));

        var clock = new KeyboardModeClock();
        clock.Update(5900, loop);
        var wrapped = clock.Update(2100, loop);
        Check("learn clock: a wrap at the loop end keeps virtual time moving forward", clock.Wraps == 1 && !clock.Jumped && Math.Abs(wrapped - 6100) < 1, wrapped.ToString());
        clock.Update(3000, loop);
        var seek = clock.Update(2600, loop);
        Check("learn clock: a jump back inside the loop is a seek", clock.Jumped && clock.Wraps == 0 && Math.Abs(seek - 2600) < 1);
        clock.Update(1000, null);
        Check("learn clock: a changed loop restarts the page", clock.Jumped);
    }

    private static void KeyboardModePaletteChecks()
    {
        foreach (var dark in new[] { true, false })
        {
            var pal = KeyboardModePalette.For(dark);
            var text = new[] { KeyboardModePalette.Contrast(pal.LabelColour, pal.PanelColour), KeyboardModePalette.Contrast(pal.LabelColour, pal.StripColour) };
            Check($"learn palette ({(dark ? "dark" : "light")}): labels read on the panel and the strip (4.5 or more)", text.Min() >= 4.5, string.Join(" ", text.Select(t => t.ToString("0.00"))));
        }
    }
}
