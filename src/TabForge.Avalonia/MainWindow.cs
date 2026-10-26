using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Layout;
using TabForge.Audio;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Lyrics;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Video;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia;

/// <summary>
/// The preview's main window, laid out as the app's: the title row (document tab strip and caption buttons) and the menu bar (from MainMenuTable) on top, the dock
/// workspace in its default layout (the shown song's score as the editor, the Fretboard, Arrangement and View and tempo panes, a
/// labelled placeholder for each pane not ported yet), the status bar and the status notice row at the bottom.
/// Owns: the window shell and where the bars sit. Does not own: the menu (MainWindowMenu, MainWindowCommands), the dock
/// (Views/Dock), the bars and panes (the Views) or the dialogs' content.
/// Tests: TabForge.Avalonia.Tests.MainWindowTests, MainWindowMenuTests, PanelBarsTests and the dialog tests.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainWindowFile _files;
    private readonly NotificationBar _topBar;

    public const string PreviewTitle = "TabForge";
    /// <summary>The smallest window size in DIPs, as the main window (MainWindow.xaml MinWidth and MinHeight).</summary>
    public const double MinimumWidth = 1080, MinimumHeight = 720;

    /// <summary>The smallest width actually enforced, clamped to the current screen's working-area width
    /// so a portrait screen is never forced to 1080 outer DIPs. Checked: nothing inside the
    /// LayoutTransformControl sets a fixed min width (MainWindow.Compose.cs:486); inner controls peak at
    /// MinWidth 200-260 (PreferencesWindow.cs:410, RenderWindow.cs:50-51), far below the window.</summary>
    internal static double EffectiveMinimumWidth(Window window) => EffectiveMinimum(window, MinimumWidth, true);

    /// <summary>The smallest height actually enforced, clamped to the current screen's working-area height.</summary>
    internal static double EffectiveMinimumHeight(Window window) => EffectiveMinimum(window, MinimumHeight, false);

    private static double EffectiveMinimum(Window window, double minimum, bool width)
    {
        try
        {
            var screens = window.Screens;
            var screen = screens?.ScreenFromWindow(window) ?? screens?.Primary;
            if (screen is null) return minimum;
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            if (!(scaling > 0)) scaling = 1.0;
            var area = width ? screen.WorkingArea.Width / scaling : screen.WorkingArea.Height / scaling;
            if (!(area > 0)) return minimum;
            return Math.Min(minimum, area);
        }
        catch (Exception) { return minimum; }
    }

    /// <param name="settings">The settings file the window starts from and saves its layout to; null keeps defaults and saves nothing.</param>
    /// <param name="windows">The windows song tabs can be dragged between; null starts a new list with this window.</param>
    /// <param name="first">The song of the first tab (a torn-off tab); null starts a new empty song.</param>
    /// <param name="playback">The process's playback sessions (shared by every window); null leaves the transport bar disabled.</param>
    /// <summary>The main window's offers to its Settings rows (Check now, Group rules, approved folders, plug-ins switched off); set by the constructor.</summary>
    internal Views.PreferencesRowActions? PreferencesActions { get; private set; }

    /// <summary>The notice when the settings file could not be read: defaults are active.</summary>
    public const string SettingsLoadFailedNotice = "Settings could not be loaded; defaults are active. Review Preferences before saving.";

    public MainWindow(MainWindowSettings? settings = null, TabWindowRegistry? windows = null, SongDocument? first = null, Transport.AvaloniaPlaybackSessions? playback = null)
    {
        ComposeWindow(settings);
        var compose = ComposeDock(settings, windows, first, playback);
        var tabs = compose.Tabs;
        var songs = compose.Songs;
        var dock = compose.Dock;
        var registry = compose.Registry;
        _topBar = compose.TopBar;
        Settings = settings;
        DockWorkspace = dock;
        Playback = playback;
        Songs = songs;
        TabStrip = tabs;
        Windows = registry;
        Windows.Add(this);
        TabDrag = new DocumentTabDragFlow(this, songs, tabs, Windows);
        Autosave = new MainWindowAutosave(new MainWindowAutosaveHost(this, songs, Windows, settings, playback, compose.StatusBar.ShowTransient), Windows.Autosave, AutosaveService.DefaultFolder, AvaloniaUiThread.ForApplication());
        Closed += (_, _) => Autosave.Dispose();
        FileAssociationSync.Follow(settings, new AvaloniaFileAssociations(), compose.StatusBar.ShowTransient, this);
        // The instrument catalogue's first read (manifest + drum badges) waits for idle: the first picker or track edit pays no IO.
        Opened += (_, _) => AvaloniaUiThread.ForApplication().Post(static () => { _ = InstrumentCatalog.All.Count; }, TabForge.Platform.UiPriority.ApplicationIdle);

        compose = ComposeCommands(compose, playback);
        _files = compose.File;
        RecordingHooks = compose.RecordingHooks;
        compose.Autosave = Autosave;
        ComposeFrame(compose);
        // A malformed settings file already falls back to defaults without overwriting it (AppSettingsStore.LoadFailed):
        // say so on the top bar, so the silence is not mistaken for a clean start.
        if (Settings?.LoadFailed == true) _topBar.Show(SettingsLoadFailedNotice, Views.NotificationKind.Warn);
    }

    /// <summary>
    /// Opens the song named on the command line in place of the empty start-up song (the app's start-up file); the outcome or the failure shows
    /// on the top bar. False when the file could not be read, leaving the empty song in place.
    /// </summary>
    public Task<bool> OpenStartupFile(string path) => _files.OpenPath(path, replaceActive: true);

    /// <summary>Reports a command-line song path that does not exist on the top bar, without opening a tab or a dialog.</summary>
    public void ReportStartupFileMissing(string path) => _topBar.Show($"File not found: {path}", Views.NotificationKind.Warn);

    /// <summary>
    /// A song double-clicked in Explorer while this window runs (a later launch's hand-over): opened in a new tab, and the
    /// window comes forward. False for a path that is not an openable song.
    /// </summary>
    public Task<bool> OpenFromAnotherLaunch(string path, bool activate = true)
    {
        if (!SingleInstanceService.IsOpenableSong(path)) return Task.FromResult(false);
        var opened = _files.OpenPath(path);
        if (activate)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
        return opened;
    }

    /// <summary>The shared settings store (null when the window runs without one); torn-off windows get the same store.</summary>
    public MainWindowSettings? Settings { get; }

    /// <summary>The process's playback sessions, or null when the window runs without playback; torn-off windows share them.</summary>
    public Transport.AvaloniaPlaybackSessions? Playback { get; }

    /// <summary>This window's recording state: the transport ends a recording through it, and the Record buttons show it.</summary>
    internal RecordingPresenter.RecordingSession RecordingHooks { get; }

    /// <summary>The dock (panes shown, hidden and moved).</summary>
    public DockWorkspaceController DockWorkspace { get; }

    public MainWindowTabs Songs { get; }

    public TabStripView TabStrip { get; }

    public TabWindowRegistry Windows { get; }

    public DocumentTabDragFlow TabDrag { get; }

    /// <summary>The window's autosave: its copies of dirty songs, the failing-autosave notice and the start-up recovery offer.</summary>
    public MainWindowAutosave Autosave { get; }
}
