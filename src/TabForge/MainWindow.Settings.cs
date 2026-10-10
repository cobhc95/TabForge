using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// Owns: the window settings flow: load, the ordered apply (SyncFromSettings; the appliers are in MainWindow.SettingsApply.cs), save, panel visibility, hotkeys and menu gestures, and MIDI output devices.
// Does not own: the settings store and file (AppSettingsStore, SettingsFileService) and the Preferences editors.
// Tests: TestSettingsStoreSharedAcrossWindows.
public partial class MainWindow
{
    // ---------- settings ----------

    /// <summary>
    /// R-09: every window works on the app's one settings object (<see cref="AppSettingsStore.Shared"/>, owned by App); no window
    /// keeps or saves its own copy. The category objects are edited in the Preferences window.
    /// </summary>
    private readonly AppSettingsStore _settingsStore = AppSettingsStore.Shared;
    private AppSettings _settings { get => _settingsStore.Settings; set => _settingsStore.Replace(value, this); }
    /// <summary>The settings file could not be read: defaults are active and nothing is saved until Preferences are applied.</summary>
    private bool _settingsLoadFailed => _settingsStore.LoadFailed;
    private readonly HotkeyMaps _hotkeys = new();
    private bool _confirmOnClose = true;

    private NotationMode PreferredNotation =>
        _settings.NotationPreferenceSet &&
        Enum.TryParse<NotationMode>(_settings.Notation, true, out var mode) &&
        Enum.IsDefined(typeof(NotationMode), mode)
            ? mode
            : NotationMode.TabAndStaff;

    internal static string SettingsPath => AppSettingsStore.DefaultPath;

    /// <summary>Applies the shared settings (loaded once by the store) to this new window and follows other windows' changes.</summary>
    private void LoadSettings()
    {
        var store = _settingsStore;
        Subscribe<Action<object?>>(h => store.Changed += h, h => store.Changed -= h, OnSharedSettingsChanged);
        Subscribe<Action<object?, Exception>>(h => store.SaveFailed += h, h => store.SaveFailed -= h, OnSharedSettingsSaveFailed);
        _suppressWorkspaceSave = true;
        if (_settings.Workspace is not null)
        {
            _dockWorkspace?.RestoreLayout(_settings.Workspace);
        }
        else if (_dockWorkspace is not null)
        {
            // Migrate the earlier fixed-layout visibility and floating-controller preferences.
            _dockWorkspace.RestoreLayout(null);
            _dockWorkspace.SetPanelVisible("instrument", _settings.Appearance.ShowFretboard);
            _dockWorkspace.SetPanelVisible("timeline", _settings.Appearance.ShowArrangementOverview);
        }
        if (_dockWorkspace is not null)
        {
            _dockWorkspace.InstrumentAtBottom = _settings.Appearance.FretboardAtBottom;
            if (_settings.Workspace is null && _settings.Appearance.FretboardAtBottom) _dockWorkspace.SetInstrumentPosition(true);
        }
        WorkspaceLayouts.ApplyInstrumentSizeLock();
        // Full notation + TAB is the fallback. Retain a valid mode from older settings even if its
        // explicit-preference marker predates the current schema.
        if (!_settings.NotationPreferenceSet)
        {
            if (!Enum.TryParse<NotationMode>(_settings.Notation, true, out var savedMode) ||
                !Enum.IsDefined(typeof(NotationMode), savedMode))
                _settings.Notation = nameof(NotationMode.TabAndStaff);
            _settings.NotationPreferenceSet = true;
        }
        if (string.Equals(_settings.Hotkeys["Tab.New"], "Ctrl+Shift+T", StringComparison.OrdinalIgnoreCase))
            _settings.Hotkeys["Tab.New"] = "";
        if (string.Equals(_settings.Hotkeys["Bar.TimeSignature"], "Ctrl+T", StringComparison.OrdinalIgnoreCase))
            _settings.Hotkeys["Bar.TimeSignature"] = "Ctrl+Shift+T";
        _settings.General.AssociateFiles = Environment.ProcessPath is { } exePath && FileAssociations.IsRegistered(exePath); // registry is the truth
        SyncFromSettings(applyWindowSize: true);
        _suppressWorkspaceSave = false;
        WorkspaceLayouts.RefreshDockPanelsMenu();
    }

    /// <summary>
    /// Another window changed the shared settings: key bindings take effect here at once, and a replaced settings object (its Settings
    /// window previewed, applied or cancelled) is applied here too, except the audio route (a re-apply of that would move the engine to this window's song; see R-10).
    /// </summary>
    private void OnSharedSettingsChanged(object? source)
    {
        if (ReferenceEquals(source, this) || !_mainWindowInitialized || _applied.SharedRefreshQueued) return;
        _applied.SharedRefreshQueued = true;   // coalesced: many saves in a row cost one refresh
        PostIfOpen(() =>
        {
            _applied.SharedRefreshQueued = false;
            BuildHotkeyMap();
            RefreshHotkeyTooltips();
            // Another window's Settings window replaced the shared object: show it here too (the audio engine stays with this window's song).
            if (ReferenceEquals(_applied.Settings, _settings)) return;
            SyncFromSettings(applyWindowSize: false, applyAudioRoute: false);
            if (_applied.VisualChanged) RepaintAfterVisualSettings();
        }, DispatcherPriority.Background);
    }

    private void OnSharedSettingsSaveFailed(object? source, Exception error)
    {
        if (_isClosed || (source is not null && !ReferenceEquals(source, this))) return;
        StatusText.Text = "Settings could not be saved. Check access to the TabForge settings folder.";
    }

    /// <summary>Pushes settings into runtime state, the view toggles, the theme and the hotkey map.</summary>
    // What the visual settings looked like when last applied: re-theming and re-laying-out the score is
    // skipped when a settings change did not touch them (it would stutter the playhead while playing).
    private readonly AppliedSettings _applied = new();

    // The call order is the original order of one long method; keep it. One data dependency: the playhead's duration glow reads
    // the colour and opacity that ApplyFollowSettings put on Editor.Appearance. The window size applies only at load. The refreshes
    // run after every value is in place, and the engine route is last.
    private void SyncFromSettings(bool applyWindowSize, bool applyAudioRoute = true)
    {
        foreach (var doc in _documents.Documents) AttachAppRules(doc.Project);   // the settings object may have been replaced
        _applied.TakeVisual(_settings);
        _applied.Settings = _settings;
        var s = _settings;

        ApplyInstrumentAndEditingSettings(s);
        ApplyTransportAndGeneralSettings(s);
        ApplyFollowSettings(s);
        ApplyScoreAppearanceSettings(s);
        ApplyPlayheadSettings(s);
        ApplyScoreLayoutSettings(s);
        ApplyTimelineSettings(s);
        ApplyIconSize(s.Appearance.IconSize); // UI scale is applied to the whole window by ThemeService
        ApplyWindowSize(s, applyWindowSize);
        ApplyViewToggleSettings(s);
        RefreshAfterSettings();
        if (applyAudioRoute) ApplyAudioRouteSettings();
    }

    /// <summary>Applies the Appearance settings to the live UI (colours, font, density, paper).</summary>
    private void ApplyAppearance()
    {
        _options.Visual.TrackTint = _settings.Appearance.TrackTintPercent / 100.0;
        _options.Visual.MutedDim = _settings.Appearance.MutedTrackDimPercent / 100.0;
        if (_settings.Appearance.ThemePresetVersion < 1)
        {
            // Settings from before theme presets: give the colour fields the chosen theme's palette once.
            ThemeService.ApplyPreset(_settings.Appearance, _settings.Appearance.ThemeMode);
            _settings.Appearance.ThemePresetVersion = 1;
        }
        ThemeService.Apply(_settings.Appearance, this);
        var light = string.Equals(_settings.Appearance.ScorePaper, "Light", StringComparison.OrdinalIgnoreCase);
        foreach (var document in _documents.Documents) document.DarkPaper = !light;
        Editor.Appearance.DarkPaper = !light;
        Editor.InvalidateScoreLayout();
        Playhead.InvalidateVisual();
        ApplyScorePageBackground();
        // Code-drawn surfaces pick up the theme palette on their next render.
        if (_mainWindowInitialized) RefreshArrangement();   // muted rows take the dimming strength when they are built
        Arrangement.InvalidateTimeline();
        Instrument.InvalidateVisual();
        RefreshToolsPalette(); // palette icons are tinted in code, so they need the new text colour
        foreach (Window window in Application.Current.Windows)
            foreach (var icon in FindVisuals<SvgIconView>(window)) icon.InvalidateVisual(); // icon art adapts to light/dark
    }

    private void ApplyScorePageBackground()
    {
        if (ScorePage is null || Editor is null) return;
        ScorePage.Background = (Brush)FindResource(Editor.Appearance.DarkPaper ? "PaperDarkBrush" : "PaperLightBrush");
        ScorePage.BorderBrush = (Brush)FindResource("BorderBrush");
    }

    private void ApplyPanelVisibility()
    {
        MainToolbar.Visibility = _settings.General.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        MainStatusBar.Visibility = _settings.General.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = _settings.Appearance.ShowTabStrip ? Visibility.Visible : Visibility.Collapsed;
        InstrumentViewMenu.IsChecked = _dockWorkspace?.IsPanelVisible("instrument") == true;
        ArrangementMenu.IsChecked = _dockWorkspace?.IsPanelVisible("timeline") == true;
        WorkspaceLayouts.RefreshDockPanelsMenu();
    }

    /// <summary>Maps every effective key gesture to its command id.</summary>
    private void BuildHotkeyMap()
    {
        _hotkeys.Rebuild(_settings.Hotkeys);
        RefreshMenuGestures();
    }

    /// <summary>The key text beside every main-menu item, read from the live bindings (blank when the command is unbound).</summary>
    private void RefreshMenuGestures() => MenuHotkey.Apply(MainMenu, MenuKey);

    /// <summary>Runs a catalogued command: the routers, then the switch for commands with conditions, then the plain-command table. Returns false when this window has no handler for it.</summary>
    private bool RunHotkey(string id)
    {
        // Note/beat commands belong to the editor (and are tested there without a window).
        if (Editor.Effects.TryRunNoteCommand(id)) return true;
        if (RunPaneHotkey(id)) return true;
        if (RunEffectEditorHotkey(id)) return true;
        switch (id)
        {
            case var tool when tool.StartsWith("Tool.", StringComparison.Ordinal):
                ToolsPaletteButton_Click(new Button { Tag = tool[5..] }, new RoutedEventArgs()); return true;
            case "Reader.ReadBar": { var text = Editor.Describer.Bar(); Editor.AnnounceText(text); StatusText.Text = text; return true; }
            case "Reader.ReadPosition": { var text = Editor.Describer.Position(); Editor.AnnounceText(text); StatusText.Text = text; return true; }
            case "View.ToggleTrackLines": Arrangement.ToggleTrackLines(); SaveTimelineAppearance(); return true;
            case "View.CyclePlayheadStyle": _settings.Timeline.PlayheadStyle = PlayheadStyles.Next(_settings.Timeline.PlayheadStyle); Arrangement.PlayheadStyle = _settings.Timeline.PlayheadStyle; SaveSettings(); StatusText.Text = $"Playback position marker: {_settings.Timeline.PlayheadStyle}"; return true;
            case "Timeline.Snap": _settings.Timeline.Snap.Enabled = !_settings.Timeline.Snap.Enabled; Arrangement.UpdateSnapButton(); SaveSettings(); StatusText.Text = _settings.Timeline.Snap.Enabled ? "Snapping on" : "Snapping off"; return true;
            case "Track.Arm": if (SelectedTrack is { } armTrack) ToggleArm(armTrack); return true;
            case "Mixer.GroupFx": if (SelectedTrack is { } groupTrack) OpenBusFx(MixerGroups.GroupOf(_project, groupTrack)); return true;
            case var clip when HotkeyCatalog.IsClipAction(clip): return _clips.RunHotkey(Doc, clip);   // from the command palette: the selected clip (keys reach it first through the clip map)
            case var range when HotkeyCatalog.IsRangeAction(range): return RunRangeHotkey(range);   // from the command palette: the selected bars
            case "Track.ConvertToAudio": case "TrackRow.Copy": case "TrackRow.Cut": case "TrackRow.Paste": case "TrackRow.Duplicate": case "TrackRow.Delete":
                return TrackFlow.RunHotkey(id, TrackMixerGrid.SelectedIndex);   // from the command palette: the selected track
        }
        return Commands.TryRun(id);   // the plain forwarding commands (MainWindow.Commands.cs)
    }

    /// <summary>The scale-highlight, string-spacing and Band view commands of <see cref="RunHotkey"/>; the plain fretboard, layout and zoom commands are in the command table.</summary>
    private bool RunPaneHotkey(string id)
    {
        switch (id)
        {
            case "View.ScaleHighlightBrighter":
            case "View.ScaleHighlightDimmer":
            {
                var step = id =="View.ScaleHighlightBrighter" ? ScaleHighlightStyles.StrengthStep : -ScaleHighlightStyles.StrengthStep;
                _settings.Editing.ScaleHighlightStrength = Math.Clamp(_settings.Editing.ScaleHighlightStrength + step, ScaleHighlightStyles.MinStrength, ScaleHighlightStyles.MaxStrength);
                InstrumentPane.SetInstrumentAppearance();
                StatusText.Text = $"Scale highlight strength: {_settings.Editing.ScaleHighlightStrength}%";
                return true;
            }
            case "View.CycleStringSpacing": InstrumentPane.SetInstrumentAppearance(stringSpacing: FretStringSpacings.Next(_settings.Editing.FretStringSpacing)); StatusText.Text = $"Fretboard string spacing: {_settings.Editing.FretStringSpacing}"; return true;
            // The switch owns the Band.* keys: Band.Run returns false while the Band view is disposed, so the key is not consumed here.
            // The Band feature module registers these ids for the catalogue only.
            case "Band.ToggleTrackRow": case "Band.RowsMore": case "Band.RowsFewer": case "Band.CycleLaneContent": case "Band.CycleLaneLayout": case "Band.CycleInstrumentSize": case "Band.ToggleSmoothFollow": case "Band.ResetRowHeights": return Band.Run(id);   // false while the Band view is disposed, so the key is not consumed
        }
        return false;
    }

    /// <summary>
    /// Key bindings changed (rebind, preset, import): every control bound with <see cref="Views.TooltipShortcuts"/> in every
    /// window shows the new key at once. Event-driven; nothing polls.
    /// </summary>
    private void RefreshHotkeyTooltips()
    {
        Views.TooltipShortcuts.SetHotkeys(_settings.Hotkeys);
        ToolPalette.RefreshTooltips();
    }

    private static Color ApplyOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(color.A * Math.Clamp(opacity, 0, 1)), 0, 255),
            color.R, color.G, color.B);

    private static string ColourToHex(Color c) => Visualization.ColourText.Hex(c);

    /// <summary>
    /// Icon glyphs are drawn from vector geometry at a configurable size, so the whole chrome follows
    /// one number (the icon styles read these resources).
    /// </summary>
    private void ApplyIconSize(double size)
    {
        Resources["IconSize"] = size;
        Application.Current.Resources["IconSize"] = size;
    }

    private bool _probeNoSave;

    private void SaveSettings()
    {
        if (_probeNoSave) return; // test probes never write the user's settings file
        if (_settingsLoadFailed)
        {
            StatusText.Text = "Settings were not saved because the existing settings file could not be loaded. Review and Apply Preferences to replace it.";
            return;
        }
        try
        {
            CaptureWindowState();
            // R-09: the shared store writes the file (debounced, atomic) and collects plug-in state files after it; a failure comes
            // back through OnSharedSettingsSaveFailed.
            _settingsStore.MarkChanged(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Settings could not be saved: {ex}");
            StatusText.Text = "Settings could not be saved. Check access to the TabForge settings folder.";
        }
    }

    /// <summary>Copies the window's own state (menu toggles, current note value, speed, layout, window size) into the shared settings, so a save or the Settings window starts from what the window shows.</summary>
    private void CaptureWindowState()
    {
        var s = _settings;
        s.ShowInstrument = InstrumentViewMenu.IsChecked;
        s.ShowArrangement = ArrangementMenu.IsChecked;
        s.DarkPaper = Editor.Appearance.DarkPaper;
        s.LeftHanded = InstrumentPane.LeftHanded;
        s.ShowNoteNames = InstrumentPane.ShowNoteNames;
        s.PreviewNotes = _previewNotes;
        s.PreviewHorizon = InstrumentPane.PreviewHorizon;
        s.ScaleHighlight = InstrumentPane.ScaleHighlight;
        s.FretboardFrets = InstrumentPane.FretboardFrets;
        s.ZoomFactor = ScoreZoom.Factor;
        s.Metronome = _transport.Metronome;
        s.CountIn = _transport.CountIn;
        s.Speed = _transport.Speed;

        s.Editing.LeftHanded = InstrumentPane.LeftHanded;
        s.Editing.ShowNoteNames = InstrumentPane.ShowNoteNames;
        s.Editing.DefaultDuration = Editor.CurrentDurationDenominator;
        s.Editing.AutoAdvance = Editor.AutoAdvanceAfterEntry;
        s.Editing.PreviewHorizon = InstrumentPane.PreviewHorizon == 0
            ? s.Editing.PreviewHorizon
            : InstrumentPane.PreviewHorizon;
        s.Editing.PreviewNotesEnabled = InstrumentPane.PreviewHorizon > 0;
        s.Editing.ScaleHighlight = InstrumentPane.ScaleHighlight;
        s.Editing.FretboardFrets = InstrumentPane.FretboardFrets;
        s.Editing.ScoreWheelScrollPixels = _scoreWheelScrollPixels;
        s.Audio.PreviewNotes = _previewNotes;
        s.Audio.Metronome = _transport.Metronome;
        s.Audio.CountIn = _transport.CountIn;
        s.Audio.Speed = _transport.Speed;
        s.Audio.MetronomeSubdivision = s.Audio.MetronomeSubdivision is 1 or 2 or 3 or 4 ? s.Audio.MetronomeSubdivision : 1;
        s.General.AutoScroll = _follow.Mode != FollowModes.Off;
        var workspace = _learnMode?.RealLayout ?? _dockWorkspace?.CaptureLayout();   // Keyboard mode on: the arrangement it replaced
        s.Workspace = workspace;
        _follow.WriteSettings(s.Follow);
        s.Appearance.IconSize = _settings.Appearance.IconSize;
        s.Appearance.ScoreSpacing = Editor.Appearance.ScoreSpacing;
        s.Appearance.LedgerLines = Editor.Appearance.LedgerLines.ToString();
        s.Appearance.FretboardPosition = Instrument.HorizontalPosition.ToString();
        s.Follow.HighlightPlayedBeat = Editor.Appearance.HighlightPlayedBeat;
        s.Follow.HighlightColour = ColourToHex(Editor.Appearance.PlaybackColor);
        s.Follow.HighlightBackground = ColourToHex(Editor.Appearance.HighlightBackground);
        s.Follow.PlayheadColour = ColourToHex(Playhead.CurrentColor);
        s.Follow.DurationGlowColour = ColourToHex(Editor.Appearance.DurationGlowColor);
        s.Follow.DurationGlowOpacity = Editor.Appearance.DurationGlowOpacity;
        s.Follow.SectionGlowIntensity = Arrangement.SectionGlowIntensity;
        s.Appearance.ShowFretboard = workspace is not null && DockWorkspace.PanelsOf(workspace).Contains("instrument");
        s.Appearance.ShowArrangementOverview = _dockWorkspace?.IsPanelVisible("timeline") == true;
        s.Appearance.ScorePaper = Editor.Appearance.DarkPaper ? "Dark" : "Light";

        s.WindowWidth = WindowState == WindowState.Maximized ? RestoreBounds.Width : Width;
        s.WindowHeight = WindowState == WindowState.Maximized ? RestoreBounds.Height : Height;
        s.Maximised = WindowState == WindowState.Maximized;
        s.Tabs = _tabSettings;
    }

    private void ResetDockWorkspace_Click(object sender, RoutedEventArgs e)
        => _dockWorkspace?.ResetAllPanels();

    private void ApplyLayout()
    {
        SetTransportActive(MetronomeButton, _transport.Metronome);
        SetTransportActive(CountInButton, _transport.CountIn);
        SetTransportActive(LoopButton, _loop);
        ApplyNotationFromSettings();
    }

    private void ApplyNotationFromSettings()
    {
        var s = _settings;
        // The default display applies to new tabs; changing it in Settings also switches the open tab
        // (like the View menu). Other settings changes leave each tab's own display alone.
        var preferred = PreferredNotation;
        if (_applied.Notation is { } previous && previous != preferred)
        {
            Editor.Notation = preferred;
            Doc.Notation = preferred;
            Editor.InvalidateMeasure();
            Editor.InvalidateScoreLayout();
        }
        _applied.Notation = preferred;
        Editor.Appearance.DarkPaper = !string.Equals(s.Appearance.ScorePaper, "Light", StringComparison.OrdinalIgnoreCase);
        ApplyScoreViewFromSettings();
    }

    /// <summary>Opens a .tforge or Guitar Pro file into a new tab (used by the file argument).</summary>
    /// <remarks>A score file imports in the background and opens when done; the returned task completes then.</remarks>
    public Task OpenDocumentFromPath(string path, bool replaceCurrent = false, bool replaceAll = false)
    {
        var job = OpenScore(path, replaceCurrent, replaceAll, (opened, loaded) =>
        {
            if (loaded) StatusText.Text = $"Opened {Path.GetFileName(path)}" + (opened.Notice is { } notice ? $" — {notice}" : "");
        });
        return job?.Completion ?? Task.CompletedTask;
    }

    /// <summary>
    /// Startup file replaces the initial blank document so the requested song is the only tab.
    /// </summary>
    /// <summary>A song double-clicked in Explorer while this window runs: open it in a new tab and come forward.</summary>
    public void OpenFromAnotherLaunch(string path, bool activate = true)
    {
        if (!SingleInstanceService.IsOpenableSong(path)) { TabForge.Services.Trace.Write("ui", $"OPEN hand-over refused {path}"); return; }
        TabForge.Services.Trace.Write("ui", $"OPEN hand-over opening {path}");
        OpenDocumentFromPath(path);
        if (activate) OwnerActivation.BringToFront(this);
    }

    /// <param name="background">False for probe / tour launches, which expect the song open when this returns.</param>
    public void OpenStartupFile(string path, bool background = true)
    {
        OpenScore(path, replaceCurrent: true, replaceAll: true, (opened, loaded) =>
        {
            if (loaded) StatusText.Text = $"Opened {Path.GetFileName(path)}" + (opened.Notice is { } notice ? $" — {notice}" : "");
        }, background);
    }

    public void ReportStartupFileMissing(string path)
    {
        StatusText.Text = $"File not found: {path}";
        MessageBox.Show(this, $"Could not find the file:\n\n{path}", "TabForge", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RefreshMidiDevices_Click(object sender, RoutedEventArgs e) => RefreshMidiDevices();
    private void RefreshMidiDevices()
    {
        MidiDevices.Clear();
        foreach (var d in _midi.GetDevices()) MidiDevices.Add(d);
        if (MidiDevices.Count == 0) MidiDevices.Add(new MidiOutputDeviceInfo { DeviceId = -1, Name = "MIDI Mapper / Windows default" });
    }

    /// <summary>Sound > MIDI / Audio setup: the selected track's MIDI output device, with a test note.</summary>
    private void ShowAudioTab_Click(object sender, RoutedEventArgs e)
    {
        var track = SelectedTrack;
        if (track is null) { StatusText.Text = "Select a track first"; return; }
        Views.TrackOutputWindow.Show(this, track.Name, MidiDevices, track.MidiOutputDeviceId,
            id => SetTrackOutput(track, id), () => TestMidi_Click(this, new RoutedEventArgs()));
        StatusText.Text = "MIDI / audio setup: the selected track's MIDI output";
    }

    private void SetTrackOutput(TrackModel track, int deviceId)
    {
        DocumentEdits.Run(Doc, _ => { track.MidiOutputDeviceId = deviceId; return true; }, invalidatesTimeline: false);
        SyncSelectedOutput(); UpdateTitle();
    }

    private async void TestMidi_Click(object sender, RoutedEventArgs e)
    {
        var track = SelectedTrack;
        var device = track?.MidiOutputDeviceId ?? -1;
        StatusText.Text = "Testing MIDI output…";
        await _midi.PreviewNoteAsync(device, track?.MidiChannel ?? 0, track?.MidiProgram ?? 24, track?.Kind == TrackKind.Bass ? 40 : 64);
        StatusText.Text = "MIDI test complete";
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ThemedConfirmDialog(
            "About TabForge",
            $"{AppInfo.VersionLine}\nTablature and notation workstation\n\nTabForge is an independent project, not affiliated with any other software maker.\n\nTabForge is under active development: keep backups of your files.\n\nMultitrack TAB + notation, durations/dots/triplets, rests/ties/fermata, repeats/endings/sections, chord/text/lyrics/markers, mixer with MIDI vol/pan/chorus/reverb, speed-trainer loop, metronome, fretboard, chord/scale finders, tuning reference, .gp3/.gp4/.gp5/.gpx/.gp import, .gp save, MIDI + ASCII export, VST2/VST3 plug-ins in a separate audio engine.",
            yesToolTip: "Close this window",
            noToolTip: "Open the third-party licence notices",
            showCancel: false,
            yesText: "Close",
            noText: "Third-party licences") { Owner = this };
        DialogHost.ShowModal(dlg);
        if (dlg.Result == MessageBoxResult.No) OpenThirdPartyLicences();
    }

    /// <summary>Opens THIRD_PARTY.md next to the exe, else the licenses folder, else the copy on GitHub.</summary>
    private static void OpenThirdPartyLicences()
    {
        var dir = AppContext.BaseDirectory;
        var target = System.IO.Path.Combine(dir, "THIRD_PARTY.md");
        if (!System.IO.File.Exists(target))
        {
            target = System.IO.Path.Combine(dir, "licenses");
            if (!System.IO.Directory.Exists(target))
                target = "https://github.com/cobhc95/TabForge/blob/main/THIRD_PARTY.md";
        }
        // A .md file often has no shell handler; Notepad always exists.
        var start = target.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{target}\"") { UseShellExecute = true }
            : new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true };
        try { System.Diagnostics.Process.Start(start); }
        catch  { Services.Trace.Error(Services.Trace.Ui, "open file or folder: no handler: failed"); /* no browser or folder handler: nothing more to try */ }
    }
}
