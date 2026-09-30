using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>GP5 mix-table fades (transition beats, all-tracks flag) and the audible-playhead latency conversion.</summary>
public static partial class SelfTest
{
    private static void TestMixFadeAndAudiblePlayhead()
    {
        // Latency is wall-clock time: the compiled timeline already runs at the playback speed (50% included),
        // so only the speed-trainer clock rate scales it.
        Near("audible playhead: 20 ms latency at 100%", 980, PlaybackEngine.AudibleStreamMs(1000, 20, 1.0), 0.001);
        Near("audible playhead: 20 ms latency, 50% speed (compiled) stays 20 ms", 980, PlaybackEngine.AudibleStreamMs(1000, 20, 1.0), 0.001);
        Near("audible playhead: 20 ms latency at a 50% trainer clock is 10 stream ms", 990, PlaybackEngine.AudibleStreamMs(1000, 20, 0.5), 0.001);

        // Raw GP5 mix table from its volume byte: vol 8, rest unchanged, empty tempo name, no tempo, 16 beats, all tracks.
        var raw = new byte[] { 8, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 16, 1, 0, 0, 0 };
        var parsed = GuitarProMixTableScanner.TryParse(raw, 0, gp5: true, hideTempoByte: true, out var mix, out _);
        Check("GP5 mix table: transition beats and all-tracks flag are read",
            parsed && mix.Volume == 8 && mix.TransitionBeats == 16 && mix.AllTracks, parsed ? mix.ToString() : "not parsed");

        // An all-tracks volume ramp set on one track fades every track's CC7 over the transition.
        var p = new SongProject { Tempo = 120 };
        p.Tracks.Add(new TrackModel { Name = "A", Volume = 100, Measures = TemplateFactory.Measures(2) });
        p.Tracks.Add(new TrackModel { Name = "B", Volume = 100, Measures = TemplateFactory.Measures(2) });
        p.Tracks[1].Measures[0].Cells[0].Mix = new MixChange { Volume = 0, TransitionBeats = 4, AllTracks = true };
        var timeline = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var rampA = timeline.Events.Where(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0 && e.Data1 == 7).OrderBy(e => e.TimeMs).ToList();
        Check("mix-table fade ramps every track's volume down to silence",
            rampA.Count >= 8 && rampA[^1].Data2 == 0 && rampA[^1].TimeMs > 1900 && rampA.Zip(rampA.Skip(1)).All(z => z.Second.Data2 <= z.First.Data2),
            string.Join(",", rampA.Select(e => $"{e.Data2}@{e.TimeMs:0}")));

        // Gradual tempo ramp: 120 -> 60 BPM over 4 beats (16 slots) from the start of bar 1. Analytic integral of
        // 60000 / (120 - 15 b) over b = 0..x beats is 4000 * ln(120 / (120 - 15 x)) ms.
        var ramp = new SongProject { Tempo = 120 };
        ramp.Tracks.Add(new TrackModel { Name = "R", Volume = 100, Measures = TemplateFactory.Measures(2) });
        ramp.Tracks[0].Measures[0].MidBarTempos = new List<TempoPoint> { new(0, 60, 16) };
        var offsets = Enumerable.Range(0, 17).Select(s => MusicTime.OffsetMs(ramp.Tracks[0].Measures[0], s, 120)).ToList();
        Check("tempo ramp: bar offsets are monotonic", offsets.Zip(offsets.Skip(1)).All(z => z.Second > z.First), string.Join(",", offsets.Select(o => o.ToString("0"))));
        Near("tempo ramp: 2 beats in (90 BPM) matches the analytic integral", 4000 * Math.Log(120.0 / 90.0), offsets[8], 0.01);
        Near("tempo ramp: whole 4-beat bar matches the analytic integral", 4000 * Math.Log(2), offsets[16], 0.01);
        Near("tempo ramp: bar 2 starts after the ramped bar at the target tempo", 4000 * Math.Log(2) + 4000, MusicTime.BarMs(ramp, 0) + MusicTime.BarMs(ramp, 1), 0.01);
        var rampBars = MidiTimelineBuilder.Build(ramp, new PlaybackOptions()).Bars;
        Near("tempo ramp: compiled timeline bar 2 start follows the ramped tempo map", 4000 * Math.Log(2), rampBars.Count > 1 ? rampBars[1].StartMs : -1, 0.5);

        var candidate = LocalSongsDisabled ? null : LocalReferenceSongs.Resolve("reference-b");
        if (candidate is not null)
        {
            var imported = GuitarProImporter.Import(candidate);
            var fades = imported.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells))
                .Select(c => c.Mix).Where(m => m is { AllTracks: true, TransitionBeats: 16, Volume: not null }).Count();
            Check("reference song B: the closing fade-out imports as all-track volume ramps", fades >= 3, $"{fades} ramps");
        }
    }
}
