using System.IO;
using System.Text;
using TabForge.AudioEngine.Synth;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Diagnostics;

/// <summary>
/// `TabForge.exe --audit-gm &lt;song&gt; &lt;report&gt;`: plays a whole song through the audio engine's General MIDI synth (every message the
/// score compiler produces, each track on its own channel, all voiced as a flute so the pitch is easy to measure), detects the pitch
/// of every clear single note and compares it with what the score says, bends and slides included. Wrong notes are listed by bar and
/// technique, so a difference between the new engine and the notation is found by measurement, across a whole song.
/// </summary>
public static class GmSongAudit
{
    public static int Run(string songPath, string reportPath) =>
        RunProject(Path.GetExtension(songPath).Equals(".tforge", StringComparison.OrdinalIgnoreCase) ? ProjectService.Load(songPath) : GuitarProImporter.Import(songPath),
            Path.GetFileName(songPath), reportPath);

    /// <summary>
    /// A test song with one bar per technique (and several bend shapes): two clear quarter notes each, so the pitch of every
    /// technique can be measured through the engine.
    /// </summary>
    public static SongProject TechniqueSong()
    {
        var song = TabForge.Presets.TemplateFactory.Blank();
        var track = song.Tracks[0];
        var bars = new List<(string Name, Action<TabNote> Apply)>();
        foreach (var name in GpEffects.All) bars.Add((name, n => n.Techniques.Add(name)));
        void Bend(string label, params (double Offset, double Value)[] points) =>
            bars.Add((label, n => { n.Techniques.Add("Bend"); n.BendPoints = points.Select(p => new BendPointModel { Offset = p.Offset, Value = p.Value }).ToList(); }));
        Bend("bend up 1 st", (0, 0), (15, 4), (60, 4));
        Bend("bend up 2 st", (0, 0), (15, 8), (60, 8));
        Bend("pre-bend 1 st", (0, 4), (60, 4));
        Bend("pre-bend and release", (0, 4), (30, 4), (45, 0), (60, 0));
        Bend("bend and release", (0, 0), (15, 4), (35, 4), (50, 0), (60, 0));
        Bend("bend up 3 st (wide)", (0, 0), (20, 12), (60, 12));
        track.Measures = TabForge.Presets.TemplateFactory.Measures(bars.Count);
        for (var i = 0; i < bars.Count; i++)
            foreach (var cell in new[] { 0, 4 })
            {
                var c = track.Measures[i].Cells[cell];
                c.DurationDenominator = 4;
                var note = new TabNote { StringIndex = 2, Fret = bars[i].Name.Contains("Harmonic") ? 12 : 5, MidiValue = track.StringTunings[2] + (bars[i].Name.Contains("Harmonic") ? 12 : 5) };
                bars[i].Apply(note);
                c.Notes.Add(note);
            }
        song.Title = "technique test: " + string.Join(", ", bars.Select(b => b.Name).Take(3)) + "…";
        TechniqueBars = bars.Select(b => b.Name).ToList();
        return song;
    }

    public static List<string> TechniqueBars { get; private set; } = new();

    public static int RunProject(SongProject project, string songName, string reportPath)
    {
        var songPath = songName;
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { RespectMuteSolo = false });
        var report = new StringBuilder();
        report.AppendLine($"song: {songPath}  tracks={project.Tracks.Count}  notes={timeline.Notes.Count}  events={timeline.Events.Count}");

        // Reference: how far off the synth's own flute sits (its sample tuning), so it is not counted against the score.
        var (calibration, _) = GmSynthProbe.Render(new[]
        {
            new GmSynthProbe.Event(0, 0xC0, 25, 0), new GmSynthProbe.Event(50, 0x90, 60, 100), new GmSynthProbe.Event(800, 0x80, 60, 0),
        }, 1000);
        var baseline = 12 * Math.Log2(GmSynthProbe.Pitch(calibration, 400) / 261.6256);
        report.AppendLine($"flute reference offset {baseline:+0.00;-0.00} semitones (removed from the measurements)");

        var byTrackChannel = timeline.ChannelSetup.Where(e => e.TrackIndex >= 0).GroupBy(e => e.TrackIndex).ToDictionary(g => g.Key, g => g.First().Channel);
        var checkedCount = 0; var wrong = new List<(string Where, string Technique, double Expected, double Measured, int Track)>();
        var measuredBars = new Dictionary<int, int>();
        var byTechnique = new SortedDictionary<string, (int Checked, int Wrong)>();

        foreach (var (trackIndex, channel) in byTrackChannel.OrderBy(p => p.Key))
        {
            if (channel == 9) continue;
            var notes = timeline.Notes.Where(n => n.TrackIndex == trackIndex && n.DurationMs >= 90).OrderBy(n => n.OnsetMs).ToList();
            if (notes.Count == 0) continue;
            var all = timeline.Events.Where(e => e.Channel == channel && e.TrackIndex == trackIndex).OrderBy(e => e.TimeMs).ToList();
            var events = new List<GmSynthProbe.Event> { new(0, 0xC0 | channel, 25, 0) };
            foreach (var e in all)
            {
                if ((e.Status & 0xF0) == 0xC0) continue;            // every track voiced as a flute
                if ((e.Status & 0xF0) == 0xB0 && e.Data1 == 7) continue;   // level: the chain applies it, not the synth
                events.Add(new GmSynthProbe.Event(e.TimeMs, e.Status, e.Data1, e.Data2));
            }
            var length = Math.Min(Math.Max(timeline.TotalMs, Math.Max(notes.Max(x => x.EndMs), all.Count == 0 ? 0 : all.Max(x => x.TimeMs))) + 500, 20 * 60_000);
            var (audio, _) = GmSynthProbe.Render(events, length);

            // The wheel position over time, in semitones (the compiler encodes +/-12 semitones full scale).
            var wheel = all.Where(e => (e.Status & 0xF0) == 0xE0).Select(e => (e.TimeMs, (((e.Data2 << 7) | e.Data1) - 8192) / 8192.0 * 12)).ToList();
            double WheelAt(double ms)
            {
                var lo = 0; var hi = wheel.Count - 1; var best = 0.0;
                while (lo <= hi) { var mid = (lo + hi) / 2; if (wheel[mid].TimeMs <= ms) { best = wheel[mid].Item2; lo = mid + 1; } else hi = mid - 1; }
                return best;
            }

            for (var i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                // A clear single note: nothing else sounds on this channel while it does.
                var overlaps = notes.Any(o => !ReferenceEquals(o, n) && o.OnsetMs < n.EndMs - 5 && o.EndMs > n.OnsetMs + 5);
                if (overlaps || n.Midi < 40 || n.Midi > 100) continue;
                var at = n.OnsetMs + Math.Min(n.DurationMs * 0.5, 160);
                var measured = GmSynthProbe.Pitch(audio, at - 30, 60);
                if (measured <= 0) continue;
                var expected = n.Midi + WheelAt(at);
                var actual = 69 + 12 * Math.Log2(measured / 440.0) - baseline;
                var technique = n.Technique ?? "(plain)";
                byTechnique.TryGetValue(technique, out var stat);
                var isWrong = Math.Abs(actual - expected) > 0.6 && technique != "TRILL";   // a trill alternates with its upper note on purpose
                byTechnique[technique] = (stat.Checked + 1, stat.Wrong + (isWrong ? 1 : 0));
                checkedCount++;
                measuredBars[n.Bar] = measuredBars.GetValueOrDefault(n.Bar) + 1;
                if (isWrong) wrong.Add(($"bar {n.Bar + 1} cell {n.Cell} track {trackIndex + 1} ({project.Tracks[trackIndex].Name})", technique, expected, actual, trackIndex));
            }
        }

        report.AppendLine($"single notes measured: {checkedCount}, wrong (more than 0.6 semitone off the score): {wrong.Count}");
        report.AppendLine("by technique (measured / wrong):");
        foreach (var (technique, (measuredCount, wrongCount)) in byTechnique.OrderByDescending(p => p.Value.Wrong))
            report.AppendLine($"  {technique,-22} {measuredCount,6} {wrongCount,6}");
        report.AppendLine("first wrong notes:");
        foreach (var w in wrong.Take(60)) report.AppendLine($"  {w.Where}  [{w.Technique}]  score {w.Expected:0.00}  heard {w.Measured:0.00}");
        if (TechniqueBars.Count > 0 && songName.StartsWith("technique"))
        {
            report.AppendLine("per bar (technique, heard vs score, semitones; blank = no clear pitch to measure):");
            foreach (var g in timeline.Notes.GroupBy(n => n.Bar).OrderBy(g => g.Key))
                if (g.Key < TechniqueBars.Count)
                {
                    var w = wrong.Where(x => x.Where.StartsWith($"bar {g.Key + 1} ")).ToList();
                    report.AppendLine($"  bar {g.Key + 1,3} {TechniqueBars[g.Key],-22} notes={g.Count()} {(w.Count > 0 ? "WRONG " + string.Join(" ", w.Select(x => $"{x.Measured - x.Expected:+0.00;-0.00}")) : measuredBars.GetValueOrDefault(g.Key) > 0 ? "ok" : "not measured (no clear pitch)")}");
                }
        }
        File.WriteAllText(reportPath, report.ToString());
        Console.Out.WriteLine(report.ToString());
        return wrong.Count == 0 ? 0 : 1;
    }
}
