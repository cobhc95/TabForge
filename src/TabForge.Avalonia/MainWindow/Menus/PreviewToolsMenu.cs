using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;

namespace TabForge.Avalonia;

/// <summary>One preview dialog: its menu caption and what opens it.</summary>
public sealed record PreviewDialogEntry(string Header, Action Open);

/// <summary>
/// The preview's dialog list (shown in the Tools menu by MainWindowMenu): one entry for each dialog ported so far. Each item opens its dialog over the preview window with the
/// preview's sample state (the preview has no document, MIDI outputs, Keyboard mode or Guitar Pro export of its own).
/// Owns: the entries and the sample state they open with. Does not own: the dialogs (Views), the menu bar (MainWindow),
/// the texts and rules (the presenters).
/// Tests: TabForge.Avalonia.Tests.SongStatsWindowTests, TrackOutputWindowTests, GpExportPreflightWindowTests,
/// KeyboardModePopoutWindowTests (each opens its dialog from this menu), MixTableColourChooserEffectValuesTests, GroupRulesAndColourPickerTests,
/// ChordFinderWindowTests, PresetManagerWindowTests and MixerWindowTests.
/// ChordFinderWindowTests, PresetManagerWindowTests, FxChainWindowTests and MidiProcessingWindowTests.
/// ChordFinderWindowTests, PresetManagerWindowTests, MixerWindowTests and PreferencesWindowTests.
/// WiringWindowTests (the Wiring item).
/// UpdateAvailableWindowTests and QuarantineWindowTests (the Update available and Plug-ins switched off items).
/// WiringWindowTests (the Wiring item), ScoreTextStyleAndInstrumentPickerTests, PasteOptionsSpecialAndDeleteBarsTests (the paste and Delete bars items),
/// ClipSnapPluginDialogTests, BandViewTests (the Band view item), DockWorkspaceViewTests (the Dock preview item).
/// </summary>
public static class PreviewToolsMenu
{
    private static void OpenWiring(Window owner)
    {
        var tracks = WiringWindow.SampleTracks();
        var slot = WiringWindow.SampleSlot();
        tracks[0].Rig.Plugins.Add(slot);
        new WiringWindow(tracks, tracks[0], slot, change => change()).Show(owner);
    }

    /// <summary>The preview has no plug-in, so the preset names live in this list for the window only.</summary>
    // The Add plug-in window on the real scan and the main window's plug-in settings (the choice is not added anywhere).
    private static void OpenPluginBrowser(Window owner)
    {
        var settings = SettingsOf(owner);
        var session = new Views.Fx.PluginBrowserSession(settings?.Settings.Plugins ?? new PluginSettings(), TrustOf(owner), TabForge.Platform.PluginFileTrusts.Create(), () => settings?.SaveSettings());
        _ = PluginBrowserWindow.ChooseAsync(owner, session, new AvaloniaFileDialogs(), window => MainWindowSettings.ShowPreferencesPage(settings, owner, SettingsCatalog.AudioVst));
    }

    // The MIDI processing window on a sample plug-in of a track that belongs to no song; its edits apply at once.
    private static void OpenMidiProcessing(Window owner)
    {
        var settings = SettingsOf(owner);
        var slot = MidiProcessingWindow.SampleSlot();
        var track = new TrackModel { Name = "Sample track", Rig = { Plugins = { slot } } };
        var host = new StandaloneFxChainHost(settings?.Settings.Plugins ?? new PluginSettings(), settings?.Settings.Hotkeys ?? new HotkeySettings(), TrustOf(owner), null, new[] { track });
        new MidiProcessingWindow(host, track, slot, apply => apply()).Show(owner);
    }

    private static void OpenPluginPreset(Window owner)
    {
        var names = new List<string> { "Warm", "Bright" };
        DialogLauncher.Ask<PluginPresetWindow, string?>(owner, () => new PluginPresetWindow(new AvaloniaDialogHost(), "Sample Reverb", () => names.ToList(), name => names.Remove(name)));
    }

    /// <summary>The preview dialogs as caption + opener pairs; MainWindowMenu makes the menu items and MainWindowCommands claims the ones a main-menu row also names.</summary>
    public static IReadOnlyList<PreviewDialogEntry> Entries(Window owner)
    {
        var entries = new List<PreviewDialogEntry>();
        AddToolEntries(entries, owner);
        AddEditorEntries(entries, owner);
        AddRenderEntries(entries, owner);
        AddWindowEntries(entries, owner);
        return entries;
    }

    /// <summary>The finders, the tuning, the song and the track-setting dialogs.</summary>
    private static void AddToolEntries(List<PreviewDialogEntry> entries, Window owner)
    {
        entries.Add(new("MIDI output…", () => OpenMidiOutput(owner)));
        entries.Add(new("Keyboard mode: view…", () => OpenKeyboardView(owner)));
        entries.Add(new("Export .gp file…", () => OpenGpPreflight(owner)));
        // The preview has no score, so the text styles are held for this window only and nothing is redrawn.
        entries.Add(new("Score text & fonts…", () => DialogLauncher.Ask<ScoreTextStyleWindow, bool?>(owner, () => new ScoreTextStyleWindow(new Dictionary<string, ScoreTextAreaStyle>(),
            FontManager.Current.SystemFonts.Select(f => f.Name)))));
        // The picker lists the shared instrument catalogue, as the track properties picker does.
        entries.Add(new("Choose instrument…", () => DialogLauncher.Ask<InstrumentPickerWindow, string?>(owner, () => new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), "Distortion Guitar"))));
        entries.Add(new("Project settings…", () => new ProjectSettingsWindow(SampleProjectSettings, trackCount: 2, barCount: 16, sectionCount: 2).ShowDialog(owner)));
    }

    /// <summary>The effect editors, the save and paste prompts, the plug-in and wiring dialogs and the track dialogs.</summary>
    private static void AddEditorEntries(List<PreviewDialogEntry> entries, Window owner)
    {
        // The preview has no note, so the effect editors open with sample values and keep saved presets for this window only.
        entries.Add(new("Trill editor…", () => OrnamentEditorWindows.Trill(5, 2, 16, SamplePresets, false, SavePreview).ShowDialog(owner)));
        entries.Add(new("Grace note editor…", () => OrnamentEditorWindows.Grace(5, -2, false, true, 32, 4, 0, SamplePresets, false, SavePreview).ShowDialog(owner)));
        entries.Add(new("Harmonic editor…", () => OrnamentEditorWindows.Harmonic(0, 12, SamplePresets, false, SavePreview).ShowDialog(owner)));
        entries.Add(new("Save changes prompt…",() => new SavePromptWindow(SavePromptPresenter.SaveChanges("Untitled song")).ShowDialog(owner)));
        entries.Add(new("Discard changes prompt…", () => OpenDiscardPrompt(owner)));
        entries.Add(new("Paste options…", () => DialogLauncher.Ask<PasteOptionsWindow, PasteAnswers?>(owner, () => new PasteOptionsWindow(PasteQuestionInfo.All))));
        entries.Add(new("Paste special…", () => DialogLauncher.Ask<PasteSpecialWindow, PasteSpecialOptions?>(owner, () => new PasteSpecialWindow(bars: true))));
        entries.Add(new("Delete bars prompt…", () => DialogLauncher.Ask<BarRangePromptWindow, BarRangeAnswer?>(owner, () => new BarRangePromptWindow("Bars 1-2 are selected. What should Delete do?", BarRangeAction.Clear, true, _ => ""))));
        entries.Add(new("Add plug-in…", () => OpenPluginBrowser(owner)));
        // The preview has no song, so the wiring window opens on its sample routing graph (see WiringWindow.SampleTracks).
        entries.Add(new("Wiring…", () => OpenWiring(owner)));
        entries.Add(new("Update available…", () => new UpdateAvailableWindow(new UpdateAvailablePresenter(UpdateAvailableWindow.SampleRelease, "0.1.0-beta.1", checkAutomatically: true)).Show(owner)));
        entries.Add(new("Plug-ins switched off…", () => new QuarantineWindow(QuarantineWindow.SamplePaths).Show(owner)));
        entries.Add(new("Colour picker…", () => _ = new ColourPickerPopup(ColourHex.FormatAuto(AccentRgba(owner)), ColourHex.FormatAuto(AccentRgba(owner)), SampleRecentColours()).AskAsync(owner, PlacementMode.Center)));
    }

    /// <summary>The video export and tutorial dialogs; the video settings are held for this window only. The render dialog opens from File (MainWindowFileExports).</summary>
    private static void AddRenderEntries(List<PreviewDialogEntry> entries, Window owner)
    {
        var videoSettings = new VideoSettings();
        entries.Add(new("Export video (MP4)…", () => OpenVideoExport(owner, videoSettings)));
    }

    /// <summary>The mixer, presets, FX and MIDI windows, settings, the clip and snap dialogs, the input picker, the dock preview and the Band view.</summary>
    private static void AddWindowEntries(List<PreviewDialogEntry> entries, Window owner)
    {
        // The preview has no plug-in scan or audio engine, so the FX chain opens with a sample chain; its MIDI... opens the MIDI list.
        entries.Add(new("FX chain…", () => FxChainWindows.OpenFor(owner, FxChainWindow.SampleTrack())));
        entries.Add(new("MIDI processing…", () => OpenMidiProcessing(owner)));
        // Settings opens on the main window's live settings store, so OK and Apply save to the settings file.
        entries.Add(new("Settings…", () => MainWindowSettings.ShowPreferencesPage(SettingsOf(owner), owner, SettingsCatalog.Home)));
        // The preview has no song or plug-in, so the clip, snap and plug-in dialogs open on sample data and change only that.
        entries.Add(new("Audio clip properties…", () => DialogLauncher.Ask<ClipPropertiesWindow, bool>(owner, () => new ClipPropertiesWindow(ClipPropertiesWindow.SampleClip()))));
        entries.Add(new("Snap / grid settings…", () => TabForge.Avalonia.Views.TransportPopups.SnapSettingsPopup.Create(new SnapSettings(), () => { }).OpenAbove(owner)));
        entries.Add(new("Save song with audio settings…", () => DialogLauncher.Ask<PluginSaveWindow, string?>(owner, () => new PluginSaveWindow("song.gp"))));
        entries.Add(new("Save plug-in preset…", () => OpenPluginPreset(owner)));
        entries.Add(new("Track input…", () => AudioInputPicker.SampleWindow(AvaloniaUiThread.ForApplication(), TabForge.Audio.MidiInputCapture.DeviceNames).ShowDialog(owner)));
        // The preview has no View menu yet, so the Band view opens from here on the demo song.
        entries.Add(new("Dock preview…", () => new Views.Docking.DockPreviewWindow().Show(owner)));
        entries.Add(new("Band view…", () => new BandWindow(new TabForge.Documents.SongDocument(() => null) { Project = DemoSongFactory.Create() }).Show(owner)));
    }

    /// <summary>The live settings store of <paramref name="owner"/> when it is a main window; null (defaults in memory) otherwise.</summary>
    internal static MainWindowSettings? SettingsOf(Window owner) => (owner as MainWindow)?.Settings;

    // The preview tools open over the main window; its registry holds the app's one trust state.
    private static TabForge.Plugins.PluginTrust TrustOf(Window owner) => (owner as MainWindow)?.Windows.Services.Trust ?? throw new InvalidOperationException("The preview tools need the main window.");

    private static void OpenVideoExport(Window owner, VideoSettings settings) =>
        new VideoExportWindow(DemoSongFactory.Create(), settings, hasSelection: false).ShowDialog(owner);

    // The preview loads the guides the app ships; a build without them shows the window's "guide isn't here yet" message.
    internal static void OpenTutorial(Window owner, TutorialGuide requested = TutorialGuide.Basic) => new TutorialWindow(
        TutorialLibrary.Load(TutorialLibrary.GuideFolder(TutorialGuide.Basic)),
        TutorialLibrary.Load(TutorialLibrary.GuideFolder(TutorialGuide.Detailed)),
        requested).Show(owner);

    private static readonly ProjectSettingsResult SampleProjectSettings = new("Demo song", "", "TabForge", "", "", "", "", "",
        120, 4, 4, 0, false, true, "", "", "");

    private static readonly EffectValuesPreset[] SamplePresets =
        { new("Default", true, new Dictionary<string, double>()), new("Wide", false, new Dictionary<string, double> { ["Step"] = 3 }) };

    // The preview has no colour row, so the colour picker opens with sample recent colours.
    private static List<string> SampleRecentColours() => ["#14B8A6", "#F59E0B", "#8B5CF6"];

    // The colour the preview's colour pickers start from: the applied accent, read when the picker opens.
    private static Rgba AccentRgba(Control owner) => AvaloniaPalette.Get("Accent", owner.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light) is var accent ? new(accent.R, accent.G, accent.B) : default;

    private static EffectValuesPreset SavePreview(IReadOnlyDictionary<string, double> values) => new("My preset", false, values);

    // The discard question of a settings window: Yes closes without applying, so it has no Cancel and asks "Don't ask me again".
    private static void OpenDiscardPrompt(Window owner)
    {
        var prompt = new SavePrompt("TabForge", "Close without applying the changes?", "Close without applying", "Keep editing");
        var changes = new[] { "Changed the tempo", "Renamed track 2" };
        new SavePromptWindow(prompt, showCancel: false, details: changes, rememberText: "Don't ask me again").ShowDialog(owner);
    }

    private static void OpenMidiOutput(Window owner)
    {
        var track = DemoSongFactory.Create().Tracks[0].Name;
        new TrackOutputWindow(track, TrackOutputWindow.SampleDevices, current: 0, chose: _ => { }, test: () => { }).ShowDialog(owner);
    }

    private static void OpenKeyboardView(Window owner)
    {
        // The preview's Keyboard mode is on and no pop-out is open, so the pop-out may open.
        if (KeyboardModePopoutPresenter.CanOpen(alreadyOpen: false, modeOn: true, keysDetached: false))
            new KeyboardModePopoutWindow().Show(owner);
    }

    private static void OpenGpPreflight(Window owner) =>
        new GpExportPreflightWindow(GpExportPreflightWindow.SampleReport(), GpExportKind.Export, "Demo song.gp", _ => { }).ShowDialog(owner);

}
