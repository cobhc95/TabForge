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

// Owns: the main window's composition: the window chrome at the start of the constructor (title, icon, first-start size, restored placement and the
//   background resource), the dock step (tabs, song, saving, score/status/tempo/notice, pane setup, dock, track-list fit, fretboard heights and settings
//   attach), the command step (every Register call and direct Add line, in registration order) and the content frame at the end (title row, menu,
//   compact strip, notices, status bar, approvals, workspace, scaler, theme binding).
// Does not own: the command tables and handlers themselves (the feature classes), the menu tables, the bars and panes' content or the window's public
//   properties.
// Tests: TabForge.Avalonia.Tests.MainWindowTests, TabForge.Avalonia.Tests.MainWindowMenuTests, TabForge.Avalonia.Tests.TitleBarTests.
public sealed partial class MainWindow
{
    /// <summary>The handles the frame step reads from the constructor. Only the fields frame composition needs; ComposeDock/ComposeCommands extend it.</summary>
    private sealed class MainWindowComposeState
    {
        public MainWindowSettings? Settings;
        public TabStripView Tabs = null!;
        public MainWindowCommands Commands = null!;
        public DockWorkspaceController Dock = null!;
        public MainWindowDocumentPanes Panes = null!;
        public MainWindowTabs Songs = null!;
        public MainWindowStatusBar StatusBar = null!;
        public StatusNoticeBarView Notice = null!;
        public NotificationBar TopBar = null!;
        public MainWindowLiveVideo LiveVideo = null!;
        public Control CompactStrip = null!;
        public MainWindowAutosave Autosave = null!;
        public DockWorkspaceView Workspace = null!;
        public MainWindowKeyboard Keyboard = null!;
        public MainWindowTempoBar TempoBar = null!;
        public AvaloniaFileDialogs Dialogs = null!;
        public MainWindowSaving Saving = null!;
        public TabWindowRegistry Registry = null!;
        public ScorePreviewView ScorePreview = null!;
        public ContextMenuHost ContextMenus = null!;
        public MainWindowTrackListFit TrackListFit = null!;
        public ScrollViewer Score = null!;
        public ToolsPaneView Tools = null!;
        public ToolsPaneView Structure = null!;
        public ToolsPaneView Rhythm = null!;
        public ToolsPaneView Layout = null!;
        public MainWindowScaleFinder ScaleFinder = null!;
        public MainWindowFile File = null!;
        public RecordingPresenter.RecordingSession RecordingHooks = null!;
        public Action? LeaveLearn;
        public MainWindowLearnMode? LearnMode;
        public InstrumentPanelStrip? InstrumentStrip;
        public FretboardContextMenu? FretboardMenu;
        public Action SyncActiveEngine = null!;
    }

    /// <summary>Applies the window chrome where the constructor used to: title, icon, first-start size, restored placement and the background resource.</summary>
    private void ComposeWindow(MainWindowSettings? settings)
    {
        Title = PreviewTitle;
        var iconPath = System.IO.Path.Combine(global::System.AppContext.BaseDirectory, "Assets", "TabForge.ico");
        // The icon file is read after the first frame (it is not needed to paint it).
        EventHandler? setIcon = null;
        setIcon = (_, _) =>
        {
            Opened -= setIcon;
            AvaloniaUiThread.ForApplication().Post(() =>
            {
                if (System.IO.File.Exists(iconPath)) Icon = new WindowIcon(iconPath);
            }, TabForge.Platform.UiPriority.ApplicationIdle);
        };
        Opened += setIcon;
        var defaults = new AppSettings();   // the first-start window size is the WPF default (AppSettings.WindowWidth and WindowHeight)
        Width = defaults.WindowWidth;
        Height = defaults.WindowHeight;
        MinWidth = EffectiveMinimumWidth(this);
        MinHeight = EffectiveMinimumHeight(this);
        var placed = MainWindowPlacement.Restore(this, settings);
        MainWindowPlacement.FollowSettings(this, settings);
        if (!placed)
        {
            // Screens/Position were not ready at construction (owner-PC MainLoop neighbourhood): one deferred
            // retry after the first Opened, then never again. Sync success needs no retry.
            EventHandler? retry = null;
            retry = (_, _) =>
            {
                Opened -= retry;
                MainWindowPlacement.Restore(this, settings);
            };
            Opened += retry;
        }
        Bind(BackgroundProperty, this.GetResourceObservable("WindowBackground"));
    }

    /// <summary>Where a score-dropped audio file lands: the running playback's song second, else the cursor bar's second.</summary>
    private static double PlaybackDropSeconds(MainWindowTabs songs, Transport.AvaloniaPlaybackSessions? playback)
    {
        var song = songs.Active;
        if (playback?.Existing(song) is { IsPlaying: true } session && !double.IsNaN(session.SchedulerSec))
            return Math.Max(0, session.SchedulerSec);
        var bars = AudioClipTimelineRules.BarCount(song.Project);
        var bar = bars == 0 ? 0 : Math.Clamp(song.CursorBar, 0, bars - 1);
        var fraction = song.CursorCell / (double)Math.Max(1, MusicTime.BarSlots(song.Project, bar));
        return AudioClipTimelineRules.SecondsAtBar(song.Project, bar, fraction);
    }
    /// <summary>Builds the content frame where the constructor used to: title row/menu/compact strip, the notices, the status bar, approvals, the dock
    /// workspace, the layout scaler and the theme binding.</summary>
    private void ComposeFrame(MainWindowComposeState state)
    {
        _ = new MainWindowLifetime(this, state.Keyboard, state.Songs, state.TempoBar, state.Dock, state.StatusBar, state.Panes.Selection, state.Songs)
        { FinishBeforeClose = state.LiveVideo.FinishBeforeClose };
        var menu = MainWindowMenu.Build(state.Commands, this, new DockViewMenus(state.Dock, state.Settings, this,
            state.StatusBar.ShowTransient,
            realLayout: () => state.LearnMode?.RealLayout,
            bandToggle: () => state.Commands.Run("View.BandView"),
            learnToggle: () => state.Commands.Run("View.LearnMode")),
            state.Settings?.Settings.Hotkeys ?? new HotkeySettings());
        if (state.Settings is { } iconSettings)
        {
            // Icon size: the menu row's icons are built once, so each applied size resizes them in place.
            void SizeIcons(AppSettings applied) { foreach (var box in menu.GetVisualDescendants().OfType<Control>().Where(c => c.Name is MenuRowToggles.PanelName or QuickActionsGroup.GroupName).SelectMany(c => c.GetVisualDescendants()).OfType<Viewbox>()) box.Width = box.Height = IconSizing.Current; }
            iconSettings.SettingsChanged += SizeIcons;
            Closed += (_, _) => iconSettings.SettingsChanged -= SizeIcons;
        }
        if (state.Settings is { } toolbarSettings && Views.QuickActionsGroup.In(menu) is { } quickGroup)
        {
            // Show the toolbar: the Open / Save group and its divider in the menu row follow the row (the transport bars follow it in TimelineSettingsBinding).
            var quickPanel = (DockPanel)quickGroup.Parent!;
            var quickDivider = quickPanel.Children[quickPanel.Children.IndexOf(quickGroup) - 1];
            void ShowQuick(AppSettings applied) => quickGroup.IsVisible = quickDivider.IsVisible = applied.General.ShowToolbar;
            ShowQuick(toolbarSettings.Settings);
            toolbarSettings.SettingsChanged += ShowQuick;
            Closed += (_, _) => toolbarSettings.SettingsChanged -= ShowQuick;
        }
        var titleRow = TitleBarView.For(this, state.Tabs, state.Commands);
        state.LiveVideo.RecordingChanged += titleRow.ShowRecording;
        var middleClick = new TabForge.Shell.MiddleClickTracker();
        titleRow.AddHandler(InputElement.PointerPressedEvent, (_, e) => TabBehaviourSettings.OnTitleBarPressed(e, state.Settings, middleClick), RoutingStrategies.Bubble);
        titleRow.AddHandler(InputElement.PointerReleasedEvent, (_, e) => TabBehaviourSettings.OnTitleBarReleased(e, state.Settings, state.Songs, middleClick), RoutingStrategies.Bubble);
        foreach (var top in new Control[] { titleRow, menu, state.CompactStrip }) DockPanel.SetDock(top, Dock.Top);
        var approvals = new MainWindowApprovalNotices(this, state.Songs, state.Settings, state.SyncActiveEngine, state.Registry.Services.Trust);
        approvals.Register(state.Commands);
        // All persistent notices live on the single top bar; the bottom rows stay docked (tombstones) but hidden.
        state.Autosave.AttachNotificationBar(state.TopBar);
        approvals.AttachNotificationBar(state.TopBar);
        PreferencesActions = new Views.PreferencesRowActions
        {
            RunCommand = id => state.Commands.Run(id),
            ManageLinkedAudio = owner => approvals.ShowLinkedAudioReviewAsync(owner),
            ManageQuarantine = approvals.ShowQuarantineAsync,
            ConfigureAsio = driver => (global::Avalonia.Application.Current as App)?.Engine?.ShowAsioPanelAsync(driver) ?? Task.FromResult("The audio engine is not available."),
            RunningAsioDevice = () => (global::Avalonia.Application.Current as App)?.Engine?.RunningAsioDevice,
        };
        DockPanel.SetDock(state.Notice, Dock.Bottom);
        DockPanel.SetDock(state.Autosave.Notice, Dock.Bottom);
        // The "Video saved" strip sits at the top, above the workspace (and its fretboard pane), not under the status bar.
        DockPanel.SetDock(state.LiveVideo.SavedNotice, Dock.Top);
        var framed = titleRow.Frame(new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                titleRow, menu, state.CompactStrip, state.LiveVideo.SavedNotice, state.Notice, state.Autosave.Notice, state.StatusBar.View,
                approvals.MediaNotice, approvals.TrustNotice, state.Workspace,
            },
        });
        var scaler = new LayoutTransformControl { Child = framed };
        Content = scaler;
        if (state.Settings is not null)
        {
            var theme = new ThemeSettingsBinding(this, scaler, state.Settings, state.Panes);
            Closed += (_, _) => theme.Dispose();
        }
    }
}
