using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the song's bar map for a plug-in transport: per performed bar its start (song seconds), its quarter-note position, tempo and time
//     signature, on the same performed timeline the MIDI playback uses (repeats expanded).
// Does not own: the engine that receives the map (AudioEngineClient.SetTransport), the cache of the map (its caller decides when to rebuild it).
// Tests: no dedicated test class.
// Threads: any thread; reads the song it is given and builds its own timeline.

/// <summary>Builds the plug-in transport's bar map from the performed timeline.</summary>
public static class PerformedTransportBars
{
    /// <summary>The bar map of <paramref name="project"/>: one entry per performed bar, up to the transport's bar limit.</summary>
    public static TransportBar[] From(SongProject project)
    {
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
        var count = Math.Min(timeline.Bars.Count, TransportMap.MaxBars);
        var bars = new TransportBar[count];
        var ppq = 0.0;
        for (var i = 0; i < count; i++)
        {
            var bar = timeline.Bars[i];
            var measure = MusicTime.BarOf(project, bar.Bar);
            var num = Math.Clamp(measure?.TimeSigNum ?? project.TimeSignatureNumerator, 1, 64);
            var den = measure?.TimeSigDenom ?? project.TimeSignatureDenominator;
            if (den is < 1 or > 64 || (den & (den - 1)) != 0) den = 4;
            var tempo = Math.Clamp(bar.Tempo > 0 ? bar.Tempo : project.Tempo, 1, 2000);
            bars[i] = new TransportBar(bar.StartMs / 1000, ppq, tempo, num, den);
            // Quarters actually performed (a Guitar Pro bar that ends early is shorter), so ppq stays continuous with song time.
            var endMs = i + 1 < count ? Math.Max(bar.StartMs, timeline.Bars[i + 1].StartMs) : bar.EndMs;
            ppq += Math.Max(0, endMs - bar.StartMs) / 60000.0 * tempo;
        }
        return bars;
    }
}
