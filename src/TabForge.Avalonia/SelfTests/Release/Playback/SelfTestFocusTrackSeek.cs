using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Owns: the check that Focus Track keeps every track's channel volume at its focus level through whatever moves the playback: a seek paused or playing
//   (in place and recompiled), a start from a later bar, a loop wrap after an edit recompiled the song, a track added while playing, and a toggle while playing.

/// <summary>Focus Track survives seeks, starts, loop wraps, recompiles, a track added while playing and toggles: the CC7 sent per track is always the focus-scaled level.</summary>
public static partial class SelfTest
{
    private static void TestFocusTrackSurvivesSeek()
    {
        // Seeks on the plain song (the schedule is reused) and on the repeat song (every seek recompiles), paused (then played) and playing.
        foreach (var repeats in new[] { false, true })
            foreach (var playing in new[] { false, true })
            {
                var project = MixSeekSong(repeats);
                var channels = ChannelAllocator.Assign(project);
                var focus = FocusOnFirst(project);
                var output = new ScheduleReuseOutput();
                using var engine = new PlaybackEngine(output) { Focus = focus };
                engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: !playing);
                var failures = new List<string>();
                foreach (var bar in playing ? new[] { 10, 25, 40, 12 } : new[] { 10, 25, 40, 5, 22 })
                {
                    var before = output.Messages.Length;
                    engine.Seek(project, bar, 0);
                    WaitForScheduleSeek(engine, bar);
                    if (!playing && repeats) engine.Resume();   // a recompiled schedule sends its setup when it runs
                    var got = FocusLevelsSent(output, before, channels[0], channels[1]);
                    if (!playing && repeats) engine.Pause();
                    var want = FocusWanted(project, focus, bar);
                    if (got != want) failures.Add($"bar {bar}: sent {got}, want {want}");
                }
                Check($"focus track survives seeks ({(repeats ? "recompiled" : "in place")}, {(playing ? "playing" : "paused, then played")}): every track's volume is the focus level after each jump",
                    failures.Count == 0, string.Join("; ", failures));
            }

        // A start from a later bar: the chased start values (the mix point in force there) are sent at the focus level.
        {
            var project = MixSeekSong(false);
            var channels = ChannelAllocator.Assign(project);
            var focus = FocusOnFirst(project);
            var output = new ScheduleReuseOutput();
            using var engine = new PlaybackEngine(output) { Focus = focus };
            engine.Start(project, new PlaybackOptions { StartBar = 25 }, _ => { }, () => { });
            var got = FocusLevelsSent(output, 0, channels[0], channels[1]);
            Check("focus track: a start after the mix points sends the chased volumes at the focus level", got == FocusWanted(project, focus, 25), $"sent {got}, want {FocusWanted(project, focus, 25)}");
        }

        // A loop: an edit while looping recompiles the whole song, swapped in at the wrap; the wrap restores the focus levels from it.
        {
            var project = MixSeekSong(false);
            var channels = ChannelAllocator.Assign(project);
            var focus = FocusOnFirst(project);
            var output = new ScheduleReuseOutput();
            using var engine = new PlaybackEngine(output) { Focus = focus };
            var revisions = 0;
            engine.TimelineRevised += _ => Interlocked.Increment(ref revisions);
            engine.Start(project, new PlaybackOptions { StartBar = 10, Loop = true, LoopStartBar = 10, LoopEndBar = 11 }, _ => { }, () => { });
            Thread.Sleep(200);
            project.Tracks[1].Measures[10].Cells[0].Notes[0].MidiValue = 70;   // a note edit inside the loop
            project.MarkTimelineChanged();
            _ = engine.RefreshArrangementInBackground(project, Enumerable.Range(0, 45).ToArray());
            var swapped = WaitForScheduleCondition(() => Volatile.Read(ref revisions) > 0, 4000);
            var before = output.Messages.Length;
            Thread.Sleep(1500);   // past the next wraps (a bar is 600 ms): each restores the channel state from the recompiled song
            var got = FocusLevelsSent(output, before, channels[0], channels[1]);
            Check("focus track: loop wraps after an edit recompiled the song keep the focus levels", swapped && got == FocusWanted(project, focus, 10), $"swapped {swapped}, sent {got}, want {FocusWanted(project, focus, 10)}");
        }

        // A track added while playing: it is lowered too once the new track list plays (from the next bar), and the others keep their levels.
        {
            var project = MixSeekSong(false);
            var focus = FocusOnFirst(project);
            var output = new ScheduleReuseOutput();
            using var engine = new PlaybackEngine(output) { Focus = focus };
            engine.Start(project, new PlaybackOptions { StartBar = 2 }, _ => { }, () => { });
            Thread.Sleep(200);
            var added = new TrackModel { Name = "T2", MidiProgram = 40, MidiChannel = 5, Measures = TemplateFactory.Measures(45) };   // a free channel, as Add track gives it: the others keep theirs
            project.Tracks.Add(added);
            for (var bar = 0; bar < 45; bar++) Beat(project, 2, bar, 0, 4, 72);
            project.MarkTimelineChanged();
            focus.Refresh(project);   // what the app's live mix push does on the track list change
            var channels = ChannelAllocator.Assign(project);
            var before = output.Messages.Length;
            engine.RefreshArrangement(project, Enumerable.Range(0, 45).ToArray());
            var swapped = WaitForScheduleCondition(() => engine.PublishedTrackIds.Length == 3, 4000);
            var sent = WaitForScheduleCondition(() => output.Messages.Skip(before).Any(m => m.Status == (0xB0 | channels[2]) && m.Note == 7), 2000);
            Thread.Sleep(30);
            var level = LastVolume(output.Messages.Skip(before).ToArray(), channels[2]);
            var want = focus.Scale(added, MixerGroups.Volume(project, added));
            var others = FocusLevelsSent(output, before, channels[0], channels[1]);
            Check("focus track: a track added while playing gets its volume at the focus level", swapped && sent && level == want && want < MixerGroups.Volume(project, added),
                $"swapped {swapped}, sent {level}, want {want}");
            Check("focus track: the other tracks keep their focus levels after a track is added", others == FocusWanted(project, focus, 2),
                $"sent {others}, want {FocusWanted(project, focus, 2)}, channels {string.Join(",", channels)}, volumes sent {string.Join(" ", output.Messages.Skip(before).Where(m => m.Status >> 4 == 0xB && m.Note == 7).Select(m => $"{m.Status & 0xF}:{m.Velocity}"))}");
        }

        // A toggle while playing: off sends the song's own levels, on brings the focus levels back, with no restart.
        {
            var project = MixSeekSong(false);
            var channels = ChannelAllocator.Assign(project);
            var focus = FocusOnFirst(project);
            var output = new ScheduleReuseOutput();
            using var engine = new PlaybackEngine(output) { Focus = focus };
            engine.Start(project, new PlaybackOptions { StartBar = 10 }, _ => { }, () => { });
            Thread.Sleep(200);
            var before = output.Messages.Length;
            focus.Set(false, project, project.Tracks[0], new FocusTrackSettings());
            engine.SetMuteSolo(project);   // what the app's live mix push does on the toggle
            var off = FocusLevelsSent(output, before, channels[0], channels[1]);
            Check("focus track: turning it off while playing sends every track's own level", off == (MixerGroups.Volume(project, project.Tracks[0]), MixerGroups.Volume(project, project.Tracks[1])), $"sent {off}");
            before = output.Messages.Length;
            focus.Set(true, project, project.Tracks[0], new FocusTrackSettings());
            engine.SetMuteSolo(project);
            var on = FocusLevelsSent(output, before, channels[0], channels[1]);
            Check("focus track: turning it on again while playing sends the focus levels", on == FocusWanted(project, focus, 10), $"sent {on}, want {FocusWanted(project, focus, 10)}");
        }
    }

    /// <summary>Focus Track on the first track by the default rules; checks that it really lowers the second track, or the test would prove nothing.</summary>
    private static PracticeFocus FocusOnFirst(SongProject project)
    {
        var focus = new PracticeFocus();
        focus.Set(true, project, project.Tracks[0], new FocusTrackSettings());
        var level = MixerGroups.Volume(project, project.Tracks[1]);
        Check("focus track: the default rules lower the other track", focus.Scale(project.Tracks[1], level) < level && focus.Scale(project.Tracks[1], 80) < 80);
        return focus;
    }

    /// <summary>The level MixSeekSong's two tracks play at in <paramref name="bar"/> (the setup, the bar 20 mix point, the bar 30 point on track 0), after Focus Track.</summary>
    private static (int, int) FocusWanted(SongProject project, PracticeFocus focus, int bar) =>
        (focus.Scale(project.Tracks[0], bar > 30 ? 32 : bar >= 20 ? 80 : MixerGroups.Volume(project, project.Tracks[0])),
         focus.Scale(project.Tracks[1], bar >= 20 ? 80 : MixerGroups.Volume(project, project.Tracks[1])));

    /// <summary>The last channel volume sent on each of the two channels since <paramref name="before"/>, once both have one (-1 when none came).</summary>
    private static (int, int) FocusLevelsSent(ScheduleReuseOutput output, int before, int first, int second)
    {
        WaitForScheduleCondition(() => output.Messages.Skip(before).Any(m => m.Status == (0xB0 | first) && m.Note == 7)
            && output.Messages.Skip(before).Any(m => m.Status == (0xB0 | second) && m.Note == 7), 2000);
        Thread.Sleep(30);
        var sent = output.Messages.Skip(before).ToArray();
        return (LastVolume(sent, first), LastVolume(sent, second));
    }

    private static int LastVolume((int Status, int Note, int Velocity)[] sent, int channel) =>
        sent.LastOrDefault(m => m.Status == (0xB0 | channel) && m.Note == 7) is { Status: > 0 } m ? m.Velocity : -1;
}
