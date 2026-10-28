using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Presets;
using TabForge.Platform.Theme;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the preset manager: its structure and names, the real tree (kinds, plug-in and processor children, counts),
///   the list (built-ins first, then the user's order), Show(kind,key), the file changes behind rename / duplicate / delete / move, the
///   reserved names, the search rule, the delete question and its setting, and the fake-catalogue seam. Renders and contrast kept.
/// Does not own: the window, the catalogue or the stores; expected texts come from the Core presenters.
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it or reads the scratch folder
public class PresetManagerWindowTests
{
    private static readonly string[] ToolLabels = { "Rename", "Duplicate", "Move up", "Move down", "Delete", "Show in tree" };
    private static readonly string[] KindTitles = { "Plug-in presets", "FX chains", "Containers", "MIDI chains", "MIDI processors", "Drum maps", "Articulations", "Bend / tremolo" };
    private static readonly string[] FxOrder = { "A chain", "B chain", "C chain" };

    [AvaloniaFact]
    public void PresetManagerHasTheWpfTitleSizeAndToolsInOrder()
    {
        using var scratch = Scratch();
        var window = Open();
        Assert.Equal("Preset manager", window.Title);
        Assert.Equal(900, window.Width);
        Assert.Equal(500, window.Height);
        Assert.Equal(860, window.MinWidth);
        Assert.Equal(320, window.MinHeight);
        Assert.Equal(ToolLabels, window.GetLogicalDescendants().OfType<Button>().Select(b => b.Content as string)
            .Where(text => text is not null && ToolLabels.Contains(text)));
        Assert.Equal("Search all presets", AutomationProperties.GetName(Named<TextBox>(window, "SearchPresets")));
        Assert.Equal("Presets", AutomationProperties.GetName(Named<ListBox>(window, "PresetList")));
        Assert.Equal("Preset kinds", AutomationProperties.GetName(Named<ListBox>(window, "PresetCatalogKinds")));
        window.Close();
    }

    [AvaloniaFact]
    public void TreeListsRealKindsChildrenAndCounts()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();

        Assert.Equal(KindTitles, window.TreeRows.Where(row => row.Node.Key is null).Select(row => row.Node.Title));
        // Plug-in presets is expanded at start: its children follow it, sorted, and only folders with presets are listed.
        Assert.Equal(new[] { "Plug-in presets", "Bass", "Lead", "FX chains", "Containers", "MIDI chains", "MIDI processors", "Drum maps", "Articulations", "Bend / tremolo" },
            window.TreeRows.Select(row => row.Node.Title));
        Assert.Equal(3, window.TreeRows[0].Count);   // two Bass presets, one Lead
        Assert.Equal(2, window.TreeRows.Single(row => row.Node.Key == "Bass").Count);
        window.Close();
    }

    [AvaloniaFact]
    public void ShowSelectsTheNodeAndListsItsPresets()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();

        window.Show(PresetCatalogKind.PluginPreset, "Bass");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal("Bass", window.SelectedNode.Key);
        Assert.Equal(new[] { "One", "Two" }, Names(window));

        window.Show(PresetCatalogKind.MidiProcessor, "noteMap");
        Assert.Equal("noteMap", window.SelectedNode.Key);
        Assert.Contains("Superior Drummer 3", Names(window));
        Assert.True(window.Listed[0].BuiltIn);
        window.Close();
    }

    [AvaloniaFact]
    public async Task RenameOnDiskAndReservedNames()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.PluginPreset, "Bass");
        window.Select("One");
        DialogTestKit.RunQueuedUiWork();
        window.BeginRename();
        Assert.True(window.IsEditing);
        await window.CommitRenameAsync("Three");
        Assert.False(window.IsEditing);
        Assert.False(File.Exists(PresetPath("Bass", "One.tfpreset")));
        Assert.True(File.Exists(PresetPath("Bass", "Three.tfpreset")));

        window.Show(PresetCatalogKind.MidiChain, null);
        window.Select("Mine");
        DialogTestKit.RunQueuedUiWork();
        window.BeginRename();
        await window.CommitRenameAsync("Humanize light");
        Assert.Equal("A preset that comes with the app has this name. Choose another name.", window.Message);
        Assert.True(window.IsEditing);
        window.Close();
    }

    [AvaloniaFact]
    public void DuplicateInsertsTheCopyWithTheCopyRule()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.FxChain, null);
        window.Select("A chain");
        DialogTestKit.RunQueuedUiWork();
        window.DuplicateSelected();
        window.Select("A chain");
        window.DuplicateSelected();

        Assert.True(File.Exists(Path.Combine(PluginLibrary.Root, "FxChains", "A chain copy.tfchain")));
        Assert.True(File.Exists(Path.Combine(PluginLibrary.Root, "FxChains", "A chain copy 2.tfchain")));
        Assert.Equal(new[] { "A chain", "A chain copy", "A chain copy 2", "B chain", "C chain" }, PresetFolderStore.FxChains().Names());
        window.Close();
    }

    [AvaloniaFact]
    public void DeleteIsAtOnceAndOffersOneUndoForTheWholeSelection()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();

        window.Show(PresetCatalogKind.FxChain, null);
        window.SelectNames("A chain", "B chain");
        DialogTestKit.RunQueuedUiWork();
        window.DeleteSelected();

        Assert.Equal(new[] { "C chain" }, PresetFolderStore.FxChains().Names());
        Assert.True(window.Toast.IsShowing);
        Assert.Equal("Deleted 2 presets", window.Toast.Text);
        window.Toast.Undo();
        Assert.Equal(new[] { "A chain", "B chain", "C chain" }, PresetFolderStore.FxChains().Names());
        Assert.Equal(new[] { "A chain", "B chain", "C chain" }, Names(window));
        window.Close();
    }

    [AvaloniaFact]
    public void DeleteGoneForGoodOnceTheMessageTimesOut()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.FxChain, null);
        window.Select("A chain");
        DialogTestKit.RunQueuedUiWork();
        window.DeleteSelected();
        Assert.Equal(new[] { "B chain", "C chain" }, PresetFolderStore.FxChains().Names());
        window.Toast.Dismiss();
        Assert.False(window.Toast.IsShowing);
        window.Toast.Undo();
        Assert.Equal(new[] { "B chain", "C chain" }, PresetFolderStore.FxChains().Names());
        window.Close();
    }

    [AvaloniaFact]
    public void MoveSelectedWritesTheOrder()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.FxChain, null);
        window.Select("C chain");
        DialogTestKit.RunQueuedUiWork();
        window.MoveSelected(-1);
        Assert.Equal(new[] { "A chain", "C chain", "B chain" }, PresetFolderStore.FxChains().Names());
        window.Close();
    }

    [AvaloniaFact]
    public void SearchAppliesEveryWordAndIncludesBuiltIns()
    {
        using var scratch = Scratch();
        SeedStandard();
        Write(Path.Combine("FxChains", "Soft lead.tfchain"));
        var window = Open();
        window.SearchText = "soft lead";
        Assert.Equal(new[] { "Soft lead" }, Names(window));
        Assert.Equal("1 preset found", Named<TextBlock>(window, "Heading").Text);
        window.SearchText = "humanize light";
        Assert.Equal(new[] { "Humanize light" }, Names(window));
        Assert.True(window.Listed[0].BuiltIn);
        window.SearchText = "soft nothing";
        Assert.Empty(Names(window));
        window.Close();
    }

    [AvaloniaFact]
    public void BuiltInsCannotBeRenamedDuplicatedOrDeletedButUsersCan()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.MidiChain, null);
        window.Select("Humanize light");
        DialogTestKit.RunQueuedUiWork();
        Assert.False(window.CanRename);
        Assert.True(window.CanDuplicate);   // a built-in chain can be copied into an editable one
        Assert.False(window.CanDelete);
        Assert.Contains("comes with the app", window.RenameTip);

        window.Select("Mine");
        DialogTestKit.RunQueuedUiWork();
        Assert.True(window.CanRename);
        Assert.True(window.CanDelete);
        window.Close();
    }

    [AvaloniaFact]
    public void WindowBuildsOverAFakeCatalogueAndStore()
    {
        var catalogue = new FakeCatalogue();
        var factory = new FakeStoreFactory();
        var window = new PresetManagerWindow(new AvaloniaDialogHost(), PresetCatalogKind.PluginPreset, null, catalogue, factory);
        window.Show();
        Assert.Equal(new[] { "Fake one" }, Names(window));
        window.Select("Fake one");
        DialogTestKit.RunQueuedUiWork();
        window.DuplicateSelected();
        Assert.Equal(new[] { "Fake one", "Fake one copy" }, factory.Store.Items);
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlASelectsEveryVisibleRow()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.MidiChain, null);
        var list = DialogTestKit.Named<ListBox>(window, "PresetList");
        list.Focus();
        DialogTestKit.RunQueuedUiWork();
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control });

        Assert.Equal(list.ItemCount, window.SelectedEntries.Count);
        Assert.Equal(window.Listed.Count, window.SelectedEntries.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void FirstEscapeCancelsTheRenameAndTheSecondCloses()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.PluginPreset, "Bass");
        window.Select("One");
        DialogTestKit.RunQueuedUiWork();
        window.BeginRename();
        Assert.True(window.IsEditing);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(window.IsEditing);
        Assert.True(window.IsVisible);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void MixedSelectionDeletesOnlyUserRows()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.MidiChain, null);
        window.SelectNames("Humanize light", "Mine");
        DialogTestKit.RunQueuedUiWork();

        Assert.False(window.CanRename);
        Assert.True(window.CanDelete);
        window.DeleteSelected();

        Assert.Equal("Deleted \"Mine\"", window.Toast.Text);
        Assert.DoesNotContain("Mine", Names(window));
        Assert.Contains("Humanize light", Names(window));
        window.Close();
    }

    [AvaloniaFact]
    public void ToolbarAndRowsCarryAutomationNames()
    {
        using var scratch = Scratch();
        SeedStandard();
        var window = Open();
        window.Show(PresetCatalogKind.MidiChain, null);

        foreach (var label in ToolLabels)
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == label);

        var list = DialogTestKit.Named<ListBox>(window, "PresetList");
        // Virtualized (H13): only visible rows have containers; names come from the containers where realized.
        list.UpdateLayout();
        DialogTestKit.RunQueuedUiWork();
        var names = Enumerable.Range(0, window.Listed.Count)
            .Select(i => list.ContainerFromIndex(i) is ListBoxItem realized ? AutomationProperties.GetName(realized) : window.Listed[i].Name)
            .ToList();
        Assert.Contains(names, name => name is not null && name.Contains("built-in"));
        Assert.Contains(names, name => name is not null && name.StartsWith("Mine", StringComparison.Ordinal));
        var tree = DialogTestKit.Named<ListBox>(window, "PresetCatalogKinds");
        Assert.All(tree.Items.OfType<ListBoxItem>(), item => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(item))));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresetManagerRendersPngsInEachTheme(bool light)
    {
        using var scratch = Scratch();
        SeedStandard();
        using var theme = UseTheme(light);
        var window = Open(show: false);
        Assert.True(new FileInfo(SavePng(window, nameof(PresetManagerWindowTests), $"preset-manager-{(light ? "light" : "dark")}.png")).Length > 0);
        window.Close();
    }

    [Theory]
    [InlineData(false, "TextPrimary")]
    [InlineData(false, "TextMuted")]
    [InlineData(true, "TextPrimary")]
    [InlineData(true, "TextMuted")]
    public void PresetManagerTextReadsAtLeastFourAndAHalfToOne(bool light, string key)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(key, light), panel) >= 4.5, $"{key} on PanelBackground (light: {light})");
    }

    private static PresetManagerWindow Open(bool show = true)
    {
        var window = new PresetManagerWindow(new AvaloniaDialogHost());
        window.Dialogs.Confirm = _ => Task.FromResult(true);
        if (show) window.Show();
        return window;
    }

    private static IEnumerable<string> Names(PresetManagerWindow window) => window.Listed.Select(entry => entry.Name);

    private static string PresetPath(string folder, string file) => Path.Combine(PluginLibrary.Root, "Presets", folder, file);

    private static IDisposable Scratch()
    {
        var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        return scratch;
    }

    private static void SeedStandard()
    {
        Write(Path.Combine("Presets", "Bass", "One.tfpreset"));
        Write(Path.Combine("Presets", "Bass", "Two.tfpreset"));
        Write(Path.Combine("Presets", "Lead", "Warm.tfpreset"));
        foreach (var chain in FxOrder) Write(Path.Combine("FxChains", chain + ".tfchain"));
        Write(Path.Combine("Containers", "Riff.tfcontainer"));
        Write(Path.Combine("MidiPresets", "Mine.json"));
        Write(Path.Combine("MidiPresets", "Processors", "humanize", "Soft.json"));
        Write(Path.Combine("DrumMaps", "Scratch kit.json"), "{\"Name\":\"Scratch kit\",\"Notes\":{\"36\":\"Kick\"}}");
        DrumMapLibrary.Reload();
    }

    private static void Write(string relativePath, string text = "{}")
    {
        var path = Path.Combine(PluginLibrary.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class FakeCatalogue : IPresetCatalogue
    {
        private readonly PresetNode _group = new(PresetCatalogKind.PluginPreset, null, "Plug-in presets", "Plug-in presets");
        private readonly PresetNode _child = new(PresetCatalogKind.PluginPreset, "Fake", "Fake", "Plug-in presets › Fake");

        public IReadOnlyList<PresetNode> Groups() => new[] { _group };
        public IReadOnlyList<PresetNode> Children(PresetNode group) => new[] { _child };
        public PresetNode Node(PresetCatalogKind kind, string? key) => key is null ? _group : _child;
        public IReadOnlyList<string> BuiltIns(PresetNode node) => Array.Empty<string>();
        public IReadOnlyList<PresetEntry> Entries(PresetNode node) => new[] { new PresetEntry(_child, "Fake one", false, null) };
        public IReadOnlyList<PresetEntry> Search(string text) => Entries(_child);
    }

    private sealed class FakeStoreFactory : IPresetStoreFactory
    {
        public FakeStore Store { get; } = new();
        public IPresetStore? StoreOf(PresetCatalogKind kind, string? key) => Store;
        public DateTime? Modified(IPresetStore store, string name) => null;
    }

    private sealed class FakeStore : IPresetStore
    {
        public List<string> Items { get; } = new() { "Fake one" };
        public event Action? Changed;
        public IReadOnlyList<string> Names() => Items.ToList();
        public void Rename(string from, string to, bool replace = false) { Items[Items.IndexOf(from)] = to; Changed?.Invoke(); }
        public void Duplicate(string from, string to) { Items.Insert(Items.IndexOf(from) + 1, to); Changed?.Invoke(); }
        public void Delete(string name) { Items.Remove(name); Changed?.Invoke(); }
        public void Move(string name, int index) { Items.Remove(name); Items.Insert(Math.Clamp(index, 0, Items.Count), name); Changed?.Invoke(); }
    }
}
