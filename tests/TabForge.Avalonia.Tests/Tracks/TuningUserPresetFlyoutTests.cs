using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the tuning flyout's own presets: the save row validates and persists, rename refuses built-ins, delete is instant, and apply resizes.
/// Does not own: the rules themselves (TuningPresetCatalogTests) or the draft (TrackTuningDraftTests). No modals: delete asks nothing.
/// </summary>
public class TuningUserPresetFlyoutTests
{
    private static TrackModel VariantTrack() => new()
    {
        Name = "Tuning test", Kind = TrackKind.Guitar, NumberOfFrets = 24,
        StringTunings = new List<int> { 62, 57, 55, 50, 45, 40 },
    };

    private static TrackPropertiesWindow Open(TrackModel track, AppSettings settings, System.Action save)
    {
        var window = new TrackPropertiesWindow(track, settingsGetter: () => settings, saveSettings: save);
        window.Show();
        return window;
    }

    private static List<Button> FlyoutButtons(TrackPropertiesWindow window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Select(b => b.Flyout).OfType<Flyout>()
            .Select(f => f.Content).OfType<Control>()
            .SelectMany(c => c.GetLogicalDescendants().Prepend(c)).OfType<Button>().Where(b => b.Name == name).ToList();

    [AvaloniaFact]
    public void SaveValidatesPersistsAndLabelsThenApplyResizes()
    {
        var settings = new AppSettings();
        var saved = 0;
        var track = VariantTrack();
        var window = Open(track, settings, () => saved++);
        try
        {
            window.FillPresetList();
            Assert.Empty(FlyoutButtons(window, "DeletePresetButton"));
            DialogTestKit.Click(DialogTestKit.Named<Button>(window, "SavePresetButton"));
            var nameBox = DialogTestKit.Named<TextBox>(window, "SavePresetNameBox");
            nameBox.Text = "";
            DialogTestKit.Click(DialogTestKit.Named<Button>(window, "SavePresetSave"));
            Assert.NotEmpty(DialogTestKit.Named<TextBlock>(window, "SavePresetError").Text ?? "");
            Assert.Empty(settings.TuningUserPresets);
            Assert.Equal(0, saved);
            nameBox.Text = "My DADGAD";
            DialogTestKit.Click(DialogTestKit.Named<Button>(window, "SavePresetSave"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, saved);
            Assert.Equal("My DADGAD", Assert.Single(settings.TuningUserPresets).Name);
            Assert.Equal("My DADGAD ▾", DialogTestKit.Named<Button>(window, "TuningPresetButton").Content);
            window.FillPresetList();
            Assert.Single(FlyoutButtons(window, "DeletePresetButton"));
            var store = new TuningUserSettingsStore(settings);
            Assert.True(store.TrySave("My dulcimer", new[] { 62, 57, 50 }, out _));
            DialogTestKit.Named<CheckBox>(window, "PresetAllCountsBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.FillPresetList();
            var applies = FlyoutButtons(window, "PresetItem");
            var dulcimer = Assert.Single(applies, b => (b.Tag as TuningMenuEntry)?.Name == "My dulcimer");
            DialogTestKit.Click(dulcimer);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, DialogTestKit.Named<ComboBox>(window, "StringCountBox").SelectedIndex);
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void RenameRefusesBuiltInAndDeleteIsInstant()
    {
        var settings = new AppSettings();
        var saved = 0;
        var window = Open(VariantTrack(), settings, () => saved++);
        try
        {
            DialogTestKit.Click(DialogTestKit.Named<Button>(window, "SavePresetButton"));
            DialogTestKit.Named<TextBox>(window, "SavePresetNameBox").Text = "My DADGAD";
            DialogTestKit.Click(DialogTestKit.Named<Button>(window, "SavePresetSave"));
            Assert.Equal(1, saved);
            window.FillPresetList();
            DialogTestKit.Click(Assert.Single(FlyoutButtons(window, "RenamePresetButton")));
            var renameBox = DialogTestKit.Named<TextBox>(window, "RenamePresetBox");
            renameBox.Text = "Standard E";
            DialogTestKit.Click(DialogTestKit.Named<Button>(window, "RenamePresetSave"));
            Assert.NotEmpty(DialogTestKit.Named<TextBlock>(window, "RenamePresetError").Text ?? "");
            Assert.Equal("My DADGAD", Assert.Single(settings.TuningUserPresets).Name);
            Assert.Equal(1, saved);
            DialogTestKit.Named<TextBox>(window, "RenamePresetBox").Text = "Kept";
            DialogTestKit.Click(DialogTestKit.Named<Button>(window, "RenamePresetSave"));
            Assert.Equal(2, saved);
            Assert.Equal("Kept", Assert.Single(settings.TuningUserPresets).Name);
            window.FillPresetList();
            DialogTestKit.Click(Assert.Single(FlyoutButtons(window, "DeletePresetButton")));
            Assert.Empty(settings.TuningUserPresets);
            Assert.Equal(3, saved);
        }
        finally { Discard(window); }
    }

    // Cancel, then Yes on the discard question: applying a preset dirties the tuning draft, so closing asks first.
    private static void Discard(TrackPropertiesWindow window)
    {
        DialogTestKit.Click(DialogTestKit.Named<Button>(window, "CancelButton"));
        Dispatcher.UIThread.RunJobs();
        if (window.IsVisible) DialogTestKit.Click(DialogTestKit.Named<Button>(window.OwnedWindows.OfType<SavePromptWindow>().Single(), "YesButton"));
        Dispatcher.UIThread.RunJobs();
    }
}
