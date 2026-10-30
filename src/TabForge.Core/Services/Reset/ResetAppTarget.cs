namespace TabForge.Services.Reset;

// Owns: the data-only description of one file, folder or settings.json key group the wipe can remove.
// Does not own: building the plan (ResetAppPlan) or deleting anything (ResetAppWipe).
// Tests: ResetAppPlanTests.
public enum ResetAppTargetKind
{
    File,
    Folder,
    SettingsJson,
}

/// <summary>The settings.json key groups; several ticks share the one file and remove only their own keys.</summary>
public enum ResetAppSettingsGroup
{
    Settings,
    Layouts,
    Presets,
    PluginLibrary,
    Recents,
}

/// <summary>One concrete path a tick wipes, plus the key group when the path is settings.json.</summary>
public sealed record ResetAppTarget(string Path, ResetAppTargetKind Kind, ResetAppSettingsGroup? SettingsGroup = null);

/// <summary>One tick: its owner wording, its targets and the cheap counts shown beside it.</summary>
public sealed class ResetAppItem
{
    public ResetAppItem(ResetAppItemKind kind, IReadOnlyList<ResetAppTarget> targets, int fileCount = 0, long bytes = 0, int valueCount = 0)
    {
        Kind = kind;
        Title = ResetAppItemCatalog.Title(kind);
        Description = ResetAppItemCatalog.Description(kind);
        Targets = targets;
        FileCount = fileCount;
        Bytes = bytes;
        ValueCount = valueCount;
    }

    public ResetAppItemKind Kind { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<ResetAppTarget> Targets { get; }
    public int FileCount { get; internal set; }
    public long Bytes { get; internal set; }
    public int ValueCount { get; internal set; }

    /// <summary>The count/size line under the tick: files and/or values, or that there is nothing saved.</summary>
    public string CountText
    {
        get
        {
            var parts = new List<string>();
            if (FileCount > 0) parts.Add(FileCount == 1 ? "1 file" : $"{FileCount} files");
            if (Bytes > 0) parts.Add(FormatBytes(Bytes));
            if (ValueCount > 0) parts.Add(ValueCount == 1 ? "1 saved value" : $"{ValueCount} saved values");
            return parts.Count == 0 ? "Nothing saved" : string.Join(", ", parts);
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}
