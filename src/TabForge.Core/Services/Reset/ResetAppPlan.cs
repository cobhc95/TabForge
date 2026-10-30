using System.IO;
using System.Text.Json.Nodes;
using TabForge.Platform;

namespace TabForge.Services.Reset;

// Owns: building the data-only wipe plan: one item per owner tick, each with its concrete paths and cheap counts.
// Does not own: deleting (ResetAppWipe), the backup zip (ResetAppBackup) or the dialog (ResetAppWindow).
// Tests: ResetAppPlanTests, ResetAppStoreCoverageTests.
/// <summary>Every path "Reset entire app" can touch, grouped by the owner's eight ticks.</summary>
public sealed class ResetAppPlan
{
    public const string SettingsFileName = "settings.json";
    public const string CrashLogFileName = "avalonia-crash.log";
    public const string LockPattern = "TabForge.Avalonia.*.lock";

    public ResetAppPlan(IAppFolders folders, IReadOnlyList<ResetAppItem> items)
    {
        Folders = folders;
        Items = items;
        Roots = RootsOf(folders);
    }

    public IAppFolders Folders { get; }
    public IReadOnlyList<ResetAppItem> Items { get; }
    /// <summary>The app folders a path must be inside before the wipe may delete it.</summary>
    public IReadOnlyList<string> Roots { get; }

    public IEnumerable<ResetAppTarget> Targets => Items.SelectMany(item => item.Targets);

    public string SettingsPath => Path.Combine(Folders.Settings, SettingsFileName);

    /// <summary>Builds the whole plan, or only <paramref name="only"/> (the confirmed ticks), with counts read now.</summary>
    public static ResetAppPlan Create(IAppFolders folders, IEnumerable<ResetAppItemKind>? only = null)
    {
        var wanted = only is null ? null : only.ToHashSet();
        var settingsRoot = ResetAppSettingsFile.TryRead(Path.Combine(folders.Settings, SettingsFileName));
        var items = ResetAppItemCatalog.All
            .Where(kind => wanted is null || wanted.Contains(kind))
            .Select(kind => BuildItem(folders, kind, settingsRoot))
            .ToList();
        return new ResetAppPlan(folders, items);
    }

    /// <summary>The app roots that contain the stores (empty ones dropped, duplicates removed).</summary>
    public static IReadOnlyList<string> RootsOf(IAppFolders folders) =>
        new[] { folders.Settings, folders.Data, folders.Cache, folders.Logs, folders.PluginCache, folders.SoundFonts }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(Platform.PathComparer.Default)
            .ToList();

    private static ResetAppItem BuildItem(IAppFolders folders, ResetAppItemKind kind, JsonObject? settings)
    {
        var settingsPath = Path.Combine(folders.Settings, SettingsFileName);
        var targets = TargetsFor(folders, kind, settingsPath);
        var item = new ResetAppItem(kind, targets);
        var (files, bytes) = CountFiles(targets);
        item.FileCount = files;
        item.Bytes = bytes;
        item.ValueCount = CountValues(settings, kind);
        return item;
    }

    private static IReadOnlyList<ResetAppTarget> TargetsFor(IAppFolders folders, ResetAppItemKind kind, string settingsPath) => kind switch
    {
        ResetAppItemKind.Settings => new[] { SettingsTarget(settingsPath, ResetAppSettingsGroup.Settings) },
        ResetAppItemKind.Layouts => new[] { SettingsTarget(settingsPath, ResetAppSettingsGroup.Layouts) },
        ResetAppItemKind.Recents => new[] { SettingsTarget(settingsPath, ResetAppSettingsGroup.Recents) },
        ResetAppItemKind.PluginLibrary => new[] { SettingsTarget(settingsPath, ResetAppSettingsGroup.PluginLibrary) },
        ResetAppItemKind.Presets => new[]
        {
            Folder(Path.Combine(folders.Settings, "Presets")),
            Folder(Path.Combine(folders.Settings, "FxChains")),
            Folder(Path.Combine(folders.Settings, "Containers")),
            Folder(Path.Combine(folders.Settings, "Articulations")),
            Folder(Path.Combine(folders.Settings, "DrumMaps")),
            Folder(Path.Combine(folders.Settings, "Templates")),
            Folder(Path.Combine(folders.Settings, "MidiPresets")),
            Folder(Path.Combine(folders.Settings, "ChainStates")),
            SettingsTarget(settingsPath, ResetAppSettingsGroup.Presets),
        },
        ResetAppItemKind.Autosave => new[] { Folder(Path.Combine(folders.Data, "Recovery")) },
        ResetAppItemKind.SoundFonts => new[] { Folder(folders.SoundFonts) },
        ResetAppItemKind.LogsAndCaches => LogsTargets(folders),
        _ => Array.Empty<ResetAppTarget>(),
    };

    private static IReadOnlyList<ResetAppTarget> LogsTargets(IAppFolders folders)
    {
        var targets = new List<ResetAppTarget>
        {
            Folder(folders.Logs),
            FileTarget(Path.Combine(folders.Settings, CrashLogFileName)),
        };
        // On Windows Cache is Data, which also holds Recovery and SoundFonts: only separate cache roots are wiped whole.
        if (!Same(folders.Cache, folders.Data) && !Same(folders.Cache, folders.Settings)) targets.Add(Folder(folders.Cache));
        foreach (var lockFile in LockFiles(folders.Data)) targets.Add(FileTarget(lockFile));
        return targets;
    }

    private static IEnumerable<string> LockFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            return Directory.EnumerateFiles(folder, LockPattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static int CountValues(JsonObject? settings, ResetAppItemKind kind)
    {
        if (settings is null) return 0;
        return kind switch
        {
            ResetAppItemKind.Settings => ResetAppSettingsFile.CountSettings(settings),
            ResetAppItemKind.Layouts => ResetAppSettingsFile.CountGroup(settings, ResetAppSettingsGroup.Layouts),
            ResetAppItemKind.Presets => ResetAppSettingsFile.CountGroup(settings, ResetAppSettingsGroup.Presets),
            ResetAppItemKind.PluginLibrary => ResetAppSettingsFile.CountGroup(settings, ResetAppSettingsGroup.PluginLibrary),
            ResetAppItemKind.Recents => ResetAppSettingsFile.CountGroup(settings, ResetAppSettingsGroup.Recents),
            _ => 0,
        };
    }

    private static (int Files, long Bytes) CountFiles(IReadOnlyList<ResetAppTarget> targets)
    {
        var files = 0;
        long bytes = 0;
        foreach (var target in targets)
        {
            if (target.Kind == ResetAppTargetKind.SettingsJson) continue;
            if (target.Kind == ResetAppTargetKind.File)
            {
                if (TrySize(target.Path) is { } size) { files++; bytes += size; }
                continue;
            }
            CountFolder(target.Path, ref files, ref bytes);
        }
        return (files, bytes);
    }

    private static void CountFolder(string folder, ref int files, ref long bytes)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;   // never follow a link out of the app folders
                if ((attributes & FileAttributes.Directory) != 0) CountFolder(entry, ref files, ref bytes);
                else if (TrySize(entry) is { } size) { files++; bytes += size; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { } // swallow: an unreadable folder shows a zero count
    }

    private static long? TrySize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    private static ResetAppTarget Folder(string path) => new(path, ResetAppTargetKind.Folder);
    private static ResetAppTarget FileTarget(string path) => new(path, ResetAppTargetKind.File);
    private static ResetAppTarget SettingsTarget(string path, ResetAppSettingsGroup group) => new(path, ResetAppTargetKind.SettingsJson, group);
    private static bool Same(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), Platform.PathComparer.Comparison);
}
