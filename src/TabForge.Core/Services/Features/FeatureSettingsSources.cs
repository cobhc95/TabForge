namespace TabForge.Services.Features;

/// <summary>A Preferences group inserted on <see cref="Page"/> after the last entry of group <see cref="AfterGroup"/>; <see cref="Keys"/> as in SettingsCatalog's layout.</summary>
public sealed record FeatureLayout(string Page, string Group, string Keys, string AfterGroup);

/// <summary>One feature's rows for the settings catalogue. Every member has a no-op default.</summary>
public interface IFeatureSettingsSource
{
    string Name { get; }
    IReadOnlyList<FeatureLayout> Layout => Array.Empty<FeatureLayout>();
    IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => Array.Empty<SettingDescriptor>();
    /// <summary>Clamps the feature's saved values to safe bounds (called from SettingsValidator through CatalogComposition.NormalizeSettings).</summary>
    void Normalize(AppSettings settings) { }
}

/// <summary>The app calls the settings rows make: theme preset, audio device and ASIO channel names (driver; null = still being asked, none = unknown; device lists: null = still being listed, then DeviceNamesSettled(driver; empty = the recording devices) completes when they are in), standard VST folders, the sample rates of an output (driver, device; none = unknown, null = still being asked, then SampleRatesSettled completes when the answer is in) and its preferred buffer size (0 = unknown).</summary>
public sealed record CatalogPlatform(
    Action<AppearanceSettings, string> ApplyThemePreset,
    Func<string, (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)?> AsioChannelNames,
    Func<IReadOnlyList<string>?> InputDeviceNames,
    Func<string, IReadOnlyList<string>?> OutputDeviceNames,
    IReadOnlyList<string> DefaultVstFolders,
    Func<string, string, IReadOnlyList<int>?> SupportedSampleRates,
    Func<string, string, Task> SampleRatesSettled,
    Func<string, string, int> PreferredBufferSize,
    Func<string, Task> DeviceNamesSettled);

// Owns: the plain data a feature adds to the settings catalogue (IFeatureSettingsSource), the app-side
//   calls those catalogues make (CatalogPlatform), and the one composition of both (CatalogComposition).
// Does not own: the catalogues (SettingsCatalog, HotkeyCatalog; feature hotkey rows are in HotkeyCatalog.Features.cs), the feature modules (app FeatureRegistry), the device or theme code.
// Tests: no dedicated test class.
/// <summary>The feature sources and platform calls the catalogues read, installed once by the composition root.</summary>
public static class CatalogComposition
{
    // Key: none (one value per process). Ceiling: one. Thread: written once at start-up before any catalogue use, then read-only.
    private static (IReadOnlyList<IFeatureSettingsSource> Sources, CatalogPlatform Platform)? _installed;

    /// <summary>Installs the sources and platform calls; a second install with other values throws.</summary>
    public static void Install(IReadOnlyList<IFeatureSettingsSource> sources, CatalogPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(platform);
        if (_installed is { } done && (done.Sources != sources || done.Platform != platform))
            throw new InvalidOperationException("The catalogues are already composed.");
        _installed = (sources, platform);
    }

    private static (IReadOnlyList<IFeatureSettingsSource> Sources, CatalogPlatform Platform) Installed =>
        _installed ?? throw new InvalidOperationException("CatalogComposition.Install must run before the settings or hotkey catalogue is used.");

    public static CatalogPlatform Platform => Installed.Platform;

    /// <summary>Runs every source's settings bounds.</summary>
    public static void NormalizeSettings(AppSettings settings)
    {
        foreach (var source in Installed.Sources) source.Normalize(settings);
    }

    /// <summary>The settings rows of every source.</summary>
    public static IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => Installed.Sources.SelectMany(m => m.SettingRows(settings));

    /// <summary>The layout table with every source group inserted after its anchor group.</summary>
    public static (string Page, string Group, string Keys)[] MergeLayout(IReadOnlyList<(string Page, string Group, string Keys)> layout)
    {
        var list = layout.ToList();
        foreach (var source in Installed.Sources)
            foreach (var g in source.Layout)
            {
                var at = list.FindLastIndex(e => e.Page == g.Page && e.Group == g.AfterGroup);
                if (at < 0) throw new InvalidOperationException($"Feature '{source.Name}': layout anchor '{g.AfterGroup}' not found.");
                list.Insert(at + 1, (g.Page, g.Group, g.Keys));
            }
        return list.ToArray();
    }
}
