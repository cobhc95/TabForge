using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Video;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the command step of the main window's composition (every Register call and direct Add line, in registration order), split from MainWindow.Compose.cs.
// Does not own: the command tables and handlers themselves (the feature classes).
public sealed partial class MainWindow
{
    /// <summary>The locals the command groups hand on to each other, in registration order.</summary>
    private sealed class ComposeShared
    {
        public Action<string> ShowStatus = null!;
        public InstrumentPickerFlow InstrumentPicker = null!;
        public Action RefreshInstrumentViews = null!;
        public MainWindowTimelineBars.DeletePrompt AskDeleteBars = null!;
    }

    /// <summary>Registers every command the window has, where the constructor used to: the feature classes' Register calls and the direct Add lines,
    /// in the same order (later registrations can override earlier ones). Returns the same state with the command table and the frame's live handles.</summary>
    private MainWindowComposeState ComposeCommands(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback)
    {
        var commands = new MainWindowCommands(this, compose.Dock.ResetAllPanels);
        compose.Commands = commands;
        // All notifications appear on the single top bar (statusBar.ShowTransient forwards there); the footer keeps only position/master readouts.
        var shared = new ComposeShared { ShowStatus = compose.StatusBar.ShowTransient };
        RegisterShellAndTrackCommands(compose, playback, shared);
        RegisterSongAndToolCommands(compose, playback, shared);
        RegisterSelectionAndViewModes(compose, playback, shared);
        RegisterTransportAndScoreMenus(compose, playback, shared);
        RegisterFileCommands(compose, playback, shared);
        WatchEngineSync(compose, playback, shared);
        RegisterEditCommands(compose, playback, shared);
        return compose;
    }

    /// <summary>The help, palette, update, track list, instrument, clip and section commands.</summary>
    private void RegisterShellAndTrackCommands(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback, ComposeShared shared)
    {
        var settings = compose.Settings;
        var songs = compose.Songs;
        var panes = compose.Panes;
        var contextMenus = compose.ContextMenus;
        var scorePreview = compose.ScorePreview;
        var trackListFit = compose.TrackListFit;
        var commands = compose.Commands;
        var showStatus = shared.ShowStatus;
        // A new menu feature class adds exactly one Register line here (own line, any position) and exposes
        // `public static readonly IReadOnlyList<string> Commands`; the menu test discovers that field by reflection.
        TabForge.Avalonia.Platform.Controls.HotkeyTip.SetSource(this, () => settings?.Settings.Hotkeys ?? new TabForge.Services.HotkeySettings());
        MainWindowHelpMenu.Register(commands, this, () => settings?.Settings.Hotkeys ?? new TabForge.Services.HotkeySettings());
        MainWindowCommandPalette.Register(commands, this, () => settings?.Settings.Hotkeys ?? new TabForge.Services.HotkeySettings());
        MainWindowUpdateCheck.Register(commands, this, settings, showStatus);
        MainWindowTrackListRows.Register(commands, songs, panes, settings, this, showStatus, () => trackListFit?.Fit());
        var instrumentPicker = new InstrumentPickerFlow(playback, () => settings?.Settings.Plugins ?? new TabForge.Services.PluginSettings());
        shared.InstrumentPicker = instrumentPicker;
        instrumentPicker.InstrumentPreviewed += () => compose.InstrumentStrip?.Refresh();   // live preview: only the panel follows
        Action refreshInstrumentViews = () => { compose.InstrumentStrip?.Refresh(); panes.Band?.RefreshInstrumentViews(); };   // the panel and the Band cards follow the track's instrument
        shared.RefreshInstrumentViews = refreshInstrumentViews;
        instrumentPicker.InstrumentChanged += refreshInstrumentViews;   // on commit or cancel
        MainWindowTrackMenu.Register(commands, songs, this, contextMenus, showStatus, instrumentPicker, track => MainWindowInsertRefresh.Landed(panes, scorePreview, songs.Active, track));
        MainWindowGlobalTuning.Register(commands, songs, this, showStatus);
        MainWindowClipKeys.Register(commands, songs, panes, () => settings?.Settings.Editing.TrimEmptyBarsAtEnd ?? new EditingSettings().TrimEmptyBarsAtEnd);
        MainWindowClipEditKeys.Register(commands, songs, panes, showStatus);
        var fxWindows = MainWindowTrackFxKeys.Register(commands, songs, this, playback, settings, compose.Registry.Services.Trust);
        songs.SongClosing += song => fxWindows.CloseSong(song);
        songs.ActiveChanged += () =>
        {
            fxWindows.SongShown(songs.Songs);   // the other songs' floating plug-in windows hide now; the shown song's open again once the engine took it
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => fxWindows.SongShown(songs.Songs), global::Avalonia.Threading.DispatcherPriority.Background);
        };
        MainWindowTrackFxCells.Register(panes, songs, this, playback, () => settings?.Settings.Plugins ?? new TabForge.Services.PluginSettings(), fxWindows);
        MainWindowClipProperties.Register(commands, songs, panes, this, showStatus);
        MainWindowSectionEdit.Register(commands, songs, scorePreview, this);
        MainWindowSectionLock.Register(commands, songs, contextMenus);
        // A clip whose audio could not be read: Retry reads it again; a click on the lock of an unapproved clip opens the linked-audio review.
        commands.Add(ContextMenuRows.RetryReadId, () =>
        {
            if (contextMenus.ClickedUnreadableClip is { } unreadable) songs.Waveforms.Retry(unreadable.File, songs.Active.Media);
        });
        void AskForApproval(ArrangementView arrangement) => arrangement.Timeline.MediaApprovalRequested += () => commands.Run("Media.ManageApprovals");
        if (panes.Arrangement is { } shownArrangement) AskForApproval(shownArrangement);
        panes.ArrangementCreated += AskForApproval;
    }

    /// <summary>Song statistics, timeline sections and bars, project settings, exports, the mixer, finders, presets and the tools and view menus.</summary>
    private void RegisterSongAndToolCommands(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback, ComposeShared shared)
    {
        var settings = compose.Settings;
        var songs = compose.Songs;
        var panes = compose.Panes;
        var dock = compose.Dock;
        var contextMenus = compose.ContextMenus;
        var scorePreview = compose.ScorePreview;
        var scaleFinder = compose.ScaleFinder;
        var commands = compose.Commands;
        var showStatus = shared.ShowStatus;
        MainWindowSongStats.Register(commands, songs, this);
        MainWindowMixTable.Register(commands, songs, scorePreview, this);
        MainWindowTimelineSections.Register(commands, songs, contextMenus, showStatus,
            bar => MainWindowBarMenu.MoveToBar(scorePreview, System.Math.Clamp(bar, 0, System.Math.Max(0, scorePreview.Editor.BarCount - 1))),
            new MainWindowTimelineSections.SectionMenuPrompts(
                async (text, caption) => await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxAnswer.No) == MessageBoxAnswer.Yes,
                (action, title, colour, bar) => DialogLauncher.Ask<MarkerWindow, MarkerAnswer?>(this, () => new MarkerWindow(action, title, colour, bar)),
                () => settings?.Settings ?? SettingsDefaults.Fallback(),
                (first, last, contents) => GapClosePromptWindow.AskAsync(this, first, last, contents)));
        MainWindowTimelineBars.DeletePrompt askDeleteBars = (question, preselect, allTracks) =>
            DialogLauncher.Ask<BarRangePromptWindow, BarRangeAnswer?>(this, () => new BarRangePromptWindow(question, preselect, allTracks, ContextMenuRows.Shortcuts()));
            shared.AskDeleteBars = askDeleteBars;
        MainWindowTimelineBars.Register(commands, songs, contextMenus, showStatus,
            askDeleteBars,
            () => settings?.Settings.Editing ?? new EditingSettings(), () => settings?.SaveSettings(),
            questions => DialogLauncher.Ask<PasteOptionsWindow, PasteAnswers?>(this, () => new PasteOptionsWindow(questions)));
        MainWindowProjectSettings.Register(commands, songs, this);
        MainWindowFileExports.Register(commands, songs, this, scorePreview, settings, playback, (text, folder, kind) =>
            compose.TopBar.Show(text, kind, NoticeCategory.Render, folder is null ? null : ("Open folder", () => FolderLauncher.Open(compose.TopBar.View, folder))),
            compose.StatusBar.Activity);
        MainWindowMixer.Register(commands, songs, this, playback, settings, showStatus);
        scaleFinder.Register(commands);
        MainWindowChordFinder.Register(commands, this, scorePreview);
        MainWindowTuner.Register(commands, this, playback?.Engine, () => System.Linq.Enumerable.ElementAtOrDefault(songs.Active.Project.Tracks, MainWindowTrackMenu.SelectedIndex(songs.Active)), compose.Registry.Services.MeterTick);
        MainWindowPresetManager.Register(commands, this, () => settings?.Settings, () => settings?.SaveSettings());
        MainWindowToolsMenu.Register(commands, scorePreview, this, settings);
        MainWindowViewMenu.Register(commands, this, dock, scorePreview, settings);
        MainWindowViewHotkeys.Register(commands, songs, dock, settings);
        MainWindowViewToggles.Register(commands, settings, showStatus);
        if (compose.FretboardMenu is { } fretboardMenu) commands.Add("View.InstrumentView", fretboardMenu.CycleInstrumentView);
        MainWindowRangeCommands.Register(commands, () => panes.Selection.ActiveScore, () => panes.Selection.Model, showStatus);
    }

    /// <summary>The selection, learn mode, compact mode, band view, options and arrangement modes.</summary>
    private void RegisterSelectionAndViewModes(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback, ComposeShared shared)
    {
        var settings = compose.Settings;
        var songs = compose.Songs;
        var panes = compose.Panes;
        var dock = compose.Dock;
        var contextMenus = compose.ContextMenus;
        var topBar = compose.TopBar;
        var trackListFit = compose.TrackListFit;
        var scaleFinder = compose.ScaleFinder;
        var commands = compose.Commands;
        var showStatus = shared.ShowStatus;
        panes.Selection.Audio = () => settings?.Settings.Audio;
        panes.Selection.Register(commands, this, contextMenus, showStatus);
        var learn = MainWindowLearnMode.Register(commands, this, dock, settings, topBar, panes, () => trackListFit?.FitReplacingUserSplitOnNextLayout());
        compose.LeaveLearn = learn.LeaveFromPane;
        compose.LearnMode = learn;
        var headerModes = new MainWindowHeaderModes(this, dock, panes, songs, settings, () => trackListFit?.FitReplacingUserSplitOnNextLayout());
        var compactStrip = headerModes.Strip;
        compose.CompactStrip = compactStrip;
        MainWindowCompactMode.Register(commands, this, dock, panes, headerModes);
        MainWindowBandView.Register(commands, this, dock, panes, headerModes, settings, showStatus,
            compose.FretboardMenu, () => _ = scaleFinder.OpenAsync());
        MainWindowOptionsMenu.Register(commands, this, settings);
        MainWindowArrangementModes.Register(commands, panes, this, settings);
    }

    /// <summary>Recording, live video and input, transport, speed, score menus, zoom and the tool panes.</summary>
    private void RegisterTransportAndScoreMenus(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback, ComposeShared shared)
    {
        var settings = compose.Settings;
        var songs = compose.Songs;
        var panes = compose.Panes;
        var dock = compose.Dock;
        var scorePreview = compose.ScorePreview;
        var topBar = compose.TopBar;
        var score = compose.Score;
        var tools = compose.Tools;
        var structure = compose.Structure;
        var rhythm = compose.Rhythm;
        var layout = compose.Layout;
        var commands = compose.Commands;
        var showStatus = shared.ShowStatus;
        var recordingHooks = MainWindowTransportRecord.Register(commands, panes, playback, songs, showStatus, () => settings?.Settings.Plugins ?? new TabForge.Services.PluginSettings(),
            () => panes.Arrangement?.Transport.Snap, live => compose.EditorPane.PianoRoll.ShowLive(live), compose.StatusBar.Activity.Notify);
        compose.RecordingHooks = recordingHooks;
        compose.StatusBar.Activity.TransportRecording = () => recordingHooks.IsRecording;
        Closed += (_, _) => recordingHooks.Dispose();
        MainWindowReRecordPass.Register(commands, songs, panes, recordingHooks);
        MainWindowTrackArmKeys.Register(commands, songs, recordingHooks.IsTrackRecording, song =>
        {
            recordingHooks.ArmChanged(song);   // a disarm while recording ends that track's capture now
            playback?.SyncEngine(song, settings?.Settings.Plugins ?? new TabForge.Services.PluginSettings());
        }, showStatus);
        MainWindowTransportMenu.Register(commands, playback, songs, recordingHooks);
        MainWindowTransportToggles.Register(commands, playback, this, settings, showStatus, panes.Selection);
        var liveVideo = MainWindowLiveVideo.Register(commands, this, songs, playback, settings, score, () => panes.Band, () => compose.InstrumentStrip, showStatus, topBar);
        compose.LiveVideo = liveVideo;
        Closed += (_, _) => liveVideo.Dispose();
        var liveInput = MainWindowLiveInput.Register(commands, panes, playback, songs, this, settings, showStatus);
        Closed += (_, _) => liveInput?.Dispose();
        MainWindowTrackOutputMenu.Register(commands, playback, songs, this, showStatus);
        MainWindowSpeedHotkeys.Register(commands, songs, playback, showStatus);
        MainWindowScoreRows.Register(commands, scorePreview, settings, this, () => MainWindowBandView.ActiveScoreMenu(dock, panes));
        compose.EditorPane.Register(commands);   // Show in piano roll / Show in score mode (this track)
        // The piano roll holds the instrument panel closed while it is shown (a transient hold that never changes the user's own toggle); the score gives it back as the user had it.
        var instrumentGate = new InstrumentPanelGate(dock, shown => MainWindowViewMenu.SetRowChecks(this, "View.InstrumentPanel", shown));
        compose.EditorPane.ModeChanged = instrumentGate.RollChanged;
        if (compose.EditorPane.ShowsPianoRoll) compose.EditorPane.ModeChanged(true);
        MainWindowNotationMenu.Register(commands, scorePreview, this, settings, () => songs.Active, showStatus);
        MainWindowScoreThemeMenu.Register(commands, scorePreview, this, () => songs.Active, showStatus);
        MainWindowZoomPresets.Register(commands, scorePreview);
        MainWindowTimelineZoom.Register(commands, panes, settings);
        tools.Attach(commands);
        structure.Attach(commands);
        rhythm.Attach(commands);
        layout.Attach(commands);
    }

    /// <summary>Tabs, new, open, drop, export, PDF and template commands.</summary>
    private void RegisterFileCommands(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback, ComposeShared shared)
    {
        var settings = compose.Settings;
        var songs = compose.Songs;
        var panes = compose.Panes;
        var scorePreview = compose.ScorePreview;
        var topBar = compose.TopBar;
        var saving = compose.Saving;
        var dialogs = compose.Dialogs;
        var score = compose.Score;
        var commands = compose.Commands;
        var showStatus = shared.ShowStatus;
        var files = new MainWindowFile(dialogs, this, songs, showStatus);
        compose.File = files;
        compose.Media.Bind(songs, panes);
        compose.Media.Register(commands, compose.ContextMenus);
        commands.Add("Tab.New", songs.New);
        commands.Add("Tab.Duplicate", () => songs.Duplicate(songs.ActiveIndex));
        MainWindowTabMenu.Register(commands, this, showStatus);
        commands.Add("File.New", songs.New);
        commands.Add("File.Open", () => _ = files.Open());
        commands.Add("File.OpenInNewTab", () => _ = files.Open(inNewTab: true));
        // The local files closure, not the _files field: the field is assigned after this method returns, so the drop
        // handler would capture null and every dropped song would fail inside its fire-and-forget open.
        // Background score/audio imports live on the top bar (sticky Info with Cancel); the footer keeps only its readouts.
        var audioDrop = MainWindowAudioDrop.Register(panes, songs, showStatus, status =>
        {
            if (status.CancelVisible && status.StatusText is not null)
                topBar.Show(status.StatusText, NotificationKind.Info, NoticeCategory.Info, ("Cancel", () =>
                {
                    topBar.HideSticky(NotificationBar.ImportSource);
                    topBar.Show("Import cancelled", NotificationKind.Info, NoticeCategory.Info);
                }), source: NotificationBar.ImportSource, tooltip: status.StatusTooltip);
            else topBar.HideSticky(NotificationBar.ImportSource);
        });
        var fileDrop = new MainWindowFileDrop(this, files, () => settings?.Settings.Tabs.DropOpensInNewTab ?? false, showStatus, audioDrop.ForWindow(() => PlaybackDropSeconds(songs, playback)));
        fileDrop.Attach();
        // Linux/X11: Avalonia has no XDND, so the file manager drops arrive through our own drop target and take the same paths as a window drop.
        X11FileDrop.Attach(this, (dropped, point) => { if (!audioDrop.DropExternal(dropped, this, point)) fileDrop.DropPaths(dropped); },
            (hovered, point) => audioDrop.HoverExternal(hovered, this, point) ?? fileDrop.HoverPaths(hovered),
            () => { audioDrop.ClearExternal(); fileDrop.EndHover(); });
        // The take-lane menu (right-click on an empty part of a clip lane): its commands and the dialogs they use.
        MainWindowTakeLane.Register(commands, songs, panes, compose.ContextMenus, showStatus, new MainWindowTakeLane.Prompts(
            async (text, caption) => await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxAnswer.No) == MessageBoxAnswer.Yes,
            (title, label, initial) => new TextPromptWindow(title, label, initial).ShowDialog<string?>(this),
            () => new AvaloniaFileDialogs(settings?.DialogFolders).OpenFile(new TabForge.Platform.FileDialogOptions { Key = TabForge.Services.DialogKeys.AudioImport, Title = "Import audio or MIDI file",
                Filter = "Audio and MIDI files|*.wav;*.mp3;*.aif;*.aiff;*.flac;*.ogg;*.m4a;*.wma;*.mid;*.midi|Audio files|*.wav;*.mp3;*.aif;*.aiff;*.flac;*.ogg;*.m4a;*.wma|MIDI files|*.mid;*.midi" }, this)),
            compose.EditorPane, audioDrop.ImportOnTrack);
        // Insert menu: the commands that are new (bars after / several / at the end, Lyrics, audio and MIDI files and an empty MIDI clip at the cursor).
        MainWindowInsertMenu.Register(commands, songs, panes, scorePreview,
            new MainWindowInsertMenu.Prompts(
                bars => DialogLauncher.Ask<InsertBarsWindow, InsertBarsAnswer?>(this, () => new InsertBarsWindow(bars)),
                midi => new AvaloniaFileDialogs(settings?.DialogFolders).OpenFile(new TabForge.Platform.FileDialogOptions { Key = midi ? TabForge.Services.DialogKeys.MidiInsert : TabForge.Services.DialogKeys.AudioInsert, Title = midi ? "Insert MIDI file" : "Insert audio file",
                    Filter = midi ? "MIDI files|*.mid;*.midi" : "Audio files|*.wav;*.mp3;*.aif;*.aiff;*.flac;*.ogg;*.m4a;*.wma" }, this)),
            showStatus, () => MainWindowInsertMenu.ShowLyrics(this, compose.Dock), audioDrop.InsertAtAsync);
        commands.Add("File.ExportMidi", () => _ = files.ExportMidi());
        commands.Add("File.ExportGuitarPro", () => _ = saving.ExportGuitarPro(songs.Active, async (report, name) => { var choice = GpExportChoice.Cancel; await new GpExportPreflightWindow(report, GpExportKind.Export, name, c => choice = c).ShowDialog(this); return choice; }));
        commands.Add("File.ExportGp5", () => _ = saving.ExportLegacyCopy(songs.Active, LegacyGpFormat.Gp5));
        commands.Add("File.ExportGp4", () => _ = saving.ExportLegacyCopy(songs.Active, LegacyGpFormat.Gp4));
        commands.Add("File.ExportGp3", () => _ = saving.ExportLegacyCopy(songs.Active, LegacyGpFormat.Gp3));
        commands.Add("File.ExportAscii", () => _ = files.ExportAscii());
        commands.Add("File.ExportMusicXml", () => _ = files.ExportMusicXml());
        MainWindowScorePdf.Register(commands, scorePreview, songs, dialogs, this, settings, showStatus);
        commands.Add("File.NewFromTemplate", () => _ = files.NewFromTemplate());
        commands.Add("File.SaveAsTemplate", () => _ = files.SaveAsTemplate());
    }

    /// <summary>The engine follows Undo, Redo and track structure changes of the shown song.</summary>
    private void WatchEngineSync(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback, ComposeShared shared)
    {
        var settings = compose.Settings;
        var songs = compose.Songs;
        var score = compose.Score;
        var structure = compose.Structure;
        var refreshInstrumentViews = shared.RefreshInstrumentViews;
        // The engine brought in line with the song shown (a chain restored by Undo or Redo, a quarantine lifted): its plug-ins read from the settings.
        Action syncActiveEngine = () => playback?.SyncEngine(songs.Active, settings?.Settings.Plugins ?? new TabForge.Services.PluginSettings());
        compose.SyncActiveEngine = syncActiveEngine;
        // Every Undo or Redo of the shown document (the score's keys, the Edit menu, the toolbar, the mixer and FX windows all end in DocumentEdits) raises its Restored once:
        // the engine follows a restored chain and the instrument pane re-reads its track (the document's selection is restored by DocumentEdits).
        // The arrangement's rows, header and armed sub-rows re-read themselves (ArrangementRowSource), so they are not reloaded here.
        MainWindowTrackStructure? trackStructure = null;
        void AfterUndoRedo()
        {
            // A changed track list is synced and refreshed by the track-structure watcher (one owner); only an unchanged list is synced here.
            if (trackStructure is not null && trackStructure.Check()) return;   // the watcher's StructureChanged re-points the pane and the Band cards
            syncActiveEngine();
            compose.InstrumentStrip?.Refresh();   // the pane points at the restored track again
        }
        var restoredWatch = new TabForge.Documents.DocumentWatch { Restored = AfterUndoRedo };
        restoredWatch.Bind(songs.Active);
        songs.ActiveChanged += () => restoredWatch.Bind(songs.Active);
        Closed += (_, _) => restoredWatch.Dispose();
        if (playback is not null)
        {
            // A track added, deleted or moved (a command, Undo, Redo): the engine and the playing schedule follow.
            var trackWatcher = new MainWindowTrackStructure(playback, () => settings?.Settings.Plugins ?? new TabForge.Services.PluginSettings());
            trackStructure = trackWatcher;
            trackWatcher.StructureChanged += refreshInstrumentViews;   // one subscription: any delete, add or move re-points the panel and the Band cards
            trackWatcher.Bind(songs.Active);
            songs.ActiveChanged += () => trackWatcher.Bind(songs.Active);
            Closed += (_, _) => trackWatcher.Dispose();
        }
    }

    /// <summary>Edit, clipboard, range, track row, tab and save commands, and the keyboard table.</summary>
    private void RegisterEditCommands(MainWindowComposeState compose, Transport.AvaloniaPlaybackSessions? playback, ComposeShared shared)
    {
        var settings = compose.Settings;
        var songs = compose.Songs;
        var panes = compose.Panes;
        var dock = compose.Dock;
        var contextMenus = compose.ContextMenus;
        var scorePreview = compose.ScorePreview;
        var saving = compose.Saving;
        var commands = compose.Commands;
        var showStatus = shared.ShowStatus;
        var instrumentPicker = shared.InstrumentPicker;
        var askDeleteBars = shared.AskDeleteBars;
        var recordingHooks = compose.RecordingHooks;
        MainWindowEdit.Register(commands, scorePreview, this, contextMenus, () => settings?.Settings.Editing.ConfirmDeleteBar ?? false, showStatus, () => dock.SetPanelVisible("sections", true), () => settings?.Settings ?? new TabForge.Services.AppSettings(), () => settings?.SaveSettings(),
            bar => contextMenus.MovePlayhead?.Invoke(bar),   // the arrangement's seek: playback, playhead, and paging the view to the bar
            afterInsert: () => MainWindowInsertRefresh.Landed(panes, scorePreview, songs.Active));
        var editRouting = new EditRouting();
        var editTargets = new EditTargets(() => panes.Selection.Model, () => songs.Active.Highlight, () => EditTargetRule.FromChoice(settings?.Settings.Editing.EditTarget));
        var clipFlow = MainWindowClipboard.Register(commands, () => panes.Selection.ActiveScore, this, () => settings?.Settings.Editing ?? new TabForge.Services.EditingSettings(), showStatus, editRouting, editTargets, Windows, compose.EditorPane.RunEdit);
        var clipboard = clipFlow.Clipboard;
        MainWindowRangePaste.Register(commands, songs, panes, clipFlow, showStatus,
            request => DialogLauncher.Ask<PasteSpecialWindow, PasteSpecialOptions?>(this, () => new PasteSpecialWindow(request)),
            () => settings?.Settings.Editing ?? new EditingSettings(), () => settings?.SaveSettings(), () => playback?.For(songs.Active)?.Transport.PlayheadBar ?? 0);
        MainWindowRangeDelete.Register(commands, songs, panes, clipboard, showStatus, askDeleteBars,
            () => settings?.Settings.Editing ?? new EditingSettings(), () => settings?.SaveSettings());
        MainWindowHighlightEdit.Register(commands, songs, clipFlow, showStatus,
            async cells =>
            {
                var outcome = await PromptWindow.AskAsync(this, PromptTextsPresenter.ClearHighlightedBars(cells));
                return (PromptTextsPresenter.IsConfirmed(outcome.Answer), outcome.RememberTicked);
            },
            () => settings?.Settings.Editing ?? new EditingSettings(), () => settings?.SaveSettings());
        contextMenus.Commands = commands;
        MainWindowTabMenu.Attach(this, contextMenus);
        contextMenus.Hotkeys = () => settings?.Settings.Hotkeys ?? new TabForge.Services.HotkeySettings();
        contextMenus.EmptyLaneClick = () => EmptyLaneChoice.Normalize(settings?.Settings.Editing.EmptyLaneClick);
        contextMenus.Clipboard = clipboard;
        MainWindowTrackRowEdits.Register(commands, songs, panes, this, contextMenus, clipboard, showStatus, editRouting);
        MainWindowTrackRowOptions.Register(panes, songs, this, instrumentPicker);
        MainWindowConvertToInstrument.Register(commands, songs, this, contextMenus, () => settings?.Settings ?? new TabForge.Services.AppSettings(), () => settings?.SaveSettings());
        commands.Add("Tab.Close", () => Platform.UiTasks.Forget(songs.CloseActive(), "Closing the tab", showStatus));
        commands.Add("File.Save", () => _ = saving.Save(songs.Active));
        commands.Add("File.SaveAs", () => _ = saving.SaveAs(songs.Active));
        var keyboard = new MainWindowKeyboard(settings?.Settings.Hotkeys ?? new TabForge.Services.HotkeySettings(), commands, recordingHooks.StopRecording, () => editTargets.Focus, editRouting);
        compose.Keyboard = keyboard;
        if (settings is not null)
        {
            // Preferences Apply and OK raise SettingsChanged; the key table follows the edited shortcuts.
            System.Action<TabForge.Services.AppSettings> rebuildKeys = live => keyboard.Rebuild(live.Hotkeys);
            settings.SettingsChanged += rebuildKeys;
            Closed += (_, _) => settings.SettingsChanged -= rebuildKeys;
        }
    }
}
