using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the remembered dialog folders: two keys keep their own folders, each kind has its default, a gone folder falls back, and the folders survive a
/// settings save and load (a bad entry is dropped on load).
/// Tests: DialogFolders, DialogFolderMemory, DialogKeys.
/// </summary>
public class DialogFoldersTests
{
    private const string Documents = @"C:\Users\Test\Documents";
    private static readonly IAppFolders Folders = AppFolders.Current;

    private static string Scratch(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "TabForgeDialogFolders", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string? StartOf(IReadOnlyDictionary<string, string> remembered, string key, string? fallback = null, Func<string, bool>? exists = null) =>
        DialogFolders.StartFolder(remembered, key, fallback, exists ?? Directory.Exists, Folders, Documents);

    [Fact]
    public void TwoKeysKeepSeparateFolders()
    {
        var songs = Scratch("songs");
        var chains = Scratch("chains");
        var remembered = new Dictionary<string, string>();

        Assert.True(DialogFolders.Remember(remembered, DialogKeys.ProjectOpen, Path.Combine(songs, "a.tforge"), isFolder: false));
        Assert.True(DialogFolders.Remember(remembered, DialogKeys.FxChainLoad, Path.Combine(chains, "chain.tfchain"), isFolder: false));

        Assert.Equal(songs, StartOf(remembered, DialogKeys.ProjectOpen));
        Assert.Equal(chains, StartOf(remembered, DialogKeys.FxChainLoad));
        Assert.Equal(songs, StartOf(remembered, DialogKeys.ProjectOpen));   // the FX pick did not move the project folder
    }

    [Fact]
    public void EachKindStartsInItsDefaultWhenNothingIsRemembered()
    {
        var empty = new Dictionary<string, string>();
        Assert.Equal(Documents, StartOf(empty, DialogKeys.ProjectSave));
        Assert.Equal(Documents, StartOf(empty, DialogKeys.ExportPdf));
        Assert.Equal(Folders.Music, StartOf(empty, DialogKeys.AudioImport));
        Assert.Equal(Folders.Videos, StartOf(empty, DialogKeys.ExportVideo));
        Assert.Equal(Folders.Settings, StartOf(empty, DialogKeys.SettingsExport));
        Assert.Equal(PluginLibrary.ChainsFolder, StartOf(empty, DialogKeys.FxChainSave));
        Assert.Equal(Path.Combine(PluginLibrary.Root, PresetFolderStore.PluginFolderName), StartOf(empty, DialogKeys.PresetsExport));
        Assert.Null(StartOf(empty, DialogKeys.IrBrowse));
        Assert.Null(StartOf(empty, "unknown.key"));
    }

    [Fact]
    public void TheCallersFallbackIsUsedBeforeTheDefaultAndAGoneFolderFallsBack()
    {
        var empty = new Dictionary<string, string>();
        Assert.Equal(@"C:\IR\Folder", StartOf(empty, DialogKeys.IrBrowse, @"C:\IR\Folder"));

        var gone = new Dictionary<string, string> { [DialogKeys.ProjectOpen] = @"Z:\gone" };
        Assert.Equal(Documents, StartOf(gone, DialogKeys.ProjectOpen, null, exists: _ => false));
        Assert.Equal(@"C:\IR\Folder", StartOf(gone, DialogKeys.ProjectOpen, @"C:\IR\Folder", exists: _ => false));
    }

    [Fact]
    public void AFolderPickStoresOnlyItsOwnKey()
    {
        var remembered = new Dictionary<string, string> { [DialogKeys.ProjectSave] = @"C:\Old" };
        Assert.True(DialogFolders.Remember(remembered, DialogKeys.RenderFolder, @"D:\Renders", isFolder: true));
        Assert.False(DialogFolders.Remember(remembered, DialogKeys.RenderFolder, @"D:\Renders", isFolder: true));   // same folder: nothing to save
        Assert.Equal(@"C:\Old", remembered[DialogKeys.ProjectSave]);
        Assert.Equal(@"D:\Renders", remembered[DialogKeys.RenderFolder]);
    }

    [Fact]
    public void TheFoldersSurviveASettingsSaveAndLoad()
    {
        var file = Path.Combine(Path.GetTempPath(), "TabForgeDialogFolders", Guid.NewGuid().ToString("N"), "settings.json");
        var songs = Scratch("songs");
        var chains = Scratch("chains");
        using (var store = AppSettingsStore.Open(file, TimeSpan.Zero))
        {
            var memory = new DialogFolderMemory(() => store.Settings, () => store.MarkChanged(notifyObservers: false), Folders, Documents);
            memory.Remember(DialogKeys.ProjectOpen, Path.Combine(songs, "a.tforge"), isFolder: false);
            memory.Remember(DialogKeys.FxChainLoad, chains, isFolder: true);
        }

        using var reopened = AppSettingsStore.Open(file, TimeSpan.Zero);
        var again = new DialogFolderMemory(() => reopened.Settings, () => { }, Folders, Documents);
        Assert.Equal(songs, again.StartFolder(DialogKeys.ProjectOpen, null));
        Assert.Equal(chains, again.StartFolder(DialogKeys.FxChainLoad, null));
    }

    [Fact]
    public void ABadRememberedEntryIsDroppedOnLoad()
    {
        var settings = new AppSettings();
        settings.RememberedFolders[DialogKeys.ProjectOpen] = @"C:\Songs";
        settings.RememberedFolders[DialogKeys.FxChainLoad] = "relative";
        settings.RememberedFolders[""] = @"C:\Nameless";
        var clean = SettingsValidator.Normalize(settings);
        Assert.Equal(new[] { DialogKeys.ProjectOpen }, clean.RememberedFolders.Keys.ToArray());
    }
}
