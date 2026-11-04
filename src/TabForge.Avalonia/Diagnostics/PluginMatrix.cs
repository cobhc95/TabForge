using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the headless Stage 10 per-plug-in run (--plugin-matrix): checks 1-10 from the plan, one process per plug-in,
// result JSON and log. The runner downloads/extracts the plug-in, enforces the timeout, screenshots the editor and
// patches check 7's pixel verdict into the JSON.
// Does not own: downloading (tools/plugin-matrix/run.py), the helper protocol (RemotePlugin), routing (Vst32Bridge) or
// the editor window (tfvst32/tfvst64/Vst3Plugin). Tests: wine-matrix.yml and the local Windows matrix run.
internal static partial class PluginMatrix
{
    public const string Argument = "--plugin-matrix";
    private const int Rate = 48000, Frames = 512;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        // --plugin-matrix <entry.json> <out-dir>
        if (args.Length < 3) return Usage();
        var entryPath = Path.GetFullPath(args[1]);
        var outDir = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(outDir);
        var result = new MatrixResult { StartedUtc = DateTime.UtcNow.ToString("O") };
        var watch = Stopwatch.StartNew();
        try
        {
            var entry = JsonSerializer.Deserialize<MatrixEntry>(File.ReadAllText(entryPath), JsonOptions)
                ?? throw new InvalidOperationException("The matrix entry JSON is empty.");
            Fill(result, entry);
            var log = new EngineLogWriter();
            log.CreateForRun(Path.Combine(outDir, "engine.log"));
            var threads = new EngineThreads(log);
            threads.MarkMainThread();
            result.Host = $"{RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}";
            result.EngineLog = "engine.log";
            await RunChecksAsync(entry, outDir, result, threads, log);
        }
        catch (Exception ex)
        {
            result.Overall = "error";
            result.Error = ex.GetBaseException().Message;
            result.FailureClass = "setup:" + Category(result.Error);
            Console.Error.WriteLine("plugin-matrix FAILED: " + result.Error);
        }
        finally
        {
            result.DurationMs = watch.ElapsedMilliseconds;
            FinalizeResult(result);
            WriteResult(outDir, result);
        }
        Console.Out.WriteLine($"plugin-matrix {result.Overall} id={result.Id} route={result.Route} " +
            $"checks={result.Checks.Count(c => c.Status == "pass")}/{result.Checks.Count} duration_ms={result.DurationMs}");
        return result.Overall == "pass" ? 0 : 1;
    }

    private static async Task RunChecksAsync(MatrixEntry entry, string outDir, MatrixResult result, EngineThreads threads, EngineLogWriter log)
    {
        var spec = Spec(entry);
        result.Route = OperatingSystem.IsWindows() && entry.Bitness == 64 ? "native" : $"bridged-{entry.Bitness}";
        if (OperatingSystem.IsLinux())
        {
            // The first probe on a fresh profile must not race prefix creation: --wine-approve prepares it first,
            // and the matrix's check 1 must do the same or the first plug-in on each shard is refused.
            try { WineRuntime.Locate()?.EnsureReady(); }
            catch (Exception ex) { log.Write("matrix prefix prepare: " + ex.GetBaseException().Message); }
        }
        await CheckAsync(result, 1, "Scan, identify, approve", c => CheckApproveAsync(entry, spec, c));
        var load = CheckReload(entry, spec, threads);
        result.Checks.Add(load);
        if (load.Status == "fail")
        {
            SkipRest(result, 2, "the plug-in did not load five times: " + load.Detail);
            return;
        }

        using var owner = new MatrixOwner("TabForge Matrix - " + entry.Id);
        IPluginInstance? plugin = null;
        try
        {
            var created = CreateInstance(entry, spec, threads);
            plugin = created.Plugin;
            result.Route = created.Route;
            result.Helper = created.Helper;
            result.HelperPid = created.Pid;
            if (created.Pid is { } helperPid) File.WriteAllText(Path.Combine(outDir, "helper-pid.txt"), helperPid.ToString());
            if (created.Helper is not null) result.WineVersion = WineVersion(created.Helper);
            if (!string.IsNullOrWhiteSpace(entry.SkipAudioReason))
            {
                Check(result, 3, "Audio: instrument/effect, bypass, silence, finite", c =>
                {
                    c.Status = "skip";
                    c.Detail = entry.SkipAudioReason;
                    c.Metrics["data_gap"] = true;
                });
            }
            else Check(result, 3, "Audio: instrument/effect, bypass, silence, finite", c => CheckAudio(entry, plugin, c));
            Check(result, 4, "State save/restore and project reopen", c => CheckState(entry, spec, plugin, threads, c));
            Check(result, 5, "Parameters list, set and read back", c => CheckParameters(entry, plugin, c));
            CheckEditor(result, entry, plugin, owner, outDir, threads);
            Check(result, 8, "Sample rates 44.1/48/96 kHz, buffers 128/256/1024", c => CheckReconfigure(entry, plugin, c));
            if (!string.IsNullOrWhiteSpace(entry.SkipAudioReason))
            {
                Check(result, 9, "Offline render equals realtime", c =>
                {
                    c.Status = "skip";
                    c.Detail = "audio content not tested: " + entry.SkipAudioReason;
                    c.Metrics["data_gap"] = true;
                });
            }
            else Check(result, 9, "Offline render equals realtime", c => CheckOffline(entry, spec, plugin, threads, c));
            Check(result, 10, "Deliberate helper kill; engine survives", c => CheckKill(entry, plugin, threads, c));
        }
        catch (Exception ex)
        {
            result.Checks.Add(new MatrixCheck
            {
                Number = 0,
                Name = "load and run",
                Status = "fail",
                Detail = ex.GetBaseException().Message,
                Metrics = new Dictionary<string, object?> { ["exception"] = ex.GetType().Name },
            });
            Console.Error.WriteLine("plugin-matrix load/run: " + ex.GetBaseException().Message);
        }
        finally
        {
            if (plugin is not null)
            {
                try { plugin.Dispose(); } catch (Exception ex) { log.Write("matrix dispose: " + ex.Message); }
            }
        }
    }

    private static (IPluginInstance Plugin, string Route, string? Helper, int? Pid) CreateInstance(MatrixEntry entry, PluginSpec spec, EngineThreads threads)
    {
        // Windows keeps the product's native 64-bit route; 32-bit VST2 and 32-bit-only VST3 go through the helper.
        // Linux sends every Windows PE through the pinned Wine helper, exactly like the engine.
        if (OperatingSystem.IsWindows() && entry.Bitness == 64)
            return (PluginFactory.Create(spec.Path, entry.Format, entry.Role == "Instrument", Rate, Frames, threads), "native", null, null);
        var launch = Vst32Bridge.Launch(spec);
        var remote = new RemotePlugin(spec, Rate, Frames, threads, launch);
        return (remote, $"bridged-{entry.Bitness}", Path.GetFileName(launch.HelperPath ?? launch.ExecutablePath), remote.HostProcessId);
    }

    private static PluginSpec Spec(MatrixEntry entry) => new(
        entry.Path, entry.Format, entry.Role == "Instrument", Enabled: true, Wet: 100, State: null,
        Name: entry.Name, Id: entry.Id, ExpectedSha256: entry.ExpectedSha256 ?? Sha256(entry.Path), LocationTrusted: false);

    private static string Sha256(string path)
    {
        if (Directory.Exists(path))
        {
            var binary = PluginIdentity.BinaryOf(path) ?? throw new InvalidOperationException($"No loadable binary in {path}");
            path = binary;
        }
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string? WineVersion(string helper)
    {
        var configured = Environment.GetEnvironmentVariable("TABFORGE_WINE_VERSION");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return OperatingSystem.IsLinux() ? "bundled (see engine.log)" : null;
    }

    private static void Fill(MatrixResult result, MatrixEntry entry)
    {
        result.Id = entry.Id;
        result.Name = entry.Name;
        result.Version = entry.Version;
        result.Format = entry.Format;
        result.Bitness = entry.Bitness;
        result.Role = entry.Role;
        result.Path = entry.Path;
    }

    private static void WriteResult(string outDir, MatrixResult result) =>
        File.WriteAllText(Path.Combine(outDir, "result.json"), JsonSerializer.Serialize(result, JsonOptions));

    private static void FinalizeResult(MatrixResult result)
    {
        if (result.Error is not null) return;
        var failed = result.Checks.FirstOrDefault(c => c.Status == "fail");
        if (failed is not null)
        {
            result.Overall = "fail";
            result.FailureClass = $"check{failed.Number}:{Category(failed.Detail)}";
            return;
        }
        var refused = result.Checks.FirstOrDefault(c => c.Status == "refused");
        if (refused is not null)
        {
            result.Overall = "refused";
            result.FailureClass = $"check{refused.Number}:refusal";
            return;
        }
        var nondeterministic = result.Checks.FirstOrDefault(c => c.Status == "nondeterministic");
        if (nondeterministic is not null)
        {
            result.Overall = "nondeterministic";
            result.FailureClass = $"check{nondeterministic.Number}:nondeterministic";
            return;
        }
        var dataGap = result.Checks.FirstOrDefault(c => c.Status == "skip" &&
            c.Metrics.TryGetValue("data_gap", out var gap) && gap is true);
        if (dataGap is not null)
        {
            result.Overall = "skipped";
            result.FailureClass = $"check{dataGap.Number}:data";
            return;
        }
        result.Overall = result.Checks.Any(c => c.Status == "pass") ? "pass" : "skipped";
        result.FailureClass = null;
    }

    private static string Category(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        if (t.Contains("timeout") || t.Contains("did not answer")) return "timeout";
        if (t.Contains("non-finite") || t.Contains("nan") || t.Contains("infinity")) return "non-finite";
        if (t.Contains("silent")) return "silent";
        if (t.Contains("not identified") || t.Contains("refus")) return "identify";
        if (t.Contains("editor")) return "editor";
        if (t.Contains("state")) return "state";
        if (t.Contains("parameter")) return "parameters";
        if (t.Contains("sample rate") || t.Contains("reconfigure") || t.Contains("block")) return "reconfigure";
        if (t.Contains("offline") || t.Contains("realtime")) return "offline";
        if (t.Contains("kill") || t.Contains("crash") || t.Contains("lost")) return "kill";
        if (t.Contains("load") || t.Contains("missing") || t.Contains("not found")) return "load";
        return "other";
    }

    private static void Check(MatrixResult result, int number, string name, Action<MatrixCheck> body)
    {
        var check = new MatrixCheck { Number = number, Name = name };
        try
        {
            body(check);
            if (check.Status == "skip" && check.Detail.Length == 0) check.Detail = "not applicable";
        }
        catch (Exception ex)
        {
            check.Status = "fail";
            check.Detail = ex.GetBaseException().Message;
            check.Metrics["exception"] = ex.GetType().Name;
        }
        result.Checks.Add(check);
        Console.Out.WriteLine($"matrix check {number} {check.Status}: {check.Detail}");
    }

    private static async Task CheckAsync(MatrixResult result, int number, string name, Func<MatrixCheck, Task> body)
    {
        var check = new MatrixCheck { Number = number, Name = name };
        try
        {
            await body(check);
            if (check.Status == "skip" && check.Detail.Length == 0) check.Detail = "not applicable";
        }
        catch (Exception ex)
        {
            check.Status = "fail";
            check.Detail = ex.GetBaseException().Message;
            check.Metrics["exception"] = ex.GetType().Name;
        }
        result.Checks.Add(check);
        Console.Out.WriteLine($"matrix check {number} {check.Status}: {check.Detail}");
    }

    private static void SkipRest(MatrixResult result, int from, string reason)
    {
        for (var number = from + 1; number <= 10; number++)
        {
            if (result.Checks.Any(c => c.Number == number)) continue;
            result.Checks.Add(new MatrixCheck
            {
                Number = number,
                Name = CheckName(number),
                Status = "skip",
                Detail = reason,
            });
        }
    }

    private static string CheckName(int number) => number switch
    {
        1 => "Scan, identify, approve",
        2 => "Load, unload, reload five times",
        3 => "Audio: instrument/effect, bypass, silence, finite",
        4 => "State save/restore and project reopen",
        5 => "Parameters list, set and read back",
        6 => "Preset/program change with the editor open",
        7 => "Editor open, screenshot, close, reopen",
        8 => "Sample rates 44.1/48/96 kHz, buffers 128/256/1024",
        9 => "Offline render equals realtime",
        10 => "Deliberate helper kill; engine survives",
        _ => "unknown",
    };

    private static TransportInfo Transport(int block, int frames, int rate = Rate) => new()
    {
        Tempo = 120,
        PpqPosition = block * (double)frames / (2.0 * rate),
        Playing = true,
        Meter = new TransportMeter { Numerator = 4, Denominator = 4, BarStartPpq = 0 },
    };

    private static void FillTestSignal(float[] left, float[] right, ref double phase, ref uint rng)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var sine = (float)(0.2 * Math.Sin(phase));
            phase += 2 * Math.PI * 1000 / Rate;
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            var burst = (i % 256) < 32 ? ((rng / (float)uint.MaxValue) * 2 - 1) * 0.15f : 0f;
            left[i] = sine + burst;
            right[i] = sine + burst;
        }
    }

    private static bool AllFinite(params float[][] channels) => channels.All(c => c.All(float.IsFinite));

    private static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var sample in samples) sum += sample * (double)sample;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static float MaxDiff(float[] a, float[] b)
    {
        var max = 0f;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) max = Math.Max(max, Math.Abs(a[i] - b[i]));
        return max;
    }

    private static bool WaitForFile(string path, int timeoutMs, IPluginInstance plugin, EngineThreads threads)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (File.Exists(path)) return true;
            try { plugin.EditorIdle(); }
            catch (Exception) { /* swallow: best-effort editor pump; the check reports the missing marker, keep the process alive */ }
            threads.RunPending();
            // The owner and the native editor live on this thread. Without pumping, the runner's
            // ShowWindow/SetWindowPos waits ~30 s for a message this loop never dispatches.
            PumpWindowsMessages();
            Thread.Sleep(20);
        }
        return File.Exists(path);
    }

    private static void PumpWindowsMessages()
    {
        if (!OperatingSystem.IsWindows()) return;
        while (PluginMatrixNative.PeekMessageW(out var message, IntPtr.Zero, 0, 0, 0x0001))
        {
            PluginMatrixNative.TranslateMessage(ref message);
            PluginMatrixNative.DispatchMessageW(ref message);
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: TabForge --plugin-matrix <entry.json> <out-dir>");
        return 2;
    }

    private sealed class PortablePluginFileTrust : IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => fullPath;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }
}
