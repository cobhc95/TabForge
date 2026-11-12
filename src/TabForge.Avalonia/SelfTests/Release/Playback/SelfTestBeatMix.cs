using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge;

// Owns: the Mix Table audit: every change a beat carries (any track's mix point, the bar tempo, a gradual tempo point) is found by the Mix Table and marked on the
//   timeline, and Focus Track keeps scaling a track after a mix-table volume point plays. With TABFORGE_AUDIT_DIR set it also audits every Guitar Pro file under that folder.

/// <summary>Mix Table / timeline mark / Focus Track audit, on generated songs (and on a sample folder when asked).</summary>
public static partial class SelfTest
{
    private static void TestBeatMixAudit()
    {
        var project = MixSeekSong(false);   // bar 20: volume 10 on every track; bar 30: volume ramp on track 0
        project.Tracks[1].Measures[5].MidBarTempos = new List<TempoPoint> { new(0, 150, 16) };
        project.Tracks[0].Measures[5].MidBarTempos = new List<TempoPoint> { new(0, 150, 16) };
        project.Tracks[0].Measures[8].TempoChange = 90;

        Check("beat mix: a gradual tempo is found at its beat with its length in beats", BeatMix.TempoAt(project, 5, 0) == new BeatTempo(150, 4));
        Check("beat mix: a bar tempo is found at the bar's first beat", BeatMix.TempoAt(project, 8, 0) == new BeatTempo(90, 0));
        Check("beat mix: a bar without a tempo shows none", BeatMix.TempoAt(project, 6, 0) is null);
        Check("beat mix: another track's all-tracks mix point is shown on this track's beat", BeatMix.MixAt(project, 1, 20, 0)?.Volume == 10);
        Check("beat mix: a point for one track only is not shown on another track", BeatMix.MixAt(project, 1, 30, 0) is null);
        Check("beat mix: bars with a mix point, a bar tempo or a gradual tempo are marked, the others are not",
            BeatMix.BarsWithChanges(project).OrderBy(b => b).SequenceEqual(new[] { 5, 8, 20, 30 }));
        BeatMix.SetTempo(project, 12, 0, new BeatTempo(170, 2));
        Check("beat mix: a set gradual tempo lands on every track and reads back", project.Tracks.All(t => t.Measures[12].MidBarTempos?.Count == 1) && BeatMix.TempoAt(project, 12, 0) == new BeatTempo(170, 2));
        BeatMix.SetTempo(project, 12, 0, null);
        Check("beat mix: clearing the tempo removes it from every track", project.Tracks.All(t => t.Measures[12].MidBarTempos is null && t.Measures[12].TempoChange is null));

        // Focus Track after a mix point: every track's volume after bar 20 carries the focus scaling (the mix point's own 80 is lowered / raised like the setup).
        var channels = ChannelAllocator.Assign(project);
        var focus = new PracticeFocus();
        focus.Set(true, project, project.Tracks[0], new FocusTrackSettings());
        var output = new ScheduleReuseOutput();
        using (var engine = new PlaybackEngine(output) { Focus = focus })
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
            var before = output.Messages.Length;
            engine.Seek(project, 25, 0);
            WaitForScheduleSeek(engine, 25);
            WaitForScheduleCondition(() => output.Messages.Skip(before).Any(m => m.Status == (0xB0 | channels[1]) && m.Note == 7), 2000);
            Thread.Sleep(30);
            var sent = output.Messages.Skip(before).ToArray();
            int Level(int track) => sent.LastOrDefault(m => m.Status == (0xB0 | channels[track]) && m.Note == 7) is { Status: > 0 } m ? m.Velocity : -1;
            Check("focus track: the track left out is still lowered after a mix point set its volume", Level(1) == focus.Scale(project.Tracks[1], 80) && Level(1) < 80, $"sent {Level(1)}, mix point 80, focus {focus.Scale(project.Tracks[1], 80)}");
            Check("focus track: the focused track keeps its boost after a mix point", Level(0) == focus.Scale(project.Tracks[0], 80), $"sent {Level(0)}, focus {focus.Scale(project.Tracks[0], 80)}");
        }

        if (Environment.GetEnvironmentVariable("TABFORGE_AUDIT_DIR") is { Length: > 0 } dir && Directory.Exists(dir)) AuditSampleFolder(dir);
    }

    private static void AuditSampleFolder(string dir)
    {
        var log = Environment.GetEnvironmentVariable("TABFORGE_AUDIT_LOG");
        var lines = new List<string>();
        string[] ext = { ".gp", ".gp3", ".gp4", ".gp5", ".gpx", ".gtp" };
        int files = 0, opened = 0, bars = 0, marked = 0, tempoPoints = 0, mixPoints = 0, ramps = 0;
        var problems = new List<string>();
        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(f => ext.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            files++;
            SongProject song;
            try { song = GuitarProImporter.Import(path); }
            catch (Exception ex) { lines.Add($"UNREADABLE {path}: {ex.GetType().Name}"); continue; }
            opened++;
            try
            {
                var marks = BeatMix.BarsWithChanges(song);
                marked += marks.Count;
                for (var t = 0; t < song.Tracks.Count; t++)
                    for (var b = 0; b < song.Tracks[t].Measures.Count; b++)
                    {
                        bars++;
                        var m = song.Tracks[t].Measures[b];
                        if (m.TempoChange is not null && BeatMix.TempoAt(song, b, 0) is null) problems.Add($"{path} bar {b + 1}: bar tempo not shown");
                        foreach (var p in m.MidBarTempos ?? new())
                        {
                            tempoPoints++;
                            if (p.RampSlots > 0) ramps++;
                            if (BeatMix.TempoAt(song, b, p.Slot) is null) problems.Add($"{path} bar {b + 1} slot {p.Slot}: tempo {p.Tempo} not shown");
                            if (!marks.Contains(b)) problems.Add($"{path} bar {b + 1}: tempo point without a mark");
                        }
                        for (var c = 0; c < m.Cells.Count; c++)
                            if (m.Cells[c].Mix is { IsEmpty: false })
                            {
                                mixPoints++;
                                if (BeatMix.MixAt(song, t, b, c) is null) problems.Add($"{path} track {t + 1} bar {b + 1}: mix point not shown");
                                if (!marks.Contains(b)) problems.Add($"{path} bar {b + 1}: mix point without a mark");
                            }
                    }
                var timeline = new ScoreToMidiCompiler(song, new PlaybackOptions()).Build();
                foreach (var e in timeline.Events.Where(e => !e.IsSetup && (e.Status & 0xF0) == 0xB0 && e.Data1 == 7 && (e.TrackIndex < 0 || e.TrackIndex >= song.Tracks.Count)).Take(1))
                    problems.Add($"{path}: volume event without a track at {e.TimeMs:F0} ms");
            }
            catch (Exception ex) { problems.Add($"{path}: audit threw {ex.GetType().Name}: {ex.Message}"); }
        }
        lines.Insert(0, $"files {files}, opened {opened}, bars {bars}, bars marked {marked}, mix points {mixPoints}, tempo points {tempoPoints} ({ramps} gradual), problems {problems.Count}");
        lines.AddRange(problems.Take(200));
        if (log is { Length: > 0 }) File.WriteAllLines(log, lines);
        Check($"mix table audit of {dir}: every mix point and tempo change is shown and marked", problems.Count == 0, $"{problems.Count} problems, first: {problems.FirstOrDefault()}");
    }
}
