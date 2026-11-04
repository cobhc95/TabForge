using System.Diagnostics;
using System.IO;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>
/// Knows what each plug-in is: its vendor, and whether it is an instrument or an effect. An unapproved or changed file is never run to find out: only passive
/// metadata is read and the role stays "" (unknown, "approve to identify").VST3 bundles say so in
/// their moduleinfo.json; anything else that the user has approved is probed once in a throwaway process (TabForge.exe --plugin-info, so a
/// plug-in that crashes cannot affect TabForge). Results are remembered in settings.
/// Owns: the identify rules and the throwaway process call. Does not own: the trust decision (PluginTrust), the file checks (<see cref="IPluginFileTrust"/>, passed in by each app), the probe process itself (--plugin-info in each app's Program).
/// Tests: none dedicated(full suite), TabForge.Avalonia.Tests.FxAddPluginTests.
/// </summary>
public static class PluginCatalog
{
    /// <summary>The throwaway probe ceiling for native plug-ins (see P1-03: one shared constant).</summary>
    public const int DefaultProbeTimeoutMs = 15000;
    /// <summary>The probe ceiling for Wine-bridged plug-ins on Linux: long enough for first-run Wine setup (prefix creation) plus a slow plug-in load. Callers pass <see cref="DefaultProbeTimeoutMs"/>; bridged paths are raised to this.</summary>
    public const int BridgedProbeTimeoutMs = 180000;

    /// <summary>The detected role ("Instrument" / "Effect") and vendor; probes the plug-in when not known yet.</summary>
    public static (string Role, string Vendor, string Name) Describe(string path, PluginSettings settings, PluginTrust trust, IPluginFileTrust files, int timeoutMs = DefaultProbeTimeoutMs)
        => DescribeDetailed(path, settings, trust, files, timeoutMs).ToTriple();

    /// <summary>The detected role/vendor plus a readable refusal when the probe could not read the plug-in (null on success or when no probe ran).</summary>
    public static (string Role, string Vendor, string Name, string? Refusal) DescribeDetailed(string path, PluginSettings settings, PluginTrust trust, IPluginFileTrust files, int timeoutMs = DefaultProbeTimeoutMs, string? format = null)
    {
        KnownPlugin? known;
        lock (ProbedLock) known = settings.Probed.FirstOrDefault(p => string.Equals(p.Path, path, PathComparer.Comparison));
        if (known is { Role.Length: > 0 }) return (known.Role, known.Vendor, known.Name, null);
        if (path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
        {
            var (vendor, role, _) = VstScannerService.Vst3ModuleInfo(path);
            if (role.Length > 0) { var r = Remember(settings, path, role, vendor, ""); return (r.Role, r.Vendor, r.Name, null); }
        }
        // Passive metadata is all an unapproved (or changed) file gets: probing loads and runs the plug-in's code. Its role stays
        // unknown ("approve to identify") and nothing is remembered; it is identified once the user approves it.
        if (!trust.IsTrusted(path, settings, files))
            return ("", path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? VstScannerService.Vst3ModuleInfo(path).Vendor : VstScannerService.Vst2Vendor(path), "", null);
        var hash = trust.ProbeHash(path, settings, files);
        if (hash.Length == 0) return (GuessRole(path), "", "", ProbeRefusal.CannotReadText);
        var dependencies = trust.ProbeDependencies(path, settings, files);
        var effectiveTimeout = NeedsBridgedProbe(path, format) ? Math.Max(timeoutMs, BridgedProbeTimeoutMs) : timeoutMs;
        (string Role, string Vendor, string Name)? probed;
        string? refusal = null;
        if (ProbeOverride is { } fake)
        {
            probed = fake(path, hash, effectiveTimeout);
            if (probed is null) refusal = ProbeRefusal.CannotIdentifyText;
        }
        else
        {
            (probed, refusal) = ProbeWithRefusal(path, hash, dependencies, effectiveTimeout);
        }
        if (probed is { } found) { var r = Remember(settings, path, found.Role, found.Vendor, found.Name); return (r.Role, r.Vendor, r.Name, null); }
        // Could not be probed (crashed or timed out): guess from the name, never remembered.
        return (GuessRole(path), "", "", refusal ?? ProbeRefusal.CannotIdentifyText);
    }

    /// <summary>True when the role of <paramref name="path"/> is remembered (read from the plug-in, not guessed from its name).</summary>
    public static bool IsIdentified(string path, PluginSettings settings)
    {
        lock (ProbedLock) return settings.Probed.Any(p => string.Equals(p.Path, path, PathComparer.Comparison) && p.Role.Length > 0);
    }

    /// <summary>True when this path must go through the Wine bridge on Linux (so the bridged probe ceiling applies).</summary>
    public static bool NeedsBridgedProbe(string path, string? format = null)
    {
        if (format is { Length: > 0 } f && (f == PluginFormats.Bridged32 || f == PluginFormats.Bridged64)) return true;
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            if (path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) return Vst3Windows.IsWindowsOnly(path);
            return PeExports.IsVst2Plugin32(path) || PeExports.IsVst2Plugin64(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string GuessRole(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var instrumentWords = new[] { "synth", "piano", "drum", "nexus", "kontakt", "sampler", "organ", "strings", "keys", "superior", "ezdrummer", "ezkeys", "ezbass" };
        return instrumentWords.Any(name.Contains) ? "Instrument" : "Effect";
    }

    /// <summary>Identification runs several probes at once: the remembered list is shared.</summary>
    private static readonly object ProbedLock = new();

    private static (string Role, string Vendor, string Name) Remember(PluginSettings settings, string path, string role, string vendor, string name)
    {
        lock (ProbedLock)
        {
        settings.Probed.RemoveAll(p => string.Equals(p.Path, path, PathComparer.Comparison));
        settings.Probed.Add(new KnownPlugin { Path = path, Role = role, Vendor = vendor, Name = name, Format = PluginFormats.OfPlugin(path) });
        }
        return (role, vendor, name);
    }

    /// <summary>Self-test seam: replaces the throwaway process (a counter shows what would have been executed).</summary>
    internal static Func<string, string, int, (string Role, string Vendor, string Name)?>? ProbeOverride;   // key: none; ceiling: one value; thread: set by a test on its own thread

    private static ((string Role, string Vendor, string Name)? Result, string? Refusal) ProbeWithRefusal(string path, string sha256,
        string dependencies, int timeoutMs)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return (null, ProbeRefusal.CannotIdentifyText);
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--plugin-info");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(sha256);   // the approved hash: the probe refuses to load anything else
            start.ArgumentList.Add(dependencies.Length == 0 ? "none" : dependencies);   // the approved local DLL closure
            using var process = Process.Start(start);
            if (process is null) return (null, ProbeRefusal.HelperCrashText);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch (InvalidOperationException ex) { Services.Trace.Error(Services.Trace.Engine, "plug-in scan: stop timed-out probe: " + ex.Message); }
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                return (null, ProbeRefusal.ForTimeout(timeoutMs));
            }
            var outText = stdout.Wait(2000) ? stdout.Result ?? "" : "";
            var errText = stderr.Wait(2000) ? stderr.Result ?? "" : "";
            var line = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var parts = line.Split('|');
            if (process.ExitCode == 0 && parts.Length >= 3 && parts[0] is ("Instrument" or "Effect"))
                return ((parts[0], parts[1].Length > 256 ? parts[1][..256] : parts[1], parts[2].Length > 256 ? parts[2][..256] : parts[2]), null);
            return (null, ProbeRefusal.Classify(errText, process.ExitCode, path));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Services.Trace.Error(Services.Trace.Engine, "plug-in probe failed to start: " + ex.Message);
            return (null, ProbeRefusal.HelperCrashText);
        }
    }

    private static (string Role, string Vendor, string Name)? Probe(string path, string sha256, int timeoutMs)
        => ProbeWithRefusal(path, sha256, "", timeoutMs).Result;
}
