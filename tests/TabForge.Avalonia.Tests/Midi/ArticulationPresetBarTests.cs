// Owns: the Articulations tab's preset bar (combo with built-ins and your own, Save preset, Delete, Manage, the applied name after reopening, the edited mark)
//   and the Export MIDI window's section-marker tick.
// Does not own: the table rows, the export compile or the layout (MidiExportWindowLayoutTests).
// Tests: this class. Headless; presets live in a scratch library.
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Articulations;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Rendering;
using Xunit;

namespace TabForge.Avalonia.Tests;

[Collection("PluginLibraryRoot")]
public sealed class ArticulationPresetBarTests
{
    private static (MidiProcessingWindow Window, PluginSlot Slot, TrackModel Track) Open()
    {
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument };
        var track = FxTestKit.Track(new[] { slot });
        var window = new MidiProcessingWindow(FxTestKit.Host(track, new FxTestKit.FakeEngine()), track, slot, apply => apply());
        window.Show();
        window.OpenArticulationsTab();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        return (window, slot, track);
    }

    private static ArticulationPresetSelector Selector(Window window) => window.GetVisualDescendants().OfType<ArticulationPresetSelector>().First();
    private static Button Named(Window window, string name) => DialogTestKit.Named<Button>(window, name);

    [AvaloniaFact]
    public void SelectorUsesTheStockComboBoxTheme()
    {
        // A ComboBox subclass without a style key override gets no control theme in the real app: zero size, invisible.
        var key = typeof(ArticulationPresetSelector).GetProperty("StyleKeyOverride", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(new ArticulationPresetSelector());
        Assert.Equal(typeof(ComboBox), key);
    }

    [AvaloniaFact]
    public void BarListsBuiltInsAndYourOwnAndChoosingLoadsRules()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        ArticulationPresets.WriteFile(ArticulationPresets.UserStore().PathOf("Mine"), ArticulationPreset.From("Mine", "", new ArticulationMap()));
        var (window, _, track) = Open();
        try
        {
            var box = Selector(window);
            var texts = box.Items.OfType<ArticulationPresetSelector.Entry>().Select(e => e.Text).ToList();
            Assert.Contains(ArticulationPresets.AmpleName, texts);
            Assert.Contains("★ Mine", texts);
            Assert.True(box.Bounds.Width > 40);
            Assert.Equal("Save preset…", Named(window, "ArticulationSaveAsButton").Content);
            Assert.True(Named(window, "ArticulationManageButton").IsVisible);
            Assert.False(Named(window, "ArticulationDeleteButton").IsEnabled);   // a built-in is selected: nothing of yours to delete
            box.SelectedItem = box.Items.OfType<ArticulationPresetSelector.Entry>().First(e => e.Name == ArticulationPresets.AmpleName);
            DialogTestKit.RunQueuedUiWork();
            Assert.True(ArticulationMapStore.MapOf(track, track.Rig.Plugins[0]).Rules.Count > 0);
            box.SelectedItem = box.Items.OfType<ArticulationPresetSelector.Entry>().First(e => e.Name == "Mine");
            DialogTestKit.RunQueuedUiWork();
            Assert.True(Named(window, "ArticulationDeleteButton").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ManageOpensTheManagerOnArticulationsAndTheAppliedPresetShowsAfterReopening()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var calls = new List<PresetCatalogKind>();
        var previous = PresetManager.Opener;
        PresetManager.Opener = (_, kind, _) => calls.Add(kind);
        var (window, slot, track) = Open();
        try
        {
            var box = Selector(window);
            Named(window, "ArticulationManageButton").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { PresetCatalogKind.Articulation }, calls);
            box.SelectedItem = box.Items.OfType<ArticulationPresetSelector.Entry>().First(e => e.Name == ArticulationPresets.AmpleName);
            DialogTestKit.RunQueuedUiWork();
            window.Close();
            var reopened = new MidiProcessingWindow(FxTestKit.Host(track, new FxTestKit.FakeEngine()), track, slot, apply => apply());
            reopened.Show();
            reopened.OpenArticulationsTab();
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(ArticulationPresets.AmpleName, Selector(reopened).SelectedEntry!.Name);
            reopened.Close();
        }
        finally { PresetManager.Opener = previous; if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public void TheTickReadsAndWritesTheSectionMarkerSetting()
    {
        var settings = new MidiExportSettings { WriteSectionMarkers = false };
        var window = new MidiExportWindow(new SongProject { Title = "S" }, settings, hasSelection: false, snip: null, selection: null,
            saveFile: null, quietSave: () => { }, report: _ => { });
        window.Show();
        try
        {
            var tick = DialogTestKit.Named<CheckBox>(window, "WriteSectionMarkers");
            Assert.False(tick.IsChecked);
            tick.IsChecked = true;
            Assert.True(settings.WriteSectionMarkers);
        }
        finally { window.Close(); }
    }
}
