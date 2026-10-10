using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Presets;

namespace TabForge.Diagnostics;

/// <summary>
/// `--pitch-audit &lt;report&gt; [max]`: loads each VST instrument of the user's remembered scan list (at most 10 by default), measures its
/// sounding octave silently (scratch-buffer measurement in the engine, nothing reaches the output) and writes a Markdown table.
/// The user's settings are read, never written.
/// </summary>
internal static class PitchAudit
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: TabForge --pitch-audit <report.md> [max]"); return 2; }
        var max = args.Length > 2 && int.TryParse(args[2], out var m) ? Math.Clamp(m, 1, 50) : 10;
        try
        {
            var outPath = FilePathPolicy.OutputFile(args[1], "diagnostic report");
            var settings = ReadPluginSettings(MainWindow.SettingsPath);
            var candidates = settings.ScanCache.Concat(settings.Probed)
                .Where(p => p.Role.Equals("Instrument", StringComparison.OrdinalIgnoreCase) && (File.Exists(p.Path) || Directory.Exists(p.Path)))
                .GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Where(p => !settings.Quarantined.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
                .Take(max).ToList();
            var client = AudioEngineClient.Instance;
            client.WarmIdle = TimeSpan.Zero;   // each plug-in gets a fresh engine (no R-10 warm period in this audit)
            client.Quarantine = () => settings.Quarantined;
            var md = new StringBuilder();
            md.AppendLine("# VST instrument pitch audit").AppendLine();
            md.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by `TabForge --pitch-audit` (silent scratch-buffer measurement; test notes E2, B2, E3 = MIDI 40/47/52).").AppendLine();
            md.AppendLine("Offset = the transpose automatic pitch matching applies (e.g. +24 when the preset sounds two octaves low).").AppendLine();
            md.AppendLine("| Plug-in | Format | Preset / program | Result | Offset | Confidence | Per-note sounding offset (st) |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            if (candidates.Count == 0) md.AppendLine("| (no VST instruments in the remembered scan list) | | | | | | |");
            foreach (var info in candidates)
            {
                Console.WriteLine($"pitch audit: {info.Name}");
                var song = TemplateFactory.Blank();
                var track = song.Tracks[0];
                track.SoundSource = SoundSources.Plugins;
                var slot = new PluginSlot { Name = info.Name, Path = info.Path, Format = info.Format, Type = PluginSlotType.Instrument };
                track.Rig.Plugins.Add(slot);
                ChainAck? ack = null; string? failed = null; PitchResult? result = null; string preset = "";
                void OnAck(ChainAck a) => ack = a;
                void OnFail(string p, string why) => failed = why;
                void OnPitch(PitchResult r) => result = r;
                void OnPrograms(int s, int i, int current, IReadOnlyList<string> names) => preset = current >= 0 && current < names.Count ? names[current] : "";
                client.ChainAcknowledged += OnAck; client.PluginFailed += OnFail; client.PitchMeasured += OnPitch; client.ProgramsReceived += OnPrograms;
                try
                {
                    client.Sync(song.Tracks, settings);
                    WaitFor(() => ack is not null || failed is not null, 30000);
                    var loaded = ack?.Plugins.Any(r => r.Status == PluginLoadStatus.Loaded) == true;
                    if (!loaded) { md.AppendLine($"| {info.Name} | {info.Format} | | skipped (failed to load{(failed is null ? "" : ": " + Esc(failed))}) | | | |"); continue; }
                    client.RequestPrograms(track, slot);
                    WaitFor(() => preset.Length > 0, 1500);
                    WaitFor(() => false, 500);   // let the instrument settle
                    var id = client.MeasurePitch(track, slot, new[] { 40, 47, 52 });
                    WaitFor(() => result is { } r && r.RequestId == id, 15000);
                    if (result is null) { md.AppendLine($"| {info.Name} | {info.Format} | {Esc(preset)} | timed out | | | |"); continue; }
                    var per = string.Join(", ", result.Notes.Select((n, i) => double.IsNaN(result.Offsets[i]) ? $"{n}: unpitched" : $"{n}: {result.Offsets[i].ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}"));
                    var offset = result.Status == PitchMatch.Status.Ok ? $"{result.Transpose:+0;-0}" : "0";
                    md.AppendLine($"| {info.Name} | {info.Format} | {Esc(preset)} | {result.Status} | {offset} | {result.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {per} |");
                }
                finally
                {
                    client.ChainAcknowledged -= OnAck; client.PluginFailed -= OnFail; client.PitchMeasured -= OnPitch; client.ProgramsReceived -= OnPrograms;
                    client.Sync(Array.Empty<TrackModel>(), settings);
                    WaitFor(() => !client.IsRunning, 3000);
                }
            }
            DiagnosticFileService.WriteText(outPath, md.ToString());
            Console.WriteLine($"Wrote {outPath} ({candidates.Count} instruments)");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException) // Not logged: diagnostic probe: the failure goes to its report, not errors.log
        {
            Console.Error.WriteLine($"Pitch audit could not run: {ex.Message}");
            return 2;
        }
    }

    /// <summary>The user's plug-in settings (read only). When the full settings file does not validate, only the scan lists are read from it.</summary>
    private static PluginSettings ReadPluginSettings(string path)
    {
        if (!File.Exists(path)) return new PluginSettings();
        try { return SettingsFileService.Load(path).Plugins; }
        catch (InvalidDataException) // Not logged: diagnostic probe: the failure goes to its report, not errors.log
        {
            var result = new PluginSettings();
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Plugins", out var pl)) return result;
            List<KnownPlugin> List(string name) => pl.TryGetProperty(name, out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array
                ? arr.EnumerateArray().Select(e => new KnownPlugin
                {
                    Name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "", Path = e.TryGetProperty("Path", out var p) ? p.GetString() ?? "" : "",
                    Format = e.TryGetProperty("Format", out var f) ? f.GetString() ?? "" : "", Role = e.TryGetProperty("Role", out var r) ? r.GetString() ?? "" : "",
                }).Where(k => k.Path.Length is > 0 and < 1024).ToList()
                : new List<KnownPlugin>();
            result.ScanCache = List("ScanCache"); result.Probed = List("Probed");
            if (pl.TryGetProperty("Quarantined", out var q) && q.ValueKind == System.Text.Json.JsonValueKind.Array)
                result.Quarantined = q.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
            result.ApprovedPluginPaths = result.ScanCache.Concat(result.Probed).Select(k => k.Path).ToList();
            return result;
        }
    }

    private static string Esc(string s) => s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");

    private static bool WaitFor(Func<bool> condition, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < ms)
        {
            Thread.Sleep(20);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }
        return condition();
    }
}
