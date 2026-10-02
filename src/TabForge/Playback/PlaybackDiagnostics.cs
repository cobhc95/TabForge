using System.Diagnostics;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

/// <summary>Timing statistics measured from the real scheduler thread (see <see cref="PlaybackDiagnostics.CaptureDispatch"/>).</summary>
public sealed class PlaytestResult
{
    public int DispatchCount;
    public int NoteOnCount;
    public int NoteOffCount;
    public double MinLatencyMs;
    public double MaxLatencyMs;
    public double MeanLatencyMs;
    public double StdDevMs;
    public double P99LatencyMs;
    public int EarlyBeyond2Ms;
    public int LateBeyond25Ms;
    public List<DispatchRecord> WorstOffenders { get; } = new();

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"dispatches={DispatchCount} noteOns={NoteOnCount} noteOffs={NoteOffCount}");
        sb.AppendLine($"latency ms: min={MinLatencyMs:0.00} mean={MeanLatencyMs:0.00} p99={P99LatencyMs:0.00} max={MaxLatencyMs:0.00} stddev={StdDevMs:0.00}");
        sb.AppendLine($"early(<-2ms)={EarlyBeyond2Ms} late(>25ms)={LateBeyond25Ms}");
        foreach (var w in WorstOffenders) sb.AppendLine($"  worst: {w}");
        return sb.ToString();
    }
}

/// <summary>
/// Headless playback diagnostics: a static audit of a compiled timeline and a live capture of the
/// scheduler's dispatch timing. Both run without the GUI, so they can be used by scripts and agents.
/// </summary>
public static class PlaybackDiagnostics
{
    /// <summary>Plays the score for a few seconds on a silent output and measures dispatch latency.</summary>
    public static PlaytestResult CaptureDispatch(SongProject project, PlaybackOptions opt, double seconds)
    {
        var output = new NullMidiOutput();
        using var engine = new PlaybackEngine(output);
        engine.StartDiagnostics();
        engine.Start(project, opt, _ => { }, () => { });
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds && engine.IsPlaying) Thread.Sleep(25);
        engine.Stop();

        var log = engine.DispatchLog;
        var result = new PlaytestResult { DispatchCount = log.Count };
        var noteOns = log.Where(r => r.IsNoteOn).ToList();
        result.NoteOnCount = noteOns.Count;
        result.NoteOffCount = log.Count(r => r.IsNoteOff);

        if (noteOns.Count > 0)
        {
            var latencies = noteOns.Select(r => r.LatencyMs).OrderBy(v => v).ToList();
            result.MinLatencyMs = latencies[0];
            result.MaxLatencyMs = latencies[^1];
            result.MeanLatencyMs = latencies.Average();
            result.StdDevMs = Math.Sqrt(latencies.Sum(v => (v - result.MeanLatencyMs) * (v - result.MeanLatencyMs)) / latencies.Count);
            result.P99LatencyMs = latencies[Math.Min(latencies.Count - 1, (int)(latencies.Count * 0.99))];
            result.EarlyBeyond2Ms = latencies.Count(v => v < -2);
            result.LateBeyond25Ms = latencies.Count(v => v > 25);
            foreach (var r in noteOns.OrderByDescending(r => Math.Abs(r.LatencyMs)).Take(5)) result.WorstOffenders.Add(r);
        }
        return result;
    }

    /// <summary>Static report of what the compiler produced for a score, without playing anything.</summary>
    public static string Audit(SongProject project, PlaybackOptions opt, int eventPreview = 0)
    {
        var timeline = MidiTimelineBuilder.Build(project, opt);
        var sb = new StringBuilder();

        var notes = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));
        var tieDestinations = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count(n => n.Tied))));
        var letRing = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count(n => n.Techniques.Contains("LetRing")))));
        var dead = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count(n => n.Dead))));
        var ghost = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count(n => n.Ghost))));
        var palmMute = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count(n => n.Techniques.Contains("PalmMute")))));
        var tempoChanges = project.MasterBarTrack?.Measures.Count(m => m.TempoChange is not null) ?? 0;
        var sigChanges = project.MasterBarTrack?.Measures.Count(m => m.TimeSigNum is not null) ?? 0;

        sb.AppendLine("== score ==");
        sb.AppendLine($"tracks={project.Tracks.Count} bars={(project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count))} notes={notes} bpm={project.Tempo}");
        sb.AppendLine($"tieDestinations={tieDestinations} letRing={letRing} dead={dead} ghost={ghost} palmMute={palmMute} tempoChanges={tempoChanges} timeSigChanges={sigChanges}");

        sb.AppendLine("== timeline ==");
        sb.AppendLine($"totalMs={timeline.TotalMs:0.0} playFromMs={timeline.PlayFromMs:0.0} countInMs={timeline.CountInMs:0.0} bars={timeline.Bars.Count} events={timeline.Events.Count}");
        sb.AppendLine($"noteOns={timeline.Events.Count(e => e.IsNoteOn)} noteOffs={timeline.Events.Count(e => e.IsNoteOff)} " +
                      $"programs={timeline.Events.Count(e => (e.Status & 0xF0) == 0xC0)} ccs={timeline.Events.Count(e => (e.Status & 0xF0) == 0xB0)} " +
                      $"bends={timeline.Events.Count(e => (e.Status & 0xF0) == 0xE0)}");
        sb.AppendLine($"noteEvents={timeline.Notes.Count} retriggerCollisions={timeline.RetriggerCollisions}");
        sb.AppendLine($"tieMerges={timeline.TieMerges} tieOrphans={timeline.TieOrphans} " +
                      $"letRingExtensions={timeline.LetRingExtensions} maxLetRingExtension={timeline.MaxLetRingExtensionMs:0.0}ms");
        sb.AppendLine($"longestSoundingNote={timeline.LongestSoundingNoteMs:0.0}ms at bar {timeline.LongestSoundingNoteAtBar + 1}");
        foreach (var n in timeline.Notes.OrderByDescending(n => n.DurationMs).Take(8))
            sb.AppendLine($"  long note: bar {n.Bar + 1} cell {n.Cell} track {n.TrackIndex + 1} pitch {n.Midi} " +
                          $"dur {n.DurationMs:0}ms onset {n.OnsetMs:0}ms letRing={n.LetRing}");

        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var perTrack = timeline.NotesFor(t);
            sb.AppendLine($"track {t + 1} '{project.Tracks[t].Name}': notes={perTrack.Length} " +
                          $"attacks={timeline.Events.Count(e => e.IsNoteOn && e.TrackIndex == t)} " +
                          $"channel={project.Tracks[t].MidiChannel} program={project.Tracks[t].MidiProgram}");
        }

        // Unmatched note-ons (hanging notes) is a hard invariant.
        var open = new Dictionary<(int ch, int note), int>();
        var lastOn = new Dictionary<(int ch, int note), ScoreEvent>();
        var orphanOffs = new List<ScoreEvent>();
        foreach (var e in timeline.Events)
        {
            var key = (e.Status & 0x0F, e.Data1);
            if (e.IsNoteOn) { open.TryGetValue(key, out var c); open[key] = c + 1; lastOn[key] = e; }
            else if (e.IsNoteOff)
            {
                if (open.TryGetValue(key, out var c) && c > 0) open[key] = c - 1;
                else orphanOffs.Add(e);
            }
        }
        sb.AppendLine($"hangingNotes={open.Values.Sum()}");
        foreach (var (key, count) in open.Where(p => p.Value > 0).Take(8))
        {
            sb.AppendLine($"  hanging: ch={key.ch} note={key.note} x{count} last on at t={lastOn[key].TimeMs:0.000} track={lastOn[key].TrackIndex}");
            foreach (var w in timeline.Events.Where(w => (w.Status & 0x0F) == key.ch && w.Data1 == key.note && (w.IsNoteOn || w.IsNoteOff) && w.TimeMs > lastOn[key].TimeMs - 400 && w.TimeMs < lastOn[key].TimeMs + 1500).Take(12))
                sb.AppendLine($"      event: t={w.TimeMs:0.000} {(w.IsNoteOn ? "on " : "off")} track={w.TrackIndex}");
            foreach (var n in timeline.Notes.Where(n => n.TrackIndex == lastOn[key].TrackIndex && n.Midi == key.note && Math.Abs(n.OnsetMs - lastOn[key].TimeMs) < 400).Take(4))
                sb.AppendLine($"      note: bar {n.Bar + 1} cell {n.Cell} voice {n.VoiceIndex} string {n.StringIndex} onset={n.OnsetMs:0.0} dur={n.DurationMs:0.0} ch={n.Channel}");
        }
        foreach (var e in orphanOffs.Take(8))
        {
            sb.AppendLine($"  off without on: t={e.TimeMs:0.000} ch={e.Channel} note={e.Data1} track={e.TrackIndex}");
            foreach (var w in timeline.Events.Where(w => (w.Status & 0x0F) == (e.Status & 0x0F) && (w.IsNoteOn || w.IsNoteOff) && w.TimeMs > e.TimeMs - 130 && w.TimeMs < e.TimeMs + 300).Take(24))
                sb.AppendLine($"      event: t={w.TimeMs:0.000} {(w.IsNoteOn ? "on " : "off")} note={w.Data1} track={w.TrackIndex}");
            foreach (var n in timeline.Notes.Where(n => n.TrackIndex == e.TrackIndex && n.Midi == e.Data1 && Math.Abs(n.OnsetMs - e.TimeMs) < 1500).Take(4))
                sb.AppendLine($"      note: bar {n.Bar + 1} cell {n.Cell} voice {n.VoiceIndex} string {n.StringIndex} onset={n.OnsetMs:0.0} dur={n.DurationMs:0.0} ch={n.Channel} letRing={n.LetRing} dead={n.Dead}");
        }

        if (eventPreview > 0)
        {
            sb.AppendLine("== first events ==");
            foreach (var e in timeline.Events.Take(eventPreview))
                sb.AppendLine($"t={e.TimeMs:0.000} ch={e.Channel} {e.Status:X2} {e.Data1} {e.Data2} track={e.TrackIndex}");
        }
        return sb.ToString();
    }
}
