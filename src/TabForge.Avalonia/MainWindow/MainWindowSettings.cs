using System;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Avalonia window's settings: the store it reads (the same AppSettingsStore and settings.json format), the
//   saved dock layout it starts from, saving the captured layout on the dock's debounced LayoutChanged and on window close, and
//   saving the Settings window's OK and Apply.
// Does not own: the file format, bounds and migration (SettingsFileService, SettingsValidator, WorkspaceNormalizer), the layout
//   rules (DockWorkspaceRules via DockWorkspaceController), the apply order of the other settings (MainSettingsFlow).
// Tests: TabForge.Avalonia.Tests.MainWindowSettingsTests.
/// <summary>Loads the settings file once and keeps the dock layout in it.</summary>
public sealed class MainWindowSettings : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(UiTiming.SettingsSaveDebounceMs);

    private readonly AppSettingsStore _store;
    private readonly ToolWindows _tools = new();
    // Windows attached and still open: key none, ceiling the open main windows, UI thread. The store closes with the last one.
    private int _openWindows;
    // Preview notices: the tick of the last one and the trailing timer; key none, ceiling one, UI thread.
    private long _lastPreviewTick;
    private IUiTimer? _previewTimer;
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(UiTiming.SettingsPreviewIntervalMs);

    private MainWindowSettings(AppSettingsStore store)
    {
        _store = store;
        _store.Changed += _ => SettingsChanged?.Invoke(_store.Settings);
        DialogFolders = TabForge.Services.DialogFolderMemory.ForApp(() => _store.Settings, SaveSettingsQuietly);
    }

    /// <summary>The remembered folders of the file dialogs: each dialog key starts in its own last folder (saved quietly, with the settings).</summary>
    public TabForge.Services.DialogFolderMemory DialogFolders { get; }

    /// <summary>Raised with the live settings when the store marks them changed (a layout save or a replaced settings object).</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>The user's settings file (the one the app uses).</summary>
    public static MainWindowSettings OpenDefault() => Open(AppSettingsStore.DefaultPath);

    /// <summary>The settings file at <paramref name="path"/>; a missing file gives defaults, an unreadable one is never overwritten.</summary>
    public static MainWindowSettings Open(string path)
    {
        return new(AppSettingsStore.Open(path, SaveDelay));
    }

    public AppSettings Settings => _store.Settings;

    /// <summary>The settings file exists but could not be read: defaults are active and nothing is saved until Preferences are applied.</summary>
    public bool LoadFailed => _store.LoadFailed;

    /// <summary>The layout the window starts from: the saved one (already normalized on load), else the default layout.</summary>
    public DockWorkspaceState StartLayout => DockWorkspaceController.StartupLayout(_store.Settings.Workspace, _store.Settings.Appearance.FretboardAtStartup);

    /// <summary>Saves the dock's layout a moment after each change and once more when <paramref name="window"/> closes.</summary>
    public void Attach(DockWorkspaceController dock, Window window)
    {
        // Every window saves its own layout into the one store (the last save wins).
        _openWindows++;
        dock.LayoutChanged += SaveLayout;
        window.Closing += (_, _) => { dock.FlushLayoutChanged(); FlushDeferred(); _store.Flush(); };   // a placement saved quietly reaches the disk with the window, not only with the last one
        window.Deactivated += (_, _) => _store.Flush();
        window.Closed += (_, _) =>
        {
            if (--_openWindows > 0) return;
            if (_holds > 0) _disposeWhenReleased = true; else Dispose();
        };
    }

    // Closes that still save after the last main window closed (the FX chain window collecting plug-in states): key none, ceiling the open FX windows, UI thread.
    private int _holds;
    private bool _disposeWhenReleased;

    /// <summary>Keeps the store open past the last main window's close until the handle is disposed; then the store closes.</summary>
    public IDisposable HoldOpen()
    {
        _holds++;
        return new Hold(this);
    }

    private sealed class Hold(MainWindowSettings owner) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            if (--owner._holds == 0 && owner._disposeWhenReleased) owner.Dispose();
        }
    }

    /// <summary>The layout the saved Workspace keeps while compact mode or the Band layout is on; null when the Workspace follows the dock.</summary>
    private DockWorkspaceState? _heldWorkspace;

    /// <summary>
    /// Keeps the saved Workspace at <paramref name="layout"/> (a copy of the layout before the mode) while compact mode or the Band layout is on,
    /// so a restart does not start with their hidden panes. Null releases it; the dock's next layout change then saves as usual.
    /// </summary>
    public void HoldWorkspace(DockWorkspaceState? layout)
    {
        _heldWorkspace = layout;
        if (layout is null) return;
        _store.Settings.Workspace = layout;
        _store.MarkChanged(this);
    }

    /// <summary>Stores <paramref name="layout"/> (a DockWorkspaceRules.Capture result) and schedules the save; while a layout is held, the held one is stored.</summary>
    public void SaveLayout(DockWorkspaceState layout)
    {
        var stored = _heldWorkspace ?? layout;
        _store.Settings.Workspace = stored;
        // The fretboard setting follows the saved layout: a pane the user closed must not be shown again by the settings' apply.
        _store.Settings.Appearance.ShowFretboard = FretboardShownIn(stored);
        _store.MarkChanged(this);
    }

    /// <summary>Whether the fretboard pane is in the layout, docked or floating.</summary>
    private static bool FretboardShownIn(DockWorkspaceState state) =>
        DockWorkspaceState.EnumeratePanelIds(state.Root).Contains(FretboardSettingsBinding.InstrumentPaneId)
        || state.Floating.Any(floating => DockWorkspaceState.EnumeratePanelIds(floating.Root).Contains(FretboardSettingsBinding.InstrumentPaneId));

    /// <summary>Schedules a save of the settings after a change made outside the layout (a row toggle).</summary>
    public void SaveSettings() => _store.MarkChanged(this);

    /// <summary>Writes a waiting save to the file now.</summary>
    internal void Flush() => _store.Flush();

    private bool _deferredSaveQueued, _deferredQuiet = true;

    /// <summary>Like <see cref="SaveSettings"/> but after the frame that paints the click: the save, its serialize and the observers' broadcast run once, later (a toggle's check shows first).</summary>
    public void SaveSettingsDeferred(bool notifyObservers = true)
    {
        _deferredQuiet &= !notifyObservers;
        if (_deferredSaveQueued) return;
        _deferredSaveQueued = true;
        AvaloniaUiThread.ForApplication().Post(FlushDeferred, UiPriority.Background);
    }

    /// <summary>Marks the deferred save now when one is waiting (the window closing must not lose it); a no-op otherwise, so the queued run after it does nothing.</summary>
    private void FlushDeferred()
    {
        if (!_deferredSaveQueued) return;
        _deferredSaveQueued = false;
        var quiet = _deferredQuiet;
        _deferredQuiet = true;
        _store.MarkChanged(this, notifyObservers: !quiet);
    }

    /// <summary>Schedules a save without telling the settings observers (a live slider drag that only the audio engine cares about).</summary>
    public void SaveSettingsQuietly() => _store.MarkChanged(this, notifyObservers: false);

    /// <summary>Raised with the factor when a window saved a new timeline zoom (<see cref="SaveTimelineZoom"/>), so the other windows' arrangement panes follow it.</summary>
    public event Action<double>? TimelineZoomSaved;

    /// <summary>Clamps <paramref name="factor"/> to the zoom range, keeps it in the settings and saves it without the full SettingsChanged broadcast (re-applying every setting stalls the playhead); only <see cref="TimelineZoomSaved"/> tells the panes.</summary>
    public void SaveTimelineZoom(double factor)
    {
        factor = TimelineZoomRules.Clamp(factor);
        Settings.Timeline.ZoomFactor = factor;
        SaveSettingsQuietly();
        TimelineZoomSaved?.Invoke(factor);
    }

    /// <summary>Schedules a save of a change the main window tracks itself (its position and size) without raising SettingsChanged.</summary>
    public void SaveWindowPlacement() => SaveSettingsQuietly();

    /// <summary>Opens the Settings window on <paramref name="page"/> over <paramref name="owner"/>, showing the shared settings (defaults when there is no store).</summary>
    /// <param name="rowKey">The setting row the page scrolls to once it is laid out (the right-click rows that name a setting); null opens the page's top.</param>
    public static void ShowPreferencesPage(MainWindowSettings? settings, Window owner, string page, string? rowKey = null)
    {
        PreferencesWindow window;
        if (settings is null)
        {
            window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
            window.ShowPage(page);
            window.Show(owner);
        }
        else
        {
            var made = false;
            window = settings.OpenPreferences(owner, () =>
            {
                made = true;
                var created = settings.CreatePreferencesWindow((owner as MainWindow)?.PreferencesActions);
                created.ShowPage(page);
                return created;
            });
            if (!made) window.ShowPage(page);
        }
        if (rowKey is null) return;
        AvaloniaUiThread.ForApplication().Post(() =>
        {
            if (window.ShownRows.Any(row => row.Key == rowKey)) window.EditorOf(rowKey).BringIntoView();
        }, UiPriority.Loaded);
    }

    /// <summary>The one Settings window: shown over <paramref name="owner"/> from <paramref name="create"/>, or the open one brought forward.</summary>
    internal PreferencesWindow OpenPreferences(Window owner, Func<PreferencesWindow> create) => _tools.OpenOrActivate("Settings", null, create, owner);

    /// <summary>The Settings window over the shared settings: its OK and Apply save them to the settings file.</summary>
    public PreferencesWindow CreatePreferencesWindow(Views.PreferencesRowActions? actions = null)
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), Settings, actions) { DialogFolders = DialogFolders };
        // Unapplied previews live in memory only; a save during the session (a resize, a deactivate) may have written them, so a session that ends with some left saves again after the restore.
        var unappliedPreview = false;
        window.Applied += () => { unappliedPreview = false; _previewTimer?.Stop(); SaveSettingsDeferred(); };   // one deferred save and broadcast covers the pending preview
        // The live output (driver, device, rate, buffer) switches the engine at once, so it is saved at once too; Cancel restores both (the Closed handler below saves the restore).
        var output = OutputKey(Settings.Plugins);
        window.Previewed += () => { unappliedPreview = true; if (OutputKey(Settings.Plugins) is var now && now != output) { output = now; SaveSettingsQuietly(); } RaisePreviewed(); };
        // A resize writes into the live settings directly (PreferencesWindow.CaptureSize); the debounced store saves it without telling the settings observers (a resize step must not re-apply every setting).
        window.WindowSizeChanged += SaveWindowPlacement;
        // The window's own Closed handler restored the snapshot first (silently); the one revert notice goes out after the window is gone.
        window.Closed += (_, _) =>
        {
            if (!unappliedPreview && !window.Restored) return;
            SaveSettingsQuietly();
            AvaloniaUiThread.ForApplication().Post(() =>
            {
                _previewTimer?.Stop();
                _lastPreviewTick = Environment.TickCount64;
                SettingsChanged?.Invoke(_store.Settings);
            }, UiPriority.Background);
        };
        return window;
    }

    internal static string OutputKey(PluginSettings p) => $"{p.Driver}|{p.Device}|{p.SampleRate}|{p.BufferSize}";

    /// <summary>
    /// Tells every settings consumer the live settings changed without saving: at once when the last notice is older than the preview interval,
    /// else one trailing notice at the interval's end, so a drag on a slider relayouts the score about every 100 ms.
    /// </summary>
    private void RaisePreviewed()
    {
        if (_previewTimer is { IsEnabled: true }) return;
        var now = Environment.TickCount64;
        if (now - _lastPreviewTick >= PreviewInterval.TotalMilliseconds)
        {
            _lastPreviewTick = now;
            SettingsChanged?.Invoke(_store.Settings);
            return;
        }
        _previewTimer ??= CreatePreviewTimer();
        _previewTimer.Start();
    }

    private IUiTimer CreatePreviewTimer()
    {
        var timer = AvaloniaUiThread.ForApplication().CreateTimer(PreviewInterval, UiPriority.Background);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _lastPreviewTick = Environment.TickCount64;
            SettingsChanged?.Invoke(_store.Settings);
        };
        return timer;
    }

    /// <summary>Writes a pending save now, a deferred one included.</summary>
    public void Dispose()
    {
        FlushDeferred();
        _previewTimer?.Stop();
        _store.Dispose();
    }
}
