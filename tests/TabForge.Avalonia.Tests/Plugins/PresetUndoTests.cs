using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.EffectEditors;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the delete that can be taken back (folder and effect stores), the saved order of reordered presets (and that an old folder with no order
///   file still lists as before), the grip drag over real rows, the built-in copy and the import of files, and the manager's new categories.
/// Does not own: the row looks (PresetRowsTests) or the manager window's list and toolbar (PresetManagerWindowTests).
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it or reads the scratch folder
public class PresetUndoTests
{
    [AvaloniaFact]
    public void FolderDeleteUndoRestoresTheBytesAndThePlace()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var store = PresetFolderStore.FxChains();
        Seed(store, "A", "B", "C");
        store.Move("C", 0);   // C, A, B: the order is now written
        var undo = PresetDeleteUndo.Delete(store, new[] { "A" });
        Assert.Equal(new[] { "C", "B" }, store.Names());
        Assert.True(undo.CanUndo);
        undo.Undo();
        Assert.Equal(new[] { "C", "A", "B" }, store.Names());
        Assert.Equal("A", File.ReadAllText(store.PathOf("A")));
    }

    [AvaloniaFact]
    public void DeletingSeveralAndUndoingPutsEveryOneBack()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var store = PresetFolderStore.FxChains();
        Seed(store, "A", "B", "C", "D");
        var undo = PresetDeleteUndo.Delete(store, new[] { "D", "B" });
        Assert.Equal(new[] { "A", "C" }, store.Names());
        undo.Undo();
        Assert.Equal(new[] { "A", "B", "C", "D" }, store.Names());
    }

    [AvaloniaFact]
    public void ReorderIsSavedBesideThePresetsAndAFolderWithoutAnOrderStillListsAlphabetically()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var store = PresetFolderStore.FxChains();
        Seed(store, "B", "A", "C");
        Assert.Equal(new[] { "A", "B", "C" }, store.Names());   // an old folder: no order file, alphabetical as before
        store.Move("C", 0);
        Assert.Equal(new[] { "C", "A", "B" }, PresetFolderStore.FxChains().Names());   // a fresh store reads the saved order
        File.WriteAllText(store.PathOf("Aa"), "Aa");
        Assert.Equal(new[] { "C", "A", "B", "Aa" }, PresetFolderStore.FxChains().Names());   // a new preset joins the end, the order stays
    }

    [AvaloniaFact]
    public void EffectPresetStoreDeleteUndoAndMovePersistInTheSettings()
    {
        var settings = new AppSettings();
        var saves = 0;
        var presets = new EffectPresetStore(settings);
        var bends = presets.Store(EffectEditorKind.Bend, () => saves++);
        var points = new[] { new BendPointModel() };
        Assert.True(presets.Save(EffectEditorKind.Bend, "One", points));
        Assert.True(presets.Save(EffectEditorKind.Bend, "Two", points));
        Assert.True(presets.Save(EffectEditorKind.TremoloBar, "Other", points));
        Assert.True(presets.Save(EffectEditorKind.Bend, "Three", points));
        bends.Move("Three", 0);
        Assert.Equal(new[] { "Three", "One", "Two" }, bends.Names());
        Assert.Equal(new[] { "Other" }, presets.Store(EffectEditorKind.TremoloBar).Names());   // another kind keeps its own list
        var undo = PresetDeleteUndo.Delete(bends, new[] { "One" });
        Assert.Equal(new[] { "Three", "Two" }, bends.Names());
        undo.Undo();
        Assert.Equal(new[] { "Three", "One", "Two" }, bends.Names());
        Assert.True(saves >= 3);
        Assert.Throws<InvalidDataException>(() => bends.Rename("One", EffectPresetStore.BuiltIn(EffectEditorKind.Bend)[0].Name));
    }

    [AvaloniaFact]
    public void DragMovesAPresetWhenTheGripIsReleasedOverAnotherRow()
    {
        var store = new ListStore("A", "B", "C");
        var host = new ComboBox();
        var rows = PresetRows.Attach(host, () => store, item => item as string, () => PresetCatalogKind.FxChain, () => null);
        var panel = new StackPanel { Width = 200 };
        foreach (var name in store.Items.ToList()) panel.Children.Add(rows.View(name));
        var window = new Window { Width = 220, Height = 200, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var grips = panel.GetLogicalDescendants().OfType<Border>().Where(b => b.Child is global::Avalonia.Controls.Shapes.Path && b.Width == DialogMetrics.PresetGripWidth).ToList();
        Assert.Equal(3, grips.Count);
        Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        var from = Centre(grips[2]);
        var over = Centre(grips[0]);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X, (from.Y + over.Y) / 2));
        window.MouseMove(over);
        window.MouseUp(over, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "C", "A", "B" }, store.Items);
        window.Close();
    }

    [AvaloniaFact]
    public void BuiltInArticulationCopiesToAnEditablePresetAndTheCategoriesExist()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var catalogue = new PresetCataloguePresenter();
        var node = catalogue.Node(PresetCatalogKind.Articulation, null);
        Assert.Contains(ArticulationPresets.AmpleName, catalogue.BuiltIns(node));
        var window = new PresetManagerWindow(new AvaloniaDialogHost(), PresetCatalogKind.Articulation, null);
        window.Show();
        Assert.Equal(PresetCatalogKind.Articulation, window.SelectedNode.Kind);
        Assert.Contains(window.Listed, e => e.BuiltIn && e.Name == ArticulationPresets.AmpleName);
        Assert.True(window.Select(ArticulationPresets.AmpleName));
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.CanDuplicate);
        Assert.False(window.CanDelete);
        window.DuplicateSelected();
        Assert.Contains(ArticulationPresets.UserStore().Names(), name => name.StartsWith(ArticulationPresets.AmpleName));
        Assert.Contains(window.TreeRows, row => row.Node.Kind == PresetCatalogKind.EffectPreset);
        window.Show(PresetCatalogKind.EffectPreset, "TremoloBar");
        Assert.Equal("TremoloBar", window.SelectedNode.Key);
        window.Close();
    }

    [AvaloniaFact]
    public void ImportAddsAFileUnderItsNameAndRefusesWhatTheKindCannotRead()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var stores = PresetStoreFactory.Default;
        var actions = new PresetManagerActions(stores, new PresetManagerDialogs());
        var node = new PresetCataloguePresenter(stores).Node(PresetCatalogKind.Articulation, null);
        var good = ArticulationPresets.ToBytes(ArticulationPreset.From("x", "", new ArticulationMap()));
        var made = actions.Import(node, "C:/somewhere/My set.json", good, new[] { ArticulationPresets.AmpleName });
        Assert.True(made.Ok);
        Assert.Equal("My set", made.Select);
        Assert.Equal(new[] { "My set" }, ArticulationPresets.UserStore().Names());
        Assert.Equal("My set copy", actions.Import(node, "My set.json", good, Array.Empty<string>()).Select);   // taken: a copy name
        var bad = actions.Import(node, "bad.json", Encoding.UTF8.GetBytes("not json"), Array.Empty<string>());
        Assert.False(bad.Ok);
        Assert.NotNull(bad.Error);
        var entry = new PresetEntry(node, "My set", false, null);
        Assert.Equal(good.Length, actions.ExportBytes(entry)!.Length);
    }

    private static void Seed(PresetFolderStore store, params string[] names)
    {
        foreach (var name in names)
        {
            var path = store.PathOf(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, name);
        }
    }

    private sealed class ListStore : IPresetStore
    {
        public List<string> Items { get; }
        public ListStore(params string[] names) => Items = names.ToList();
        public event Action? Changed;
        public IReadOnlyList<string> Names() => Items.ToList();
        public void Rename(string from, string to, bool replace = false) { Items[Items.IndexOf(from)] = to; Changed?.Invoke(); }
        public void Duplicate(string from, string to) { Items.Insert(Items.IndexOf(from) + 1, to); Changed?.Invoke(); }
        public void Delete(string name) { Items.Remove(name); Changed?.Invoke(); }
        public void Move(string name, int index) { Items.Remove(name); Items.Insert(Math.Clamp(index, 0, Items.Count), name); Changed?.Invoke(); }
    }
}
