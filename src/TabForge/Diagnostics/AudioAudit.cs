using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Rendering;
using TabForge.Services;
using static TabForge.Diagnostics.AudioAuditDsp;

namespace TabForge.Diagnostics;

// `--audio-audit <song> <outdir>`: headless tool. Renders the whole song offline through the real engine process with the
// Null (no device) driver and the built-in General MIDI synth, exactly the path File > Render uses, then writes mix.wav, one stem per
// track, audio-report.json and audio-report.md. No window, no sound card.
internal static partial class DiagnosticCommands
{
    private static int RunAudioAudit(string[] args)
    {
        if (args.Length < 3) return Usage("--audio-audit <song> <outdir>");
        return Guard("Audio audit", () =>
        {
            var report = AudioAudit.Run(LoadAny(args[1]), FilePathPolicy.OutputDirectory(args[2], "audit folder"), Console.WriteLine);
            return report.Problems.Count == 0 ? Ok : CheckFailed;
        });
    }
}

// Owns: the headless --audio-audit tool: the offline render through the engine with the Null driver, one stem per track, the audio
//   report (JSON and Markdown) and its problem checks.
// Does not own: the command dispatch (DiagnosticCommands above), the measurements (AudioAuditDsp) and the File > Render path it mirrors.
// Tests: TestAudioAudit, TestAudioAuditRepeatedSections.
internal static class AudioAudit
{
    private sealed record Section(string Name, int FirstBar, int LastBar, double StartMs, double EndMs);
    private sealed record Cluster(double Ms, int Bar, int MaxVelocity);

    private const double ClipLevel = 0.98855;            // -0.1 dBFS
    private const int TailMs = 4000;
    private const double ReleaseAllowanceMs = 1500;

    public static AuditReport Run(SongProject project, string outDir, Action<string>? log = null)
    {
        log ??= _ => { };
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        var tl = RenderSpecBuilder.Compile(project);
        var (startMs, endMs) = RenderSpecBuilder.Bounds(tl, RenderBounds.Song, 0, 0, 0, 0, 0, 0);

        var stemPaths = new Dictionary<TrackModel, string>();
        var infos = new List<AuditTrackInfo>();
        var noteOns = project.Tracks.Select((t, i) => tl.Events.Count(e => e.TrackIndex == i && e.IsNoteOn && !e.IsMetronome)).ToArray();
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var t = project.Tracks[i];
            var file = $"{i + 1:00} {Sanitize(t.Name)}.wav";
            stemPaths[t] = Path.Combine(outDir, file);
            infos.Add(new AuditTrackInfo(i, t.Name, t.Kind.ToString(), t.MidiProgram, IsSlowAttack(t) ? "slow-attack" : "sharp",
                MixerGroups.IsAudible(project, t), file, noteOns[i]));
        }
        var mixPath = Path.Combine(outDir, "mix.wav");
        foreach (var p in stemPaths.Values.Append(mixPath)) try { File.Delete(p); } catch (IOException) { }   // the render never overwrites // Not logged: diagnostic cleanup loop: a locked stem is left behind.

        // ---- 1. Offline render (no device: the Null driver), through File > Render's own job.
        var client = AudioEngineClient.Instance;
        var previousWarm = client.WarmIdle;
        client.WarmIdle = TimeSpan.Zero;
        var settings = new PluginSettings { Driver = TabForge.AudioEngine.Output.AudioOutputFactory.Null, Device = "", SampleRate = 48000 };
        RenderResult? result;
        var engineMessages = new List<string>();
        Action<string> onDeviceError = m => engineMessages.Add(m);
        client.DeviceError += onDeviceError;
        try
        {
            var request = new RenderRequest
            {
                Project = project, Plugins = settings, MasterPercent = 100, Timeline = tl, StartMs = startMs, EndMs = endMs,
                Settings = new RenderSettings { Format = 2, TailMode = 1, TailMs = TailMs, SampleRate = 0, Mono = false,
                    SafetyLimiter = Environment.GetEnvironmentVariable("TABFORGE_AUDIT_NOLIMITER") != "1" },   // =1: the unlimited mix, to see what the limiter catches
                MasterFile = mixPath, Stems = stemPaths,
                ConfirmIncomplete = _ => Task.FromResult(true),
            };
            log("Rendering offline...");
            var task = RenderJob.RunAsync(request, null, CancellationToken.None, client);
            var clock = Stopwatch.StartNew();
            while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromMinutes(15))
            {
                Thread.Sleep(10);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            }
            if (!task.IsCompleted) throw new TimeoutException("The offline render did not finish within 15 minutes.");
            try { result = task.GetAwaiter().GetResult(); }
            catch (RenderException ex)
            {
                for (var i = 0; i < 30; i++) { Thread.Sleep(10); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); }
                throw new InvalidOperationException(ex.Message + $" [running {client.IsRunning}, engine pid {client.EngineProcessId}, rendering {client.Rendering}, exe {Environment.ProcessPath}]" + (engineMessages.Count > 0 ? " Engine said: " + string.Join(" | ", engineMessages) : ""), ex);
            }
        }
        finally
        {
            client.DeviceError -= onDeviceError;
            try { client.Sync(Array.Empty<TrackModel>(), settings); } catch (Exception ex) { Services.Trace.Error(Services.Trace.Engine, "audio audit: stop engine: " + ex.Message); }   // WarmIdle zero: stops the engine
            client.WarmIdle = previousWarm;
        }

        var report = new AuditReport
        {
            Song = string.IsNullOrWhiteSpace(project.Title) ? "(untitled)" : project.Title,
            Generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            RenderElapsedSeconds = result.ElapsedSeconds, Tracks = infos,
        };

        // ---- 2. Analysis.
        log("Analysing...");
        var (mixL, mixR, rate) = ReadWav(mixPath);
        report.SampleRate = rate;
        report.RenderSeconds = mixL.Length / (double)rate;
        report.SongSeconds = endMs / 1000.0;
        var sections = BuildSections(project, tl);
        var moments = new List<ListenMoment>();
        void Moment(double sec, string track, string reason, double severity) =>
            moments.Add(new ListenMoment(Math.Round(sec, 2), AuditReport.Clock(sec), BarOf(tl, sec * 1000), track, reason, severity));

        // Expected onsets per track and the last note-off.
        var clusters = new List<Cluster>[project.Tracks.Count];
        var lastOffMs = 0.0;
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var list = new List<Cluster>();
            foreach (var e in tl.Events.Where(e => e.TrackIndex == i && e.IsNoteOn && !e.IsMetronome).OrderBy(e => e.TimeMs))
            {
                if (list.Count > 0 && e.TimeMs - list[^1].Ms < 30) { list[^1] = list[^1] with { MaxVelocity = Math.Max(list[^1].MaxVelocity, e.Data2) }; continue; }
                list.Add(new Cluster(e.TimeMs, tl.BarAt(e.TimeMs).Bar + 1, e.Data2));
            }
            clusters[i] = list;
        }
        foreach (var e in tl.Events.Where(e => e.TrackIndex >= 0 && !e.IsMetronome && (e.IsNoteOff || e.IsNoteOn))) lastOffMs = Math.Max(lastOffMs, e.TimeMs);
        var lastOff = lastOffMs;

        // Attack mask for the click detector: 3 ms before to 15 ms after any expected onset on any track.
        var allOnsets = clusters.SelectMany(c => c).Select(c => c.Ms).ToList();
        var mixMask = OnsetMask(allOnsets, mixL.Length, rate);

        // Mix: artifacts, section levels.
        report.Artifacts.Add(AnalyseArtifacts("mix.wav", "(mix)", mixL, mixR, rate, mixMask, lastOff, null, tl, report.ArtifactEvents, moments, Moment));
        var mixK = new[] { (float[])mixL.Clone(), (float[])mixR.Clone() };
        KWeight(mixK[0], rate); KWeight(mixK[1], rate);
        foreach (var s in sections)
        {
            long a = (long)(s.StartMs * rate / 1000), b = (long)(s.EndMs * rate / 1000);
            var ms = (MeanSquare(mixK[0], a, b) + MeanSquare(mixK[1], a, b));
            var rmsAll = (MeanSquare(mixL, a, b) + MeanSquare(mixR, a, b)) / 2;
            report.Sections.Add(new SectionLevel(s.Name, s.FirstBar + 1, s.LastBar + 1, s.StartMs / 1000, s.EndMs / 1000,
                ms <= 1e-12 ? -120 : Math.Round(-0.691 + 10 * Math.Log10(ms), 2), Math.Round(Db(Math.Sqrt(rmsAll)), 2),
                Math.Round(Db(Math.Max(PeakAbs(mixL, a, b), PeakAbs(mixR, a, b))), 2)));
        }
        BuildArc(report);
        for (var i = 1; i < report.Sections.Count; i++)
        {
            var jump = report.Sections[i].MixLufsish - report.Sections[i - 1].MixLufsish;
            if (Math.Abs(jump) >= 8 && report.Sections[i - 1].MixLufsish > -60 && report.Sections[i].MixLufsish > -60)
                Moment(report.Sections[i].StartSec, "(mix)", $"level jumps {jump:+0.0;-0.0} LU into '{report.Sections[i].Section}'", Math.Abs(jump) / 2);
        }
        // Index-aligned with 'sections' (two sections can share a name and first bar, e.g. 1-bar auto-sections).
        var mixRmsBySection = report.Sections.Select(s => s.MixRmsDbfs).ToArray();

        // Stems: timing, artifacts, per-section levels, drum humanisation.
        var perBarTiming = new Dictionary<(int bar, string track), (int exp, int miss, int extra, int late, double max)>();
        foreach (var info in infos)
        {
            var track = project.Tracks[info.Index];
            if (!File.Exists(stemPaths[track])) { report.Problems.Add($"stem missing: {info.StemFile}"); continue; }
            var (l, r, _) = ReadWav(stemPaths[track]);
            var mono = Mono(l, r);
            var mask = OnsetMask(clusters[info.Index].Select(c => c.Ms).ToList(), l.Length, rate);
            var own = tl.Events.Where(e => e.TrackIndex == info.Index && !e.IsMetronome && (e.IsNoteOff || e.IsNoteOn)).Select(e => e.TimeMs).DefaultIfEmpty(0).Max();
            report.Artifacts.Add(AnalyseArtifacts(info.StemFile, info.Name, l, r, rate, mask, own, clusters[info.Index], tl, report.ArtifactEvents, moments, Moment));

            // Timing.
            var slow = info.AttackClass == "slow-attack";
            var expected = clusters[info.Index];
            if (expected.Count > 0)
            {
                var detected = DetectOnsets(mono, rate, slow).Select(s => s * 1000).ToList();
                var tol = slow ? 200.0 : 50.0;
                var used = new bool[detected.Count];
                var errors = new List<double>();
                var missingBars = new List<Cluster>();
                foreach (var exp in expected)
                {
                    var bestJ = -1; var bestD = tol + 1;
                    var j0 = detected.BinarySearch(exp.Ms - tol); if (j0 < 0) j0 = ~j0;
                    for (var j = j0; j < detected.Count && detected[j] <= exp.Ms + tol; j++)
                        if (!used[j] && Math.Abs(detected[j] - exp.Ms) < bestD) { bestD = Math.Abs(detected[j] - exp.Ms); bestJ = j; }
                    var key = (exp.Bar, info.Name);
                    perBarTiming.TryGetValue(key, out var bt);
                    bt.exp++;
                    if (bestJ < 0) { bt.miss++; missingBars.Add(exp); }
                    else
                    {
                        used[bestJ] = true; var err = detected[bestJ] - exp.Ms; errors.Add(err);
                        if (Math.Abs(err) > 30) bt.late++;
                        bt.max = Math.Max(bt.max, Math.Abs(err));
                        if (!slow && Math.Abs(err) > 25) Moment(exp.Ms / 1000, info.Name, $"onset {err:+0;-0} ms from the score", Math.Abs(err) / 5);
                    }
                    perBarTiming[key] = bt;
                }
                var extras = 0;
                for (var j = 0; j < detected.Count; j++)
                    if (!used[j] && detected[j] < endMs + 50)
                    {
                        extras++;
                        var bar = BarOf(tl, detected[j]);
                        perBarTiming.TryGetValue((bar, info.Name), out var bt);
                        bt.extra++; perBarTiming[(bar, info.Name)] = bt;
                    }
                var abs = errors.Select(Math.Abs).OrderBy(x => x).ToList();
                var sorted = errors.OrderBy(x => x).ToList();
                var missing = expected.Count - errors.Count;
                report.Timing.Add(new TimingTrackResult(info.Name, info.AttackClass, expected.Count, detected.Count, errors.Count, missing, extras,
                    Math.Round(Percentile(sorted, 0.5), 2), Math.Round(abs.Count == 0 ? 0 : abs.Average(), 2), Math.Round(Percentile(abs, 0.95), 2),
                    Math.Round(abs.Count == 0 ? 0 : abs[^1], 2), abs.Count(x => x <= 10), abs.Count(x => x <= 20), abs.Count(x => x <= 50)));
                if (!slow)
                {
                    foreach (var m in missingBars.GroupBy(c => c.Bar).OrderByDescending(g => g.Count()).Take(2).Where(g => g.Count() >= 2))
                        Moment(m.First().Ms / 1000, info.Name, $"{m.Count()} expected onsets not found in this bar", m.Count());
                    if (missing > expected.Count * 0.1 && expected.Count >= 8) report.Problems.Add($"{info.Name}: {missing} of {expected.Count} expected onsets not found in the stem");
                }
            }

            // Levels per section (this stem against the mix).
            for (var si = 0; si < sections.Count; si++)
            {
                var s = sections[si];
                long a = (long)(s.StartMs * rate / 1000), b = (long)(s.EndMs * rate / 1000);
                var notes = expected.Count(c => c.Ms >= s.StartMs && c.Ms < s.EndMs);
                if (notes == 0) continue;
                var rms = Math.Sqrt((MeanSquare(mono, a, b)));
                var active = ActiveRms(mono, a, b, rate);
                var peak = Db(PeakAbs(mono, a, b));
                var mixRms = mixRmsBySection[si];
                var rel = Db(active) - mixRms;
                var flag = "";
                if (info.InMaster && notes >= 3)
                {
                    if (peak < -70) flag = "silent: notes expected, nothing audible";
                    else if (rel < -30) flag = "inaudible against the mix";
                }
                report.TrackLevels.Add(new TrackSectionLevel(s.Name, info.Name, notes, Math.Round(Db(rms), 2), Math.Round(Db(active), 2), Math.Round(peak, 2), Math.Round(rel, 1), flag));
                if (flag.Length > 0) Moment(s.StartMs / 1000, info.Name, $"{flag} in '{s.Name}'", 6);
            }

            // Drum humanisation.
            if (project.Tracks[info.Index].Kind == TrackKind.Drums)
                report.Humanisation.Add(Humanise(info, tl, sections, mono, rate, Moment));
        }
        foreach (var g in perBarTiming.Where(kv => project.Tracks.Any(t => t.Name == kv.Key.track && !IsSlowAttack(t))))
            report.WorstBars.Add(new TimingBarResult(g.Key.bar, g.Key.track, g.Value.exp, g.Value.miss, g.Value.extra, g.Value.late, Math.Round(g.Value.max, 1)));
        report.WorstBars = report.WorstBars.OrderByDescending(b => b.Missing + b.Extra + b.Late30Ms).ThenByDescending(b => b.MaxAbsErrorMs)
            .Where(b => b.Missing + b.Extra + b.Late30Ms > 0).Take(15).ToList();

        // Consistency.
        report.Consistency = Consistency(project, tl, outDir, result, rate, report.RenderSeconds);
        foreach (var issue in report.Consistency.Issues) { report.Problems.Add("consistency: " + issue); Moment(0, "(song)", "consistency mismatch: " + issue, 9); }

        // Problems from the artifact table.
        foreach (var a in report.Artifacts)
        {
            if (a.ClippedSamples > 0) report.Problems.Add($"{a.File}: {a.ClippedSamples} samples at or above -0.1 dBFS in {a.ClipRegions} region(s), peak {a.PeakDbfs:0.0} dBFS");
            if (a.Clicks > 0) report.Problems.Add($"{a.File}: {a.Clicks} click(s)/discontinuities (worst jump {a.WorstClickJump:0.00})");
            if (Math.Max(Math.Abs(a.DcLeft), Math.Abs(a.DcRight)) > 0.005) report.Problems.Add($"{a.File}: DC offset {a.DcLeft:0.0000} / {a.DcRight:0.0000}");
            if (a.TailRmsDbfs > -60) report.Problems.Add($"{a.File}: still {a.TailRmsDbfs:0.0} dBFS RMS more than {ReleaseAllowanceMs / 1000:0.0} s after the last note-off (ringing or stuck note)");
            if (a.SilentNotes > 0) report.Problems.Add($"{a.File}: {a.SilentNotes} of {a.ExpectedNotes} expected notes are silent");
        }
        report.MomentsToListenTo = moments.OrderByDescending(m => m.Severity).Take(25).OrderBy(m => m.TimeSec).ToList();
        if (report.TrackLevels.Any(t => t.Flag.Length > 0)) report.Problems.Add($"{report.TrackLevels.Count(t => t.Flag.Length > 0)} track/section pair(s) inaudible or silent against the mix");
        if (report.Tracks.Any(t => !t.InMaster)) report.Notes.Add("muted / not-soloed tracks are left out of mix.wav exactly as File > Render does (their stems are still written): " + string.Join(", ", report.Tracks.Where(t => !t.InMaster).Select(t => t.Name)));
        report.Notes.Add("the render-side note counts come from the render's own compile (RenderSpecBuilder.Compile); its event file is deleted by the render job.");

        var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        File.WriteAllText(Path.Combine(outDir, "audio-report.json"), JsonSerializer.Serialize(report, options), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "audio-report.md"), report.ToMarkdown(), new UTF8Encoding(false));
        log($"Wrote {Path.Combine(outDir, "audio-report.md")} ({report.Problems.Count} problem(s) flagged)");
        return report;
    }

    // ---------------------------------------------------------------------------------------------------------------

    private static bool IsSlowAttack(TrackModel t) =>
        t.Kind != TrackKind.Drums && t.MidiProgram is (>= 16 and <= 20) or (>= 40 and <= 55) or (>= 88 and <= 95);

    private static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "Track" : s.Length > 60 ? s[..60] : s;
    }

    private static int BarOf(ScoreTimeline tl, double ms) => tl.Bars.Count == 0 ? 0 : tl.BarAt(ms).Bar + 1;

    private static (float[] L, float[] R, int Rate) ReadWav(string path)
    {
        using var reader = new WaveFileReader(path);
        var channels = reader.WaveFormat.Channels;
        var frames = (int)reader.SampleCount;
        var l = new float[frames]; var r = channels > 1 ? new float[frames] : l;
        var provider = reader.ToSampleProvider();
        var buffer = new float[4096 * channels];
        var pos = 0; int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
            for (var i = 0; i + channels <= n && pos < frames; i += channels) { l[pos] = buffer[i]; if (channels > 1) r[pos] = buffer[i + 1]; pos++; }
        return (l, r, reader.WaveFormat.SampleRate);
    }

    private static float[] Mono(float[] l, float[] r)
    {
        if (ReferenceEquals(l, r)) return l;
        var m = new float[l.Length];
        for (var i = 0; i < m.Length; i++) m[i] = (l[i] + r[i]) * 0.5f;
        return m;
    }

    private static bool[] OnsetMask(List<double> onsetsMs, int length, int rate)
    {
        var mask = new bool[length / (rate / 1000) + 2];   // one flag per millisecond
        foreach (var o in onsetsMs)
            for (var ms = (int)Math.Floor(o) - 3; ms <= (int)Math.Ceiling(o) + 15; ms++)
                if (ms >= 0 && ms < mask.Length) mask[ms] = true;
        return mask;
    }

    /// <summary>RMS over the 50 ms blocks louder than -65 dBFS (the level while the part is actually sounding).</summary>
    private static double ActiveRms(float[] x, long from, long to, int rate)
    {
        var block = rate / 20; double sum = 0; long count = 0;
        for (var a = Math.Max(0, from); a < Math.Min(x.Length, to); a += block)
        {
            var b = Math.Min(Math.Min(x.Length, to), a + block);
            var ms = MeanSquare(x, a, b);
            if (ms > 3.2e-7) { sum += ms * (b - a); count += b - a; }   // -65 dBFS RMS
        }
        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private static ArtifactFileResult AnalyseArtifacts(string file, string track, float[] l, float[] r, int rate, bool[] attackMask, double lastOffMs,
        List<Cluster>? expected, ScoreTimeline tl, List<ArtifactEvent> events, List<ListenMoment> moments, Action<double, string, string, double> moment)
    {
        var stereo = !ReferenceEquals(l, r);
        double peak = 0; long clipped = 0; var regions = 0; var lastClip = -100000;
        var clipFirst = new List<int>();
        for (var ch = 0; ch < (stereo ? 2 : 1); ch++)
        {
            var x = ch == 0 ? l : r; var lastInChannel = -100000;
            for (var i = 0; i < x.Length; i++)
            {
                var a = Math.Abs(x[i]);
                if (a > peak) peak = a;
                if (a >= ClipLevel)
                {
                    clipped++;
                    if (i - lastInChannel > rate / 1000) { regions++; if (clipFirst.Count < 50) clipFirst.Add(i); }
                    lastInChannel = i;
                }
            }
            lastClip = lastInChannel;
        }
        foreach (var i in clipFirst.Take(5))
            events.Add(new ArtifactEvent(file, "clip", Math.Round(i / (double)rate, 3), BarOf(tl, i * 1000.0 / rate), 0, "sample at or above -0.1 dBFS"));
        if (clipFirst.Count > 0) moment(clipFirst[0] / (double)rate, track, $"first clipping in {file} ({clipped} samples)", 8);

        double Mean(float[] x) { double s = 0; foreach (var v in x) s += v; return x.Length == 0 ? 0 : s / x.Length; }
        double dcL = Mean(l), dcR = stereo ? Mean(r) : dcL, maxBlockDc = 0;
        for (var a = 0; a + rate <= l.Length; a += rate)
        {
            double s = 0; for (var i = a; i < a + rate; i++) s += l[i];
            maxBlockDc = Math.Max(maxBlockDc, Math.Abs(s / rate));
        }

        // Clicks: a sample step far above the local step level, away from note attacks.
        var clicks = new List<(int Index, double Jump)>();
        for (var ch = 0; ch < (stereo ? 2 : 1); ch++)
            foreach (var c in FindClicks(ch == 0 ? l : r, rate, attackMask))
                if (!clicks.Any(k => Math.Abs(k.Index - c.Index) < rate / 500)) clicks.Add(c);
        clicks = clicks.OrderBy(c => c.Index).ToList();
        foreach (var c in clicks.OrderByDescending(c => c.Jump).Take(5))
        {
            events.Add(new ArtifactEvent(file, "click", Math.Round(c.Index / (double)rate, 3), BarOf(tl, c.Index * 1000.0 / rate), Math.Round(c.Jump, 3), "sample-to-sample jump"));
        }
        if (clicks.Count > 0) { var worst = clicks.OrderByDescending(c => c.Jump).First(); moment(worst.Index / (double)rate, track, $"click / discontinuity (jump {worst.Jump:0.00}) in {file}", 7); }

        // Tail and decay after the last expected note-off.
        var mono = Mono(l, r);
        long tailStart = (long)((lastOffMs + ReleaseAllowanceMs) * rate / 1000);
        var tailRms = Db(Math.Sqrt(MeanSquare(mono, tailStart, mono.Length)));
        var block = rate / 20; var decay = 0.0;
        var lastLoud = -1;
        for (var b = 0; (b + 1) * block <= mono.Length; b++)
            if (Db(Math.Sqrt(MeanSquare(mono, (long)b * block, (long)(b + 1) * block))) >= -70) lastLoud = b;
        var offSec = lastOffMs / 1000.0;
        if (lastLoud >= 0) decay = Math.Max(0, (lastLoud + 1) * 0.05 - offSec);
        if (tailRms > -60) moment(offSec + ReleaseAllowanceMs / 1000, track, $"still sounding ({tailRms:0} dBFS) after the last note-off in {file}", 6);

        // Silence where notes are expected.
        var silent = 0;
        if (expected is not null)
            foreach (var c in expected)
            {
                long a = (long)((c.Ms + 5) * rate / 1000), b = (long)((c.Ms + 45) * rate / 1000);
                if (b > mono.Length) continue;
                if (PeakAbs(mono, a, b) < 3.2e-4f)   // -70 dBFS
                {
                    silent++;
                    if (silent <= 3) { events.Add(new ArtifactEvent(file, "silent-note", Math.Round(c.Ms / 1000, 3), c.Bar, c.MaxVelocity, "expected note, nothing audible")); moment(c.Ms / 1000, track, "note expected here but the stem is silent", 7); }
                }
            }
        return new ArtifactFileResult(file, Math.Round(Db(peak), 2), clipped, regions, Math.Round(dcL, 5), Math.Round(dcR, 5), Math.Round(maxBlockDc, 5),
            clicks.Count, Math.Round(clicks.Count == 0 ? 0 : clicks.Max(c => c.Jump), 3), Math.Round(tailRms, 2), Math.Round(decay, 2), silent, expected?.Count ?? 0);
    }

    /// <summary>
    /// Steep sample steps far above the local step level, away from note attacks. Steep edges that recur at a steady period (the saw of a
    /// synth-bass program: same sign, similar size, 2-100 ms apart, counting edges inside the attack mask too) belong to the waveform, not a click.
    /// </summary>
    internal static List<(int Index, double Jump)> FindClicks(float[] x, int rate, bool[] attackMask)
    {
        var found = new List<(int, double)>();
        var edges = new List<(int Index, double Step)>();
        int w = Math.Max(8, rate / 330);   // about 3 ms each side
        double D(int j) => j < 1 || j >= x.Length ? 0 : x[j] - x[j - 1];
        double s = 0;
        for (var j = 1; j <= w; j++) s += D(j) * D(j);
        var lastIndex = -100000;
        for (var i = 1; i < x.Length; i++)
        {
            if (i > 1) { var add = D(i + w); var sub = D(i - w - 1); s += add * add - sub * sub; }
            var d = Math.Abs(D(i));
            if (d < 0.03) continue;
            var local = Math.Sqrt(Math.Max(0, s - d * d) / (2 * w));
            if (d < 8 * local) continue;
            edges.Add((i, D(i)));
            var ms = i / (rate / 1000);
            if (ms < attackMask.Length && attackMask[ms]) continue;
            if (i - lastIndex < rate / 500 && found.Count > 0 && found[^1].Item2 >= d) { lastIndex = i; continue; }
            if (i - lastIndex < rate / 500 && found.Count > 0) found.RemoveAt(found.Count - 1);
            found.Add((i, d)); lastIndex = i;
        }
        // Periodic edges are waveform, not clicks. `edges` is in index order, so each candidate only scans its neighbourhood.
        int minGap = rate / 500, maxGap = rate / 10;
        // One edge is several adjacent samples (a band-limited step) and its single largest sample varies with the sub-sample position, so
        // edges are clustered and sized by the whole step; otherwise a saw whose period is sliding (a dive) looks uneven and one edge counts as two neighbours.
        var clusters = new List<(int Index, double Step)>();
        for (var k = 0; k < edges.Count;)
        {
            var m = k; while (m + 1 < edges.Count && edges[m + 1].Index - edges[m].Index <= 6) m++;
            double sum = 0; for (var j = edges[k].Index - 2; j <= edges[m].Index + 2; j++) sum += D(j);
            clusters.Add((edges[k].Index, sum));
            k = m + 1;
        }
        edges = clusters;
        // Two similar neighbours are needed: a waveform has many, while a pair of real clicks (say a note-off click 50 ms after a note-on click) has one.
        bool Recurs(int index, double step)
        {
            var at = edges.BinarySearch((index, 0), Comparer<(int Index, double Step)>.Create((a, b) => a.Index.CompareTo(b.Index)));
            if (at < 0) at = ~at;
            var similar = 0;
            for (var k = at - 1; k >= 0 && index - edges[k].Index <= maxGap; k--) if (Similar(index - edges[k].Index, edges[k].Step) && ++similar >= 2) return true;
            for (var k = at; k < edges.Count && edges[k].Index - index <= maxGap; k++) if (Similar(edges[k].Index - index, edges[k].Step) && ++similar >= 2) return true;
            return false;
            bool Similar(int gap, double other) => gap >= minGap && Math.Sign(other) == Math.Sign(step) && Math.Abs(other) / Math.Abs(step) is > 0.6 and < 1.67;
        }
        found.RemoveAll(c =>
        {
            var own = clusters.FindIndex(e => c.Item1 >= e.Index - 6 && c.Item1 <= e.Index + 12);
            return Recurs(own >= 0 ? clusters[own].Index : c.Item1, own >= 0 ? clusters[own].Step : D(c.Item1));
        });
        return found;
    }

    internal static int SectionCountForTest(SongProject project, out int distinctKeys)
    {
        var s = BuildSections(project, RenderSpecBuilder.Compile(project));
        distinctKeys = s.Select(x => (x.Name, x.FirstBar)).Distinct().Count();
        return s.Count;
    }

    private static List<Section> BuildSections(SongProject project, ScoreTimeline tl)
    {
        var sections = new List<Section>();
        var markers = project.Markers.OrderBy(m => m.MeasureIndex).ToList();
        if (tl.Bars.Count == 0) return sections;
        string NameOf(int bar, out int key)
        {
            var idx = markers.FindLastIndex(m => m.MeasureIndex <= bar);
            key = idx;
            return idx >= 0 ? (string.IsNullOrWhiteSpace(markers[idx].Title) ? $"Section {idx + 1}" : markers[idx].Title) : "(start)";
        }
        var seen = new Dictionary<string, int>();
        var i = 0;
        while (i < tl.Bars.Count)
        {
            string name; int key;
            int j = i;
            if (markers.Count == 0)
            {
                j = Math.Min(tl.Bars.Count - 1, i + 7);
                name = $"Bars {tl.Bars[i].Bar + 1}-{tl.Bars[j].Bar + 1}";
            }
            else
            {
                name = NameOf(tl.Bars[i].Bar, out key);
                while (j + 1 < tl.Bars.Count && tl.Bars[j + 1].Bar == tl.Bars[j].Bar + 1 && NameOf(tl.Bars[j + 1].Bar, out var k2) == name && k2 == key) j++;
                seen[name] = seen.GetValueOrDefault(name) + 1;
                if (seen[name] > 1) name += $" (pass {seen[name]})";
            }
            sections.Add(new Section(name, tl.Bars[i].Bar, tl.Bars[j].Bar, tl.Bars[i].StartMs, tl.Bars[j].EndMs));
            i = j + 1;
        }
        return sections;
    }

    private static void BuildArc(AuditReport report)
    {
        var valid = report.Sections.Where(s => s.MixLufsish > -60).ToList();
        if (valid.Count == 0) { report.DynamicsArc = "(silent)"; return; }
        double min = valid.Min(s => s.MixLufsish), max = valid.Max(s => s.MixLufsish);
        report.DynamicsRangeLu = Math.Round(max - min, 1);
        var sb = new StringBuilder();
        foreach (var s in report.Sections)
        {
            var bars = s.MixLufsish <= -60 ? 0 : 1 + (int)Math.Round((s.MixLufsish - min) / Math.Max(1, max - min) * 30);
            sb.AppendLine($"{s.Section,-28} {s.MixLufsish,6:0.0} LU {new string('#', bars)}");
        }
        report.DynamicsArc = sb.ToString().TrimEnd();
        if (report.DynamicsRangeLu < 3 && valid.Count >= 4) report.Problems.Add($"flat dynamics: the loudest and quietest sections differ by only {report.DynamicsRangeLu:0.0} LU");
    }

    private static HumanisationResult Humanise(AuditTrackInfo info, ScoreTimeline tl, List<Section> sections, float[] stem, int rate, Action<double, string, string, double> moment)
    {
        var hits = tl.Events.Where(e => e.TrackIndex == info.Index && e.IsNoteOn && !e.IsMetronome).OrderBy(e => e.TimeMs).ToList();
        var flags = new List<string>();
        // Peak of the stem right after each hit; isolated = no other hit within 40 ms.
        double PeakDb(double ms) => Db(PeakAbs(stem, (long)((ms - 2) * rate / 1000), (long)((ms + 30) * rate / 1000)));
        bool Isolated(int i) => (i == 0 || hits[i].TimeMs - hits[i - 1].TimeMs > 40) && (i == hits.Count - 1 || hits[i + 1].TimeMs - hits[i].TimeMs > 40);

        var secStats = new List<DrumSectionStats>();
        foreach (var s in sections)
        {
            var idx = Enumerable.Range(0, hits.Count).Where(i => hits[i].TimeMs >= s.StartMs && hits[i].TimeMs < s.EndMs).ToList();
            if (idx.Count == 0) continue;
            var vel = idx.Select(i => (double)hits[i].Data2).ToList();
            var withinSd = new List<double>(); var isolated = 0;
            foreach (var g in idx.Where(Isolated).GroupBy(i => hits[i].Data1))
            {
                var peaks = g.Select(i => PeakDb(hits[i].TimeMs)).Where(p => p > -90).ToList();
                isolated += peaks.Count;
                if (peaks.Count >= 4) withinSd.Add(StdDev(peaks));
            }
            secStats.Add(new DrumSectionStats(s.Name, idx.Count, (int)vel.Min(), (int)vel.Max(), Math.Round(vel.Average(), 1), Math.Round(StdDev(vel), 2),
                vel.Distinct().Count(), Math.Round(withinSd.Count == 0 ? 0 : withinSd.Average(), 2), isolated));
            if (idx.Count >= 24 && StdDev(vel) < 2) flags.Add($"'{s.Name}': {idx.Count} drum hits with a velocity spread of only {StdDev(vel):0.0} (machine-like)");
        }
        var pieces = new List<DrumPieceStats>();
        foreach (var g in hits.GroupBy(e => e.Data1).OrderByDescending(g => g.Count()))
        {
            var v = g.Select(e => e.Data2).ToList();
            int run = 1, longest = 1;
            for (var i = 1; i < v.Count; i++) { run = v[i] == v[i - 1] ? run + 1 : 1; longest = Math.Max(longest, run); }
            pieces.Add(new DrumPieceStats(g.Key, v.Count, Math.Round(StdDev(v.Select(x => (double)x).ToList()), 2), v.Distinct().Count(), longest));
            if (v.Count >= 16 && longest >= 16) flags.Add($"GM note {g.Key}: {longest} consecutive hits at the same velocity");
        }
        // Does the audible peak follow the written velocity? Pooled correlation within each piece over isolated hits.
        double sxy = 0, sxx = 0, syy = 0; var samples = 0;
        foreach (var g in Enumerable.Range(0, hits.Count).Where(Isolated).GroupBy(i => hits[i].Data1))
        {
            var list = g.ToList(); if (list.Count < 6) continue;
            var x = list.Select(i => Db(hits[i].Data2 / 127.0)).ToList(); var y = list.Select(i => PeakDb(hits[i].TimeMs)).ToList();
            double mx = x.Average(), my = y.Average();
            for (var i = 0; i < x.Count; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); syy += (y[i] - my) * (y[i] - my); }
            samples += list.Count;
        }
        var corr = sxx > 1e-9 && syy > 1e-9 ? sxy / Math.Sqrt(sxx * syy) : 0;
        if (samples >= 30 && sxx > 1e-9 && corr < 0.3) flags.Add($"the drum stem's hit peaks follow the written velocities weakly (correlation {corr:0.00})");
        return new HumanisationResult(info.Name, secStats, pieces.Take(12).ToList(), Math.Round(corr, 3), samples, flags);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Consistency: playback compile vs the exported MIDI file vs the offline render's tempo map.

    private static ConsistencyResult Consistency(SongProject project, ScoreTimeline renderTl, string outDir, RenderResult render, int rate, double renderFileSeconds)
    {
        var issues = new List<string>();
        var playback = MidiTimelineBuilder.Build(project, new PlaybackOptions { RespectMuteSolo = false, Metronome = false, CountIn = false });
        var midiPath = Path.Combine(outDir, "export.mid");
        MidiExportService.Export(project, midiPath);
        var midi = ReadMidi(midiPath);

        string N(int i) => $"{i + 1:00} {project.Tracks[i].Name}";
        var notesPlayback = new Dictionary<string, int>(); var notesMidi = new Dictionary<string, int>(); var notesRender = new Dictionary<string, int>();
        double maxNoteErr = 0; var over5 = 0;
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var pb = playback.Events.Where(e => e.TrackIndex == i && e.IsNoteOn).OrderBy(e => e.TimeMs).ToList();
            var rd = renderTl.Events.Count(e => e.TrackIndex == i && e.IsNoteOn && !e.IsMetronome);
            var fileNotes = i + 1 < midi.Tracks.Count ? midi.Tracks[i + 1].Where(n => n.On).ToList() : new List<(long Tick, int Note, int Vel, bool On)>();
            notesPlayback[N(i)] = pb.Count; notesMidi[N(i)] = fileNotes.Count; notesRender[N(i)] = rd;
            if (pb.Count != fileNotes.Count) issues.Add($"{N(i)}: {pb.Count} note-ons in playback, {fileNotes.Count} in the MIDI file");
            if (pb.Count != rd) issues.Add($"{N(i)}: {pb.Count} note-ons in playback, {rd} in the render compile");
            if (pb.Count == fileNotes.Count)
            {
                var times = fileNotes.OrderBy(n => n.Tick).Select(n => midi.TickToMs(n.Tick)).ToList();
                for (var k = 0; k < times.Count; k++)
                {
                    var err = Math.Abs(times[k] - pb[k].TimeMs);
                    maxNoteErr = Math.Max(maxNoteErr, err); if (err > 5) over5++;
                }
            }
        }
        // The file stores whole-BPM tempos at 480 ticks per quarter: a few ms of rounding is expected, more is a real difference.
        if (maxNoteErr > 25) issues.Add($"note-on times differ by up to {maxNoteErr:0.0} ms between the MIDI file and playback ({over5} over 5 ms)");

        // Total duration.
        double playbackTotal = Math.Max(playback.TotalMs, playback.Bars.Count > 0 ? playback.Bars[^1].EndMs : 0);
        var midiTotal = midi.TickToMs(midi.LastTick);
        var lastEventMs = playback.Events.Where(e => e.TrackIndex >= 0 && !e.IsMetronome && (e.IsNoteOn || e.IsNoteOff)).Select(e => e.TimeMs).DefaultIfEmpty(0).Max();
        var midiLastEvent = midi.TickToMs(midi.Tracks.SelectMany(t => t).Select(n => n.Tick).DefaultIfEmpty(0).Max());
        if (Math.Abs(midiLastEvent - lastEventMs) > 5) issues.Add($"last event: {lastEventMs:0.0} ms in playback, {midiLastEvent:0.0} ms in the MIDI file");
        var renderTl2 = renderTl.Bars.Count > 0 ? renderTl.Bars[^1].EndMs : 0;
        if (Math.Abs(playbackTotal - renderTl2) > 1) issues.Add($"total duration: {playbackTotal:0.0} ms in playback, {renderTl2:0.0} ms in the render compile");
        var expectedFrames = RenderSpecBuilder.ToFrames(Math.Max(renderTl.TotalMs, renderTl2), rate) + RenderSpecBuilder.ToFrames(TailMs, rate);
        if (Math.Abs(render.Frames - expectedFrames) > 1) issues.Add($"rendered file has {render.Frames} frames, expected {expectedFrames} (song + {TailMs} ms tail)");

        // Bar starts: MIDI file (its own time signatures and tempo map) and the render's tempo map (frames per quarter-note position).
        var tempoMap = RenderSpecBuilder.TempoMap(renderTl, rate, project.Tempo, project);
        double FrameOfPpq(double ppq)
        {
            var p = tempoMap.LastOrDefault(t => t.Ppq <= ppq + 1e-9);
            if (p.Tempo <= 0) p = tempoMap[0];
            return p.Frame + (ppq - p.Ppq) * 60.0 / p.Tempo * rate;
        }
        double maxMidi = 0, maxMap = 0; double ppqPos = 0; long tick = 0;
        for (var i = 0; i < playback.Bars.Count; i++)
        {
            var bar = playback.Bars[i];

            maxMidi = Math.Max(maxMidi, Math.Abs(midi.TickToMs(tick) - bar.StartMs));
            maxMap = Math.Max(maxMap, Math.Abs(FrameOfPpq(ppqPos) * 1000.0 / rate - bar.StartMs));
            tick += (long)Math.Round(bar.Slots * (double)midi.Ppq / MusicTime.SlotsPerQuarter);
            ppqPos += bar.Slots / (double)MusicTime.SlotsPerQuarter;
        }
        if (maxMidi > 2) issues.Add($"bar start times differ by up to {maxMidi:0.0} ms between the MIDI file and playback");
        if (maxMap > 2) issues.Add($"bar start times differ by up to {maxMap:0.0} ms between the render tempo map and playback");
        return new ConsistencyResult(Math.Round(playbackTotal, 1), Math.Round(midiTotal, 1), Math.Round(renderTl2, 1), Math.Round(renderFileSeconds, 3), notesPlayback, notesMidi, notesRender,
            playback.Bars.Count, Math.Round(maxMidi, 2), Math.Round(maxMap, 2), Math.Round(maxNoteErr, 2), over5, issues);
    }

    internal sealed class MidiInfo
    {
        public int Ppq;
        public long LastTick;
        public List<(long Tick, int Mpq)> Tempos = new();
        public List<(long Tick, int Num, int Den)> TimeSigs = new();
        public List<List<(long Tick, int Note, int Vel, bool On)>> Tracks = new();
        private long[]? _tempoTicks; private double[]? _tempoMs; private readonly List<int> _mpq = new();
        public double TickToMs(long tick)
        {
            if (_tempoTicks is null)
            {
                var sorted = Tempos.OrderBy(t => t.Tick).ToList();
                _tempoTicks = new long[sorted.Count]; _tempoMs = new double[sorted.Count];
                double ms = 0; long at = 0; var mpq = 500000;
                for (var i = 0; i < sorted.Count; i++)
                {
                    ms += (sorted[i].Tick - at) * (double)mpq / Ppq / 1000.0; at = sorted[i].Tick; mpq = sorted[i].Mpq;
                    _tempoTicks[i] = at; _tempoMs[i] = ms; _mpq.Add(mpq);
                }
            }
            var idx = Array.BinarySearch(_tempoTicks, tick);
            if (idx < 0) idx = ~idx - 1;
            if (idx < 0) return tick * 500000.0 / Ppq / 1000.0;
            return _tempoMs![idx] + (tick - _tempoTicks[idx]) * (double)_mpq[idx] / Ppq / 1000.0;
        }
    }

    internal static MidiInfo ReadMidi(string path)
    {
        var data = File.ReadAllBytes(path);
        var info = new MidiInfo();
        int pos = 0;
        int Be16() { var v = (data[pos] << 8) | data[pos + 1]; pos += 2; return v; }
        int Be32() { var v = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]; pos += 4; return v; }
        pos = 8; Be16(); var count = Be16(); info.Ppq = Be16();
        for (var t = 0; t < count; t++)
        {
            pos += 4; var length = Be32(); var end = pos + length;
            var notes = new List<(long, int, int, bool)>();
            long tick = 0; var running = 0;
            while (pos < end)
            {
                long delta = 0; int b;
                do { b = data[pos++]; delta = (delta << 7) | (uint)(b & 0x7F); } while ((b & 0x80) != 0);
                tick += delta;
                int status = data[pos];
                if (status < 0x80) status = running; else { pos++; if (status < 0xF0) running = status; }
                if (status == 0xFF)
                {
                    var type = data[pos++]; var len = 0; int c;
                    do { c = data[pos++]; len = (len << 7) | (c & 0x7F); } while ((c & 0x80) != 0);
                    if (type == 0x51 && len == 3) info.Tempos.Add((tick, (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2]));
                    else if (type == 0x58 && len >= 2) info.TimeSigs.Add((tick, data[pos], 1 << data[pos + 1]));
                    pos += len;
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    var len = 0; int c;
                    do { c = data[pos++]; len = (len << 7) | (c & 0x7F); } while ((c & 0x80) != 0);
                    pos += len;
                }
                else
                {
                    var kind = status & 0xF0;
                    var d1 = data[pos++]; var d2 = kind is 0xC0 or 0xD0 ? 0 : data[pos++];
                    if (kind == 0x90 && d2 > 0) notes.Add((tick, d1, d2, true));
                    else if (kind == 0x80 || kind == 0x90) notes.Add((tick, d1, d2, false));
                }
                info.LastTick = Math.Max(info.LastTick, tick);
            }
            info.Tracks.Add(notes);
            pos = end;
        }
        return info;
    }
}
