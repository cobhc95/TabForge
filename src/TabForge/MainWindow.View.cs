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
using TabForge.Services.Band;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Views.Band;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, view: dock workspace, theme/notation/clipboard, zoom, fullscreen, fretboard options and panel toggles.
// Owns: the dock workspace, theme, notation and clipboard commands, zoom, full screen, fretboard options and panel toggles.
// Does not own: the dock layout (Docking/) and the theme service (Services/ThemeService).
// Tests: listed in docs/feature-map/windows-tabs-and-documents.md.
public partial class MainWindow : IScoreZoomHost, IBandCommandHost
{
    private void InitializeDockWorkspace()
    {
        _dockWorkspace = new DockWorkspace(this);

        // Move the fretboard out of the score surface so it is a normal registered dock panel.
        EditorWorkspace.Children.Remove(InstrumentHost);
        var editorSplitter = EditorWorkspace.Children.OfType<GridSplitter>().FirstOrDefault();
        if (editorSplitter is not null) EditorWorkspace.Children.Remove(editorSplitter);
        EditorWorkspace.RowDefinitions.Clear();
        Grid.SetRow(ScoreEditorHost, 0);
        Grid.SetColumn(ScoreEditorHost, 0);
        EditorWorkspace.ColumnDefinitions.Clear();
        // The score scrolls internally; on short windows it should give up height before
        // the arrangement's track controls are clipped below the workspace.
        _dockWorkspace.SetEditorContent(EditorWorkspace, 460, 90);

        // Hard minimum: the height the current instrument needs to draw completely (strings, markers,
        // fret-number row, legend; keyboard keys; drum map rows), kept up to date as the track, string
        // count or view mode changes, so no splitter, layout, resize or dock can clip it.
        foreach (var (pane, title) in new (FrameworkElement Element, string Title)[]
        {
            (InstrumentHost, "Fretboard pane"), (ArrangementHost, "Arrangement pane"), (ToolsPanelContent, "Tools pane"),
            (SectionsPanelContent, "Sections pane")
        })
            if (string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(pane))) System.Windows.Automation.AutomationProperties.SetName(pane, title);
        // The Sections pane fills its cell, or scrolls when the cell is shorter than the content's minimum (large UI scale).
        SectionsPanelContent.SizeChanged += (_, e) =>
            SectionsPanelGrid.Height = Math.Max(0, e.NewSize.Height - SectionsPanelContent.Padding.Top - SectionsPanelContent.Padding.Bottom);
        SectionsPanelGrid.Height = 0;
        // One row per pane in DockPaneTable; the window supplies each pane's element.
        DockPaneSetup.RegisterAll(_dockWorkspace, id => id switch
        {
            "instrument" => InstrumentHost, "timeline" => ArrangementHost, "band" => Band.View, "learn" => KeyboardModePane, "tools" => ToolsPanelContent,
            "sections" => SectionsPanelContent, _ => ToolPalette.PanelContents[id]
        });
        WorkspaceLayouts.ApplyInstrumentMinHeight();
        Instrument.RequiredHeightChanged += _ => WorkspaceLayouts.ApplyInstrumentMinHeight();
        Instrument.MaximumHeightChanged += _ => WorkspaceLayouts.ApplyInstrumentMaxHeight();
        // The score zoom and playback speed live in the top toolbar; a narrow window sheds them in steps there.
        new ToolbarZoomSpeedFit(MainToolbar, MainMenu, PinnedToolStrip, ToolbarTempoGroup, ZoomSpeedGroup).Update(MainToolbar.ActualWidth);

        DockLayout.Children.Clear();
        DockLayout.RowDefinitions.Clear();
        DockLayout.ColumnDefinitions.Clear();
        DockLayout.Children.Add(_dockWorkspace);
        _dockWorkspace.LayoutChanged += (_, _) =>
        {
            InstrumentViewMenu.IsChecked = _dockWorkspace.IsPanelVisible("instrument");
            ArrangementMenu.IsChecked = _dockWorkspace.IsPanelVisible("timeline");
            BandViewMenu.IsChecked = _dockWorkspace.IsPanelVisible("band");
            WorkspaceLayouts.RefreshDockPanelsMenu();
            if (!_suppressWorkspaceSave) SaveSettings();
        };
        WorkspaceLayouts.ToggleLearn = ToggleKeyboardMode;
        WorkspaceLayouts.BuildDockPanelsMenu();
        _dockWorkspace.RestoreLayout(null);
        _trackListFit = new TrackListFitController(this, this);
        WorkspaceLayouts.BuildLayoutsMenu();
    }

    // ---------- workspace layouts (DockLayoutController) ----------

    Window IPaneHost.Window => this;
    AppSettings IPaneHost.Settings => _settings;
    TabEditorControl IPaneHost.Editor => Editor;
    SongProject IPaneHost.Project => _project;
    TrackModel? IPaneHost.SelectedTrack => SelectedTrack;
    void IPaneHost.SaveSettings() => SaveSettings();
    void IPaneHost.SetStatus(string text) => StatusText.Text = text;

    private DockLayoutController? _workspaceLayouts;
    private DockLayoutController WorkspaceLayouts =>
        _workspaceLayouts ??= new DockLayoutController(new DockHost(this, () => _dockWorkspace, DockPanelsMenu, Instrument, InstrumentHost));

    // ---------- Band view (BandViewController) ----------

    private BandViewController? _band;
    internal BandViewController Band => _band ??= new BandViewController(new BandHost(this, _documents, _options,
        () => (InstrumentPane.LeftHanded, InstrumentPane.ShowNoteNames, InstrumentPane.ScaleHighlight, InstrumentPane.PreviewHorizon),
        MoveTrackTo, i => TrackMixerGrid.SelectedIndex = i, () => ScoreZoom, OpenSettings));
    private void ToggleBandView_Click(object sender, RoutedEventArgs e) => WorkspaceLayouts.ToggleBandView();

    /// <summary>Runs one Band.* command for the feature module (Services/Band); the same handler as the RunHotkey switch.</summary>
    public void RunBandCommand(string id) => Band.Run(id);

    // ---------- theme / notation / clipboard ----------

    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetPaper(dark: true);
    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetPaper(dark: false);

    private void SetPaper(bool dark)
    {
        Editor.Appearance.DarkPaper = dark;
        Doc.DarkPaper = dark;
        _settings.Appearance.ScorePaper = dark ? "Dark" : "Light";
        ApplyScorePageBackground();
        Editor.InvalidateScoreLayout();
        SaveSettings();
        StatusText.Text = dark ? "Dark score page" : "Light score page";
    }

    private void NotationBoth_Click(object sender, RoutedEventArgs e) => SetNotation(NotationMode.TabAndStaff);
    private void NotationTab_Click(object sender, RoutedEventArgs e) => SetNotation(NotationMode.TabOnly);
    private void NotationStaff_Click(object sender, RoutedEventArgs e) => SetNotation(NotationMode.StaffOnly);

    private void SetNotation(NotationMode mode)
    {
        Editor.Notation = mode;
        Doc.Notation = mode;
        _settings.Notation = mode.ToString();
        _settings.NotationPreferenceSet = true;
        _applied.Notation = mode;
        Editor.InvalidateMeasure();
        Editor.InvalidateScoreLayout();
        StatusText.Text = "Notation: " + mode switch
        {
            NotationMode.TabOnly => "tablature only",
            NotationMode.StaffOnly => "standard notation only",
            _ => "tablature + standard"
        };
        SaveSettings();
    }

    private void SetLedgerLines(LedgerLineMode mode)
    {
        Editor.Appearance.LedgerLines = mode;
        _settings.Appearance.LedgerLines = mode.ToString();
        Editor.InvalidateScoreLayout();
        SaveSettings();
    }

    private void PreviewNotes_Click(object sender, RoutedEventArgs e)
    {
        _previewNotes = !_previewNotes;
        PreviewNotesMenu.IsChecked = _previewNotes;
        StatusText.Text = _previewNotes ? "Note preview on" : "Note preview off";
    }

    /// <summary>True when the selection is a timeline range over every track: the keys then act like the timeline menu.</summary>
    private bool AllTracksRange => _selection.HasRange && _selection.Scope == SelectionScope.AllTracks;

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (AllTracksRange) { _sections.CopyArea(Doc, _selection.StartBar, _selection.EndBar, _selection.ScopeTrack); return; }
        if (CopyScoreSelection(out _) is { } status) StatusText.Text = status;
    }

    /// <summary>Copies the score selection (or the cursor beat) to the shared clipboard; returns the status text.</summary>
    private string? CopyScoreSelection(out ScoreClip? clip)
    {
        clip = Editor.CaptureClip(out var error);
        if (clip is null) return error;
        // Another program can hold the Windows clipboard open; the clip then stays in TabForge's own clipboard.
        var written = ClipboardService.Shared.Copy(clip);
        var what = clip.Kind == ScoreClipKind.Bars
            ? (clip.BarCount == 1 ? "Copied 1 bar" : $"Copied {clip.BarCount} bars")
            : (clip.Tracks[0].Events.Count == 1 ? "Copied 1 beat" : $"Copied {clip.Tracks[0].Events.Count} beats");
        return written ? what : what + " (Windows clipboard busy: paste works in TabForge only)";
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        if (AllTracksRange) { _sections.CutArea(Doc, _selection.StartBar, _selection.EndBar, _selection.ScopeTrack); return; }
        var status = CopyScoreSelection(out var clip);
        if (clip is null) { if (status is not null) StatusText.Text = status; return; }
        if (EditCommands.CutTakesBars(Doc.Project, clip.Kind, Editor.SelectedTrackIndex, Editor.ActiveVoiceIndex, Editor.SelectionCellRange, Editor.SelectionIsWholeBars) is var (m1, m2))
        {   // as GP5 (one track: the bars go; the cursor stays on its bar number, the start of what is there now: quiet runs b02, b09)
            var bar = Editor.SelectedMeasure;
            _sections.CutArea(Doc, m1, m2, Editor.SelectedTrackIndex);
            if (Editor.Track is { Measures.Count: > 0 } t) Editor.SelectForEdit(Math.Min(bar, t.Measures.Count - 1), 0, Editor.SelectedString);
            return;
        }
        Editor.CutSelection(clip);
        StatusText.Text = "Cut" + status!["Copied".Length..];
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (AllTracksRange) { _sections.PasteAreaAt(Doc, _selection.StartBar); return; }
        if (ClipboardService.Shared.TryGetClip(out var error) is not { } clip) { StatusText.Text = error ?? ClipboardService.NotTabForgeNotesMessage; return; }
        if (Editor.Track is null) return;
        var target = Editor.PasteTarget;
        var asker = new WpfPasteQuestionAsker(this);
        var outcome = EditCommands.RunPaste(Doc, clip, target, _settings.Editing, asker);
        if (outcome.Asked.Count > 0) SaveSettings();   // a "Remember my choice" answer
        FinishPaste(outcome);
    }

    /// <summary>Paste Special: one small dialog (repeat, mode, octave shift, keep string and fret, bar settings), then the same paste path with explicit answers.</summary>
    private void PasteSpecial_Click(object sender, RoutedEventArgs e)
    {
        if (ClipboardService.Shared.TryGetClip(out var error) is not { } clip) { StatusText.Text = error ?? ClipboardService.NotTabForgeNotesMessage; return; }
        if (Editor.Track is null) return;
        var dialog = new PasteSpecialDialog(clip.Kind);
        if (IsLoaded) dialog.Owner = this;
        if (DialogHost.ShowModal(dialog) != true || dialog.Result is not { } options) return;
        var target = Editor.PasteTarget;
        FinishPaste(EditCommands.RunPasteSpecial(Doc, clip, target, options, _settings.Editing));
    }

    private void FinishPaste(PasteOutcome outcome)
    {
        if (!outcome.Changed) { StatusText.Text = outcome.Status; return; }
        if (outcome.BarMap is { } map) FinishSectionStructureEdit(outcome.Status, map);
        Editor.NotifyEdited();
        if (outcome.CursorBar >= 0) Editor.SelectForEdit(outcome.CursorBar, outcome.CursorCell, Editor.SelectedString);   // as GP5: the cursor sits on the last pasted beat
        StatusText.Text = outcome.Status;
    }

    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        var txt = GpDialogs.Prompt(TooltipShortcuts.Append("Go to bar", "Bar.GoTo"), "Bar number or section name:", (Editor.SelectedMeasure + 1).ToString());
        if (txt is null) return;
        if (int.TryParse(txt, out var bar)) { Editor.SetBar(Math.Clamp(bar - 1, 0, Math.Max(0, MaxMeasures() - 1))); ScrollToCursor(); return; }
        var marker = _project.Markers.FirstOrDefault(m => m.Title.Contains(txt, StringComparison.OrdinalIgnoreCase));
        if (marker is not null) { Editor.SetBar(Math.Clamp(marker.MeasureIndex, 0, Math.Max(0, MaxMeasures() - 1))); ScrollToCursor(); }
        else StatusText.Text = "No matching bar or section";
    }

    private void GlobalView_Click(object sender, RoutedEventArgs e)
    {
        ArrangementMenu.IsChecked = !ArrangementMenu.IsChecked;
        ToggleArrangement_Click(sender, e);
    }

    // ---------- creative workflow helpers ----------

    private void DuplicateBar_Click(object sender, RoutedEventArgs e)
    {
        // Bars are shared by every track, so the copy goes into every track (insert / delete bar do the same):
        // the cursor bar, or the whole selected bar range, is copied to right after itself in one undo step.
        var barCount = MaxMeasures();
        if (barCount == 0) return;
        var cursor = Math.Clamp(Editor.SelectedMeasure, 0, barCount - 1);
        var hadRange = _selection.HasRange;
        var (first, last) = hadRange ? (Math.Clamp(_selection.StartBar, 0, barCount - 1), Math.Clamp(_selection.EndBar, 0, barCount - 1)) : (cursor, cursor);
        var count = last - first + 1;
        if (_arrangementController.DuplicateBars(Doc, first, last).Value is not { } map) { StatusText.Text = "Cannot duplicate: the song would get too long"; return; }
        var at = last + 1;
        Editor.SetPosition(Math.Clamp(at, 0, Math.Max(0, MaxMeasures() - 1)), 0, Editor.SelectedString, seekPlayback: false);
        var status = count == 1 ? $"Duplicated bar {first + 1}" : $"Duplicated bars {first + 1}-{last + 1}";
        FinishSectionStructureEdit(status, map);
        if (hadRange) ApplyLoopRange(at, at + count - 1);   // after the remap: the copy is selected, like a paste
        StatusText.Text = status;
    }

    private void RepeatSelection_Click(object sender, RoutedEventArgs e)
    {
        var (ls, le) = GetLoopRange();
        if (le < ls) return;
        var count = GpDialogs.Prompt("Repeat selection", "How many times should the selected bars be repeated?", "1");
        if (count is null || !int.TryParse(count, out var times) || times < 1) return;
        times = Math.Clamp(times, 1, 16);
        DocumentEdits.Run(Doc, project => _arrangementController.RepeatRange(project, ls, le, times) > 0);
        RefreshArrangement();
        RefreshTabs();
        UpdateTitle();
        StatusText.Text = $"Repeated bars {ls + 1}-{le + 1} ×{times}";
    }


    // ---------- view: zoom and page width (ScoreZoomController) ----------

    private ScoreZoomController? _scoreZoom;
    private ScoreZoomController ScoreZoom => _scoreZoom ??= new ScoreZoomController(this);
    ComboBox IScoreZoomHost.ZoomCombo => ZoomCombo;
    ScrollViewer IScoreZoomHost.ScoreScroll => ScoreScroll;
    Border IScoreZoomHost.ScorePage => ScorePage;
    ScoreFollowCoordinator IScoreZoomHost.Follow => _follow;
    bool IScoreZoomHost.Restoring => _restoring;

    private void ZoomCombo_Changed(object sender, SelectionChangedEventArgs e) => ScoreZoom.OnComboChanged();
    private void ZoomCombo_ReSync(object sender, RoutedEventArgs e) => ScoreZoom.UpdateZoomControl();
    private void ZoomCombo_VisibleReSync(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) ScoreZoom.UpdateZoomControl();
    }
    private void ZoomCombo_LostFocus(object sender, RoutedEventArgs e) => ScoreZoom.CommitCustomZoom();
    private void ZoomCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ScoreZoom.CommitCustomZoom();
        e.Handled = true;
    }

    /// <summary>Makes an editable zoom combo display the given zoom (0 = fit width); returns the text shown.</summary>
    internal static string ShowZoomOn(ComboBox combo, double zoomFactor) => ScoreZoomController.ShowZoomOn(combo, zoomFactor);
    private void ScoreScroll_SizeChanged(object sender, SizeChangedEventArgs e) => ScoreZoom.OnScrollSizeChanged();
    private void Zoom75_Click(object sender, RoutedEventArgs e) => ScoreZoom.ApplyZoomText("75%");
    private void Zoom100_Click(object sender, RoutedEventArgs e) => ScoreZoom.ApplyZoomText("100%");
    private void Zoom150_Click(object sender, RoutedEventArgs e) => ScoreZoom.ApplyZoomText("150%");

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => WorkspaceLayouts.ToggleFullscreen();

    private void Multitrack_Click(object sender, RoutedEventArgs e) { OpenMixer(); StatusText.Text = "Multitrack: all tracks in the mixer; click a color block to jump"; }
    /// <summary>View > Mixer / VST: the same command as the Mixer button and hotkey (opens the Mixer window, or raises it).</summary>
    private void ShowMixer_Click(object sender, RoutedEventArgs e) => OpenMixer();

    private void Stylesheet_Click(object sender, RoutedEventArgs e) => Prefs_Click(sender, e);
    /// <summary>Opens Settings on one page (e.g. Audio & VST).</summary>
    private void OpenSettingsCategory(string category) => OpenSettings(category);

    /// <summary>Opens Settings on <paramref name="category"/> and, when given, scrolls to and highlights the row <paramref name="rowKey"/>
    /// (the "... settings..." entries of the right-click menus).</summary>
    private void OpenSettings(string category, string? rowKey = null)
    {
        Views.PreferencesWindow.SetTarget(category, rowKey);
        Prefs_Click(this, new RoutedEventArgs());
    }

    private void Prefs_Click(object sender, RoutedEventArgs e)
    {
        // Approvals can change while the dialog is open (another window, the Linked audio window): each preview / apply merges with the live list instead of replacing it.
        var approvals = new MediaApprovalMerge(() => _settings.Audio.ApprovedMedia);
        StatusText.Text = _settingsWindowHost.Show(_settings, staged => { approvals.Stage(staged); ApplyPreferences(staged); }, staged => { approvals.Stage(staged); PreviewPreferences(staged); }) switch
        {
            SettingsShowResult.Applied => "Settings updated",
            SettingsShowResult.Cancelled => "Settings cancelled",
            _ => "Settings could not be opened"
        };
    }

    private void ApplyPreferences(AppSettings settings)
    {
        ApplyFileAssociations(settings.General.AssociateFiles);
        var workspaceChanged = !string.Equals(JsonSerializer.Serialize(_settings.Workspace),
            JsonSerializer.Serialize(settings.Workspace), StringComparison.Ordinal);
        var fretboardVisibilityChanged = settings.Appearance.ShowFretboard != _settings.Appearance.ShowFretboard;
        var arrangementVisibilityChanged = settings.Appearance.ShowArrangementOverview != _settings.Appearance.ShowArrangementOverview;
        TabForge.Plugins.PluginQuarantine.KeepLive(settings.Plugins, _settings.Plugins);   // a crash while Preferences was open must survive Apply
        _settings = settings;
        _settingsStore.AcceptCurrentAsReplacement();   // reviewed and applied: it may now replace an unreadable settings file
        if (workspaceChanged) _dockWorkspace?.RestoreLayout(settings.Workspace);
        if (settings.Appearance.FretboardAtBottom != _dockWorkspace?.InstrumentAtBottom) _dockWorkspace?.SetInstrumentPosition(settings.Appearance.FretboardAtBottom);
        if (fretboardVisibilityChanged) _dockWorkspace?.SetPanelVisible("instrument", settings.Appearance.ShowFretboard);
        if (arrangementVisibilityChanged) _dockWorkspace?.SetPanelVisible("timeline", settings.Appearance.ShowArrangementOverview);
        SyncFromSettings(applyWindowSize: false);
        ScheduleFitTimelineToTracks();   // auto-fit or the row height may have changed
        if (Arrangement.PanKnobs != settings.Audio.PanKnobs || Arrangement.VolumeKnobs != settings.Audio.VolumeKnobs)
        {
            Arrangement.PanKnobs = settings.Audio.PanKnobs;
            Arrangement.VolumeKnobs = settings.Audio.VolumeKnobs;
            RefreshArrangement();
        }
        if (_applied.VisualChanged) RepaintAfterVisualSettings();
        SaveSettings();
    }

    private void PreviewPreferences(AppSettings settings)
    {
        var workspaceChanged = !string.Equals(JsonSerializer.Serialize(_settings.Workspace),
            JsonSerializer.Serialize(settings.Workspace), StringComparison.Ordinal);
        TabForge.Plugins.PluginQuarantine.KeepLive(settings.Plugins, _settings.Plugins);
        _settings = settings;
        _suppressWorkspaceSave = true;
        try
        {
            if (workspaceChanged) _dockWorkspace?.RestoreLayout(settings.Workspace);
            else
            {
                _dockWorkspace?.SetPanelVisible("instrument", settings.Appearance.ShowFretboard);
                _dockWorkspace?.SetPanelVisible("timeline", settings.Appearance.ShowArrangementOverview);
            }
            SyncFromSettings(applyWindowSize: false);
        if (Arrangement.PanKnobs != settings.Audio.PanKnobs || Arrangement.VolumeKnobs != settings.Audio.VolumeKnobs)
        {
            Arrangement.PanKnobs = settings.Audio.PanKnobs;
            Arrangement.VolumeKnobs = settings.Audio.VolumeKnobs;
            RefreshArrangement();
        }
            if (_applied.VisualChanged) RepaintAfterVisualSettings();
        }
        finally { _suppressWorkspaceSave = false; }
    }

    private static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>Makes the registry match the Windows-integration setting (only when it differs).</summary>
    private void ApplyFileAssociations(bool wanted)
    {
        var exe = ExePath;
        if (exe.Length == 0 || FileAssociations.IsRegistered(exe) == wanted) return;
        try
        {
            if (wanted) FileAssociations.Register(exe); else FileAssociations.Unregister();
            StatusText.Text = wanted ? "TabForge now opens .gp and .tforge files" : "File associations removed";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StatusText.Text = "Windows refused the file-association change";
        }
    }

    private void RepaintAfterVisualSettings()
    {
        Editor.InvalidateVisual();
        Arrangement.RefreshAll();
        Playhead.InvalidateVisual();
        RefreshInstrument();
    }

    private void Tutorial_Click(object sender, RoutedEventArgs e) => Views.TutorialWindow.ShowOrActivate(this);
    private void TutorialDetailed_Click(object sender, RoutedEventArgs e) => Views.TutorialWindow.ShowOrActivate(this, TabForge.Services.TutorialGuide.Detailed);

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            ShortcutHelp.Build(_settings.Hotkeys),
            "Keyboard shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    // ---------- practice / view toggles ----------

    /// <summary>Redraws the fretboard and saves after a practice display option changed in its menu.</summary>
    private void PracticeOptionChanged()
    {
        RefreshInstrument();
        SaveSettings();
    }

    /// <summary>
    /// Right-click on the fretboard / keyboard / drum pads: the lean menu of <see cref="InstrumentMenus"/>.
    /// Appearance, sizes, colours and look-ahead live in Preferences behind "Fretboard settings...".
    /// </summary>
    private void Instrument_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowInstrumentContextMenu(fromKeyboard: false);
        e.Handled = true;
    }

    /// <summary>The fretboard / keyboard menu; from the keyboard (Shift+F10, the Menu key) it opens at the panel's top-left with the first item focused.</summary>
    private void ShowInstrumentContextMenu(bool fromKeyboard)
    {
        var track = SelectedTrack;
        if (track is null) return;
        var drums = !Instrument.CanRepositionFretboard && !Instrument.ShowsKeyboard;
        var current = InstrumentPane.ViewFor(track);
        var state = new InstrumentMenuState(Instrument.ShowsKeyboard, drums, track.Name, current, InstrumentViews.All.ToList(),
            MusicTheoryService.NoteNames.ToList(), MusicTheoryService.Scales.Keys.ToList(), InstrumentPane.ScaleHighlight, InstrumentPane.ShowNoteNames,
            InstrumentPane.PreviewHorizon > 0, InstrumentPane.LeftHanded, _settings.Appearance.LockInstrumentSize, _settings.Appearance.FretboardAtBottom);
        var menu = SpecMenus.New("Fretboard options", InstrumentMenus.Build(state, MenuKey), spec => RunInstrumentCommand(spec, track), Instrument);
        Instrument.ContextMenu = menu;
        SpecMenus.Open(menu, Instrument, new Point(8, 8), fromKeyboard);
    }

    private void RunInstrumentCommand(MenuSpec spec, TrackModel track)
    {
        switch (spec.Id)
        {
            case InstrumentMenus.ViewId: InstrumentPane.SetInstrumentView(spec.Arg, track); break;
            case InstrumentMenus.ScaleId: InstrumentPane.SetScaleHighlight(spec.Arg); break;
            case InstrumentMenus.FindScaleId: InstrumentPane.OpenScaleFinder(); break;
            case InstrumentMenus.ClearScaleId: InstrumentPane.ClearScaleHighlight(); break;
            // Look-ahead off keeps the chosen length in Settings; turning it back on restores that length.
            case InstrumentMenus.NoteNamesId: InstrumentPane.ShowNoteNames = !spec.Checked; PracticeOptionChanged(); break;
            case InstrumentMenus.PreviewId:
                InstrumentPane.PreviewHorizon = spec.Checked ? 0 : Math.Clamp(_settings.Editing.PreviewHorizon, 1, 10);
                PracticeOptionChanged();
                break;
            case InstrumentMenus.LeftHandedId: InstrumentPane.LeftHanded = !spec.Checked; PracticeOptionChanged(); break;
            case InstrumentMenus.PositionId: WorkspaceLayouts.SetInstrumentPosition(spec.Arg == "bottom"); break;
            case InstrumentMenus.LockId: WorkspaceLayouts.ToggleInstrumentSizeLock(); break;
            case InstrumentMenus.SettingsId: OpenSettings(SettingsCatalog.Fretboard, InstrumentMenus.SettingsRow); break;
        }
    }


    private void ToggleSidePanel_Click(object sender, RoutedEventArgs e) => WorkspaceLayouts.ToggleSidePanel();

    /// <summary>View menu "Instrument view", the toolbar fretboard button and the View.InstrumentPanel hotkey.</summary>
    private void ToggleInstrumentView_Click(object sender, RoutedEventArgs e) => ToggleInstrumentPanel();

    private void ToggleInstrumentPanel()
    {
        if (_dockWorkspace is null) return;
        var show = !_dockWorkspace.IsPanelVisible("instrument");
        InstrumentViewMenu.IsChecked = show;
        _dockWorkspace.SetPanelVisible("instrument", show);   // LayoutChanged saves Appearance.ShowFretboard
        StatusText.Text = show ? "Fretboard / keyboard shown" : "Fretboard / keyboard hidden";
    }

    private void ToggleArrangement_Click(object sender, RoutedEventArgs e)
    {
        var show = !_dockWorkspace!.IsPanelVisible("timeline");
        ArrangementMenu.IsChecked = show;
        _dockWorkspace.SetPanelVisible("timeline", show);
    }

    private void ArrangementIndividualNotesMenu_Click(object sender, RoutedEventArgs e)
    {
        Arrangement.ShowIndividualNotes = ArrangementIndividualNotesMenu.IsChecked;
        ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
        ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        SaveTimelineAppearance();
    }

    private void ArrangementContinuousBlocksMenu_Click(object sender, RoutedEventArgs e)
    {
        Arrangement.ShowContinuousBlocks = ArrangementContinuousBlocksMenu.IsChecked;
        ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
        ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        SaveTimelineAppearance();
    }
}
