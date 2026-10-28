using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

// Owns: the check that a seek made while a song plays returns on the calling (UI) thread at once, whatever the song holds.

/// <summary>A seek during playback of a bend-heavy seven-track song (reused schedule, a seek that recompiles, and a seek after an edit) returns in well under a frame.</summary>
public static partial class SelfTest
{
    private const double SeekCallBudgetMs = 16;

    private static SongProject SeekBendSong()
    {
        var p = new SongProject { Tempo = 105 };
        for (var t = 0; t < 7; t++) p.Tracks.Add(new TrackModel { Name = "T" + t, MidiProgram = 29 + t, Measures = TemplateFactory.Measures(12) });
        for (var bar = 0; bar < 12; bar++)
            for (var t = 0; t < 7; t++)
                for (var cell = 0; cell < 16; cell += 2)
                {
                    var beat = Beat(p, t, bar, cell, 8, 50 + t + cell % 5);
                    beat.Notes[0].BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 20, Value = 4 }, new() { Offset = 60, Value = 4 }, new() { Offset = 100, Value = 0 } };
                    beat.Notes[0].BendTypeName = "BendRelease";
                }
        return p;
    }

    private static void TestSeekDuringPlayback()
    {
        var project = SeekBendSong();
        using var engine = new PlaybackEngine(new ScheduleReuseOutput());
        engine.Start(project, new PlaybackOptions(), _ => { }, () => { });
        Thread.Sleep(300);
        double Seek(int bar)
        {
            var timer = Stopwatch.StartNew();
            engine.Seek(project, bar, 0);
            var ms = timer.Elapsed.TotalMilliseconds;
            Thread.Sleep(80);
            return ms;
        }
        Seek(3);   // the first call pays the JIT of the seek path, as the app's first seek does after its start
        var reuse = new[] { 2, 3, 2, 5, 1 }.Select(Seek).Max();
        Check("seek during playback (schedule reused): returns on the calling thread at once", reuse < SeekCallBudgetMs, $"{reuse:0.0} ms");

        project.Tracks[0].Measures[6].Cells[0].Notes[0].Fret = 3;   // an edit the schedule does not know: the next seek takes a fresh copy and recompiles
        project.IsDirty = true;
        project.MarkTimelineChanged();
        var rebuild = new[] { 2, 4, 1 }.Select(Seek).Max();
        Check("seek during playback (after an edit, recompiled in the background): returns on the calling thread at once", rebuild < SeekCallBudgetMs, $"{rebuild:0.0} ms");

        var started = Stopwatch.StartNew();
        engine.Seek(project, 2, 0);
        var resumed = WaitForScheduleCondition(() => engine.Playhead().Bar == 2, 2000);
        Check("seek during playback: the playhead is at the target bar", resumed, $"bar {engine.Playhead().Bar} after {started.ElapsedMilliseconds} ms");
        engine.Stop();
    }
}
