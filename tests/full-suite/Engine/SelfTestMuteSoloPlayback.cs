using System.Diagnostics;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

/// <summary>
/// Mute / solo during playback is mixer state: no timeline invalidation (so no recompile or splice), no UI-thread cost, no timing disturbance.
/// Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Twenty rapid toggles during playback: nothing recompiles, each takes under 2 ms of caller time, the audible track's beats keep their times.</summary>
    private static void TestMuteSoloDoesNotDisturbPlayback()
    {
        var song = new SongProject { Tempo = 120 };
        for (var track = 0; track < 2; track++)
        {
            var t = new TrackModel { Name = $"T{track}", Kind = TrackKind.Guitar, MidiChannel = track, Measures = TemplateFactory.Measures(2) };
            foreach (var measure in t.Measures)
                foreach (var cell in new[] { 0, 4, 8, 12 })
                {
                    measure.Cells[cell].DurationDenominator = 4;
                    measure.Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = 0, MidiValue = 60 + track });
                }
            song.Tracks.Add(t);
        }
        var port = new StampingMidiOutput();
        var routed = new RoutedMidiOutput(port, AudioEngineClient.Instance);
        routed.SetRoutes(Enumerable.Repeat(-1, 16).ToArray());
        var session = new DocumentSession(new PlaybackEngine(routed)) { Project = song };
        var engine = session.Playback.Engine;
        var timelineEvents = 0;
        engine.TimelineChanged += _ => Interlocked.Increment(ref timelineEvents);
        try
        {
            engine.Start(song, new PlaybackOptions { RepeatExpansion = true }, _ => { }, () => { });
            Thread.Sleep(150);
            var eventsBefore = Volatile.Read(ref timelineEvents);
            var revisionBefore = song.TimelineRevision;
            // One warm-up pair (first-call JIT is not the steady click cost), then the measured twenty.
            for (var warm = 0; warm < 2; warm++) { song.Tracks[1].Mute = !song.Tracks[1].Mute; engine.SetMuteSolo(song); DocumentEdits.MarkChanged(session, invalidatesTimeline: false); }
            var worstMs = 0.0;
            for (var toggle = 0; toggle < 20; toggle++)
            {
                var watch = Stopwatch.StartNew();
                song.Tracks[1].Mute = !song.Tracks[1].Mute;
                engine.SetMuteSolo(song);
                DocumentEdits.MarkChanged(session, invalidatesTimeline: false);
                worstMs = Math.Max(worstMs, watch.Elapsed.TotalMilliseconds);
                Thread.Sleep(80);
            }
            Thread.Sleep(100);
            engine.Stop();
            Check("Mute/solo toggles during playback: the timeline revision is untouched (nothing recompiles or splices) and the song is dirty",
                song.TimelineRevision == revisionBefore && Volatile.Read(ref timelineEvents) == eventsBefore && song.IsDirty,
                $"revision {revisionBefore}->{song.TimelineRevision}, timeline events {eventsBefore}->{timelineEvents}");
            Check("Mute/solo toggles: each one takes under 2 ms on the calling thread (worst of 20)", worstMs < 2, $"{worstMs:0.00} ms");
            List<(long Ticks, int Status, int Data1)> sent; lock (port.Sent) sent = port.Sent.ToList();
            var audible = sent.Where(x => (x.Status & 0xF0) == 0x90 && (x.Status & 0x0F) == 0).Select(x => x.Ticks).ToList();
            var gaps = audible.Zip(audible.Skip(1), (a, b) => (b - a) * 1000.0 / Stopwatch.Frequency).Where(g => g < 900).ToList();
            var worstGap = gaps.Count == 0 ? double.MaxValue : gaps.Max(g => Math.Abs(g - 500));
            Check("Mute/solo toggles: the audible track's beats keep their times (within 12 ms of 500 ms)", gaps.Count >= 3 && worstGap < 12, $"{gaps.Count} gaps, worst off by {worstGap:0.0} ms");
            var cuts = sent.Count(x => (x.Status & 0xF0) == 0xB0 && x.Data1 == 123);
            var audibleCuts = sent.Count(x => (x.Status & 0xF0) == 0xB0 && x.Data1 == 123 && (x.Status & 0x0F) == 0);
            Check("Mute/solo toggles: all-notes-off goes only to the track that was silenced, never the audible one", audibleCuts == 0 && cuts >= 1, $"{cuts} cuts, {audibleCuts} on the audible channel");
            var before = song.TimelineRevision;
            DocumentEdits.MarkChanged(session);
            Check("A score edit still invalidates the timeline (the default of MarkChanged)", song.TimelineRevision != before);
        }
        finally { engine.Dispose(); }
    }
}
