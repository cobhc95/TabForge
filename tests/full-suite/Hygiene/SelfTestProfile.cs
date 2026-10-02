using System.IO;
using System.Security.Cryptography;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

/// <summary>Audit 5 H-1: with <c>--profile &lt;folder&gt;</c> every per-user path resolves inside that folder and the real user folders are left byte-identical.</summary>
public static partial class SelfTest
{
    private static string FolderFingerprint(string folder)
    {
        if (!Directory.Exists(folder)) return "absent";
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            all.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file) + "\n"));
            try { all.AppendData(File.ReadAllBytes(file)); }
            catch (IOException) { all.AppendData(System.Text.Encoding.UTF8.GetBytes("locked")); }
        }
        return Convert.ToHexString(all.GetHashAndReset());
    }

    private static void TestProfileLeavesUserFoldersUntouched()
    {
        var realRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabForge");
        var realLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabForge");
        // The self-test's own log lives in the real Diagnostics folder by default, so it is excluded from the before/after comparison.
        string Roaming() => FolderFingerprint(realRoaming);
        string LocalNoDiag()
        {
            if (!Directory.Exists(realLocal)) return "absent";
            var sub = Directory.EnumerateDirectories(realLocal).Where(d => !Path.GetFileName(d).Equals("Diagnostics", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Select(FolderFingerprint);
            return string.Join("|", sub);
        }
        var roamingBefore = Roaming();
        var localBefore = LocalNoDiag();

        var profile = Path.Combine(Path.GetTempPath(), $"tf-profile-{Guid.NewGuid():N}");
        var previous = UserPaths.ProfileRoot;
        var previousRoot = PluginLibrary.RootOverride;
        try
        {
            var rest = UserPaths.ApplyProfileArgument(new[] { "--selftest", "x.log", "--profile", profile });
            PluginLibrary.RootOverride = null;
            Check("--profile is consumed and activates the profile folder",
                rest.SequenceEqual(new[] { "--selftest", "x.log" }) && UserPaths.IsProfile && UserPaths.ProfileRoot == Path.GetFullPath(profile));

            var resolved = new Dictionary<string, string>
            {
                ["settings"] = AppSettingsStore.DefaultPath,
                ["plug-in library (approvals, scan cache)"] = PluginLibrary.Root,
                ["chain states"] = ChainStateStore.Folder,
                ["drum maps"] = DrumMapLibrary.UserFolder,
                ["templates"] = UserTemplates.Folder,
                ["recovery"] = UserPaths.Recovery,
                ["autosave"] = AutosaveService.DefaultFolder,
                ["diagnostics / crash logs"] = FilePathPolicy.DefaultDiagnosticsPath("crash-test.log"),
            };
            var outside = resolved.Where(kv => !Path.GetFullPath(kv.Value).StartsWith(Path.GetFullPath(profile), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key + " -> " + kv.Value).ToList();
            Check("every per-user path resolves inside the profile folder", outside.Count == 0, string.Join("; ", outside));

            // Write through the real code paths.
            var store = AppSettingsStore.Open(AppSettingsStore.DefaultPath, TimeSpan.FromMilliseconds(1));
            store.MarkChanged();
            store.Flush();
            store.Dispose();
            foreach (var path in new[] { Path.Combine(ChainStateStore.Folder, "probe.txt"), Path.Combine(DrumMapLibrary.UserFolder, "probe.txt"),
                         Path.Combine(UserTemplates.Folder, "probe.txt"), Path.Combine(AutosaveService.DefaultFolder, "probe.txt"),
                         FilePathPolicy.DefaultDiagnosticsPath("crash-test.log"), Path.Combine(PluginLibrary.Root, "probe.txt") })
                FilePathPolicy.WriteAtomically(path, s => s.WriteByte(1), createDirectory: true);
            Check("settings and every other per-user file were written inside the profile", File.Exists(AppSettingsStore.DefaultPath) &&
                Directory.EnumerateFiles(profile, "*", SearchOption.AllDirectories).Count() >= 7);

            Check("%APPDATA%\\TabForge is byte-identical after a run with --profile", Roaming() == roamingBefore);
            Check("%LOCALAPPDATA%\\TabForge (except this run's own diagnostics log) is byte-identical after a run with --profile", LocalNoDiag() == localBefore);
        }
        finally
        {
            UserPaths.SetProfile(previous);
            PluginLibrary.RootOverride = previousRoot;
            try { Directory.Delete(profile, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Check("without --profile the paths are the normal per-user folders again",
            !UserPaths.IsProfile == (previous is null) && (previous is not null || AppSettingsStore.DefaultPath == Path.Combine(realRoaming, "settings.json")));
    }
}
