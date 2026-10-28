using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

// Owns: the check that a score edit made while a song is loaded in the engine replaces the compiled schedule (deleted notes stop sounding).

/// <summary>Deleting notes after a song was compiled: a mixer-only mark keeps the schedule, a delete replaces it with one without the notes, paused or playing.</summary>
public static partial class SelfTest
{
    private static SongProject EditRefreshSong()
    {
        var p = new SongProject { Tempo = 300 };
        p.Tracks.Add(new TrackModel { Name = "T", MidiProgram = 25, Measures = TemplateFactory.Measures(4) });
        for (var bar = 0; bar < 4; bar++) Beat(p, 0, bar, 0, 4, 60 + bar);   // one distinct pitch per bar
        return p;
    }

    private static bool Sounds(ScoreTimeline? timeline, int pitch) => timeline is not null && timeline.Events.Any(e => e.IsNoteOn && !e.IsMetronome && e.Data1 == pitch);

    private static void TestPlaybackEditRefresh()
    {
        foreach (var paused in new[] { true, false })
        {
            var project = EditRefreshSong();
            var output = new ScheduleReuseOutput();
            using var engine = new PlaybackEngine(output);
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: paused);
            var refresh = new PlaybackEditRefresh(engine);
            var label = paused ? "paused" : "playing";
            Check($"edit refresh ({label}): the compiled schedule has bar 3's note before the edit", Sounds(engine.Timeline, 63));

            project.Tracks[0].Volume = 50;   // a mixer-only mark: nothing audible changed
            project.IsDirty = true;
            Check($"edit refresh ({label}): a mixer-only mark keeps the schedule", !refresh.Run(project) && Sounds(engine.Timeline, 63));

            foreach (var cell in project.Tracks[0].Measures[3].Cells) { cell.Notes.Clear(); cell.IsRest = true; }   // delete bar 3's note
            project.IsDirty = true;
            project.MarkTimelineChanged();
            Check($"edit refresh ({label}): a delete re-schedules", refresh.Run(project));
            var replaced = WaitForScheduleCondition(() => !Sounds(engine.Timeline, 63), 3000);
            Check($"edit refresh ({label}): the deleted note is gone from the compiled schedule", replaced && Sounds(engine.Timeline, 60), $"bar 3 still sounds: {!replaced}");
            Check($"edit refresh ({label}): a second refresh with no further edit does nothing", !refresh.Run(project));
            engine.Stop();
        }
    }
}
