using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using TabForge.Platform;
using TabForge.Services.Reset;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Reset entire app: the plan covers every persisted store, unticked stores stay, outside paths are refused, the typed word
/// gates the second step, counts drive the button, the backup zip holds the ticked items and Presets-only leaves settings.
/// Tests: this class.
/// </summary>
public sealed class ResetAppPlanTests : IDisposable
{
    private readonly TempFolders _folders = new();

    public void Dispose() => _folders.Dispose();

    [Fact]
    public void PlanCoversEveryPersistedStore()
    {
        SeedAllStores(_folders);
        var plan = ResetAppPlan.Create(_folders);
        var targets = plan.Targets.Select(target => Path.GetFullPath(target.Path)).ToList();

        Assert.Contains(Path.Combine(_folders.Settings, "settings.json"), targets);
        Assert.Contains(new[] { ResetAppSettingsGroup.Settings, ResetAppSettingsGroup.Layouts, ResetAppSettingsGroup.Presets, ResetAppSettingsGroup.PluginLibrary, ResetAppSettingsGroup.Recents },
            group => plan.Targets.Any(target => target.SettingsGroup == group));
        foreach (var folder in new[] { "Presets", "FxChains", "Containers", "Articulations", "DrumMaps", "Templates", "MidiPresets", "ChainStates" })
            Assert.Contains(Path.Combine(_folders.Settings, folder), targets);
        Assert.Contains(Path.Combine(_folders.Data, "Recovery"), targets);
        Assert.Contains(_folders.SoundFonts, targets);
        Assert.Contains(_folders.Logs, targets);
        Assert.Contains(Path.Combine(_folders.Settings, "avalonia-crash.log"), targets);
        Assert.Contains(Path.Combine(_folders.Data, "TabForge.Avalonia.abc.lock"), targets);
        Assert.Contains(_folders.Cache, targets);
        Assert.Equal(8, plan.Items.Count);
    }

    [Fact]
    public void DryRunCountsButLeavesEverything()
    {
        SeedAllStores(_folders);
        var plan = ResetAppPlan.Create(_folders);
        var result = ResetAppWipe.Run(plan, dryRun: true);

        Assert.True(result.Files > 0 || result.Values > 0);
        Assert.True(File.Exists(Path.Combine(_folders.Settings, "settings.json")));
        Assert.True(File.Exists(Path.Combine(_folders.Settings, "Presets", "Kit", "a.tfpreset")));
        Assert.True(File.Exists(Path.Combine(_folders.Data, "SoundFonts", "GeneralUser-GS.sf2")));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PresetsOnlyLeavesSettingsAndOtherStores()
    {
        SeedAllStores(_folders);
        var plan = ResetAppPlan.Create(_folders, new[] { ResetAppItemKind.Presets });
        var result = ResetAppWipe.Run(plan);

        Assert.False(Directory.Exists(Path.Combine(_folders.Settings, "Presets")));
        Assert.False(Directory.Exists(Path.Combine(_folders.Settings, "ChainStates")));
        Assert.True(File.Exists(Path.Combine(_folders.Data, "SoundFonts", "GeneralUser-GS.sf2")));
        Assert.True(File.Exists(Path.Combine(_folders.Settings, "settings.json")));
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(_folders.Settings, "settings.json")))!.AsObject();
        Assert.False(json.ContainsKey("EffectPresets"));
        Assert.True(json.ContainsKey("Workspace"));
        Assert.True(json.ContainsKey("General"));
        Assert.True(json["Plugins"]!["ApprovedPluginPaths"] is JsonArray);
        Assert.True(result.Values > 0);
    }

    [Fact]
    public void SettingsOnlyResetsSettingsAndLeavesEveryOtherTickedOffGroup()
    {
        SeedAllStores(_folders);
        var plan = ResetAppPlan.Create(_folders, new[] { ResetAppItemKind.Settings });
        ResetAppWipe.Run(plan);

        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(_folders.Settings, "settings.json")))!.AsObject();
        Assert.False(json.ContainsKey("Hotkeys"));
        Assert.True(json.ContainsKey("Workspace"));
        Assert.True(json.ContainsKey("EffectPresets"));
        Assert.True(json["Plugins"]!["ApprovedPluginPaths"] is JsonArray);
        Assert.True(json.ContainsKey("RememberedFolders"));
        Assert.True(json.ContainsKey("MidiExport"));
        Assert.True(json.ContainsKey("Video"));
        Assert.True(json["Render"]!["Format"] is not null);
        Assert.Null(json["Render"]!["SafetyLimiter"]);   // the one Render field that is a Settings row
    }

    [Fact]
    public void PathsOutsideTheAppRootsAreRefused()
    {
        var outside = Path.Combine(_folders.Documents, "my-song.tforge");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "song");
        var plan = new ResetAppPlan(_folders, new[]
        {
            new ResetAppItem(ResetAppItemKind.Presets, new[] { new ResetAppTarget(outside, ResetAppTargetKind.File) }),
        });

        Assert.False(ResetAppWipe.IsInsideAnyRoot(outside, plan.Roots));
        var result = ResetAppWipe.Run(plan);
        Assert.Contains(outside, result.Refused);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void TypedResetIsRequiredExactly()
    {
        Assert.True(ResetAppPrompt.CanWipe("RESET"));
        Assert.False(ResetAppPrompt.CanWipe("reset"));
        Assert.False(ResetAppPrompt.CanWipe("RESET "));
        Assert.False(ResetAppPrompt.CanWipe(""));
        Assert.False(ResetAppPrompt.CanWipe(null));
    }

    [Fact]
    public void SelectionStartsFullAndTheButtonCountsToZero()
    {
        var selection = new ResetAppSelection(ResetAppPlan.Create(_folders));
        Assert.Equal(8, selection.CheckedCount);
        Assert.True(selection.CanWipe);
        Assert.Equal("Wipe 8 items", selection.WipeText);
        Assert.False(selection.BackUpFirst);

        selection.SelectNone();
        Assert.Equal(0, selection.CheckedCount);
        Assert.False(selection.CanWipe);
        Assert.Equal("Wipe 0 items", selection.WipeText);

        selection.SelectAll();
        Assert.Equal(8, selection.CheckedCount);
        Assert.True(selection.CanWipe);
    }

    [Fact]
    public void BackupZipHoldsTheTickedItemsOnly()
    {
        SeedAllStores(_folders);
        var plan = ResetAppPlan.Create(_folders, new[] { ResetAppItemKind.Presets });
        var result = ResetAppBackup.Write(plan, _folders.Documents, new DateTime(2026, 10, 29, 12, 0, 0));

        Assert.True(result.Ok);
        using var zip = ZipFile.OpenRead(result.Path!);
        var names = zip.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains(names, name => name.Contains("Presets/Kit/a.tfpreset", StringComparison.Ordinal));
        Assert.Contains(names, name => name.EndsWith("settings.json", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("SoundFonts", StringComparison.Ordinal));
        Assert.Contains("TabForge-backups", result.Path!);
    }

    [Fact]
    public void PendingMarkerRunsAtTheNextStartAndIsConsumed()
    {
        SeedAllStores(_folders);
        PendingAppWipe.Write(_folders, new[] { ResetAppItemKind.Presets }, backupTaken: false);
        var marker = PendingAppWipe.MarkerPath(_folders);
        Assert.True(File.Exists(marker));

        Assert.True(PendingAppWipe.TryRun(_folders));
        Assert.False(File.Exists(marker));
        Assert.False(Directory.Exists(Path.Combine(_folders.Settings, "Presets")));
        Assert.True(File.Exists(Path.Combine(_folders.Settings, "settings.json")));
        Assert.False(PendingAppWipe.TryRun(_folders));
    }

    internal static void SeedAllStores(TempFolders folders)
    {
        Write(folders.Settings, "settings.json", """
        {
          "General": { "Autosave": 2 },
          "Workspace": { "ClosedPanels": ["mixer"] },
          "WorkspaceBeforeSideHide": { "ClosedPanels": [] },
          "SavedLayouts": { "Mine": { "ClosedPanels": [] } },
          "LastLayout": "Mine",
          "EffectPresets": [ { "Kind": "Bend", "Name": "Mine" } ],
          "Plugins": {
            "ApprovedPluginPaths": [ "C:\\\\VST\\\\a.dll" ],
            "TrustRecords": [ { "Path": "C:\\\\VST\\\\a.dll", "Sha256": "abc" } ],
            "ScanCache": [ { "Name": "A", "Path": "C:\\\\VST\\\\a.dll" } ],
            "Probed": [], "Quarantined": [], "Folders": [ "C:\\\\VST" ],
            "RecentImpulseFiles": [ "C:\\\\IR\\\\a.wav" ]
          },
          "RememberedFolders": { "file.open": "C:\\\\Songs" },
          "Render": { "Format": "wav", "SafetyLimiter": true },
          "MidiExport": { "Format": "mid" },
          "Video": { "Format": "mp4" },
          "Hotkeys": { "Preset": "TabForge" }
        }
        """);
        Write(folders.Settings, "Presets/Kit/a.tfpreset", "{}");
        Write(folders.Settings, "Presets/Kit/preset-order.txt", "a");
        Write(folders.Settings, "FxChains/a.tfchain", "[]");
        Write(folders.Settings, "Containers/a.tfcontainer", "{}");
        Write(folders.Settings, "Articulations/a.json", "{}");
        Write(folders.Settings, "DrumMaps/a.json", "{}");
        Write(folders.Settings, "Templates/a.tforge", "{}");
        Write(folders.Settings, "MidiPresets/a.json", "{}");
        Write(folders.Settings, "MidiPresets/Processors/Type/a.json", "{}");
        Write(folders.Settings, "ChainStates/a.state", "state");
        Write(folders.Settings, "avalonia-crash.log", "crash");
        Write(folders.Data, "Recovery/autosave-1-0123456789abcdef0123456789abcdef-Song.tforge", "song");
        Write(folders.Data, "Recovery/autosave-1-0123456789abcdef0123456789abcdef-Song.errors.txt", "error");
        Write(folders.Data, "SoundFonts/GeneralUser-GS.sf2", "sf2");
        Write(folders.Data, "TabForge.Avalonia.abc.lock", "lock");
        Write(folders.Logs, "errors.log", "error");
        Write(folders.Cache, "cache.bin", "cache");
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

/// <summary>A throwaway set of app folders; Documents is outside every app root on purpose.</summary>
internal sealed class TempFolders : IAppFolders, IDisposable
{
    public TempFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "tabforge-reset-app-" + Guid.NewGuid().ToString("N"));
        Root = root;
        Settings = Path.Combine(root, "Settings");
        Data = Path.Combine(root, "Data");
        Cache = Path.Combine(root, "Cache");
        Logs = Path.Combine(Data, "Diagnostics");
        Temp = Path.Combine(root, "Temp");
        Runtime = Temp;
        PluginCache = Settings;
        SoundFonts = Path.Combine(Data, "SoundFonts");
        Music = Path.Combine(root, "Music");
        Videos = Path.Combine(root, "Videos");
        Documents = Path.Combine(root, "Documents");
        ReaperResources = Path.Combine(root, "REAPER");
        UserPluginRoot = Path.Combine(root, "Common");
        foreach (var folder in new[] { Settings, Data, Cache, Logs, Temp, SoundFonts, Music, Videos, Documents, ReaperResources, UserPluginRoot })
            Directory.CreateDirectory(folder);
    }

    public string Root { get; }
    public string Settings { get; }
    public string Data { get; }
    public string Cache { get; }
    public string Logs { get; }
    public string Temp { get; }
    public string Runtime { get; }
    public string PluginCache { get; }
    public string SoundFonts { get; }
    public string Music { get; }
    public string Videos { get; }
    public string Documents { get; }
    public string ReaperResources { get; }
    public string UserPluginRoot { get; }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
