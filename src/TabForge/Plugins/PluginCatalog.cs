using System.Diagnostics;
using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>
/// Knows what each plug-in is: its vendor, and whether it is an instrument or an effect. VST3 bundles say so in
/// their moduleinfo.json; anything else is probed once in a throwaway process (TabForge.exe --plugin-info, so a
/// plug-in that crashes cannot affect TabForge). Results are remembered in settings.
/// </summary>
public static class PluginCatalog
{
    /// <summary>The detected role ("Instrument" / "Effect") and vendor; probes the plug-in when not known yet.</summary>
    public static (string Role, string Vendor, string Name) Describe(string path, PluginSettings settings, int timeoutMs = 15000)
    {
        KnownPlugin? known;
        lock (ProbedLock) known = settings.Probed.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        if (known is { Role.Length: > 0 }) return (known.Role, known.Vendor, known.Name);
        if (path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
        {
            var (vendor, role) = VstScannerService.Vst3ModuleInfo(path);
            if (role.Length > 0) return Remember(settings, path, role, vendor, "");
        }
        var probed = Probe(path, timeoutMs);
        if (probed is { } found) return Remember(settings, path, found.Role, found.Vendor, found.Name);
        // Could not be probed (crashed or timed out): guess from the name, never remembered.
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var instrumentWords = new[] { "synth", "piano", "drum", "nexus", "kontakt", "sampler", "organ", "strings", "keys", "superior", "ezdrummer", "ezkeys", "ezbass" };
        return (instrumentWords.Any(name.Contains) ? "Instrument" : "Effect", "", "");
    }

    /// <summary>Identification runs several probes at once: the remembered list is shared.</summary>
    private static readonly object ProbedLock = new();

    private static (string Role, string Vendor, string Name) Remember(PluginSettings settings, string path, string role, string vendor, string name)
    {
        lock (ProbedLock)
        {
        settings.Probed.RemoveAll(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        settings.Probed.Add(new KnownPlugin { Path = path, Role = role, Vendor = vendor, Name = name, Format = path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? "VST3" : "VST2" });
        }
        return (role, vendor, name);
    }

    private static (string Role, string Vendor, string Name)? Probe(string path, int timeoutMs)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return null;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--plugin-info");
            start.ArgumentList.Add(path);
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadLineAsync();
            if (!process.WaitForExit(timeoutMs)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            if (process.ExitCode != 0 || !output.Wait(1000) || output.Result is not { } line) return null;
            var parts = line.Split('|');
            if (parts.Length < 3 || parts[0] is not ("Instrument" or "Effect")) return null;
            return (parts[0], parts[1].Length > 256 ? parts[1][..256] : parts[1], parts[2].Length > 256 ? parts[2][..256] : parts[2]);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
}
