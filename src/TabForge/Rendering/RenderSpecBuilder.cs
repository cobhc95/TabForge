using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Rendering;

/// <summary>Last-used File > Render options (stored in the app settings).</summary>
public sealed class RenderSettings
{
    public int Source { get; set; }          // RenderSource
    public List<string>? StemTrackNames { get; set; }   // checked tracks for "Stems: selected tracks" (null = not chosen yet)
    public int Bounds { get; set; }          // RenderBounds
    public double CustomStartSec { get; set; }
    public double CustomEndSec { get; set; }
    public int TailMode { get; set; } = 1;   // RenderTailMode
    public int TailMs { get; set; } = 3000;
    public string Directory { get; set; } = "";
    public string Pattern { get; set; } = "$project";
    public int SampleRate { get; set; }      // 0 = engine
    public bool Mono { get; set; }
    public bool SingleThread { get; set; }
    public bool RealtimePace { get; set; }
    public int Format { get; set; } = 1;     // 0 WAV16, 1 WAV24, 2 WAV32f, 3 MP3
    public int Mp3Kbps { get; set; } = 192;
    public bool OpenFolder { get; set; } = true;
}

public enum RenderSource { Master, StemsSelected, StemsAll, MasterAndStems }
public enum RenderBounds { Song, TimeSelection, Bars, Custom }

/// <summary>Turns a song into a <see cref="RenderSpec"/> and its event file (see docs/history/RENDER_PLAN.md).</summary>
public static class RenderSpecBuilder
{
    /// <summary>The song as MIDI, from bar 0, with no metronome, count-in or loop and ignoring mute/solo (the render applies those itself).</summary>
    public static ScoreTimeline Compile(SongProject project) => MidiTimelineBuilder.Build(project, new PlaybackOptions
    {
        Metronome = false, CountIn = false, Loop = false, RespectMuteSolo = false, Speed = 1.0, LiveMetronomeEvents = false,
    });

    /// <summary>Range in song milliseconds for a bounds choice. Bars are source bar indices; cells are grid cells inside them (-1 = to the bar end).</summary>
    public static (double StartMs, double EndMs) Bounds(ScoreTimeline tl, RenderBounds bounds, int startBar, int startCell, int endBar, int endCell, double customStartSec, double customEndSec)
    {
        double total = Math.Max(tl.TotalMs, tl.Bars.Count > 0 ? tl.Bars[^1].EndMs : 0);
        switch (bounds)
        {
            case RenderBounds.Custom: return (Math.Max(0, customStartSec * 1000), Math.Min(total, customEndSec * 1000));
            case RenderBounds.Bars:
            case RenderBounds.TimeSelection:
                if (tl.Bars.Count == 0) break;
                var first = tl.Bars.FirstOrDefault(b => b.Bar >= startBar, tl.Bars[^1]);
                var last = tl.Bars.LastOrDefault(b => b.Bar <= endBar, tl.Bars[0]);
                var s = first.StartMs; var e = last.EndMs;
                if (bounds == RenderBounds.TimeSelection)
                {
                    if (first.Bar == startBar && first.Slots > 0 && startCell > 0) s = first.MsAtFraction((double)Math.Min(startCell, first.Slots) / first.Slots);
                    if (last.Bar == endBar && last.Slots > 0 && endCell >= 0) e = last.MsAtFraction((double)Math.Min(endCell + 1, last.Slots) / last.Slots);
                }
                return (s, Math.Max(s, e));
        }
        return (0, total);
    }

    public static long ToFrames(double ms, int rate) => (long)Math.Round(ms * rate / 1000.0);

    /// <summary>
    /// Tempo map (frame, tempo, quarter-note position) from the performed bars. With the project, bars holding mid-bar tempo
    /// changes or ramps get a point at every sixteenth slot whose whole-BPM tempo differs, using the same per-slot data
    /// (<see cref="MusicTime.TempoAtSlot"/> / <see cref="MusicTime.OffsetMs"/>) as playback and the MIDI export's tempo track.
    /// </summary>
    public static List<RenderTempoPoint> TempoMap(ScoreTimeline tl, int rate, double fallbackTempo, SongProject? project = null)
    {
        var map = new List<RenderTempoPoint>();
        if (tl.Bars.Count == 0) { map.Add(new RenderTempoPoint(0, fallbackTempo > 0 ? fallbackTempo : 120, 0)); return map; }
        double ppq = 0; double prevTempo = tl.Bars[0].Tempo > 0 ? tl.Bars[0].Tempo : 120; var prevVaried = false;
        foreach (var bar in tl.Bars)
        {
            var startTempo = bar.Tempo > 0 ? bar.Tempo : (int)prevTempo;
            var measure = project is null ? null : MusicTime.BarOf(project, bar.Bar);
            var varied = measure?.MidBarTempos is { Count: > 0 };
            double tempo = varied ? MusicTime.TempoAtSlot(measure, 0, startTempo) : startTempo;   // a step at slot 0 already holds
            // A bar after a varying one is re-anchored so slot-level rounding does not carry into it.
            if (map.Count == 0 || prevVaried || Math.Abs(map[^1].Tempo - tempo) > 1e-6) map.Add(new RenderTempoPoint(ToFrames(bar.StartMs, rate), tempo, ppq));
            var last = tempo;
            var firstInBar = map.Count;
            if (varied)
                for (var slot = 1; slot < bar.Slots; slot++)
                {
                    var slotTempo = MusicTime.TempoAtSlot(measure, slot, startTempo);
                    if (slotTempo == last) continue;
                    map.Add(new RenderTempoPoint(ToFrames(bar.StartMs + FermataSpan.Warp(bar.Fermatas, MusicTime.OffsetMs(measure, slot, startTempo)), rate), slotTempo, ppq + slot / (double)MusicTime.SlotsPerQuarter));
                    last = slotTempo;
                }
            // A fermata hold is a slower stretch of the map: the beat's quarter-notes take their length plus the hold.
            if (bar.Fermatas is { } holds)
            {
                foreach (var hold in holds)
                {
                    var holdTempo = varied ? MusicTime.TempoAtSlot(measure, hold.Slot, startTempo) : startTempo;
                    var endSlot = hold.Slot + hold.LengthSlots;
                    var endTempo = varied ? MusicTime.TempoAtSlot(measure, endSlot, startTempo) : startTempo;
                    var baseEnd = hold.BaseStartMs + hold.BaseLengthMs;
                    map.Add(new RenderTempoPoint(ToFrames(bar.StartMs + FermataSpan.Warp(holds, hold.BaseStartMs), rate),
                        holdTempo * hold.BaseLengthMs / Math.Max(1e-6, hold.BaseLengthMs + hold.ExtraMs), ppq + hold.Slot / (double)MusicTime.SlotsPerQuarter));
                    map.Add(new RenderTempoPoint(ToFrames(bar.StartMs + FermataSpan.Warp(holds, baseEnd), rate),
                        endTempo, ppq + endSlot / (double)MusicTime.SlotsPerQuarter));
                    last = endTempo;
                }
                var added = map.GetRange(firstInBar, map.Count - firstInBar).OrderBy(point => point.Ppq).ToList();
                map.RemoveRange(firstInBar, map.Count - firstInBar);
                map.AddRange(added);
            }
            ppq += bar.Slots / (double)MusicTime.SlotsPerQuarter;
            prevTempo = varied ? last : tempo; prevVaried = varied || bar.Fermatas is not null;
        }
        if (map[0].Frame != 0) map.Insert(0, new RenderTempoPoint(0, map[0].Tempo, 0));
        return map;
    }

    /// <summary>Sorted, slot-addressed events: setup (program, volume, pan...) at frame 0, notes only inside the range.</summary>
    public static List<RenderEvent> Events(ScoreTimeline tl, SongProject project, AudioEngineClient engine, int rate, long startFrame, long endFrame)
    {
        var slots = project.Tracks.Select(engine.SlotOf).ToArray();
        var setup = new List<RenderEvent>(); var events = new List<RenderEvent>();
        var seen = new HashSet<ScoreEvent>(ReferenceEqualityComparer.Instance);
        foreach (var e in tl.ChannelSetup.Concat(tl.Events))
        {
            if (e.IsMetronome || e.TrackIndex < 0 || e.TrackIndex >= slots.Length || slots[e.TrackIndex] < 0 || !seen.Add(e)) continue;
            var frame = ToFrames(e.TimeMs, rate);
            var kind = e.Status & 0xF0;
            var re = new RenderEvent { Frame = frame, Slot = slots[e.TrackIndex], Status = (byte)e.Status, Data1 = (byte)e.Data1, Data2 = (byte)e.Data2 };
            if (e.IsSetup || (frame < startFrame && kind != 0x80 && kind != 0x90)) { re.Frame = 0; setup.Add(re); continue; }
            if (frame >= endFrame || frame < startFrame) continue;
            events.Add(re);
        }
        events = events.OrderBy(x => x.Frame).ToList();   // stable
        setup.AddRange(events);
        return setup;
    }

    public static RenderSpec Build(SongProject project, ScoreTimeline tl, AudioEngineClient engine, int masterPercent, long startFrame, long endFrame,
        RenderSettings s, string masterPath, IReadOnlyDictionary<TrackModel, string> stems, string eventFile, int rate)
    {
        var anySolo = project.Tracks.Any(t => t.Solo);
        var spec = new RenderSpec
        {
            StartFrame = startFrame, EndFrame = endFrame, TailMode = (RenderTailMode)Math.Clamp(s.TailMode, 0, 2), TailMs = Math.Clamp(s.TailMs, 0, 30000),
            SampleRate = s.SampleRate, Channels = s.Mono ? 1 : 2, MasterPath = masterPath, EventFile = eventFile,
            MasterGain = Math.Clamp(masterPercent, 0, 100) / 100f, Threads = s.SingleThread ? RenderThreads.One : RenderThreads.Auto, RealtimePace = s.RealtimePace,
            Tempo = TempoMap(tl, rate, project.Tempo, project),
            Format = s.Format switch { 0 => RenderFormat.Pcm16, 2 or 3 => RenderFormat.Float32, _ => RenderFormat.Pcm24 },
        };
        foreach (var t in project.Tracks)
        {
            var slot = engine.SlotOf(t);
            if (slot < 0) continue;
            var silent = t.Mute || (anySolo && !t.Solo) || MixerGroups.GroupSilences(project, t);
            spec.Slots.Add(new RenderSlot
            {
                Slot = slot, Volume = MixerGroups.Volume(project, t), Pan = MixerGroups.Pan(project, t), InMaster = !silent,
                StemPath = stems.TryGetValue(t, out var stem) ? stem : "",
            });
        }
        return spec;
    }
}
