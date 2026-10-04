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

// MainWindow, settings: load, apply, save; hotkeys; MIDI devices and VST browser.
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
            if (!_settings.General.PlaybackControllerDocked)
                _dockWorkspace.FloatPanelAt("playback", new Point(100, 100), 340, 180);
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
    /// Another window changed the shared settings: key bindings take effect here at once. Everything else is applied on this
    /// window's next settings sync (a full re-apply here would also move the audio engine to this window's song; see R-10).
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

    private void SyncFromSettings(bool applyWindowSize)
    {
        _applied.TakeVisual(_settings);
        var s = _settings;

        InstrumentPane.LeftHanded = s.Editing.LeftHanded;
        InstrumentPane.ShowNoteNames = s.Editing.ShowNoteNames;
        _previewNotes = s.Audio.PreviewNotes;
        var instrumentBefore = (InstrumentPane.PreviewHorizon, InstrumentPane.ScaleHighlight, InstrumentPane.FretboardFrets, InstrumentPane.LeftHanded, InstrumentPane.ShowNoteNames,
            _options.Visual.Gp5Mode);
        InstrumentPane.PreviewHorizon = s.Editing.PreviewNotesEnabled ? Math.Clamp(s.Editing.PreviewHorizon, 1, 10) : 0;
        ApplyFretboardStyle(s.Audio.FretboardStyle);
        InstrumentPane.ScaleHighlight = s.Editing.ScaleHighlight;
        InstrumentPane.FretboardFrets = s.Editing.FretboardFrets is 12 or 24 ? s.Editing.FretboardFrets : 24;
        _scoreWheelScrollPixels = Math.Clamp(s.Editing.ScoreWheelScrollPixels, 12, 96);
        // Fretboard options only showed after the next note/playhead move; redraw it now when one changed.
        if (instrumentBefore != (InstrumentPane.PreviewHorizon, InstrumentPane.ScaleHighlight, InstrumentPane.FretboardFrets, InstrumentPane.LeftHanded, InstrumentPane.ShowNoteNames,
                _options.Visual.Gp5Mode) && _mainWindowInitialized)
            RefreshInstrument();
        Editor.CurrentDurationDenominator = new[] { 1, 2, 4, 8, 16, 32, 64 }.Contains(s.Editing.DefaultDuration)
            ? s.Editing.DefaultDuration : 4;
        Editor.AutoAdvanceAfterEntry = s.Editing.AutoAdvance;
        Editor.ReversePlusMinusDuration = s.Editing.ReversePlusMinusDuration;
        s.Hotkeys.ReverseDurationKeys = s.Editing.ReversePlusMinusDuration;   // the default keys of Longer / Shorter swap with it
        Editor.PreventBarOverflow = s.Editing.PreventBarOverflow;
        Editor.FillBarsWithRests = s.Editing.FillBarsWithRests;
        Editor.MergeRestsOnDelete = s.Editing.MergeRestsOnDelete;
        _transport.Metronome = s.Audio.Metronome;
        _transport.CountIn = s.Audio.CountIn;
        s.Audio.MetronomeVolume = Math.Clamp(s.Audio.MetronomeVolume, 0, 100);
        s.Audio.MetronomeAccentVolume = Math.Clamp(s.Audio.MetronomeAccentVolume, 0, 100);
        s.Audio.MetronomeClickVolume = Math.Clamp(s.Audio.MetronomeClickVolume, 0, 100);
        s.Audio.MetronomeSubdivision = s.Audio.MetronomeSubdivision is 1 or 2 or 3 or 4 ? s.Audio.MetronomeSubdivision : 1;
        _transport.Speed = Math.Clamp(s.Audio.Speed <= 0 ? 1 : s.Audio.Speed, 0.25, 2.0);
        UpdateSpeedControls();
        if (_midi.IsPlaying) _midi.SetSpeed(_project, _transport.Speed);
        SyncMetronomeSettingsPopup();
        ApplyMetronomeSettingsToEngines();
        _confirmOnClose = s.General.ConfirmOnClose;
        _tabSettings = s.Tabs;

        // Follow / highlight / readability.
        var follow = s.Follow;
        // Legacy compatibility: the old "keep the playhead in view" flag mapped to Off.
        if (!s.General.AutoScroll && follow.Mode == FollowModes.Jump) follow.Mode = FollowModes.Off;
        _follow.ApplySettings(follow);
        Editor.Appearance.HighlightPlayedBeat = follow.HighlightPlayedBeat;
        Editor.Appearance.PlaybackColor = ParseColour(follow.HighlightColour, Color.FromRgb(0x3F, 0xB9, 0x50));
        Editor.Appearance.HighlightBackground = ParseColour(follow.HighlightBackground, Color.FromRgb(0x1E, 0x3A, 0x2A));
        Editor.DurationGlowColor = ParseColour(follow.DurationGlowColour, Color.FromRgb(0x3F, 0xB9, 0x50));
        Editor.DurationGlowOpacity = Math.Clamp(follow.DurationGlowOpacity, 0, 1);
        Editor.PlayingBarEnabled = follow.PlayingBarEnabled;
        Editor.Appearance.PlayingBarColor = ParseColour(follow.PlayingBarColour, Color.FromRgb(0xFF, 0xE0, 0x66));
        Editor.Appearance.PlayingBarOpacity = Math.Clamp(follow.PlayingBarOpacity, 0.05, 0.6);
        Editor.Appearance.PlayingBarWhenStopped = follow.PlayingBarWhenStopped;
        PlayingBarMenu.IsChecked = follow.PlayingBarEnabled;
        Editor.Appearance.DarkPaperColor = ParseColour(s.Appearance.DarkScorePaperColour, Color.FromRgb(0x15, 0x18, 0x1D));
        Editor.Appearance.LightPaperColor = ParseColour(s.Appearance.LightScorePaperColour, Colors.White);
        Editor.Appearance.DarkInkColor = ParseColour(s.Appearance.DarkScoreInkColour, Color.FromRgb(0xE7, 0xEA, 0xEF));
        Editor.Appearance.LightInkColor = ParseColour(s.Appearance.LightScoreInkColour, Color.FromRgb(0x11, 0x11, 0x11));
        Editor.Appearance.DarkStaffLineColor = ApplyOpacity(ParseColour(s.Appearance.DarkScoreLinesColour, Color.FromRgb(0x34, 0x39, 0x40)), s.Appearance.StaffLineOpacity);
        Editor.Appearance.LightStaffLineColor = ApplyOpacity(ParseColour(s.Appearance.LightScoreLinesColour, Color.FromRgb(0xD5, 0xD5, 0xD5)), s.Appearance.StaffLineOpacity);
        Editor.LedgerLines = Enum.TryParse<LedgerLineMode>(s.Appearance.LedgerLines, true, out var ledgerMode) &&
                             Enum.IsDefined(typeof(LedgerLineMode), ledgerMode)
            ? ledgerMode
            : LedgerLineMode.Minimal;
        Editor.Appearance.AccentColor = ParseColour(s.Appearance.Accent, Color.FromRgb(0x4C, 0x9A, 0xFF));
        Editor.Appearance.SelectionColor = ParseColour(s.Appearance.SelectionColour, Color.FromRgb(0x4C, 0x9A, 0xFF));
        Editor.Appearance.HoverColor = ParseColour(s.Appearance.HoverColour, Color.FromRgb(0x98, 0xA1, 0xAE));
        Editor.Appearance.HoverHighlightIntensity = Math.Clamp(s.Appearance.HoverHighlightIntensity, 0, 1);
        Editor.Appearance.SelectionHighlightIntensity = Math.Clamp(s.Appearance.SelectionHighlightIntensity, 0, 1);
        Editor.Appearance.ShowBarNumbers = s.Appearance.ShowScoreBarNumbers;
        Editor.Appearance.BarNumberFrequency = Math.Clamp(s.Appearance.ScoreBarNumberFrequency, 1, 16);
        Editor.Appearance.ShowSectionHeadings = s.Appearance.ShowSectionHeadings;
        Editor.Appearance.ShowDynamics = s.Appearance.ShowDynamics;
        Editor.Appearance.CursorColor = ParseColour(s.Appearance.CursorColour, Color.FromRgb(0xF2, 0xC1, 0x4E));
        Playhead.SetColor(ParseColour(follow.PlayheadColour, Color.FromRgb(0x3F, 0xB9, 0x50)));
        Playhead.SetDurationStyle(Editor.DurationGlowColor, Editor.DurationGlowOpacity, follow.DurationTintEnabled);
        Playhead.SetThickness(follow.PlayheadThickness);
        Editor.Appearance.ScoreSpacing = s.Appearance.ScoreSpacing;
        Editor.Appearance.SystemVerticalSpacing = s.Appearance.SystemVerticalSpacing;
        Editor.Appearance.MeasureHorizontalSpacing = s.Appearance.MeasureHorizontalSpacing;
        TabEditorControl.ConfigureScoreTextStyle(s.Appearance.ScoreFontFamily, s.Appearance.ScoreTextSize,
            s.Appearance.ScoreTextBold, s.Appearance.ScoreTextItalic);
        TabEditorControl.ConfigureTextAreas(s.Appearance.ScoreTextAreas);
        if (_applied.VisualChanged) Editor.InvalidateScoreLayout();
        Arrangement.SectionBracketThickness = Math.Clamp(s.Appearance.SectionBracketThickness, 1, 24);
        Arrangement.SectionGlowIntensity = Math.Clamp(follow.SectionGlowIntensity, 0, 1);
        Arrangement.ShowBarNumbers = s.Timeline.ShowBarNumbers;
        Arrangement.ShowSectionNames = s.Timeline.ShowSectionNames;
        Arrangement.ShowSectionBrackets = s.Timeline.ShowSectionBrackets;
        if (Arrangement.MatchSimilarSectionColours != s.Timeline.MatchSimilarSectionColours)
        {
            Arrangement.MatchSimilarSectionColours = s.Timeline.MatchSimilarSectionColours;
            if (_mainWindowInitialized) RefreshMarkers(); // section colours are resolved when the list is built
        }
        Arrangement.AnimateSectionDragging = s.Timeline.SectionDragAnimation;
        // Timeline appearance (Preferences rows; the View menu keeps the two note-drawing toggles in step).
        Arrangement.ShowContinuousBlocks = s.Timeline.ShowContinuousLine;
        Arrangement.ShowIndividualNotes = s.Timeline.ShowIndividualNotes && !s.Timeline.ShowContinuousLine;
        Arrangement.HideEmptyTimelineGrid = s.Timeline.HideEmptyGrid;
        Arrangement.ShowBarGlow = s.Timeline.BarGlow;
        Arrangement.PlayheadStyle = s.Timeline.PlayheadStyle;
        Arrangement.ShowAddTrackLane = s.Timeline.ShowAddTrackLane;
        ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
        ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        if (_mainWindowInitialized) WorkspaceLayouts.ApplyInstrumentSizeLock();   // "Lock fretboard size" is a Preferences row too
        SetSectionGlowResources(Arrangement.SectionGlowIntensity);
        ApplyIconSize(s.Appearance.IconSize); // UI scale is applied to the whole window by ThemeService

        if (applyWindowSize && s.General.RestoreWindow)
        {
            Width = Math.Max(MinWidth, s.WindowWidth);
            Height = Math.Max(MinHeight, s.WindowHeight);
            if (s.Maximised) WindowState = WindowState.Maximized;
        }

        InstrumentViewMenu.IsChecked = s.Appearance.ShowFretboard;
        Instrument.HorizontalPosition = Enum.TryParse<FretboardHorizontalPosition>(s.Appearance.FretboardPosition, true, out var fretboardPosition) &&
                                        Enum.IsDefined(typeof(FretboardHorizontalPosition), fretboardPosition)
            ? fretboardPosition
            : FretboardHorizontalPosition.Centre;
        ArrangementMenu.IsChecked = s.Appearance.ShowArrangementOverview;
        PracticeNamesCheck.IsChecked = InstrumentPane.ShowNoteNames;
        LeftHandedCheck.IsChecked = InstrumentPane.LeftHanded;
        PracticePreviewCheck.IsChecked = InstrumentPane.PreviewHorizon > 0;
        PreviewNotesMenu.IsChecked = _previewNotes;
        PreviewHorizonSlider.Value = InstrumentPane.PreviewHorizon;
        FretboardFretsCombo.SelectedIndex = InstrumentPane.FretboardFrets == 12 ? 1 : 0;
        MetronomeMenu.IsChecked = _transport.Metronome;
        SetTransportActive(MetronomeButton, _transport.Metronome);
        CountInMenu.IsChecked = _transport.CountIn;
        SetTransportActive(CountInButton, _transport.CountIn);

        Tabs.Settings = _tabSettings;
        Tabs.Refresh();
        if (_applied.VisualChanged) ApplyAppearance();
        if (_mainWindowInitialized) ApplyNotationFromSettings(); // default score display changed in Settings
        InstrumentPane.SyncInstrumentViewSetting();
        RefreshToolsPalette();
        ApplyPanelVisibility();
        BuildHotkeyMap();
        RefreshHotkeyTooltips();
        // A changed audio driver, device, channel pair or rate reaches the engine now (only when a setting differs).
        if (_mainWindowInitialized && _settings.Plugins.PlayAllThroughEngine != _engine.Mixer.PlayAllThroughEngine)
            _settings.Plugins.PlayAllSetAutomatically = false; // the user changed it by hand: a manual choice is kept
        var routeChanged = _engine.Mixer.PlayAllThroughEngine != _settings.Plugins.PlayAllThroughEngine;
        _engine.Mixer.PlayAllThroughEngine = _settings.Plugins.PlayAllThroughEngine;
        TabForge.Audio.RoutedMidiOutput.WindowsMidiLatencyMs = _settings.Plugins.WindowsMidiLatencyMs;
        if (_mainWindowInitialized) { SyncAudioEngine(); if (routeChanged) _midi.Rebuild(_project); }
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
        Editor.DarkPaper = !light;
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
        ScorePage.Background = (Brush)FindResource(Editor.DarkPaper ? "PaperDarkBrush" : "PaperLightBrush");
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

    /// <summary>Runs a catalogued command. Returns false when this window has no handler for it.</summary>
    private bool RunHotkey(string id)
    {
        // Note/beat commands belong to the editor (and are tested there without a window).
        if (Editor.TryRunNoteCommand(id)) return true;
        if (RunPaneHotkey(id)) return true;
        var args = new RoutedEventArgs();
        switch (id)
        {
            case "File.New": New_Click(this, args); return true;
            case "File.NewFromTemplate": ApplyTemplate_Click(this, args); return true;
            case "File.SaveAsTemplate": SaveAsTemplate_Click(this, args); return true;
            case var tool when tool.StartsWith("Tool.", StringComparison.Ordinal):
                ToolsPaletteButton_Click(new Button { Tag = tool[5..] }, new RoutedEventArgs()); return true;
            case "File.Open": Open_Click(this, args); return true;
            case "File.OpenInNewTab": OpenInNewTab_Click(this, args); return true;
            case "File.Save": Save_Click(this, args); return true;
            case "File.SaveAs": SaveAs_Click(this, args); return true;
            case "File.Print": Print_Click(this, args); return true;
            case "File.Render": Render_Click(this, args); return true;
            case "File.CancelImport": CancelImport_Click(this, args); return true;
            case "File.ExportPdf": ExportPdf_Click(this, args); return true;
            case "File.ExportMidi": ExportMidi_Click(this, args); return true;
            case "File.ExportAscii": ExportAscii_Click(this, args); return true;
            case "File.ExportGuitarPro": ExportGuitarPro_Click(this, args); return true;
            case "File.ProjectSettings": ProjectSettings_Click(this, args); return true;
            case "Transport.Metronome": Metronome_Click(this, args); return true;
            case "Transport.CountIn": CountIn_Click(this, args); return true;
            case "File.ExportMusicXml": ExportMusicXml_Click(this, args); return true;
            case "App.CommandPalette": CommandPalette_Click(this, args); return true;
            case "File.PrintPreview": PrintPreview_Click(this, args); return true;
            case "Tab.New": NewTab_Click(this, args); return true;
            case "Tab.Close": CloseTab_Click(this, args); return true;
            case "Tab.Duplicate": DuplicateDocument(_documents.ActiveIndex); return true;
            case "App.Preferences": Prefs_Click(this, args); return true;
            case "App.Shortcuts": Shortcuts_Click(this, args); return true;
            case "Edit.Undo": Undo_Click(this, args); return true;
            case "Edit.Redo": Redo_Click(this, args); return true;
            case "Edit.Copy": Copy_Click(this, args); return true;
            case "Edit.Cut": Cut_Click(this, args); return true;
            case "Edit.Paste": Paste_Click(this, args); return true;
            case "Edit.PasteSpecial": PasteSpecial_Click(this, args); return true;
            case "Edit.SelectAll": Editor.SelectAll(); return true;
            case "Edit.RepeatSelection": RepeatSelection_Click(this, args); return true;
            case "Transport.PlayPause": TogglePlayback(); return true;
            case "Transport.PlayFromStart": PlayFromStart_Click(this, args); return true;
            case "Transport.Stop": Stop_Click(this, args); return true;
            case "Transport.Loop": Loop_Click(this, args); return true;
            case "Bar.Insert": InsertBar_Click(this, args); return true;
            case "Bar.Delete": DeleteBar_Click(this, args); return true;
            case "Bar.TimeSignature": TimeSig_Click(this, args); return true;
            case "Beat.MixTable": ShowMixTable(); return true;
            case "Bar.KeySignature": KeySig_Click(this, args); return true;
            case "Bar.Clef": Clef_Click(this, args); return true;
            case "Bar.Directions": Directions_Click(this, args); return true;
            case "Bar.GoTo": GoTo_Click(this, args); return true;
            case "Reader.ReadBar": { var text = Editor.DescribeBar(); Editor.AnnounceText(text); StatusText.Text = text; return true; }
            case "Reader.ReadPosition": { var text = Editor.DescribePosition(); Editor.AnnounceText(text); StatusText.Text = text; return true; }
            case "Bar.First": FirstBar_Click(this, args); return true;
            case "Bar.Last": LastBar_Click(this, args); return true;
            case "Bar.Check": CheckBars_Click(this, args); return true;
            case "Bar.ScoreInfo": ScoreInfo_Click(this, args); return true;
            case "Section.Add": AddSectionAt(Editor.SelectedMeasure); return true;
            case "Tools.Transpose": Transpose_Click(this, args); return true;
            case "Tools.Tuner": Tuner_Click(this, args); return true;
            case "View.ToggleAddTrackLane": ToggleAddTrackLane(); return true;
            case "View.CyclePlayheadStyle": _settings.Timeline.PlayheadStyle = PlayheadStyles.Next(_settings.Timeline.PlayheadStyle); Arrangement.PlayheadStyle = _settings.Timeline.PlayheadStyle; SaveSettings(); StatusText.Text = $"Playback position marker: {_settings.Timeline.PlayheadStyle}"; return true;
            case "View.Mixer": OpenMixer(); return true;
            case "Transport.Record": ToggleRecording(); return true;
            case "Timeline.Snap": _settings.Timeline.Snap.Enabled = !_settings.Timeline.Snap.Enabled; Arrangement.UpdateSnapButton(); SaveSettings(); StatusText.Text = _settings.Timeline.Snap.Enabled ? "Snapping on" : "Snapping off"; return true;
            case "Track.Arm": if (SelectedTrack is { } armTrack) ToggleArm(armTrack); return true;
            case "Track.FxChain": OpenFxChain(SelectedTrack); return true;
            case "View.AutoFitTrackList": ToggleAutoFitTrackList(); return true;
            case "View.ResetTrackRowHeight": _trackListFit?.ResetRowHeight(); return true;
            case "Track.Wiring": OpenWiring(SelectedTrack); return true;
            case "Playback.SpeedUp": ApplySpeed(NextSpeedPreset(_transport.Speed, 1)); return true;
            case "Playback.SpeedDown": ApplySpeed(NextSpeedPreset(_transport.Speed, -1)); return true;
            case "Playback.SpeedReset": ApplySpeed(1.0); return true;
            case "Track.MoveUp": MoveTrack(-1); return true;
            case "Track.MoveDown": MoveTrack(1); return true;
            case "View.ShowTrackGroups": ((IMixerHost)this).SetTrackListShows("groups", !_project.Mixer.ShowGroupsInTrackList); return true;
            case "Media.ManageApprovals": ReviewLinkedAudio(this); return true;
            case "Mixer.MasterFx": OpenBusFx(null); return true;
            case "Mixer.MonitorFx": OpenMonitorFx(); return true;
            case "Mixer.GroupFx": if (SelectedTrack is { } groupTrack) OpenBusFx(MixerGroups.GroupOf(_project, groupTrack)); return true;
            case "Track.MidiProcessing": OpenMidiProcessing(SelectedTrack); return true;
            case "Help.Tutorial": Tutorial_Click(this, args); return true;
            case "Help.TutorialDetailed": TutorialDetailed_Click(this, args); return true;
            case "Help.CheckForUpdates": _ = CheckForUpdatesAsync(manual: true); return true;
            case "Section.Edit": Section_Click(this, args); return true;
            case "Section.Previous": PrevSection_Click(this, args); return true;
            case "Section.Next": NextSection_Click(this, args); return true;
            case "Note.Chord": Chord_Click(this, args); return true;
            case "Note.Text": Text_Click(this, args); return true;
            case "Track.Add": AddGuitar_Click(this, args); return true;
            case "Track.Delete": DeleteTrack_Click(this, args); return true;
            case var range when HotkeyCatalog.IsRangeAction(range): return RunRangeHotkey(range);   // from the command palette: the selected bars
            case "Track.ConvertToAudio": case "TrackRow.Copy": case "TrackRow.Cut": case "TrackRow.Paste": case "TrackRow.Duplicate": case "TrackRow.Delete":
                return TrackFlow.RunHotkey(id, TrackMixerGrid.SelectedIndex);   // from the command palette: the selected track
            case "Track.Properties": TrackProps_Click(this, args); return true;
            case "Track.Next": SelectTrack(1); return true;
            case "Track.Previous": SelectTrack(-1); return true;
            case "View.Multitrack": Multitrack_Click(this, args); return true;
            case "View.Global": GlobalView_Click(this, args); return true;
            case "View.SmoothFollow": SetSmoothFollow(!_follow.Continuous); return true;
            case "View.HorizontalScroll": SetHorizontalScoreView(!Editor.HorizontalScroll); return true;
            case "View.PlayingBar": SetPlayingBar(!_settings.Follow.PlayingBarEnabled); return true;
        }
        return false;
    }

    /// <summary>The fretboard, workspace layout and zoom commands of <see cref="RunHotkey"/>.</summary>
    private bool RunPaneHotkey(string id)
    {
        switch (id)
        {
            case "View.InstrumentView": InstrumentPane.CycleInstrumentView(); return true;
            case "Tools.ScaleFinder": InstrumentPane.OpenScaleFinder(); return true;
            case "View.ClearScale": InstrumentPane.ClearScaleHighlight(); return true;
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
            case "View.SidePanel": WorkspaceLayouts.ToggleSidePanel(); return true;
            case "View.InstrumentPanel": ToggleInstrumentPanel(); return true;
            case "View.LockInstrumentSize": WorkspaceLayouts.ToggleInstrumentSizeLock(); return true;
            case "View.LayoutCompose": WorkspaceLayouts.SwitchLayout("Compose"); return true;
            case "View.LayoutPractice": WorkspaceLayouts.SwitchLayout("Practice"); return true;
            case "View.LayoutMix": WorkspaceLayouts.SwitchLayout("Mix"); return true;
            case "View.Fullscreen": WorkspaceLayouts.ToggleFullscreen(); return true;
            case "View.ZoomIn": ScoreZoom.ZoomBy(1); return true;
            case "View.ZoomOut": ScoreZoom.ZoomBy(-1); return true;
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

    /// <summary>Parses "#RRGGBB", falling back to a sane default rather than throwing.</summary>
    private static Color ParseColour(string? hex, Color fallback) =>
        ThemeService.TryParse(hex, out var colour) ? colour : fallback;

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
        var clamped = Math.Clamp(size, 10, 22);
        Resources["IconSize"] = clamped;
        Application.Current.Resources["IconSize"] = clamped;
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
            var s = _settings;
            s.ShowInstrument = InstrumentViewMenu.IsChecked;
            s.ShowArrangement = ArrangementMenu.IsChecked;
            s.DarkPaper = Editor.DarkPaper;
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
                ? Math.Clamp(s.Editing.PreviewHorizon, 1, 10)
                : InstrumentPane.PreviewHorizon;
            s.Editing.PreviewNotesEnabled = InstrumentPane.PreviewHorizon > 0;
            s.Editing.ScaleHighlight = InstrumentPane.ScaleHighlight;
            s.Editing.FretboardFrets = InstrumentPane.FretboardFrets;
            s.Editing.ScoreWheelScrollPixels = _scoreWheelScrollPixels;
            s.Audio.PreviewNotes = _previewNotes;
            s.Audio.Metronome = _transport.Metronome;
            s.Audio.CountIn = _transport.CountIn;
            s.Audio.Speed = _transport.Speed;
            s.Audio.MetronomeVolume = Math.Clamp(s.Audio.MetronomeVolume, 0, 100);
            s.Audio.MetronomeAccentVolume = Math.Clamp(s.Audio.MetronomeAccentVolume, 0, 100);
            s.Audio.MetronomeClickVolume = Math.Clamp(s.Audio.MetronomeClickVolume, 0, 100);
            s.Audio.MetronomeSubdivision = s.Audio.MetronomeSubdivision is 1 or 2 or 3 or 4 ? s.Audio.MetronomeSubdivision : 1;
            s.General.AutoScroll = _follow.Mode != FollowModes.Off;
            var workspace = _dockWorkspace?.CaptureLayout();
            s.Workspace = workspace;
            s.General.PlaybackControllerDocked = _dockWorkspace?.IsPanelFloating("playback") != true;
            if (workspace is not null && _dockWorkspace?.IsPanelFloating("playback") == true)
            {
                var floatingController = workspace.Floating.FirstOrDefault(f => f.Root is not null &&
                    DockWorkspaceState.EnumeratePanelIds(f.Root).Contains("playback", StringComparer.Ordinal));
                if (floatingController is not null)
                {
                    s.General.PlaybackControllerX = floatingController.Left;
                    s.General.PlaybackControllerY = floatingController.Top;
                    s.General.PlaybackControllerHeight = floatingController.Height;
                }
            }
            _follow.WriteSettings(s.Follow);
            s.Appearance.IconSize = _settings.Appearance.IconSize;
            s.Appearance.ScoreSpacing = Editor.Appearance.ScoreSpacing;
            s.Appearance.LedgerLines = Editor.LedgerLines.ToString();
            s.Appearance.FretboardPosition = Instrument.HorizontalPosition.ToString();
            s.Follow.HighlightPlayedBeat = Editor.Appearance.HighlightPlayedBeat;
            s.Follow.HighlightColour = ColourToHex(Editor.Appearance.PlaybackColor);
            s.Follow.HighlightBackground = ColourToHex(Editor.Appearance.HighlightBackground);
            s.Follow.PlayheadColour = ColourToHex(Playhead.CurrentColor);
            s.Follow.DurationGlowColour = ColourToHex(Editor.DurationGlowColor);
            s.Follow.DurationGlowOpacity = Math.Clamp(Editor.DurationGlowOpacity, 0, 1);
            s.Follow.SectionGlowIntensity = Arrangement.SectionGlowIntensity;
            s.Appearance.ShowFretboard = _dockWorkspace?.IsPanelVisible("instrument") == true;
            s.Appearance.ShowArrangementOverview = _dockWorkspace?.IsPanelVisible("timeline") == true;
            s.Appearance.ScorePaper = Editor.DarkPaper ? "Dark" : "Light";

            s.WindowWidth = WindowState == WindowState.Maximized ? RestoreBounds.Width : Width;
            s.WindowHeight = WindowState == WindowState.Maximized ? RestoreBounds.Height : Height;
            s.Maximised = WindowState == WindowState.Maximized;
            s.Tabs = _tabSettings;

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
        Editor.DarkPaper = !string.Equals(s.Appearance.ScorePaper, "Light", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opens a .tforge or Guitar Pro file into a new tab (used by the file argument).</summary>
    /// <remarks>A Guitar Pro file imports in the background (A5-07) and opens when done; the returned task completes then.</remarks>
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
    public void OpenFromAnotherLaunch(string path)
    {
        if (!SingleInstanceService.IsOpenableSong(path)) { TabForge.Services.Trace.Write("ui", $"OPEN hand-over refused {path}"); return; }
        TabForge.Services.Trace.Write("ui", $"OPEN hand-over opening {path}");
        OpenDocumentFromPath(path);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false; // bring to the front even when another app has focus
        Focus();
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

    private void SelectedOutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoring || SelectedTrack is null || SelectedOutputCombo.SelectedValue is not int id) return;
        var selected = SelectedTrack;
        DocumentEdits.Run(Doc, _ => { selected.MidiOutputDeviceId = id; return true; }, invalidatesTimeline: false);
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

    private void ShowAudioTab_Click(object sender, RoutedEventArgs e)
    {
        SyncSelectedOutput();
        ShowInPracticePanel(TrackMixerGrid); // the MIDI output / mixer grid is in the Practice / Mixer panel
        StatusText.Text = "MIDI / audio setup: choose each track's output in the mixer";
    }

    private void OpenSelectedFxChain_Click(object sender, RoutedEventArgs e) => OpenFxChain(SelectedTrack);

    private void OpenMixer_Click(object sender, RoutedEventArgs e) => OpenMixer();

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
        catch { /* no browser or folder handler: nothing more to try */ }
    }
}
