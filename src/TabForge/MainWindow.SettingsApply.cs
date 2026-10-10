using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// Owns: the small named appliers that SyncFromSettings (MainWindow.Settings.cs) calls in order, one per settings area.
// Does not own: the order they run in (SyncFromSettings states it), the settings store and the Preferences rows.
// Tests: TestEverySettingIsWired, TestPreferencesCatalog, TestTabEditorPlayheadAndAppearance (a window applies the shared settings through these).
public partial class MainWindow
{
    /// <summary>Follow, highlight and playing-bar settings (the legacy auto-scroll flag still maps to Off first).</summary>
    private void ApplyFollowSettings(AppSettings s)
    {
        var follow = s.Follow;
        // Legacy compatibility: the old "keep the playhead in view" flag mapped to Off.
        if (!s.General.AutoScroll && follow.Mode == FollowModes.Jump) follow.Mode = FollowModes.Off;
        _follow.ApplySettings(follow);
        Editor.Appearance.HighlightPlayedBeat = follow.HighlightPlayedBeat;
        Editor.Appearance.PlaybackColor = ColourText.ParseOr(follow.HighlightColour, Color.FromRgb(0x3F, 0xB9, 0x50));
        Editor.Appearance.HighlightBackground = ColourText.ParseOr(follow.HighlightBackground, Color.FromRgb(0x1E, 0x3A, 0x2A));
        Editor.Appearance.DurationGlowColor = ColourText.ParseOr(follow.DurationGlowColour, Color.FromRgb(0x3F, 0xB9, 0x50));
        Editor.Appearance.DurationGlowOpacity = follow.DurationGlowOpacity;
        Editor.Appearance.PlayingBarEnabled = follow.PlayingBarEnabled;
        Editor.Appearance.PlayingBarColor = ColourText.ParseOr(follow.PlayingBarColour, Color.FromRgb(0xFF, 0xE0, 0x66));
        Editor.Appearance.PlayingBarOpacity = follow.PlayingBarOpacity;
        Editor.Appearance.PlayingBarWhenStopped = follow.PlayingBarWhenStopped;
        PlayingBarMenu.IsChecked = follow.PlayingBarEnabled;
    }

    /// <summary>Score paper, ink, staff-line, ledger, accent / selection / hover, bar-number, heading and cursor settings.</summary>
    private void ApplyScoreAppearanceSettings(AppSettings s)
    {
        Editor.Appearance.DarkPaperColor = ColourText.ParseOr(s.Appearance.DarkScorePaperColour, Color.FromRgb(0x15, 0x18, 0x1D));
        Editor.Appearance.LightPaperColor = ColourText.ParseOr(s.Appearance.LightScorePaperColour, Colors.White);
        Editor.Appearance.DarkInkColor = ColourText.ParseOr(s.Appearance.DarkScoreInkColour, Color.FromRgb(0xE7, 0xEA, 0xEF));
        Editor.Appearance.LightInkColor = ColourText.ParseOr(s.Appearance.LightScoreInkColour, Color.FromRgb(0x11, 0x11, 0x11));
        Editor.Appearance.DarkStaffLineColor = ApplyOpacity(ColourText.ParseOr(s.Appearance.DarkScoreLinesColour, Color.FromRgb(0x34, 0x39, 0x40)), s.Appearance.StaffLineOpacity);
        Editor.Appearance.LightStaffLineColor = ApplyOpacity(ColourText.ParseOr(s.Appearance.LightScoreLinesColour, Color.FromRgb(0xD5, 0xD5, 0xD5)), s.Appearance.StaffLineOpacity);
        Editor.Appearance.LedgerLines = Enum.TryParse<LedgerLineMode>(s.Appearance.LedgerLines, true, out var ledgerMode) &&
                             Enum.IsDefined(typeof(LedgerLineMode), ledgerMode)
            ? ledgerMode
            : LedgerLineMode.Minimal;
        Editor.Appearance.AccentColor = ColourText.ParseOr(s.Appearance.Accent, Color.FromRgb(0x4C, 0x9A, 0xFF));
        Editor.Appearance.SelectionColor = ColourText.ParseOr(s.Appearance.SelectionColour, Color.FromRgb(0x4C, 0x9A, 0xFF));
        Editor.Appearance.HoverColor = ColourText.ParseOr(s.Appearance.HoverColour, Color.FromRgb(0x98, 0xA1, 0xAE));
        Editor.Appearance.HoverHighlightIntensity = s.Appearance.HoverHighlightIntensity;
        Editor.Appearance.SelectionHighlightIntensity = s.Appearance.SelectionHighlightIntensity;
        Editor.Appearance.ShowBarNumbers = s.Appearance.ShowScoreBarNumbers;
        Editor.Appearance.BarNumberFrequency = s.Appearance.ScoreBarNumberFrequency;
        Editor.Appearance.ShowSectionHeadings = s.Appearance.ShowSectionHeadings;
        Editor.Appearance.ShowDynamics = s.Appearance.ShowDynamics;
        Editor.Appearance.CursorColor = ColourText.ParseOr(s.Appearance.CursorColour, Color.FromRgb(0xF2, 0xC1, 0x4E));
    }

    /// <summary>The playhead line: colour, the duration glow (reads the colour and opacity the follow applier just set) and thickness.</summary>
    private void ApplyPlayheadSettings(AppSettings s)
    {
        var follow = s.Follow;
        Playhead.SetColor(ColourText.ParseOr(follow.PlayheadColour, Color.FromRgb(0x3F, 0xB9, 0x50)));
        Playhead.SetDurationStyle(Editor.Appearance.DurationGlowColor, Editor.Appearance.DurationGlowOpacity, follow.DurationTintEnabled);
        Playhead.SetThickness(follow.PlayheadThickness);
    }

    /// <summary>Score spacing and text style; re-lays-out the score only when a visual setting changed (see <see cref="AppliedSettings"/>).</summary>
    private void ApplyScoreLayoutSettings(AppSettings s)
    {
        Editor.Appearance.ScoreSpacing = s.Appearance.ScoreSpacing;
        Editor.Appearance.SystemVerticalSpacing = s.Appearance.SystemVerticalSpacing;
        Editor.Appearance.MeasureHorizontalSpacing = s.Appearance.MeasureHorizontalSpacing;
        TabEditorControl.ConfigureScoreTextStyle(s.Appearance.ScoreFontFamily, s.Appearance.ScoreTextSize,
            s.Appearance.ScoreTextBold, s.Appearance.ScoreTextItalic);
        TabEditorControl.ConfigureTextAreas(s.Appearance.ScoreTextAreas);
        if (_applied.VisualChanged) Editor.InvalidateScoreLayout();
    }

    /// <summary>Fretboard (instrument pane) options and the editor entry rules.</summary>
    private void ApplyInstrumentAndEditingSettings(AppSettings s)
    {
        InstrumentPane.LeftHanded = s.Editing.LeftHanded;
        InstrumentPane.ShowNoteNames = s.Editing.ShowNoteNames;
        _previewNotes = s.Audio.PreviewNotes;
        var instrumentBefore = (InstrumentPane.PreviewHorizon, InstrumentPane.ScaleHighlight, InstrumentPane.FretboardFrets, InstrumentPane.LeftHanded, InstrumentPane.ShowNoteNames,
            _options.Visual.Gp5Mode);
        InstrumentPane.PreviewHorizon = s.Editing.PreviewNotesEnabled ? s.Editing.PreviewHorizon : 0;
        ApplyFretboardStyle(s.Audio.FretboardStyle);
        InstrumentPane.ScaleHighlight = s.Editing.ScaleHighlight;
        InstrumentPane.FretboardFrets = s.Editing.FretboardFrets is 12 or 24 ? s.Editing.FretboardFrets : 24;
        _scoreWheelScrollPixels = s.Editing.ScoreWheelScrollPixels;
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
    }

    /// <summary>Metronome, count-in and speed (pushed to the engines), plus the close confirmation and the tab settings.</summary>
    private void ApplyTransportAndGeneralSettings(AppSettings s)
    {
        _transport.Metronome = s.Audio.Metronome;
        _transport.CountIn = s.Audio.CountIn;
        s.Audio.MetronomeSubdivision = s.Audio.MetronomeSubdivision is 1 or 2 or 3 or 4 ? s.Audio.MetronomeSubdivision : 1;
        _transport.Speed = s.Audio.Speed;
        UpdateSpeedControls();
        if (_midi.IsPlaying) _midi.SetSpeed(_project, _transport.Speed);
        SyncMetronomeSettingsPopup();
        ApplyMetronomeSettingsToEngines();
        _confirmOnClose = s.General.ConfirmOnClose;
        _tabSettings = s.Tabs;
    }

    /// <summary>The arrangement timeline: section brackets and glow, labels, note drawing, grid, lock-size and the glow resources.</summary>
    private void ApplyTimelineSettings(AppSettings s)
    {
        Arrangement.SectionBracketThickness = s.Appearance.SectionBracketThickness;
        Arrangement.SectionGlowIntensity = s.Follow.SectionGlowIntensity;
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
        Arrangement.ShowTrackLines = s.Timeline.ShowTrackLines;
        Arrangement.ShowBarGlow = s.Timeline.BarGlow;
        Arrangement.PlayheadStyle = s.Timeline.PlayheadStyle;
        Arrangement.ShowAddTrackLane = s.Timeline.ShowAddTrackLane;
        ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
        ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        if (_mainWindowInitialized) WorkspaceLayouts.ApplyInstrumentSizeLock();   // "Lock fretboard size" is a Preferences row too
        SetSectionGlowResources(Arrangement.SectionGlowIntensity);
    }

    /// <summary>The saved window size and state, applied only at load (<paramref name="apply"/>).</summary>
    private void ApplyWindowSize(AppSettings s, bool apply)
    {
        if (apply && s.General.RestoreWindow)
        {
            Width = Math.Max(MinWidth, s.WindowWidth);
            Height = Math.Max(MinHeight, s.WindowHeight);
            if (s.Maximised) WindowState = WindowState.Maximized;
        }
    }

    /// <summary>The View and transport menu check marks and the fretboard position.</summary>
    private void ApplyViewToggleSettings(AppSettings s)
    {
        InstrumentViewMenu.IsChecked = s.Appearance.ShowFretboard;
        Instrument.HorizontalPosition = Enum.TryParse<FretboardHorizontalPosition>(s.Appearance.FretboardPosition, true, out var fretboardPosition) &&
                                        Enum.IsDefined(typeof(FretboardHorizontalPosition), fretboardPosition)
            ? fretboardPosition
            : FretboardHorizontalPosition.Centre;
        ArrangementMenu.IsChecked = s.Appearance.ShowArrangementOverview;
        PreviewNotesMenu.IsChecked = _previewNotes;
        MetronomeMenu.IsChecked = _transport.Metronome;
        SetTransportActive(MetronomeButton, _transport.Metronome);
        CountInMenu.IsChecked = _transport.CountIn;
        SetTransportActive(CountInButton, _transport.CountIn);
    }

    /// <summary>The surfaces that read the settings just applied: tabs, theme, notation, palette, panels and hotkeys.</summary>
    private void RefreshAfterSettings()
    {
        Tabs.Settings = _tabSettings;
        Tabs.Refresh();
        if (_applied.VisualChanged) ApplyAppearance();
        if (_mainWindowInitialized) ApplyNotationFromSettings(); // default score display changed in Settings
        InstrumentPane.SyncInstrumentViewSetting();
        RefreshToolsPalette();
        ApplyPanelVisibility();
        BuildHotkeyMap();
        RefreshHotkeyTooltips();
    }

    /// <summary>A changed audio driver, device, channel pair or rate reaches the engine now (only when a setting differs).</summary>
    private void ApplyAudioRouteSettings()
    {
        if (_mainWindowInitialized && _settings.Plugins.PlayAllThroughEngine != _engine.Mixer.PlayAllThroughEngine)
            _settings.Plugins.PlayAllSetAutomatically = false; // the user changed it by hand: a manual choice is kept
        var routeChanged = _engine.Mixer.PlayAllThroughEngine != _settings.Plugins.PlayAllThroughEngine;
        _engine.Mixer.PlayAllThroughEngine = _settings.Plugins.PlayAllThroughEngine;
        TabForge.Audio.RoutedMidiOutput.WindowsMidiLatencyMs = _settings.Plugins.WindowsMidiLatencyMs;
        if (_mainWindowInitialized) { SyncAudioEngine(); if (routeChanged) _midi.Rebuild(_project); }
    }
}
