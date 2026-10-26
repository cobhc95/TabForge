using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Docking;
using TabForge.Services;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Avalonia Settings window: rail and card structure, rows and captions from PreferencesPagePresenter, the enable
/// rule of the Custom palette, values written, reset and the discard question, PNGs and contrast.
/// Tests: this class.
/// </summary>
public class PreferencesWindowTests
{
    /// <summary>Reset all settings: the values to apply carry the default dock layout as the saved Workspace, and applying them stores it.</summary>
    [AvaloniaFact]
    public void ResetAllSettingsStoresTheDefaultWorkspaceOnApply()
    {
        var saved = new AppSettings();
        var defaults = PreferencesCatalog.ResetAllDefaults();
        PreferencesCatalog.CopyInto(defaults, saved, includeWorkspace: true);
        // Each default layout gets fresh host ids, so the ids are left out of the comparison; the pane shape must match.
        static string Shape(object? layout) => System.Text.RegularExpressions.Regex.Replace(System.Text.Json.JsonSerializer.Serialize(layout), @"""HostId"":""[0-9a-f]*""", "");
        Assert.Equal(Shape(DockWorkspaceController.DefaultLayout()), Shape(saved.Workspace));
    }

    /// <summary>OK or Apply writes a changed value to the settings file: a fresh store opened on that file reads it back.</summary>
    [AvaloniaFact]
    public void ApplySavesTheChangedValueToTheSettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-prefs-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = MainWindowSettings.Open(path);
        var window = settings.CreatePreferencesWindow();
        window.Write("general.restorewindow", false);
        window.Apply();
        settings.Dispose();
        var reloaded = TabForge.Services.AppSettingsStore.Open(path, TimeSpan.Zero).Settings;
        Assert.False(PreferencesCatalog.Build(reloaded).Single(r => r.Key == "general.restorewindow").Get() is true);
        File.Delete(path);
    }

    /// <summary>Plain [Fact], no Avalonia App: the catalogue is installed by the assembly's module initializer, before any type use.</summary>
    [Fact]
    public void CatalogueIsInstalledByTheModuleInitializerWithoutAnApp()
    {
        var install = typeof(PreferencesCatalog).GetMethod("InstallCatalog", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.NotNull(install.GetCustomAttributes(typeof(System.Runtime.CompilerServices.ModuleInitializerAttribute), false).SingleOrDefault());
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(PreferencesCatalog).Module.ModuleHandle);
        Assert.NotNull(TabForge.Services.Features.CatalogComposition.Platform);
        Assert.NotEmpty(TabForge.Services.HotkeyCatalog.BuildMap(new TabForge.Services.HotkeySettings()));
    }

    [AvaloniaFact]
    public void RailListsTheWpfPagesUnderTheirCaptions()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        var rail = window.GetLogicalDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Settings pages");
        var items = rail.Items.OfType<ListBoxItem>().Select(RailLabel).ToList();
        Assert.Equal(new[] { "Common settings", "BASICS", "General", "Appearance", "MUSIC", "Score & Notation", "Fretboard & Keyboard", "Timeline & Tracks",
            "Editing", "SOUND", "Playback & Practice", "Audio & Plug-ins", "Recording", "SYSTEM", "Tabs & Windows", "Shortcuts", "Files & Backups", "Advanced" }, items);
        Assert.Equal("TabForge Settings", window.Title);
        foreach (var text in new[] { "Reset page…", "Manage settings", "Cancel", "Apply", "OK" })
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == text);
    }

    /// <summary>Every page row carries its glyph, and only the open page's glyph is selected.</summary>
    [AvaloniaFact]
    public void RailRowsCarryTheirGlyphAndTheOpenPageIsSelected()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.ShowPage(PreferencesCatalog.Appearance);
        var rail = window.GetLogicalDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Settings pages");
        var glyphs = rail.GetLogicalDescendants().OfType<SettingsNavigationIconView>().ToList();
        Assert.Equal(rail.Items.OfType<ListBoxItem>().Count(i => i.Tag is string), glyphs.Count);
        Assert.Equal(1, glyphs.Count(g => g.IsSelected));
        var appearanceRow = rail.Items.OfType<ListBoxItem>().Single(i => i.Tag as string == PreferencesCatalog.Appearance);
        Assert.True(appearanceRow.GetLogicalDescendants().OfType<SettingsNavigationIconView>().Single().IsSelected);
    }

    /// <summary>The page name of a rail row: a caption's text, or the name beside a page's glyph.</summary>
    private static string RailLabel(ListBoxItem item) => item.Content as string ?? ((TextBlock)((Grid)item.Content!).Children[1]).Text!;

    [AvaloniaFact]
    public void CardsAndRowsComeFromThePresenter()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        foreach (var page in new[] { PreferencesCatalog.General, PreferencesCatalog.Appearance })
        {
            window.ShowPage(page);
            var rows = PreferencesCatalog.Build(new TabForge.Services.AppSettings()).Where(r => r.Category == page).ToList();
            var cards = PreferencesPagePresenter.Cards(rows, searching: false);
            var borders = window.GetLogicalDescendants().OfType<Border>().Where(b => cards.Any(c => c.Title == b.Name)).Select(b => b.Name).ToList();
            Assert.Equal(cards.Select(c => c.Title), borders);
            Assert.Equal(cards.SelectMany(c => c.Basic.Concat(c.More)).Select(r => r.Key), window.ShownRows.Select(r => r.Key));
            foreach (var card in cards.Where(c => c.More.Count > 0))
                Assert.Contains(window.GetLogicalDescendants().OfType<ToggleButton>(), t => t.Content as string == $"More options ({card.More.Count})");
        }
        Assert.Equal(new[] { "Updates", "Window", "Video recording", "Video encoder (experimental)", "Confirmations" },
            PreferencesPagePresenter.Cards(PreferencesCatalog.Build(new TabForge.Services.AppSettings()).Where(r => r.Category == "General"), false).Select(c => c.Title));
    }

    [AvaloniaFact]
    public void CustomPaletteIsEnabledOnlyForTheCustomTheme()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.ShowPage(PreferencesCatalog.Appearance);
        Assert.False(window.IsRowEnabled("appearance.background"));
        Assert.True(window.IsRowEnabled("appearance.accent"));
        ((ComboBox)window.EditorOf("appearance.thememode")).SelectedItem = "Custom";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();   // the row change lands after the frame that paints the pick
        Assert.True(window.IsRowEnabled("appearance.background"));
        Assert.Equal("Custom", window.Working.Appearance.ThemeMode);
    }

    /// <summary>Colour rows are one colour button (swatch and name); picking a colour through it writes the value.</summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task ColourRowIsAColourButtonAndPickingWritesIt()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.ShowPage(PreferencesCatalog.Appearance);
        var button = Assert.IsType<TabForge.Avalonia.Platform.Controls.ColourButton>(window.EditorOf("appearance.accent"));
        Assert.StartsWith("Default", button.DisplayName);
        button.Chooser = _ => System.Threading.Tasks.Task.FromResult<string?>("#4C9AFF");
        await button.OpenAsync();
        Assert.Equal("#4C9AFF", window.Working.Appearance.Accent);
        Assert.Equal("Default glide blue", button.DisplayName);
    }

    /// <summary>One click on a colour in the open popup previews it in the working settings at once; the saved settings change only on Apply.</summary>
    [AvaloniaFact]
    public void AColourClickInThePopupPreviewsLiveAndSavesNothingUntilApply()
    {
        var saved = new TabForge.Services.AppSettings();
        var window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
        window.ShowPage(PreferencesCatalog.Appearance);
        var button = Assert.IsType<TabForge.Avalonia.Platform.Controls.ColourButton>(window.EditorOf("appearance.accent"));
        var before = saved.Appearance.Accent;
        window.Show();
        DialogTestKit.Click(button);
        var grid = button.ActivePicker!.Panel.GetLogicalDescendants().OfType<UniformGrid>().Single();
        var cell = (Button)grid.Children[2];
        DialogTestKit.Click(cell);
        var picked = new TabForge.Presenters.ColourPickerPresenter(before, before, new List<string>()).Presets[2].Hex;
        Assert.Equal(picked, window.Working.Appearance.Accent, ignoreCase: true);
        Assert.Equal(before, saved.Appearance.Accent);
        Assert.True(button.ActivePicker.Popup!.IsOpen);
        DialogTestKit.Click(button.ActivePicker.Panel.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel"));
        Assert.Equal(before, window.Working.Appearance.Accent, ignoreCase: true);
        window.Close();
    }

    [AvaloniaFact]
    public void WritesMatchTheCatalogueSettersAndApplyKeepsThemInMemory()
    {
        var saved = new TabForge.Services.AppSettings();
        var window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
        window.ShowPage(PreferencesCatalog.General);
        ((CheckBox)window.EditorOf("general.checkupdates")).IsChecked = false;
        var barChoice = TabForge.Services.BarRangePromptText.Choices[1];
        ((ComboBox)window.EditorOf("editing.bardelete")).SelectedItem = barChoice;
        window.Write("appearance.uiscale", 9.0);
        window.Write("appearance.thememode", "Light");

        // The values the setters store: the switch, the stored bar answer, the clamped scale and the light palette.
        var expected = new TabForge.Services.AppearanceSettings();
        ThemeColours.ApplyPreset(expected, light: true);
        Assert.False(window.Working.General.CheckForUpdates);
        Assert.Equal(TabForge.Services.BarRangePromptText.FromChoice(barChoice), window.Working.Editing.BarRangeDelete);
        Assert.Equal(1.5, window.Working.Appearance.UiScale);
        Assert.Equal(expected.Background, window.Working.Appearance.Background);

        var changes = window.PendingChanges();
        Assert.Contains("General › Check for updates automatically: On → Off", changes);
        Assert.Contains($"Appearance › Interface size: {PreferencesPagePresenter.ShowValue(1.0)} → {PreferencesPagePresenter.ShowValue(1.5)}", changes);
        Assert.False(saved.General.CheckForUpdates);   // the live settings already hold the previewed value
        window.Apply();
        Assert.False(saved.General.CheckForUpdates);
        Assert.Equal("All changes saved", window.StatusText);
    }

    /// <summary>The window opens at the remembered size: a stored size at or above the minimum opens as-is.</summary>
    [AvaloniaFact]
    public void SettingsWindowOpensAtTheRememberedSize()
    {
        var saved = new TabForge.Services.AppSettings { SettingsWindowWidth = 900, SettingsWindowHeight = 650 };
        var window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
        Assert.Equal(900, window.Width);
        Assert.Equal(650, window.Height);
        Assert.Equal(ChromeMetrics.SettingsWindowMinWidth, window.MinWidth);
        Assert.Equal(ChromeMetrics.SettingsWindowMinHeight, window.MinHeight);
    }

    /// <summary>A fresh settings file opens at the default size.</summary>
    [AvaloniaFact]
    public void SettingsWindowOpensAtTheDefaultSize()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        Assert.Equal(ChromeMetrics.SettingsWindowWidth, window.Width);
        Assert.Equal(ChromeMetrics.SettingsWindowHeight, window.Height);
        Assert.Equal(1040, window.Width);
        Assert.Equal(690, window.Height);
    }

    /// <summary>An unusable remembered size (zero, not a number, infinite, below the minimum) opens at the default instead.</summary>
    [AvaloniaFact]
    public void SettingsWindowFallsBackToTheDefaultSize()
    {
        foreach (var (width, height) in new[] { (0.0, 0.0), (double.NaN, 650.0), (900.0, double.PositiveInfinity), (700.0, 650.0), (900.0, 500.0) })
        {
            var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings { SettingsWindowWidth = width, SettingsWindowHeight = height });
            Assert.Equal(ChromeMetrics.SettingsWindowWidth, window.Width);
            Assert.Equal(ChromeMetrics.SettingsWindowHeight, window.Height);
        }
    }

    /// <summary>Cancel (closing without OK) reverts unapplied edits but keeps a resize: the size is chrome, written to the live settings directly.</summary>
    [AvaloniaFact]
    public void CancelKeepsTheResizedWindowSize()
    {
        var saved = new TabForge.Services.AppSettings();
        var window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
        window.Show();
        window.Write("general.restorewindow", false);
        Assert.False(saved.General.RestoreWindow);
        // A resize wrote here directly, as CaptureSize does on a bounds change.
        saved.SettingsWindowWidth = 1100;
        saved.SettingsWindowHeight = 720;
        window.Close();
        Assert.True(saved.General.RestoreWindow);
        Assert.Equal(1100, saved.SettingsWindowWidth);
        Assert.Equal(720, saved.SettingsWindowHeight);
    }

    /// <summary>Switching the output device shows that device's own buffer size and sample rate, edits go to the current device only, and Cancel restores them.</summary>
    [AvaloniaFact]
    public void SwitchingTheOutputDeviceShowsItsOwnBufferAndRateAndCancelRestores()
    {
        var saved = new AppSettings();
        var pl = saved.Plugins!;
        pl.Driver = AudioDrivers.DirectSound; pl.Device = "A"; pl.BufferSize = 128; pl.SampleRate = 48000;
        pl.OutputProfiles[PluginSettings.OutputKey(AudioDrivers.DirectSound, "B")] = new OutputProfile { BufferSize = 512, SampleRate = 96000 };
        var window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
        window.Show();
        window.ShowPage(SettingsCatalog.AudioVst);
        string Shown(string key) => (string)((ComboBox)window.EditorOf(key)).SelectedItem!;

        window.Write("vst.device", "B");
        Assert.Equal(("512", "96 kHz"), (Shown("vst.buffer"), Shown("vst.samplerate")));
        Assert.Equal((512, 96000), (pl.BufferSize, pl.SampleRate));
        window.Write("vst.buffer", "64");
        window.Write("vst.device", "A");
        Assert.Equal(("128", "48 kHz"), (Shown("vst.buffer"), Shown("vst.samplerate")));
        Assert.Equal((128, 48000), (pl.BufferSize, pl.SampleRate));
        Assert.Equal(64, pl.OutputProfiles[PluginSettings.OutputKey(AudioDrivers.DirectSound, "B")].BufferSize);

        window.Close();
        Assert.Equal(("A", 128, 48000), (pl.Device, pl.BufferSize, pl.SampleRate));
        Assert.Equal(512, pl.OutputProfiles[PluginSettings.OutputKey(AudioDrivers.DirectSound, "B")].BufferSize);
    }

    /// <summary>Writing a resize: usable bounds are stored, anything else is ignored, and an unchanged size writes nothing.</summary>
    [Fact]
    public void RememberedSizeKeepsOnlyUsableBounds()
    {
        var saved = new TabForge.Services.AppSettings();
        Assert.False(PreferencesWindow.TryRememberSize(double.NaN, 650, saved));
        Assert.False(PreferencesWindow.TryRememberSize(900, double.PositiveInfinity, saved));
        Assert.False(PreferencesWindow.TryRememberSize(700, 650, saved));
        Assert.False(PreferencesWindow.TryRememberSize(900, 500, saved));
        Assert.Equal(ChromeMetrics.SettingsWindowWidth, saved.SettingsWindowWidth);
        Assert.Equal(ChromeMetrics.SettingsWindowHeight, saved.SettingsWindowHeight);
        Assert.True(PreferencesWindow.TryRememberSize(900, 650, saved));
        Assert.Equal(900, saved.SettingsWindowWidth);
        Assert.Equal(650, saved.SettingsWindowHeight);
        Assert.False(PreferencesWindow.TryRememberSize(900, 650, saved));
    }

    /// <summary>Loaded sizes are bounded: not-a-number falls back to the default, the rest clamp like the main window.</summary>
    [Fact]
    public void RememberedSizeIsValidatedOnLoad()
    {
        var fallback = SettingsValidator.Normalize(new TabForge.Services.AppSettings { SettingsWindowWidth = double.NaN, SettingsWindowHeight = double.NaN });
        Assert.Equal(ChromeMetrics.SettingsWindowWidth, fallback.SettingsWindowWidth);
        Assert.Equal(ChromeMetrics.SettingsWindowHeight, fallback.SettingsWindowHeight);
        var clamped = SettingsValidator.Normalize(new TabForge.Services.AppSettings { SettingsWindowWidth = 100_000, SettingsWindowHeight = 10 });
        Assert.Equal(7_680, clamped.SettingsWindowWidth);
        Assert.Equal(480, clamped.SettingsWindowHeight);
    }

    [AvaloniaFact]
    public async Task ResetAndDiscardAskThePresenterQuestions()
    {
        var questions = new List<string>();
        var window = new PreferencesWindow(Host(questions, MessageBoxAnswer.Yes), new TabForge.Services.AppSettings());
        window.ShowPage(PreferencesCatalog.General);
        await window.ResetPageAsync();
        Assert.Empty(questions);
        Assert.Equal(PreferencesPagePresenter.NoChangedSettings, window.StatusText);

        window.Write("general.restorewindow", false);
        Assert.True(await window.ConfirmDiscardAsync());
        Assert.Contains(DiscardPreferencesPresenter.Message, questions.Last());
        Assert.Contains("General · Remember the window size: On → Off", questions.Last());
        await window.ResetPageAsync();
        Assert.Contains(PreferencesPagePresenter.ResetPage("General", 1).Message, questions.Last());
        Assert.True(window.Working.General.RestoreWindow);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreferencesRendersPngsInEachTheme(bool light)
    {
        using var theme = UseTheme(light);
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.ShowPage(PreferencesCatalog.Appearance);
        Assert.True(new FileInfo(SavePng(window, nameof(PreferencesWindowTests), $"preferences-{(light ? "light" : "dark")}.png")).Length > 0);
        window.Close();
    }

    [Theory]
    [InlineData(false, "TextPrimary")]
    [InlineData(false, "TextMuted")]
    [InlineData(true, "TextPrimary")]
    [InlineData(true, "TextMuted")]
    public void PreferencesTextReadsAtLeastFourAndAHalfToOne(bool light, string key)
    {
        foreach (var surface in new[] { "PanelBackground", "WindowBackground" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(key, light), AvaloniaPalette.Get(surface, light)) >= 4.5, $"{key} on {surface} (light: {light})");
    }

    private static AvaloniaDialogHost Host(List<string> questions, MessageBoxAnswer answer) => new()
    {
        Capture = dialog =>
        {
            questions.Add(string.Join("\n", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)));
            return answer;
        },
    };
}
