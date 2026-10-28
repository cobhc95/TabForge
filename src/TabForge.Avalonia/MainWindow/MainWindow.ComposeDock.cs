using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using TabForge.Platform.Theme;
using TabForge.Audio;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Lyrics;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Docking;
using TabForge.Avalonia.Video;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the dock step of the main window composition: the tab strip, the song tabs, the score and status parts, the dock panes and the dock controller, built in sections that hand their parts on in a DockBuild.
// Does not own: the window frame (MainWindow.Compose.cs) or the command registration (MainWindow.ComposeCommands.cs).
// Tests: no dedicated test class.
public sealed partial class MainWindow
{
    /// <summary>Builds the dock and everything that depends on it where the constructor used to: tabs, song, saving, score/status/tempo/notice, the pane
    /// setup and dock, the track-list fit and fretboard heights, the settings attach and the ActiveChanged order.</summary>
    private MainWindowComposeState ComposeDock(MainWindowSettings? settings, TabWindowRegistry? windows, SongDocument? first, Transport.AvaloniaPlaybackSessions? playback)
    {
        var d = new DockBuild { Settings = settings, Windows = windows, First = first, Playback = playback, Compose = new MainWindowComposeState() };
        var compose = d.Compose;
        var subscriptions = new SubscriptionBag();   // every subscription below that outlives a statement is undone here when the window closes
        d.Subscriptions = subscriptions;
        Closed += (_, _) => subscriptions.Dispose();
        ComposeTabsAndSong(d);
        ComposeScoreAndStatus(d);
        ComposeDockPanes(d);
        ComposeInstrumentAndSharedPanes(d);
        ComposeDockController(d);
        FillComposeState(d);
        return compose;
    }

    /// <summary>The parts the sections of <see cref="ComposeDock"/> hand on to each other, in build order.</summary>
    private sealed class DockBuild
    {
        public TabStripView Tabs = null!;
        public StatusNoticeBarView? Notice;
        public NotificationBar TopBar = null!;
        public Action<string> ShowStatus = null!;
        public AvaloniaFileDialogs Dialogs = null!;
        public TabWindowRegistry Registry = null!;
        public MainWindowSaving Saving = null!;
        public MainWindowMediaCollect Media = null!;
        public MainWindowTabs Songs = null!;
        public ViewTempoBarView ViewTempo = null!;
        public MainWindowTempoBar TempoBar = null!;
        public ContextMenuHost ContextMenus = null!;
        public ScorePreviewView ScorePreview = null!;
        public MainWindowStatusBar StatusBarCreated = null!;
        public MainWindowDocumentPanes Panes = null!;
        public ScrollViewer Score = null!;
        public EditorPaneSwitch EditorPane = null!;   // the score pane's slot: the score or the piano roll of the selected track
        public ToolsPaneView Tools = null!;
        public ToolsPaneView Structure = null!;
        public ToolsPaneView Rhythm = null!;
        public ToolsPaneView Layout = null!;
        public DockPaneSetup Setup = null!;
        public TimelineSettingsBinding TimelineSettings = null!;
        public FretboardContextMenu FretboardMenu = null!;
        public FretboardSettingsBinding Fretboard = null!;
        public MainWindowScaleFinder ScaleFinder = null!;
        public DockWorkspaceView Workspace = null!;
        public SubscriptionBag Subscriptions = null!;
        public MainWindowComposeState Compose = null!;
        public MainWindowSettings? Settings;
        public Transport.AvaloniaPlaybackSessions? Playback;
        public TabWindowRegistry? Windows;
        public SongDocument? First;
        public DockWorkspaceController Dock = null!;   // set once the dock exists
        public MainWindowTrackListFit TrackListFit = null!;   // set once the dock exists; the Timeline settings ask it to fit again
        public System.Action? CloseInstrument;   // set once the dock exists; the pane X button calls it
    }

    /// <summary>Creates the tab strip, the notification bar, the saving flow and the tabs of songs.</summary>
    private void ComposeTabsAndSong(DockBuild d)
    {
        var settings = d.Settings;
        var playback = d.Playback;
        var windows = d.Windows;
        var first = d.First;
        var tabs = new TabStripView(settings?.Settings.Tabs ?? new TabSettings());
        var initial = first ?? MainWindowTabs.NewSong();
        if (first is null) NewSongDefaults.Apply(initial, settings?.Settings ?? SettingsDefaults.Fallback());
        StatusNoticeBarView? notice = null;
        // All notifications appear on the single top bar (the video-saved bar): transients are Info 5 s (8 s with an action), engine/autosave/approval/import/video are sticky.
        var topView = new StatusNoticeBarView("Video saved", "Open folder", () => { });
        var topBar = new NotificationBar(topView, AvaloniaUiThread.ForApplication());
        Action<string> showStatus = new TopBarNoticeSink(topBar).Show;   // plain report(text) is Info and dropped; report.Error(text) etc. show
        var dialogs = new AvaloniaFileDialogs(settings?.DialogFolders);
        var registry = windows ?? new TabWindowRegistry(new AppServices(playback?.Trust ?? new TabForge.Plugins.PluginTrust()));   // the windows song tabs move between, and the state they share (autosave, degraded mode)
        Func<SavePrompt, Task<PromptButton>> askSave = async prompt => { var ask = new SavePromptWindow(prompt); await ask.ShowDialog(this); return ask.Result; };
        var media = new MainWindowMediaCollect(this, dialogs, settings, topBar, showStatus);
        Func<GpPreflightReport, string, LegacyGpFormat, Task<bool>> askLegacy = async (report, name, format) =>
        {
            var choice = GpExportChoice.Cancel;
            await new GpExportPreflightWindow(report, format, name, c => choice = c).ShowDialog(this);
            return choice == GpExportChoice.ExportCompatible;
        };
        var saving = new MainWindowSaving(dialogs, this, showStatus, SongPluginStates.ForEngine(playback?.Engine, () => settings?.Settings.Plugins.MonitorFx),
            () => (settings?.Settings ?? SettingsDefaults.Fallback()).General.DefaultSaveFormat, askSave, () => registry.Degraded, media.Step, askLegacy);
        var songs = new MainWindowTabs(initial, askSave, saving.Save, tabs, Width - CaptionButtons.ReservedWidth, settings?.Settings, showStatus, playback, TabForge.Audio.WaveformCache.Default, saving.WhenIdle);
        songs.EffectiveThemeIsLight = () => ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
        ActualThemeVariantChanged += (_, _) => songs.RefreshDefaultPaper();
        Opened += (_, _) => songs.RefreshDefaultPaper();
        var viewTempo = new ViewTempoBarView();
        var tempoBar = new MainWindowTempoBar(songs, viewTempo);
        notice = new StatusNoticeBarView("Preview notice", "Dismiss", () => notice?.Hide());
        d.Tabs = tabs;
        d.Notice = notice;
        d.TopBar = topBar;
        d.ShowStatus = showStatus;
        d.Dialogs = dialogs;
        d.Registry = registry;
        d.Saving = saving;
        d.Media = media;
        d.Songs = songs;
        d.ViewTempo = viewTempo;
        d.TempoBar = tempoBar;
        d.Notice = notice;
    }

    /// <summary>Creates the score view with its settings bindings, the status bar and the document panes.</summary>
    private void ComposeScoreAndStatus(DockBuild d)
    {
        var topBar = d.TopBar;
        var registry = d.Registry;
        var songs = d.Songs;
        var tempoBar = d.TempoBar;
        var subscriptions = d.Subscriptions;
        var settings = d.Settings;
        var playback = d.Playback;
        var notice = d.Notice;
        var contextMenus = new ContextMenuHost();
        var scorePreview = new ScorePreviewView(contextMenus);
        var scoreSettings = new ScoreSettingsBinding(scorePreview, settings?.Settings ?? SettingsDefaults.Fallback());
        if (settings is not null) { settings.SettingsChanged += scoreSettings.Apply; subscriptions.Add(() => settings.SettingsChanged -= scoreSettings.Apply); }
        tempoBar.FollowScore(scorePreview);
        if (playback is not null) tempoBar.FollowPlayback(playback);
        var followSettings = new ScoreSettingsApplier(scorePreview);
        followSettings.Apply(settings?.Settings ?? SettingsDefaults.Fallback());
        if (settings is not null)
        {
            settings.SettingsChanged += followSettings.Apply;
            subscriptions.Add(() => settings.SettingsChanged -= followSettings.Apply);
        }
        var statusBarCreated = new MainWindowStatusBar(songs, scorePreview, settings, playback?.Engine, this);
        subscriptions.Add(statusBarCreated.Dispose);
        if (playback is not null)
        {
            var liveTempo = new TabForge.Avalonia.Transport.LiveTempo(() => songs.Active, playback);   // one source for the tempo readouts
            songs.ActiveChanged += liveTempo.Rebind;
            subscriptions.Add(() => { songs.ActiveChanged -= liveTempo.Rebind; liveTempo.Dispose(); });
            tempoBar.FollowTempo(liveTempo);
            var livePlayhead = new TabForge.Avalonia.Transport.LivePlayhead(() => songs.Active, playback);   // the status bar's transport state and playhead readouts
            songs.ActiveChanged += livePlayhead.Rebind;
            subscriptions.Add(() => { songs.ActiveChanged -= livePlayhead.Rebind; livePlayhead.Dispose(); });
            statusBarCreated.FollowPlayhead(livePlayhead);
        }
        statusBarCreated.BindNotice(notice!);
        statusBarCreated.AttachNotificationBar(topBar);
        var panes = new MainWindowDocumentPanes(songs, scorePreview, playback, registry.Services.MeterTick);
        MainWindowEngineSync.Register(this, panes, songs, playback, settings, text => topBar.Show(text, NotificationKind.Error, NoticeCategory.Error, source: NotificationBar.EngineSource), registry.Services.MeterTick);
        d.ContextMenus = contextMenus;
        d.ScorePreview = scorePreview;
        d.StatusBarCreated = statusBarCreated;
        d.Panes = panes;
    }

    /// <summary>Registers the panes of the dock: the score, the tools, the instrument strip, the sections and the rest, and wires the timeline settings and the track-list fit.</summary>
    private void ComposeDockPanes(DockBuild d)
    {
        var showStatus = d.ShowStatus;
        var songs = d.Songs;
        var scorePreview = d.ScorePreview;
        var statusBarCreated = d.StatusBarCreated;
        var panes = d.Panes;
        var subscriptions = d.Subscriptions;
        var settings = d.Settings;
        var score = new ScrollViewer { Content = scorePreview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var tools = new ToolsPaneView(scorePreview);
        var structure = new ToolsPaneView(scorePreview, PaletteToolCatalog.StructureTab);
        var rhythm = new ToolsPaneView(scorePreview, PaletteToolCatalog.RhythmTab);
        var layout = new ToolsPaneView(scorePreview, PaletteToolCatalog.LayoutTab);
        var setup = new DockPaneSetup();
        // The score pane holds the score or the piano roll of the selected track (per track, saved in the song).
        var editorPane = new EditorPaneSwitch(score, scorePreview, songs, panes, d.Playback, settings, () => this, showStatus);
        // The dock rebuilds its tree on every change: the one score view moves to the new editor host; arrangement/band/instrument are reused (Detached/Shared).
        setup.RegisterEditor(() => DockPaneSetup.Detached(editorPane.Host));
        var timelineSettings = new TimelineSettingsBinding(settings?.Settings ?? SettingsDefaults.Fallback(), shown => d.Dock?.SetPanelVisible("timeline", shown), () => d.TrackListFit?.Fit());
        // One wiring per arrangement (replace, not append): a rebuilt arrangement drops the previous
        // track-list fit before taking it, so a repeated creation never stacks duplicate fits.
        ArrangementView? fittedArrangement = null;
        void FitTrackList() => d.TrackListFit?.Fit();
        void GrowForArm(int row) => d.TrackListFit?.GrowForArm(row);
        Action<ArrangementView> onArrangement = arrangement =>
        {
            timelineSettings.Attach(arrangement);
            statusBarCreated.View.SetTimelineScrollBar(arrangement.FooterScrollBar);
            if (!ReferenceEquals(fittedArrangement, arrangement))
            {
                if (fittedArrangement is not null) { fittedArrangement.TrackRowsCountChanged -= FitTrackList; fittedArrangement.TrackArmChanged -= GrowForArm; }
                fittedArrangement = arrangement;
                arrangement.TrackRowsCountChanged += FitTrackList;
                arrangement.TrackArmChanged += GrowForArm;
            }
            MainWindowGlobalTuning.BindTransport(arrangement.Transport, songs, this, showStatus);
        };
        panes.ArrangementCreated += onArrangement;
        subscriptions.Add(() =>
        {
            panes.ArrangementCreated -= onArrangement;
            if (fittedArrangement is not null) { fittedArrangement.TrackRowsCountChanged -= FitTrackList; fittedArrangement.TrackArmChanged -= GrowForArm; }
        });
        d.Score = score;
        d.EditorPane = editorPane;
        d.Tools = tools;
        d.Structure = structure;
        d.Rhythm = rhythm;
        d.Layout = layout;
        d.Setup = setup;
        d.TimelineSettings = timelineSettings;
    }

    /// <summary>Registers the fretboard and instrument pane, the sections, the shared tool panes and the lyrics, and creates the fretboard menu and the scale finder.</summary>
    private void ComposeInstrumentAndSharedPanes(DockBuild d)
    {
        var showStatus = d.ShowStatus;
        var songs = d.Songs;
        var viewTempo = d.ViewTempo;
        var scorePreview = d.ScorePreview;
        var panes = d.Panes;
        var subscriptions = d.Subscriptions;
        var compose = d.Compose;
        var settings = d.Settings;
        var playback = d.Playback;
        var setup = d.Setup;
        var tools = d.Tools;
        var structure = d.Structure;
        var rhythm = d.Rhythm;
        var layout = d.Layout;
        var fretboardDefaults = new TabForge.Services.AppSettings();
        var fretboardMenu = new FretboardContextMenu(() => settings?.Settings ?? fretboardDefaults, () => settings?.SaveSettings(),
            () => MainWindowSettings.ShowPreferencesPage(settings, this, TabForge.Services.SettingsCatalog.Fretboard),
            () => scorePreview.Editor.Project is { Tracks.Count: > 0 } ? scorePreview.Editor.Track : null);
        compose.FretboardMenu = fretboardMenu;
        var fretboard = new FretboardSettingsBinding(settings?.Settings ?? fretboardDefaults);
        var scaleFinder = new MainWindowScaleFinder(songs, scorePreview, () => panes.Selection.Model, () => settings?.Settings.Editing ?? new TabForge.Services.EditingSettings(), () => settings?.SaveSettings(), this);
        // Perf E2b (H4/H5): the instrument pane is reused across Shows (was a fresh InstrumentPanelStrip per Show).
        // The strip itself is untouched; only its registration caches the control and detaches it on rebuild.
        InstrumentPanelStrip? cachedInstrument = null;
        setup.Register("instrument", () =>
        {
            if (cachedInstrument is not null)
            {
                compose.InstrumentStrip = cachedInstrument;
                return DockPaneSetup.Detached(cachedInstrument);
            }
            cachedInstrument = new InstrumentPanelStrip(scorePreview, () => d.CloseInstrument?.Invoke(), () => songs.Songs.Count == 0 ? null : playback?.For(songs.Active), fretboardMenu, () => _ = scaleFinder.OpenAsync());
            compose.InstrumentStrip = fretboard.Attach(cachedInstrument);
            return cachedInstrument;
        },keepsNaturalHeight: InstrumentPanelStrip.LocksRowHeight(settings?.Settings.Appearance));
        songs.ActiveChanged += () => compose.InstrumentStrip?.RebindPlayback();
        setup.Register("learn", () => panes.NewKeyboardMode(() => compose.LeaveLearn?.Invoke()));
        setup.Register("band", panes.NewBand);
        setup.Register("timeline", panes.NewArrangement);
        _ = new NotePreviewSound(scorePreview.Editor, () => playback, () => settings?.Settings.Audio ?? new TabForge.Services.AudioSettings(), showStatus,
            () => songs.Songs.Count == 0 ? null : playback?.For(songs.Active)?.Routing);
        var sectionLinks = new SectionLinks(songs, scorePreview, playback, () => panes.Arrangement);
        panes.ArrangementCreated += sectionLinks.Follow;
        subscriptions.Add(() => { panes.ArrangementCreated -= sectionLinks.Follow; sectionLinks.Dispose(); });
        setup.Register("sections", sectionLinks.NewPanel);
        setup.RegisterShared("tools", tools);
        setup.RegisterShared("structure", structure);
        setup.RegisterShared("rhythm", rhythm);
        setup.RegisterShared("layout", layout);
        setup.RegisterShared(DockPaneTable.ViewTempoId, viewTempo);
        LyricsPaneFlow.Register(setup, songs, playback);
        setup.RegisterPlaceholdersForTheRest();
        d.FretboardMenu = fretboardMenu;
        d.Fretboard = fretboard;
        d.ScaleFinder = scaleFinder;
    }

    /// <summary>Creates the dock workspace and its controller, and the fretboard heights, the settings attach and the settings subscriptions.</summary>
    private void ComposeDockController(DockBuild d)
    {
        var showStatus = d.ShowStatus;
        var songs = d.Songs;
        var panes = d.Panes;
        var setup = d.Setup;
        var timelineSettings = d.TimelineSettings;
        var fretboard = d.Fretboard;
        var subscriptions = d.Subscriptions;
        var settings = d.Settings;
        var playback = d.Playback;
        var workspace = new DockWorkspaceView(setup.ContentOf);
        d.Dock = new DockWorkspaceController(workspace, setup, settings?.StartLayout ?? DockWorkspaceController.DefaultLayout());
        // The dock builds the first arrangement here, so the clip-edit refresh attaches to it and to every later pane.
        MainWindowClipRefresh.Register(panes, settings, playback, showStatus, this);
        d.CloseInstrument = () => d.Dock.ClosePanel("instrument");
        d.TrackListFit = new MainWindowTrackListFit(d.Dock, workspace, () => panes.Arrangement, () => (settings?.Settings ?? SettingsDefaults.Fallback()).Timeline.AutoFitTrackList, AvaloniaUiThread.ForApplication());
        // The fretboard's height is per tab: the tab being left keeps its height before the timeline refits (the refit moves the split beside the pane),
        // and the tab shown starts its fretboard after it. Hiding and showing the fretboard keeps the height too (the rebuild is read before and after).
        var fretboardHeights = new MainWindowFretboardHeight(d.Dock, songs, AvaloniaUiThread.ForApplication());
        songs.ActiveChanged += fretboardHeights.RememberShownHeight;
        songs.ActiveChanged += d.TrackListFit.FitReplacingUserSplit;
        songs.ActiveChanged += fretboardHeights.ApplyShownTab;
        d.Dock.LayoutRebuilding += fretboardHeights.RememberShownHeight;
        d.Dock.LayoutRebuilt += fretboardHeights.RestoreWhenShown;
        // The first tab starts the fretboard pane at its natural height, saved layout or not, as a new tab and an opened song do; the splitter can then move it.
        // The split beside the pane is laid out after the dock's own size change, so the fit runs on the UI thread's next turn.
        workspace.SizeChanged += (_, _) => fretboardHeights.Refit();
        settings?.Attach(d.Dock, this);
        fretboard.BindDock(d.Dock);
        if (settings is not null)
        {
            settings.SettingsChanged += songs.ApplySettings;
            settings.SettingsChanged += fretboard.Apply;
            settings.SettingsChanged += timelineSettings.Apply;
            MainWindowPlacement.Track(this, settings);
            settings.TimelineZoomSaved += timelineSettings.ApplyZoom;
            subscriptions.Add(() => { settings.SettingsChanged -= songs.ApplySettings; settings.SettingsChanged -= fretboard.Apply; settings.SettingsChanged -= timelineSettings.Apply; settings.TimelineZoomSaved -= timelineSettings.ApplyZoom; });
        }
        songs.CloseWindowRequested += () => Close();
        d.Workspace = workspace;
    }

    /// <summary>Hands the built parts to the compose state.</summary>
    private void FillComposeState(DockBuild d)
    {
        var tabs = d.Tabs;
        var topBar = d.TopBar;
        var dialogs = d.Dialogs;
        var registry = d.Registry;
        var saving = d.Saving;
        var songs = d.Songs;
        var tempoBar = d.TempoBar;
        var contextMenus = d.ContextMenus;
        var scorePreview = d.ScorePreview;
        var statusBarCreated = d.StatusBarCreated;
        var panes = d.Panes;
        var score = d.Score;
        var tools = d.Tools;
        var structure = d.Structure;
        var rhythm = d.Rhythm;
        var layout = d.Layout;
        var scaleFinder = d.ScaleFinder;
        var workspace = d.Workspace;
        var compose = d.Compose;
        var settings = d.Settings;
        var notice = d.Notice;
        compose.Settings = settings;
        compose.Tabs = tabs;
        compose.Songs = songs;
        compose.Dock = d.Dock;
        compose.Panes = panes;
        compose.StatusBar = statusBarCreated;
        compose.Notice = notice!;
        compose.TopBar = topBar;
        compose.TempoBar = tempoBar;
        compose.Dialogs = dialogs;
        compose.Saving = saving;
        compose.Media = d.Media;
        compose.Registry = registry;
        compose.Workspace = workspace;
        compose.ScorePreview = scorePreview;
        compose.ContextMenus = contextMenus;
        compose.TrackListFit = d.TrackListFit;
        compose.Score = score;
        compose.EditorPane = d.EditorPane;
        compose.Tools = tools;
        compose.Structure = structure;
        compose.Rhythm = rhythm;
        compose.Layout = layout;
        compose.ScaleFinder = scaleFinder;
    }
}
