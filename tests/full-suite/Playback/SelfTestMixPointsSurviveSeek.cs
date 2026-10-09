using System.Linq;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

/// <summary>A mix-table point (program, volume, pan) keeps its effect after a seek, a recompile or a loop wrap past it.</summary>
public static partial class SelfTest
{
    // Bar 20: program 30, volume 10 (=80), pan 4 (=96), volume on every track. Bar 30: volume 4 (=32) ramped over two beats.
    private static SongProject MixSeekSong(bool repeats)
    {
        var p = new SongProject { Tempo = 400 };
        for (var t = 0; t < 2; t++) p.Tracks.Add(new TrackModel { Name = "T" + t, MidiProgram = 25 + t, Measures = TemplateFactory.Measures(45) });
        for (var bar = 0; bar < 45; bar++) for (var t = 0; t < 2; t++) Beat(p, t, bar, 0, 4, 60 + t);
        p.Tracks[0].Measures[20].Cells[0].Mix = new MixChange { Program = 30, Volume = 10, Pan = 4, AllTracks = true };
        p.Tracks[0].Measures[30].Cells[0].Mix = new MixChange { Volume = 4, TransitionBeats = 2 };
        if (repeats) { p.Tracks[0].Measures[0].RepeatStart = true; p.Tracks[0].Measures[44].RepeatEnd = true; }
        return p;
    }

    private static void TestMixPointsSurviveSeek()
    {
        foreach (var repeats in new[] { false, true })
        {
            var project = MixSeekSong(repeats);
            var channels = ChannelAllocator.Assign(project);
            var output = new ScheduleReuseOutput();
            using var engine = new PlaybackEngine(output);
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
            var failures = new List<string>();
            foreach (var bar in new[] { 10, 20, 25, 40, 30, 20, 10, 40, 25 })
            {
                var before = output.Messages.Length;
                engine.Seek(project, bar, 0);
                WaitForScheduleSeek(engine, bar);
                if (repeats) engine.Resume();   // a recompiled schedule sends its setup when it runs
                WaitForScheduleCondition(() => output.Messages.Skip(before).Any(m => m.Status == (0xB0 | channels[0]) && m.Note == 7), 2000);
                Thread.Sleep(30);
                if (repeats) engine.Pause();
                var sent = output.Messages.Skip(before).ToArray();
                int Last(int status, int data = -1) => sent.LastOrDefault(m => m.Status == status && (data < 0 || m.Note == data)) is { Status: > 0 } m
                    ? (status >> 4 == 0xC ? m.Note : m.Velocity) : -1;
                var program = bar >= 20 ? 30 : 25;
                var volume = bar > 30 ? 32 : bar >= 20 ? 80 : MixerGroups.Volume(project, project.Tracks[0]);
                var pan = bar >= 20 ? 96 : MixerGroups.Pan(project, project.Tracks[0]);
                var other = bar >= 20 ? 80 : MixerGroups.Volume(project, project.Tracks[1]);
                var got = (Last(0xC0 | channels[0]), Last(0xB0 | channels[0], 7), Last(0xB0 | channels[0], 10), Last(0xB0 | channels[1], 7));
                if (bar == 30 && got.Item2 is >= 32 and <= 80) volume = got.Item2;   // the ramp has begun by the time the paused schedule is read
                if (got != (program, volume, pan, other)) failures.Add($"bar {bar} ({sent.Length} msgs, playhead {engine.Playhead().Bar}): got {got}, expected ({program}, {volume}, {pan}, {other})");
            }
            Check($"mix points survive seeks ({(repeats ? "repeat song, recompiled" : "plain song, in place")}): program/volume/pan sent after each jump", failures.Count == 0, string.Join("; ", failures));
        }

        // A start (stopped, fresh compile) at a later bar carries the same state.
        foreach (var bar in new[] { 10, 21, 25, 40 })
        {
            var project = MixSeekSong(true);
            var channels = ChannelAllocator.Assign(project);
            var timeline = new ScoreToMidiCompiler(project, new PlaybackOptions { StartBar = bar }).Build();
            int Setup(int status, int data) => timeline.ChannelSetup.Last(e => e.TrackIndex == 0 && e.Status == status && (status >> 4 == 0xC || e.Data1 == data)) is var e
                ? (status >> 4 == 0xC ? e.Data1 : e.Data2) : -1;
            var ok = Setup(0xC0 | channels[0], 0) == (bar >= 20 ? 30 : 25) && Setup(0xB0 | channels[0], 10) == (bar >= 20 ? 96 : MixerGroups.Pan(project, project.Tracks[0]));
            Check($"mix points survive a fresh compile starting at bar {bar}", ok, $"program {Setup(0xC0 | channels[0], 0)}, pan {Setup(0xB0 | channels[0], 10)}");
        }
    }
}
