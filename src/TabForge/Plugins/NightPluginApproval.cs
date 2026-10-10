using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>Where <see cref="NightPluginApproval"/> looks (read-only). The defaults are the usual install folders; tests pass their own.</summary>
public sealed record NightRoots(string[] Vst2Folders, string[] Vst3Folders, string[] ReaperInstalls, string[] CrashTestSearchStarts, string[]? AllScanRoots = null)
{
    /// <summary>The folders the normal scanner reads for "all" (standard VST2/VST3 folders); tests pass their own.</summary>
    public string[] ScanRoots() => AllScanRoots ?? VstScannerService.StandardFolders.Concat(VstScannerService.ExpandFolders(VstScannerService.DefaultCommonFolders))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static NightRoots Default()
    {
        static IEnumerable<string> Bases(params Environment.SpecialFolder[] folders) =>
            folders.Select(Environment.GetFolderPath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        var programs = Bases(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86).ToArray();
        var common = Bases(Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86).ToArray();
        return new NightRoots(
            programs.SelectMany(p => new[] { Path.Combine(p, "VSTPlugins"), Path.Combine(p, "Steinberg", "VSTPlugins") }).ToArray(),
            common.Select(p => Path.Combine(p, "VST3")).ToArray(),
            programs.SelectMany(p => new[] { Path.Combine(p, "REAPER (x64)"), Path.Combine(p, "REAPER") }).ToArray(),
            new[] { AppContext.BaseDirectory, Environment.CurrentDirectory });
    }
}

/// <summary>
/// <c>--profile &lt;folder&gt; --approve-night-plugins</c> pre-approves, in that profile's plug-in trust store only, the plug-ins an
/// unattended stress run may use: Nexus, Superior Drummer 3, FabFilter, REAPER's bundled FX (Plugins\FX\*.dll of the REAPER install) and TabForge's crash-test DLL
/// (native\crashtest\bin). Each is approved by exact path through <see cref="PluginTrust.Approve"/> (path plus hash where the location is
/// user-writable) and nothing else is touched. With <c>all</c> (<c>--approve-night-plugins all</c>) it approves every plug-in the normal scanner finds in the standard VST2/VST3 folders instead
/// (for stress testing), by the same rules. Without <c>--profile</c> the flag is refused either way: the user's real approvals are never changed.
/// </summary>
public static class NightPluginApproval
{
    public const string Switch = "--approve-night-plugins";
    public const string CrashTestDll = "TabForgeCrashTest.dll";

    public enum Outcome { NotRequested, Refused, Approved }

    public static bool Requested(IEnumerable<string> args) => args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for <c>--approve-night-plugins all</c>: every plug-in the normal scanner finds, instead of the named list.</summary>
    public static bool AllRequested(IEnumerable<string> args)
    {
        var list = args.ToList();
        var i = list.FindIndex(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < list.Count && string.Equals(list[i + 1], "all", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The flag (and its optional <c>all</c>) removed from <paramref name="args"/>.</summary>
    public static string[] Without(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], Switch, StringComparison.OrdinalIgnoreCase)) { result.Add(args[i]); continue; }
            if (i + 1 < args.Length && string.Equals(args[i + 1], "all", StringComparison.OrdinalIgnoreCase)) i++;
        }
        return result.ToArray();
    }

    /// <summary>
    /// Approves the night plug-ins in <paramref name="settings"/> when the flag is present AND a profile is active; otherwise nothing changes.
    /// <paramref name="approved"/> lists the paths added (or confirmed) in that call.
    /// </summary>
    public static Outcome Apply(IEnumerable<string> args, PluginSettings settings, out List<string> approved, NightRoots? roots = null)
    {
        approved = new List<string>();
        if (!Requested(args)) return Outcome.NotRequested;
        if (!UserPaths.IsProfile || UserPaths.ProfileIsRealUserFolder) return Outcome.Refused;   // never touches the real %APPDATA% approvals
        roots ??= NightRoots.Default();
        var paths = Find(roots);
        if (AllRequested(args))   // every installed plug-in, still only inside a profile (checked above)
            foreach (var plugin in VstScannerService.Scan(roots.ScanRoots(), CancellationToken.None, null, settings))
                if (!paths.Contains(plugin.Path, StringComparer.OrdinalIgnoreCase)) paths.Add(plugin.Path);
        foreach (var path in paths)
        {
            PluginTrust.Approve(settings, path);
            approved.Add(PluginTrust.Normalize(path));
        }
        return Outcome.Approved;
    }

    /// <summary>The exact plug-in paths that exist on this machine (read-only search of the given folders).</summary>
    public static List<string> Find(NightRoots roots)
    {
        var found = new List<string>();
        void Add(string? path) { if (path is not null && !found.Contains(path, StringComparer.OrdinalIgnoreCase)) found.Add(path); }

        // Nexus and Superior Drummer 3: the exact file names, in the VST2 folders (up to one vendor folder deep) and as VST3.
        foreach (var name in new[] { "Nexus.dll", "Superior Drummer 3.dll" })
            foreach (var folder in roots.Vst2Folders) Add(FindNamed(folder, name, file: true));
        foreach (var name in new[] { "Nexus.vst3", "Superior Drummer 3.vst3" })
            foreach (var folder in roots.Vst3Folders) Add(FindNamed(folder, name, file: null));

        // FabFilter (heavyweight test effects): "FabFilter *.vst3" in the VST3 folders, directly or one vendor folder down.
        foreach (var folder in roots.Vst3Folders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                var places = new[] { folder }.Concat(Directory.EnumerateDirectories(folder).Where(d => !d.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)));
                foreach (var place in places)
                    foreach (var entry in Directory.EnumerateFileSystemEntries(place, "FabFilter *.vst3"))
                        Add(entry);
            }
            catch (IOException ex) { Services.Trace.Error(Services.Trace.Engine, "plug-in approval: scan folder: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Services.Trace.Error(Services.Trace.Engine, "plug-in approval: scan folder: " + ex.Message); }
        }

        // REAPER's bundled FX: the VST DLLs in Plugins\FX of the install (not the reaper_*.dll extensions, not ReaPlugs).
        foreach (var install in roots.ReaperInstalls)
            foreach (var fx in new[] { Path.Combine(install, "Plugins", "FX"), Path.Combine(install, "reaper_plugins", "FX") })
                foreach (var dll in SafeFiles(fx)) Add(dll);

        // The crash-test DLL of this checkout, looked for upwards from the app / working folder.
        foreach (var start in roots.CrashTestSearchStarts)
        {
            var dir = string.IsNullOrWhiteSpace(start) ? null : new DirectoryInfo(start);
            for (var up = 0; dir is not null && up < 8; up++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "native", "crashtest", "bin", CrashTestDll);
                if (File.Exists(candidate)) { Add(candidate); break; }
            }
        }
        return found;
    }

    /// <summary>An exact-named plug-in directly in <paramref name="folder"/> or one sub-folder down; <paramref name="file"/> true = file, null = file or bundle folder.</summary>
    private static string? FindNamed(string folder, string name, bool? file)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            var direct = Path.Combine(folder, name);
            if (File.Exists(direct) || (file is null && Directory.Exists(direct))) return direct;
            foreach (var sub in Directory.EnumerateDirectories(folder))
            {
                var nested = Path.Combine(sub, name);
                if (File.Exists(nested) || (file is null && Directory.Exists(nested))) return nested;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Not logged: optional file probe in a loop.
        return null;
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Engine, "night approval: list folder: " + ex.Message); return new List<string>(); }
    }
}
