using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Diagnostics;

/// <summary>
/// Headless command-line modes (no window). Every mode writes its report through the same bounded,
/// path-checked output as the rest of the app and returns a process exit code:
/// 0 = success, 1 = the check ran and found a problem, 2 = the command could not run.
/// </summary>
internal static partial class DiagnosticCommands
{
    private const int Ok = 0;
    private const int CheckFailed = 1;
    private const int CouldNotRun = 2;

    private static readonly Dictionary<string, Func<string[], int>> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--selftest"] = args => SelfTest.Run(args.Length > 1 ? args[1] : FilePathPolicy.DefaultDiagnosticsPath("selftest.log")),
        ["--audit"] = args => RunAudit(args, playtest: false),
        ["--playtest"] = args => RunAudit(args, playtest: true),
        ["--render"] = RunRender,
        ["--render-identity"] = args => Guard("Render identity", () => ScoreRenderIdentity.Run(args, LoadAny, path => FilePathPolicy.OutputDirectory(path, "render identity folder"))),
        ["--render-identity-compare"] = args => Guard("Render identity compare", () => ScoreRenderIdentity.Compare(args)),
        ["--render-fretboard"] = RunRenderFretboard,
        ["--tutorial-shot"] = RunTutorialShot,
        ["--tutorial-pdf"] = RunTutorialPdf,
        ["--layout-audit"] = RunLayoutAudit,
        ["--render-bars"] = RunRenderBars,
        ["--render-timeline"] = RunRenderTimeline,
        ["--render-gp-export-dialog"] = RunRenderGpExportDialog,
        ["--midi-export"] = RunMidiExport,
        ["--gendiag"] = RunGenerateDiagnosticSongs,
        ["--gendemo"] = RunGenerateDemo,
        ["--dump"] = RunDump,
        ["--plausibility"] = RunPlausibility,
        ["--exportgp"] = RunExportGp,
        ["--musicxml-export"] = args => args.Length < 3 ? Usage("--musicxml-export <song> <out.musicxml|out.xml>") : Guard("MusicXML export", () =>
        {
            var song = LoadAny(args[1]);
            var outPath = FilePathPolicy.OutputFile(args[2], "MusicXML file", ".musicxml", ".xml");
            MusicXmlExportService.Export(song, outPath);
            Console.WriteLine($"Wrote {outPath}");
            return Ok;
        }),
        ["--probe-midi-latency"] = args => Guard("Windows MIDI latency probe", () =>
        {
            var (ms, detail) = Audio.WindowsMidiLatency.Measure(new SharedMidiOutput());
            var text = ms is { } v ? $"Windows MIDI latency {v:0.0} ms ({detail})" : $"not measured: {detail}";
            Console.WriteLine(text);
            if (args.Length > 1) File.WriteAllText(FilePathPolicy.OutputFile(args[1], "diagnostic report"), text);
            return ms is null ? CheckFailed : Ok;
        }),
        ["--memreport"] = RunMemoryReport,
        ["--audit-drums"] = RunDrumAudit,
        ["--audit-timing"] = RunTimingAudit,
        ["--probe-audio"] = RunAudioProbe,
        ["--render-probe"] = RunRenderProbe,
        ["--audio-audit"] = RunAudioAudit,
        ["--level-match"] = LevelMatch.Run,
        ["--pitch-audit"] = PitchAudit.Run,
        ["--midi-timing"] = args => args.Length < 3 ? Usage("--midi-timing <song> <out.mid>") : Guard("MIDI timing", () =>
        {
            var result = MidiTimingAudit.Measure(LoadAny(args[1]), FilePathPolicy.OutputFile(args[2], "MIDI export", ".mid", ".midi"));
            Console.WriteLine(result);
            return result.MaxBarStartErrMs <= 1 && Math.Abs(result.EndErrMs) <= 1 ? Ok : CheckFailed;
        }),
#if FULL_SUITE   // these commands live in tests/full-suite
        ["--write-gp-fixture"] = args =>
        {
            if (args.Length < 2) return Usage("--write-gp-fixture <out.gp> [basic|showcase]");
            // The synthetic, redistributable test songs the self-test uses, as a clean Guitar Pro file to open by hand.
            var basic = args.Length > 2 && args[2].Equals("basic", StringComparison.OrdinalIgnoreCase);
            Services.GuitarProExporter.Save(basic ? SelfTest.BuildSyntheticGpSong() : SelfTest.BuildShowcaseSong(), args[1], embedProject: false);
            Console.WriteLine($"Wrote {args[1]}");
            return Ok;
        },
#endif
        ["--write-demo-song"] = args => args.Length < 2 ? Usage("--write-demo-song <out.gp>") : Guard("Demo song", () =>
        {
            // The built-in full demo "Ashen Meridian" (docs/DEMO_SONG_PLAN.md) with the whole project embedded, then verified.
            var song = FullDemoSongFactory.Create();
            var outPath = FilePathPolicy.OutputFile(args[1], "Guitar Pro file", ".gp");
            GuitarProExporter.Save(song, outPath, embedProject: true);
            var back = GuitarProExporter.TryReadEmbedded(outPath);
            var same = back is not null && ProjectService.ContentHash(back).AsSpan().SequenceEqual(ProjectService.ContentHash(song));
            Console.WriteLine(same ? $"Wrote {outPath}" : $"Wrote {outPath}, but the embedded project does not read back identically");
            return same ? Ok : CheckFailed;
        }),
        ["--write-tutorial-starters"] = args => args.Length < 2 ? Usage("--write-tutorial-starters <dir>") : Guard("Tutorial starter songs", () => TutorialStarterSongs.Write(FilePathPolicy.OutputDirectory(args[1], "starter song folder")) ? Ok : CheckFailed),
        ["--audit-gm-techniques"] = args => args.Length < 2 ? Usage("--audit-gm-techniques <report>") : GmSongAudit.RunProject(GmSongAudit.TechniqueSong(), "technique test song", args[1]),
#if FULL_SUITE
        ["--roundtrip-diff"] = args => args.Length < 3 ? Usage("--roundtrip-diff <song|@list.txt> <out.txt> [gp,tforge,midi]") : Guard("Round-trip diff", () => SelfTest.RunRoundTripDiff(args[1], args[2], args.Length > 3 ? args[3] : "")),
        ["--write-gp-fixtures"] = args => args.Length < 2 ? Usage("--write-gp-fixtures <dir>") : Guard("GP fixtures", () => SelfTest.RunWriteGpFixtures(FilePathPolicy.OutputDirectory(args[1], "fixture folder"))),
        ["--gp-capability"] = args => args.Length < 2 ? Usage("--gp-capability <out.md> [gp-folder]") : Guard("GP capability record", () => SelfTest.RunGpCapability(FilePathPolicy.OutputFile(args[1], "capability record", ".md"), args.Length > 2 ? args[2] : null)),
        ["--gp-compare"] = args => args.Length < 3 ? Usage("--gp-compare <a.gp> <b.gp> [report.txt]") : Guard("GP compare", () => SelfTest.RunGpCompare(args[1], args[2], args.Length > 3 ? FilePathPolicy.OutputFile(args[3], "comparison report", ".txt") : null)),
        ["--write-gp-probes"] = args => args.Length < 2 ? Usage("--write-gp-probes <dir>") : Guard("GP probes", () => SelfTest.RunWriteGpProbes(FilePathPolicy.OutputDirectory(args[1], "probe folder"))),
        ["--gp-open"] = args => args.Length < 2 ? Usage("--gp-open <file.gp>") : Guard("GP open", () => SelfTest.RunGpOpen(FilePathPolicy.ExistingFile(args[1], "Guitar Pro file", ".gp"))),
        ["--gp-compat-doc"] = args => args.Length < 2 ? Usage("--gp-compat-doc <out.md>") : Guard("Compatibility page", () => SelfTest.RunGpCompatDoc(FilePathPolicy.OutputFile(args[1], "compatibility page", ".md"))),
        ["--gp-loss-coverage"] = args => args.Length < 2 ? Usage("--gp-loss-coverage <report> [group]") : SelfTest.RunGpLossCoverage(args[1], args.Length > 2 && args[2] == "group"),
        ["--roundtrip-semantics"] = args => args.Length < 2 ? Usage("--roundtrip-semantics <report>") : SelfTest.RunRoundTripSemantics(args[1]),
#endif
        ["--audit-gm"] = args => args.Length < 3 ? Usage("--audit-gm <song> <report>") : GmSongAudit.Run(args[1], args[2]),
        ["--import-measure"] = args => args.Length < 3 ? Usage("--import-measure <song> <report.txt> [worker|inproc]") : Guard("Import measure", () => ImportMeasure.Run(args)),
        ["--feature-map"] = args => Guard("Feature map", () => FeatureMapGenerator.Run(args)),
        ["--find"] = args => Guard("Find", () => FeatureFinder.Run(args)),
    };

    /// <summary>The names of the diagnostic modes (docs/DEBUGGING.md lists them; a self-test keeps the two in step).</summary>
    internal static IReadOnlyCollection<string> CommandNames => Commands.Keys;

    /// <summary>Runs a diagnostic mode when the first argument names one.</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = Ok;
        if (args.Length == 0 || !Commands.TryGetValue(args[0], out var command)) return false;
        Views.TabEditorControl.RethrowRenderFailures = true;   // a drawing error fails a command-line run instead of being contained
        exitCode = command(args);
        return true;
    }

    /// <summary>`--audit &lt;song&gt; [out]` and `--playtest &lt;song&gt; [seconds] [out]` (silent output; 1 = late/early notes).</summary>
    private static int RunAudit(string[] args, bool playtest)
    {
        if (args.Length < 2) return Usage(playtest ? "--playtest <song> [seconds] [out]" : "--audit <song> [out]");
        var outPath = playtest
            ? (args.Length > 3 ? args[3] : FilePathPolicy.DefaultDiagnosticsPath("playtest.log"))
            : (args.Length > 2 ? args[2] : FilePathPolicy.DefaultDiagnosticsPath("audit.log"));
        var seconds = playtest && args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            ? Math.Clamp(s, 1, 600) : 8.0;
        return Guard(playtest ? "Playback test" : "Playback audit", () =>
        {
            outPath = FilePathPolicy.OutputFile(outPath, "diagnostic report");
            var project = LoadAny(args[1]);
            var options = new PlaybackOptions { RepeatExpansion = true };
            var text = PlaybackDiagnostics.Audit(project, options, eventPreview: playtest ? 0 : 40);
            if (!playtest)
            {
                DiagnosticFileService.WriteText(outPath, text);
                return Ok;
            }
            var result = PlaybackDiagnostics.CaptureDispatch(project, options, seconds);
            DiagnosticFileService.WriteText(outPath, text + Environment.NewLine + "== scheduler (live dispatch) ==" + Environment.NewLine + result);
            var onTime = result.NoteOnCount > 0 && result.EarlyBeyond2Ms == 0 && result.MaxLatencyMs < 30;
            return onTime ? Ok : CheckFailed;
        });
    }

    /// <summary>`--render &lt;song&gt; &lt;out.png&gt; [pageWidth] [maxHeight]`: the first track's score, off-screen.</summary>
    private static int RunRender(string[] args)
    {
        if (args.Length < 3) return Usage("--render <song> <out.png> [pageWidth] [maxHeight] [track]");
        return Guard("Render", () =>
        {
            var outPath = FilePathPolicy.OutputFile(args[2], "score render", ".png");
            var project = LoadAny(args[1]);
            var trackIndex = args.Length > 5 && int.TryParse(args[5], out var ti) ? Math.Clamp(ti, 0, Math.Max(0, project.Tracks.Count - 1)) : 0;
            var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = trackIndex };
            // Look checks: TF_RENDER_LIGHT=1 draws the light paper; TF_RENDER_STAFFOPACITY=0..1 scales the staff-line (and ledger-line) opacity.
            if (Environment.GetEnvironmentVariable("TF_RENDER_LIGHT") == "1") editor.DarkPaper = false;
            if (double.TryParse(Environment.GetEnvironmentVariable("TF_RENDER_STAFFOPACITY"), NumberStyles.Float, CultureInfo.InvariantCulture, out var staffOpacity))
            {
                static System.Windows.Media.Color Fade(System.Windows.Media.Color c, double o) => System.Windows.Media.Color.FromArgb((byte)Math.Round(255 * Math.Clamp(o, 0, 1)), c.R, c.G, c.B);
                editor.Appearance.DarkStaffLineColor = Fade(editor.Appearance.DarkStaffLineColor, staffOpacity);
                editor.Appearance.LightStaffLineColor = Fade(editor.Appearance.LightStaffLineColor, staffOpacity);
            }
            if (args.Length > 3 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var pageWidth) && pageWidth > 200)
                editor.PageWidthOverride = pageWidth;
            var maxHeight = args.Length > 4 && int.TryParse(args[4], out var mh) ? Math.Clamp(mh, 200, 16000) : 3000;
            editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = Math.Max(1, (int)Math.Ceiling(editor.DesiredSize.Width));
            var height = Math.Clamp((int)Math.Ceiling(editor.DesiredSize.Height), 1, maxHeight);
            editor.Arrange(new Rect(0, 0, editor.DesiredSize.Width, editor.DesiredSize.Height));
            editor.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(editor);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            FilePathPolicy.WriteAtomically(outPath, encoder.Save);
            return Ok;
        });
    }

    /// <summary>`--midi-export &lt;song&gt; &lt;out.mid&gt;` (used by the audio-verification tooling).</summary>
    private static int RunMidiExport(string[] args)
    {
        if (args.Length < 3) return Usage("--midi-export <song> <out.mid>");
        return Guard("MIDI export", () =>
        {
            MidiExportService.Export(LoadAny(args[1]), args[2]);
            return Ok;
        });
    }

    /// <summary>`--gendiag [dir]`: the purpose-built diagnostic songs plus their expected note-on times.</summary>
    private static int RunGenerateDiagnosticSongs(string[] args) => Guard("Diagnostic-song generation", () =>
    {
        var dir = args.Length > 1 ? args[1] : Path.Combine(
            Path.GetDirectoryName(FilePathPolicy.DefaultDiagnosticsPath("placeholder"))!, "diag-songs");
        dir = FilePathPolicy.OutputDirectory(dir, "diagnostics folder");
        Directory.CreateDirectory(dir);
        foreach (var (name, song) in DiagnosticSongFactory.All())
        {
            ProjectService.Save(Path.Combine(dir, name + ".tforge"), song);
            var noteOns = MidiTimelineBuilder.Build(song, new PlaybackOptions()).Events
                .Where(ev => ev.IsNoteOn)
                .Select(ev => $"{ev.TimeMs.ToString("0.###", CultureInfo.InvariantCulture)},{ev.Data1},{ev.Data2},{ev.Channel}");
            var csvPath = FilePathPolicy.OutputFile(Path.Combine(dir, name + ".notes.csv"), "diagnostics CSV", ".csv");
            DiagnosticFileService.WriteText(csvPath, string.Join(Environment.NewLine, noteOns) + Environment.NewLine);
        }
        return Ok;
    });

    /// <summary>`--gendemo [out.tforge]`: the built-in demo song.</summary>
    private static int RunGenerateDemo(string[] args) => Guard("Demo generation", () =>
    {
        ProjectService.Save(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "tabforge-demo.tforge"), DemoSongFactory.Create());
        return Ok;
    });

    private static string DescribeNote(object n) => string.Join(" ", n.GetType().GetProperties()
        .Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum))
        .Select(p => { try { var v = p.GetValue(n); return v is null || (v is bool bv && !bv) || (v is double dv && dv == 0) ? null : $"{p.Name}={v}"; } catch { return null; } })
        .Where(x => x is not null));

    private static string ResolveViaMapper(AlphaTab.Model.Note n)
    {
        if (Environment.GetEnvironmentVariable("TF_DRUM_DETAIL") == "2") return DescribeNote(n) + " | staffTuning=" + string.Join(",", n.Beat.Voice.Bar.Staff.Tuning) + " track=" + DescribeNote(n.Beat.Voice.Bar.Staff);
        var m = typeof(AlphaTab.Model.InstrumentArticulation).Assembly.GetType("AlphaTab.Model.PercussionMapper")
            ?.GetMethod("GetArticulation", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        try { return m?.Invoke(null, new object[] { n }) is AlphaTab.Model.InstrumentArticulation a ? $"id{a.Id}/out{a.OutputMidiNumber}/{a.ElementType}" : "null"; }
        catch (Exception ex) { return ex.GetBaseException().GetType().Name; }
    }

    /// <summary>
    /// `--audit-timing &lt;folder&gt; &lt;out.txt&gt;`: for each Guitar Pro file, the bars TabForge plays (order, length,
    /// tempo, time signature) against alphaTab's playback tick lookup, plus mix-table point counts per type.
    /// </summary>
    private static int RunTimingAudit(string[] args)
    {
        if (args.Length < 3) return Usage("--audit-timing <folder> <out.txt>");
        return Guard("Timing audit", () =>
        {
            var text = new StringBuilder();
            var files = Directory.EnumerateFiles(args[1])
                .Where(f => Services.FileTypes.IsGuitarPro(Path.GetExtension(f))).OrderBy(f => f);
            foreach (var file in files)
            {
                try
                {
                    var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(GuitarProImporter.WithoutLeadingJunk(File.ReadAllBytes(file)), new AlphaTab.Settings());
                    var midi = new AlphaTab.Midi.MidiFile();
                    var generator = new AlphaTab.Midi.MidiFileGenerator(score, new AlphaTab.Settings(), new AlphaTab.Midi.AlphaSynthMidiFileHandler(midi, false));
                    generator.Generate();
                    var theirs = new List<(int bar, double ms, double tempo, bool midBarTempo)>();
                    foreach (var lookup in generator.TickLookup.MasterBars)
                    {
                        // Piecewise tempo inside the bar (mid-bar tempo automations).
                        var changes = lookup.TempoChanges.Cast<object>()
                            .Select(c => (tick: Convert.ToDouble(c.GetType().GetProperty("Tick")!.GetValue(c)), tempo: Convert.ToDouble(c.GetType().GetProperty("Tempo")!.GetValue(c))))
                            .OrderBy(c => c.tick).ToList();
                        double ms = 0, tick = lookup.Start, tempo = lookup.Tempo;
                        foreach (var (at, value) in changes)
                        {
                            if (at > tick && at < lookup.End) { ms += (at - tick) / 960.0 * 60000.0 / tempo; tick = at; }
                            if (at < lookup.End) tempo = value;
                        }
                        ms += (lookup.End - tick) / 960.0 * 60000.0 / tempo;
                        theirs.Add(((int)lookup.MasterBar.Index, ms, lookup.Tempo, changes.Any(c => c.tick > lookup.Start + 1 && c.tick < lookup.End)));
                    }
                    var song = GuitarProImporter.Import(file);
                    var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions());
                    var ours = timeline.Bars.Select(b => (bar: b.Bar, ms: b.EndMs - b.StartMs, tempo: (double)b.Tempo, shortened: b.Slots < MusicTime.BarSlots(song, b.Bar))).ToList();
                    // Incomplete Guitar Pro bars play only as long as their content; alphaTab pads them.
                    var shortened = ours.Count(o => o.shortened);

                    var issues = new List<string>();
                    var orderMismatch = Enumerable.Range(0, Math.Min(ours.Count, theirs.Count)).FirstOrDefault(i => ours[i].bar != theirs[i].bar, -1);
                    if (ours.Count != theirs.Count || orderMismatch >= 0)
                        issues.Add($"ORDER played bars {ours.Count}/{theirs.Count}" + (orderMismatch >= 0 ? $", first difference at play step {orderMismatch + 1}: ours bar {ours[orderMismatch].bar + 1} vs {theirs[orderMismatch].bar + 1}" : ""));
                    var steps = Math.Min(ours.Count, theirs.Count);
                    var firstTempo = Enumerable.Range(0, steps).FirstOrDefault(i => ours[i].bar == theirs[i].bar && Math.Abs(ours[i].tempo - theirs[i].tempo) > 0.5, -1);
                    // (No start-tempo check: alphaTab reports a bar's first automation but plays the last; lengths decide.)
                    var firstLength = Enumerable.Range(0, steps).FirstOrDefault(i => ours[i].bar == theirs[i].bar && !ours[i].shortened && theirs[i].ms > 1 && Math.Abs(ours[i].ms / theirs[i].ms - 1) > 0.02, -1);
                    var lengthCount = Enumerable.Range(0, steps).Count(i => ours[i].bar == theirs[i].bar && !ours[i].shortened && theirs[i].ms > 1 && Math.Abs(ours[i].ms / theirs[i].ms - 1) > 0.02);
                    if (firstLength >= 0)
                    {
                        var bar = ours[firstLength].bar;
                        var measure = MusicTime.BarOf(song, bar);
                        issues.Add($"LENGTH {lengthCount} bars, first bar {bar + 1}: ours {ours[firstLength].ms:0}ms vs {theirs[firstLength].ms:0}ms (ts ours {measure?.TimeSigNum ?? song.TimeSignatureNumerator}/{measure?.TimeSigDenom ?? song.TimeSignatureDenominator} vs {score.MasterBars[bar].TimeSignatureNumerator}/{score.MasterBars[bar].TimeSignatureDenominator}{(theirs[firstLength].midBarTempo ? ", mid-bar tempo change" : "")})");
                    }
                    var totalOurs = ours.Where((o, i) => !o.shortened).Sum(o => o.ms);
                    var totalTheirs = theirs.Where((t, i) => i >= ours.Count || !ours[i].shortened).Sum(t => t.ms);
                    if (shortened > 0) issues.Add($"(info: {shortened} incomplete bars played at content length)");
                    var midBar = theirs.Count(t => t.midBarTempo);

                    // Mix-table points: alphaTab beat automations per type vs TabForge mix points / tempo changes.
                    var autos = score.Tracks.SelectMany(t => t.Staves).SelectMany(st => st.Bars).SelectMany(b => b.Voices).SelectMany(v => v.Beats)
                        .SelectMany(bt => bt.Automations).GroupBy(a => a.Type.ToString()).ToDictionary(g => g.Key, g => g.Count());
                    var mixCells = song.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells)).Where(c => c.Mix is not null).Select(c => c.Mix!).ToList();
                    var mixText = $"mix src[{string.Join(",", autos.Select(kv => $"{kv.Key}={kv.Value}"))}] ours[vol={mixCells.Count(m => m.Volume is not null)},pan={mixCells.Count(m => m.Pan is not null)},prog={mixCells.Count(m => m.Program is not null)},tempoBars={song.Tracks.FirstOrDefault()?.Measures.Count(m => m.TempoChange is not null) ?? 0}]";
                    if (Environment.GetEnvironmentVariable("TF_TEMPO_BARS") is { Length: > 0 } range)
                    {
                        var parts = range.Split('-'); var from = int.Parse(parts[0]) - 1; var to = int.Parse(parts[^1]) - 1;
                        for (var mbi = from; mbi <= to && mbi < score.MasterBars.Count; mbi++)
                        {
                            var mbar = score.MasterBars[mbi];
                            var autos2 = string.Join(",", mbar.TempoAutomations.Select(a => $"{a.Value}@{a.RatioPosition:0.###}{(a.IsLinear ? "lin" : "")}"));
                            var beatAutos = string.Join(",", score.Tracks.SelectMany(t => t.Staves).Select(st => st.Bars[mbi]).SelectMany(b => b.Voices).SelectMany(v => v.Beats)
                                .SelectMany(bt => bt.Automations.Where(a => a.Type == AlphaTab.Model.AutomationType.Tempo).Select(a => $"{a.Value}@beat{bt.PlaybackStart}{(a.IsLinear ? "lin" : "")}")).Distinct());
                            var m = MusicTime.BarOf(song, mbi);
                            issues.Add($"bar{mbi + 1}: rs={mbar.IsRepeatStart} rc={mbar.RepeatCount} alt={mbar.AlternateEndings} dir=[{(mbar.Directions is null ? "" : string.Join(",", mbar.Directions))}] ours rs={m?.RepeatStart} re={m?.RepeatEnd} rc={m?.RepeatCount} alt={m?.AlternateEnding} master[{autos2}] beats[{beatAutos}] ours start={m?.TempoChange} mid=[{string.Join(",", m?.MidBarTempos?.Select(t => $"{t.Tempo}@{t.Slot}") ?? Array.Empty<string>())}]");
                        }
                    }
                    if (Environment.GetEnvironmentVariable("TF_TEMPO_BARS") is { Length: > 0 })
                        issues.Add("order ours: " + string.Join(" ", ours.Take(200).Select(o => o.bar + 1)) + " | theirs: " + string.Join(" ", theirs.Take(200).Select(t => t.bar + 1)));
                    var flag = issues.Any(i => !i.StartsWith("(info")) || Math.Abs(totalOurs / Math.Max(1, totalTheirs) - 1) > 0.01 ? "TIMING" : "ok";
                    text.AppendLine($"{flag,-7} {Path.GetFileName(file)}: total ours {totalOurs / 1000:0.0}s vs {totalTheirs / 1000:0.0}s | midBarTempo={midBar} | {string.Join(" | ", issues)} | {mixText}");
                }
                catch (Exception ex) { text.AppendLine($"ERROR   {Path.GetFileName(file)}: {ex.GetBaseException().Message}"); }
            }
            DiagnosticFileService.WriteText(FilePathPolicy.OutputFile(args[2], "timing audit report"), text.ToString()); return Ok;
        });
    }

    /// <summary>`--audit-drums &lt;folder&gt; &lt;out.txt&gt;`: for each .gpx/.gp file, alphaTab's percussion tracks and notes vs TabForge's drum tracks and notes.</summary>
    private static int RunDrumAudit(string[] args)
    {
        if (args.Length < 3) return Usage("--audit-drums <folder> <out.txt>");
        return Guard("Drum audit", () =>
        {
            var text = new StringBuilder();
            foreach (var file in Directory.EnumerateFiles(args[1]).Where(f => Services.FileTypes.IsGuitarPro(Path.GetExtension(f))).OrderBy(f => f))
            {
                try
                {
                    var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(GuitarProImporter.WithoutLeadingJunk(File.ReadAllBytes(file)), new AlphaTab.Settings());
                    var sourceDrums = score.Tracks.Where(t => t.Staves.Any(s => s.IsPercussion)).ToList();
                    var sourceNotes = sourceDrums.Sum(t => t.Staves.Sum(s => s.Bars.Sum(b => b.Voices.Sum(v => v.Beats.Sum(beat => beat.Notes.Count)))));
                    var importContext = new ImportContext();
                    var song = GuitarProImporter.Import(file, importContext);
                    var drums = song.Tracks.Where(t => t.Kind == Models.TrackKind.Drums || t.MidiChannel == 9).ToList();
                    var notes = drums.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count) + m.Voice2Cells.Sum(c => c.Notes.Count)));
                    // Which source drum sounds would be discarded (outside the GM drum range) and why.
                    var dropped = new SortedDictionary<string, int>();
                    foreach (var t in sourceDrums)
                    {
                        var arts = t.PercussionArticulations?.Cast<object>().ToList() ?? new List<object>();
                        foreach (var n in t.Staves.SelectMany(st => st.Bars).SelectMany(b => b.Voices).SelectMany(v => v.Beats).SelectMany(bt => bt.Notes))
                        {
                            var pitch = GuitarProImporter.DrumPitch(n, arts);
                            if (pitch is >= 27 and <= 87) continue;
                            var key = $"art{n.PercussionArticulation}->{pitch}(arts={arts.Count})";
                            if (!dropped.ContainsKey(key) && Environment.GetEnvironmentVariable("TF_DRUM_DETAIL") is { Length: > 0 })
                                key += $" [tieDest={n.IsTieDestination} origin={(n.TieOrigin is { } o ? $"art{o.PercussionArticulation}/f{o.Fret}/rv{o.RealValue}/tieDest={o.IsTieDestination}" : "none")} str={n.String} fret={n.Fret} rv={n.RealValue} dead={n.IsDead} ghost={n.IsGhost} bar={n.Beat.Voice.Bar.Index} voice={n.Beat.Voice.Index} mapper={ResolveViaMapper(n)}]";
                            dropped[key] = dropped.GetValueOrDefault(key) + 1;
                        }
                    }
                    var layout = string.Join(" ", sourceDrums.SelectMany(t => t.Staves.Select((st, si) => $"staff{si}:" + string.Join(",",
                        Enumerable.Range(0, st.Bars.Count == 0 ? 0 : st.Bars.Max(b => b.Voices.Count))
                            .Select(vi => $"v{vi}={st.Bars.Sum(b => vi < b.Voices.Count ? b.Voices[vi].Beats.Sum(bt => bt.Notes.Count) : 0)}")))));
                    var skipped = importContext.SkippedDuplicates;
                    var dupSamples = Environment.GetEnvironmentVariable("TF_DRUM_DETAIL") is { Length: > 0 } ? " dups[" + string.Join("; ", importContext.DuplicateSamples) + "]" : "";
                    // Export round trip: what Guitar Pro 7/8 (and alphaTab/TuxGuitar) read back from our .gp.
                    var exportNote = "";
                    if (drums.Count > 0)
                    {
                        var tmp = Path.Combine(Path.GetTempPath(), $"tabforge-drum-roundtrip-{Environment.ProcessId}.gp");
                        try
                        {
                            GuitarProExporter.Save(song, tmp);
                            var back = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(File.ReadAllBytes(tmp), new AlphaTab.Settings());
                            var ours = drums.SelectMany(t => t.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes))
                                .GroupBy(n => n.MidiValue).ToDictionary(g => g.Key, g => g.Count());
                            var theirs = back.Tracks.Where(t => t.Staves.Any(st => st.IsPercussion))
                                .SelectMany(t =>
                                {
                                    var arts = t.PercussionArticulations.Cast<AlphaTab.Model.InstrumentArticulation>().ToList();
                                    return t.Staves.SelectMany(st => st.Bars).SelectMany(bb => bb.Voices).SelectMany(v => v.Beats).SelectMany(bt => bt.Notes)
                                        .Select(n => (int)n.PercussionArticulation is var i && i >= 0 && i < arts.Count ? (int)arts[i].OutputMidiNumber : -1);
                                })
                                .GroupBy(m => m).ToDictionary(g => g.Key, g => g.Count());
                            var same = ours.Keys.Count == theirs.Keys.Count && ours.All(kv => theirs.TryGetValue(kv.Key, out var got) && got == kv.Value);
                            exportNote = same ? " | export .gp: identical drums" : $" | EXPORT-MISMATCH ours={ours.Values.Sum()} gp={theirs.Values.Sum()} ({string.Join(",", theirs.Where(kv => !ours.ContainsKey(kv.Key)).Select(kv => $"{kv.Key}x{kv.Value}").Take(6))})";
                        }
                        catch (Exception ex) { exportNote = $" | EXPORT-ERROR {ex.GetBaseException().Message}"; }
                        finally { try { File.Delete(tmp); } catch (IOException) { } }
                    }
                    // Every instrument, not only drums: notes per track against the reference reader.
                    var trackIssues = new List<string>();
                    for (var ti = 0; ti < Math.Min(score.Tracks.Count, song.Tracks.Count); ti++)
                    {
                        // Distinct hits: the same sound at the same instant written twice (e.g. a kick on two
                        // strings) is one musical event, which the importer merges on purpose.
                        var isDrum = score.Tracks[ti].Staves.Any(st => st.IsPercussion);
                        var trackArts = score.Tracks[ti].PercussionArticulations.Cast<object>().ToList();
                        var src = score.Tracks[ti].Staves.SelectMany(st => st.Bars).SelectMany(bb => bb.Voices).SelectMany(v => v.Beats)
                            .SelectMany(bt => bt.Notes.Select(n => (bar: bt.Voice.Bar.Index, voice: bt.Voice.Index == 0 ? 0 : 1, start: bt.PlaybackStart,
                                grace: bt.GraceType != AlphaTab.Model.GraceType.None,
                                key: isDrum ? GuitarProImporter.DrumPitch(n, trackArts) : n.String > 0 ? n.String * 1000 + n.Fret : 100_000 + (int)n.RealValue)))
                            .Distinct().Count();
                        var got = song.Tracks[ti].Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count) + m.Voice2Cells.Sum(c => c.Notes.Count));
                        if (src > 0 && (got < src * 0.97 || got > src * 1.01)) trackIssues.Add($"{score.Tracks[ti].Name}: src={src} got={got}");
                    }
                    var flag = trackIssues.Count > 0 || score.Tracks.Count != song.Tracks.Count
                        ? "MISMATCH" : exportNote.Contains("EXPORT-") ? "EXPORT" : "ok";
                    if (sourceDrums.Count != drums.Count) exportNote += " | info: drum-track labels differ (channel 10 / name)";
                    if (trackIssues.Count > 0) exportNote += " | tracks: " + string.Join("; ", trackIssues);
                    text.AppendLine($"{flag,-8} {Path.GetFileName(file)}: alphaTab drum tracks={sourceDrums.Count} notes={sourceNotes} | TabForge drum tracks={drums.Count} notes={notes} | tracks {score.Tracks.Count}/{song.Tracks.Count}" +
                        (sourceDrums.Count > 0 ? $" | source: {string.Join("; ", sourceDrums.Select(t => $"{t.Name} ch={t.PlaybackInfo.PrimaryChannel} prog={t.PlaybackInfo.Program}"))}" : "") +
                        $" | layout {layout} dupSkipped={skipped}{dupSamples}" + exportNote + (dropped.Count > 0 ? $" | dropped: {string.Join(", ", dropped.Take(12).Select(kv => $"{kv.Key} x{kv.Value}"))}" : "") +
                        (drums.Count > 0 || sourceDrums.Count > 0 ? $" | imported: {string.Join("; ", song.Tracks.Select(t => $"{t.Name}:{t.Kind}/ch{t.MidiChannel}"))}" : ""));
                }
                catch (Exception ex) { text.AppendLine($"ERROR    {Path.GetFileName(file)}: {ex.GetBaseException().Message}"); }
            }
            DiagnosticFileService.WriteText(FilePathPolicy.OutputFile(args[2], "drum audit report"), text.ToString()); return Ok;
        });
    }

    /// <summary>`--plausibility &lt;file|folder&gt; &lt;out.txt&gt; [seconds per file]`: the damaged-file check on one song or every Guitar Pro file under a folder (one line each; exit 1 when any would warn).</summary>
    private static int RunPlausibility(string[] args)
    {
        if (args.Length < 3) return Usage("--plausibility <file|folder> <out.txt> [seconds per file]");
        var seconds = args.Length > 3 && int.TryParse(args[3], out var s) ? Math.Clamp(s, 1, 600) : 60;
        return Guard("Plausibility", () =>
        {
            var files = Directory.Exists(args[1])
                ? Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Where(f => GuitarProImporter.SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string> { args[1] };
            var report = new StringBuilder();
            int warned = 0, failed = 0, timedOut = 0, clean = 0, odd = 0;
            foreach (var file in files)
            {
                try
                {
                    var work = Task.Run(() => ImportPlausibility.Scan(GuitarProImporter.Import(file), File.ReadAllBytes(file)));
                    if (!work.Wait(TimeSpan.FromSeconds(seconds))) { timedOut++; report.AppendLine($"TIMEOUT {file}"); continue; }
                    var r = work.Result;
                    if (r.Count == 0 && r.TextBytes == 0) { clean++; continue; }
                    odd++;
                    if (r.ShouldWarn) warned++;
                    report.AppendLine($"{(r.ShouldWarn ? "WARN" : "odd ")} {file} count={r.Count} text={r.TextBytes} first=bar {r.FirstBar} '{r.FirstTrack}' [{string.Join(", ", r.ByKind.Select(k => $"{k.Key}={k.Value}"))}]");
                }
                catch (Exception ex) { failed++; report.AppendLine($"ERROR   {file}: {ex.GetBaseException().Message}"); }
            }
            var summary = $"files={files.Count} clean={clean} below-threshold={odd - warned} WARN={warned} import-errors={failed} timeouts={timedOut} threshold={ImportPlausibility.WarnThreshold}";
            DiagnosticFileService.WriteText(FilePathPolicy.OutputFile(args[2], "plausibility report"), summary + Environment.NewLine + report);
            Console.WriteLine(summary);
            return warned == 0 ? Ok : CheckFailed;
        });
    }

    /// <summary>`--dump &lt;song&gt; &lt;out.txt&gt; [firstBar] [bars]`: the imported model as text (tuning, metre, beats, notes, effects).</summary>
    private static int RunDump(string[] args)
    {
        if (args.Length < 3) return Usage("--dump <song> <out.txt> [firstBar] [bars]");
        var first = args.Length > 3 && int.TryParse(args[3], out var f) ? Math.Max(0, f) : 0;
        var count = args.Length > 4 && int.TryParse(args[4], out var c) ? Math.Clamp(c, 1, 10_000) : 4;
        return Guard("Dump", () =>
        {
            var song = LoadAny(args[1]);
            var text = new StringBuilder();
            text.AppendLine($"song {song.Title} tempo={song.Tempo} ts={song.TimeSignatureNumerator}/{song.TimeSignatureDenominator}");
            for (var t = 0; t < song.Tracks.Count; t++)
            {
                var track = song.Tracks[t];
                text.AppendLine($"track {t} {track.Name} kind={track.Kind} ch={track.MidiChannel} prog={track.MidiProgram} vol={track.Volume} capo={track.Capo} tuning=[{string.Join(",", track.StringTunings)}]");
                for (var b = first; b < Math.Min(track.Measures.Count, first + count); b++)
                {
                    var measure = track.Measures[b];
                    text.AppendLine($"  bar {b + 1} ts={measure.TimeSigNum}/{measure.TimeSigDenom} rs={measure.RepeatStart} re={measure.RepeatEnd}");
                    foreach (var cell in measure.Cells)
                        text.AppendLine($"    dur={cell.DurationDenominator} dots={cell.Dots} tup={cell.TupletNumerator} rest={cell.IsRest} pos={cell.RhythmicPosition} notes=" +
                            string.Join(" ", cell.Notes.Select(n => $"s{n.StringIndex}f{n.Fret}m{n.MidiValue}{(n.SlideTargetMidi > 0 ? $"->{n.SlideTargetMidi}" : "")}[{string.Join(",", n.Techniques)}]{(n.BendPoints.Count > 0 ? "bend" : "")}")));
                }
            }
            DiagnosticFileService.WriteText(FilePathPolicy.OutputFile(args[2], "dump report"), text.ToString());
            return Ok;
        });
    }

    /// <summary>`--exportgp &lt;song&gt; &lt;out.gp&gt;`: writes a .gp, reads it back and reports what survived (1 = notes lost).</summary>
    private static int RunExportGp(string[] args)
    {
        if (args.Length < 3) return Usage("--exportgp <song> <out.gp> [clean]");
        return Guard("Guitar Pro export", () =>
        {
            var song = LoadAny(args[1]);
            var outPath = FilePathPolicy.OutputFile(args[2], "Guitar Pro file", ".gp");
            GuitarProExporter.Save(song, outPath, embedProject: !(args.Length > 3 && args[3].Equals("clean", StringComparison.OrdinalIgnoreCase)));
            var back = GuitarProImporter.Import(outPath);
            static int Notes(SongProject p) => p.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Concat(m.Voice2Cells).Sum(c => c.Notes.Count)));
            static int Bars(SongProject p) => p.Tracks.Count == 0 ? 0 : p.Tracks.Max(t => t.Measures.Count);
            var report = $"tracks {song.Tracks.Count}->{back.Tracks.Count} bars {Bars(song)}->{Bars(back)} notes {Notes(song)}->{Notes(back)} tempo {song.Tempo}->{back.Tempo}\n" +
                string.Join("\n", song.Tracks.Zip(back.Tracks, (a, b) =>
                    $"{a.Name}: prog {a.MidiProgram}->{b.MidiProgram} ch {a.MidiChannel}->{b.MidiChannel} tuning {string.Join(",", a.StringTunings)}->{string.Join(",", b.StringTunings)}"));
            DiagnosticFileService.WriteText(FilePathPolicy.OutputFile(outPath + ".txt", "export report"), report);
            return Notes(song) == Notes(back) && song.Tracks.Count == back.Tracks.Count ? Ok : CheckFailed;
        });
    }

    /// <summary>`--memreport &lt;song&gt; &lt;out.txt&gt;`: what each stage keeps alive and what a single edit costs.</summary>
    private static int RunMemoryReport(string[] args)
    {
        if (args.Length < 3) return Usage("--memreport <song> <out.txt>");
        return Guard("Memory report", () =>
        {
            static double Live() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true) / 1048576.0; }
            var options = new PlaybackOptions { Metronome = true, LiveMetronomeEvents = true, RespectMuteSolo = false };
            var report = new StringBuilder();
            var baseline = Live();
            report.AppendLine($"baseline {baseline:0.0} MB");
            var song = LoadAny(args[1]);
            var afterModel = Live();
            var cells = song.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Count + m.Voice2Cells.Count));
            var notes = song.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));
            report.AppendLine($"song model {afterModel - baseline:0.0} MB  (tracks {song.Tracks.Count}, bars {BarRangeEditor.MaxMeasures(song)}, cells {cells}, notes {notes})");
            var timeline = MidiTimelineBuilder.Build(song, options);
            var afterTimeline = Live();
            report.AppendLine($"playback timeline {afterTimeline - afterModel:0.0} MB  (events {timeline.Events.Count}, notes {timeline.Notes.Count})");
            var snapshot = ProjectService.SnapshotBytes(song);
            var afterSnapshot = Live();
            report.AppendLine($"one undo snapshot {afterSnapshot - afterTimeline:0.0} MB ({snapshot.Length / 1024.0:0} KB)");
            report.AppendLine($"undo snapshot time {AverageMs(() => ProjectService.SnapshotBytes(song)):0.0} ms");
            report.AppendLine($"unsaved-changes hash time {AverageMs(() => ProjectService.ContentHash(song)):0.0} ms");
            report.AppendLine($"playback timeline compile time {AverageMs(() => MidiTimelineBuilder.Build(song, options)):0.0} ms");
            var restored = ProjectService.RestoreBytes(ProjectService.SnapshotBytes(song));
            var identical = ProjectService.ContentHash(restored).AsSpan().SequenceEqual(ProjectService.ContentHash(song));
            report.AppendLine($"lossless round-trip: {(identical ? "identical" : "DIFFERENT")}");
            GC.KeepAlive(timeline);
            DiagnosticFileService.WriteText(FilePathPolicy.OutputFile(args[2], "memory report"), report.ToString());
            return identical ? Ok : CheckFailed;
        });
    }

    // Warm once, then average five runs: what a single edit costs on the UI thread.
    private static double AverageMs(Action action)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) action();
        return watch.Elapsed.TotalMilliseconds / 5;
    }

    private static SongProject LoadAny(string path) =>
        Path.GetExtension(path).Equals(".tforge", StringComparison.OrdinalIgnoreCase)
            ? ProjectService.Load(path)
            : GuitarProImporter.Import(path);

    private static int Usage(string usage)
    {
        Console.Error.WriteLine($"usage: TabForge.exe {usage}");
        return CouldNotRun;
    }

    // A command-line boundary: any failure becomes exit code 2 with a one-line reason (never a crash dialog).
    private static int Guard(string what, Func<int> run)
    {
        try { return run(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"{what} failed: {ex}");
            Console.Error.WriteLine($"{what} failed ({ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}).");
            if (Environment.GetEnvironmentVariable("TABFORGE_DIAG_STACK") == "1") Console.Error.WriteLine(ex.ToString());
            return CouldNotRun;
        }
    }
}
