using System;
using System.Globalization;
using System.IO;
using System.Linq;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the preset manager's Core catalogue and store factory: the tree (group order and titles, plug-in and processor children), the
///   presets of a node (built-ins first, then the user's), the search across every word, the date text and the store of every kind.
/// Does not own: the window, the files' rules (PresetFolderStore, DrumMapStore) or the built-in names (MidiProcessorCatalog, DrumMapLibrary).
/// Tests: this class.
/// </summary>
public class PresetCatalogueTests
{
    private static readonly PresetCataloguePresenter Catalogue = new();
    private static readonly DateTime Today = new(2026, 10, 18);

    [Fact]
    public void GroupsAreInWpfOrderWithTheirTitles()
    {
        var groups = Catalogue.Groups();

        Assert.Equal(new[] { "Plug-in presets", "FX chains", "Containers", "MIDI chains", "MIDI processors", "Drum maps" }, groups.Select(g => g.Title));
        Assert.Equal(new[]
        {
            PresetCatalogKind.PluginPreset, PresetCatalogKind.FxChain, PresetCatalogKind.Container, PresetCatalogKind.MidiChain, PresetCatalogKind.MidiProcessor, PresetCatalogKind.DrumMap,
        }, groups.Select(g => g.Kind));
        Assert.Equal(new[] { true, false, false, false, true, false }, groups.Select(g => g.IsGroup));
        Assert.All(groups, group => Assert.Equal(group.Title, group.Where));
    }

    [Fact]
    public void PluginChildrenAreSortedAndOnlyFoldersWithPresets()
    {
        using var scratch = Scratch();
        Write("Presets/Bravo/One.tfpreset");
        Write("Presets/alpha/Two.tfpreset");
        Directory.CreateDirectory(Path.Combine(PluginLibrary.Root, "Presets", "Empty"));

        var children = Catalogue.Children(Group(PresetCatalogKind.PluginPreset));

        Assert.Equal(new[] { "alpha", "Bravo" }, children.Select(child => child.Title));
        Assert.All(children, child => Assert.Equal(child.Title, child.Key));
        Assert.Equal("Plug-in presets › alpha", children[0].Where);
    }

    [Fact]
    public void ProcessorChildrenIncludeATypeWithOnlyBuiltInPresets()
    {
        using var scratch = Scratch();
        Write("MidiPresets/Processors/humanize/One.json");

        var children = Catalogue.Children(Group(PresetCatalogKind.MidiProcessor));
        var noteMap = MidiProcessorCatalog.Find("noteMap")!;

        Assert.Contains(children, child => child.Key == "humanize" && child.Title == MidiProcessorCatalog.Find("humanize")!.Name);
        Assert.Contains(children, child => child.Key == "noteMap" && child.Title == noteMap.Name);
        Assert.Equal(children.Select(child => child.Title).OrderBy(title => title, StringComparer.OrdinalIgnoreCase), children.Select(child => child.Title));
    }

    [Fact]
    public void BuiltInsComeBeforeTheUsersPresets()
    {
        using var scratch = Scratch();
        Write("MidiPresets/Mine.json");
        Write("MidiPresets/Processors/noteMap/Mine.json");

        var chains = Catalogue.Entries(Group(PresetCatalogKind.MidiChain));
        Assert.Equal(MidiProcessorCatalog.BuiltInPresets.Select(preset => preset.Name), chains.Take(MidiProcessorCatalog.BuiltInPresets.Count).Select(entry => entry.Name));
        Assert.All(chains.Take(MidiProcessorCatalog.BuiltInPresets.Count), entry => Assert.True(entry.BuiltIn));
        Assert.Equal(("Mine", false), (chains[^1].Name, chains[^1].BuiltIn));

        var noteMap = Catalogue.Entries(Catalogue.Node(PresetCatalogKind.MidiProcessor, "noteMap"));
        Assert.Equal("Tabs to SD3", noteMap[0].Name);
        Assert.True(noteMap[0].BuiltIn);
        Assert.Equal(("Mine", false), (noteMap[^1].Name, noteMap[^1].BuiltIn));
    }

    [Fact]
    public void EveryKindListsItsPresets()
    {
        using var scratch = Scratch();
        Write("Presets/Kit/One.tfpreset");
        Write("FxChains/One.tfchain");
        Write("Containers/One.tfcontainer");
        Write("MidiPresets/One.json");
        Write("MidiPresets/Processors/humanize/One.json");
        Write("DrumMaps/Scratch kit.json", "{\"Name\":\"Scratch kit\",\"Notes\":{\"36\":\"Kick\"}}");
        DrumMapLibrary.Reload();

        var groups = Catalogue.Groups();
        Assert.Single(Catalogue.Entries(groups[0]));   // plug-in presets
        Assert.Single(Catalogue.Entries(groups[1]));   // FX chains
        Assert.Single(Catalogue.Entries(groups[2]));   // containers
        Assert.Equal(MidiProcessorCatalog.BuiltInPresets.Count + 1, Catalogue.Entries(groups[3]).Count);   // MIDI chains
        Assert.Equal(2, Catalogue.Entries(groups[4]).Count);   // MIDI processors: noteMap's built-in, humanize's user preset
        Assert.Contains(Catalogue.Entries(groups[5]), entry => entry.Name == "Scratch kit" && !entry.BuiltIn);
    }

    [Fact]
    public void SearchAppliesEveryWordAndIncludesBuiltIns()
    {
        using var scratch = Scratch();
        Write("Presets/Lead/Soft lead.tfpreset");
        Write("Presets/Lead/Hard lead.tfpreset");
        Write("FxChains/Soft chain.tfchain");

        Assert.Equal(new[] { "Soft lead" }, Catalogue.Search("soft lead").Select(entry => entry.Name));
        Assert.Equal(new[] { "Hard lead", "Soft lead" }.OrderBy(name => name), Catalogue.Search("LEAD").Select(entry => entry.Name).OrderBy(name => name));
        Assert.Contains(Catalogue.Search("humanize light"), entry => entry.Name == "Humanize light" && entry.BuiltIn);
        Assert.Empty(Catalogue.Search("soft nothing"));
    }

    [Fact]
    public void DateTextAddsTheYearOnlyWhenItIsNotThisYear()
    {
        var thisYear = new DateTime(2026, 10, 12);
        var anotherYear = new DateTime(2024, 10, 12);

        Assert.Equal("", PresetCataloguePresenter.DateText(null, Today));
        Assert.Equal(thisYear.ToString("d MMM", CultureInfo.CurrentCulture), PresetCataloguePresenter.DateText(thisYear, Today));
        Assert.Equal(anotherYear.ToString("d MMM yyyy", CultureInfo.CurrentCulture), PresetCataloguePresenter.DateText(anotherYear, Today));
    }

    [Fact]
    public void StoreOfReturnsTheStoreOfEveryKindAndNeedsAKeyForTheKeyedKinds()
    {
        using var scratch = Scratch();

        Assert.Null(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.PluginPreset, null));
        Assert.Null(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.MidiProcessor, null));
        Assert.IsType<PresetFolderStore>(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.PluginPreset, "Kit"));
        Assert.IsType<PresetFolderStore>(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.FxChain, null));
        Assert.IsType<PresetFolderStore>(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.Container, null));
        Assert.IsType<PresetFolderStore>(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.MidiChain, null));
        Assert.IsType<PresetFolderStore>(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.MidiProcessor, "noteMap"));
        Assert.IsType<DrumMapStore>(PresetStoreFactory.Default.StoreOf(PresetCatalogKind.DrumMap, null));
    }

    [Fact]
    public void PluginStoreKeepsAFolderKeyLongerThanSixtyFourCharacters()
    {
        using var scratch = Scratch();
        var key = new string('k', 70);
        Write(Path.Combine("Presets", key, "Long.tfpreset"));

        var store = PresetStoreFactory.Default.StoreOf(PresetCatalogKind.PluginPreset, key);

        Assert.NotNull(store);
        Assert.Contains("Long", store!.Names());
    }

    private static PresetNode Group(PresetCatalogKind kind) => Catalogue.Groups().Single(group => group.Kind == kind);

    /// <summary>A scratch preset root, and the drum map library reading from it (its cache survives a scratch root).</summary>
    private static IDisposable Scratch()
    {
        var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        return new ScratchRoot(scratch);
    }

    private sealed class ScratchRoot : IDisposable
    {
        private readonly IDisposable _scratch;
        public ScratchRoot(IDisposable scratch) => _scratch = scratch;
        public void Dispose() { _scratch.Dispose(); DrumMapLibrary.Reload(); }   // leave no cache pointing at a deleted folder
    }

    private static void Write(string relativePath, string text = "{}")
    {
        var path = Path.Combine(PluginLibrary.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
