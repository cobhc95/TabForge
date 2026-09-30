using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>Where <see cref="NightPluginApproval"/> looks (read-only). The defaults are the usual install folders; tests pass their own.</summary>
public sealed record NightRoots(string[] Vst2Folders, string[] Vst3Folders, string[] ReaperInstalls, string[] CrashTestSearchStarts)
{
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
/// Audit 5 hook H-5. <c>--profile &lt;folder&gt; --approve-night-plugins</c> pre-approves, in that profile's plug-in trust store only, the plug-ins a
/// night run may use: Nexus, Superior Drummer 3, REAPER's bundled FX (Plugins\FX\*.dll of the REAPER install) and TabForge's crash-test DLL
/// (native\crashtest\bin). Each is approved by exact path through <see cref="PluginTrust.Approve"/> (path plus hash where the location is
/// user-writable) and nothing else is touched. Without <c>--profile</c> the flag is refused: the user's real approvals are never changed.
/// </summary>
public static class NightPluginApproval
{
    public const string Switch = "--approve-night-plugins";
    public const string CrashTestDll = "TabForgeCrashTest.dll";

    public enum Outcome { NotRequested, Refused, Approved }

    public static bool Requested(IEnumerable<string> args) => args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

    /// <summary>The flag removed from <paramref name="args"/>.</summary>
    public static string[] Without(string[] args) => args.Where(a => !string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>
    /// Approves the night plug-ins in <paramref name="settings"/> when the flag is present AND a profile is active; otherwise nothing changes.
    /// <paramref name="approved"/> lists the paths added (or confirmed) in that call.
    /// </summary>
    public static Outcome Apply(IEnumerable<string> args, PluginSettings settings, out List<string> approved, NightRoots? roots = null)
    {
        approved = new List<string>();
        if (!Requested(args)) return Outcome.NotRequested;
        if (!UserPaths.IsProfile) return Outcome.Refused;   // never touches the real %APPDATA% approvals
        foreach (var path in Find(roots ?? NightRoots.Default()))
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new List<string>(); }
    }
}
