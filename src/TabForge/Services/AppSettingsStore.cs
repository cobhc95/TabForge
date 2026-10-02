using System.Diagnostics;
using System.IO;

namespace TabForge.Services;

/// <summary>
/// R-09: the one <see cref="AppSettings"/> of the running app. <see cref="App"/> owns the shared store (created at startup, flushed on
/// exit); every window reads and writes <see cref="Settings"/> through it and none keeps or saves its own copy, so a change made in one
/// window (a plug-in approval, a hotkey, the scan cache) can no longer be overwritten by another window's stale copy.
/// Saves are debounced (<see cref="SaveDelay"/>) and atomic (<see cref="SettingsFileService.SaveAtomic"/>). UI-framework free: the
/// debounced save runs on the <see cref="SynchronizationContext"/> the store was created on (the UI thread), or inline without one.
/// </summary>
public sealed class AppSettingsStore : IDisposable
{
    public static string DefaultPath => UserPaths.SettingsFile;

    private static AppSettingsStore? _shared;

    /// <summary>The app-wide store. <see cref="App"/> creates it at startup; code that runs without App gets it on first use.</summary>
    public static AppSettingsStore Shared => _shared ??= CreateDefault();

    /// <summary>App start: loads the settings file once for the whole app.</summary>
    internal static AppSettingsStore InitializeShared() => _shared ??= CreateDefault();

    /// <summary>Test seam: real main windows read the shared store; a test points it at its own scratch store so no user file is read or written. Dispose puts the previous one back.</summary>
    internal static IDisposable OverrideSharedForTest(AppSettingsStore store)
    {
        var previous = _shared;
        _shared = store;
        return new SharedRestore(previous);
    }

    private sealed class SharedRestore(AppSettingsStore? previous) : IDisposable
    {
        public void Dispose() => _shared = previous;
    }

    /// <summary>App exit: writes a pending debounced save now. Does nothing when the store was never created (headless runs).</summary>
    internal static void FlushShared() => _shared?.Flush();

    private static AppSettingsStore CreateDefault()
    {
        var store = Open(DefaultPath, TimeSpan.FromMilliseconds(400));
        // Plug-in state files are collected only after the settings file that references them was written.
        store.AfterSave = settings => TabForge.Plugins.ChainStateStore.CollectGarbage(settings.Plugins);
        return store;
    }

    public static AppSettingsStore Open(string path, TimeSpan saveDelay)
    {
        var store = new AppSettingsStore(path, saveDelay);
        store.Load();
        return store;
    }

    private readonly SynchronizationContext? _context;
    private readonly object _timerGate = new();
    private Timer? _timer;
    private bool _dirty;
    // Weak: the store lives as long as the app, and the source of a change is usually a window that may close before the debounced save.
    private WeakReference<object>? _lastSource;

    private AppSettingsStore(string path, TimeSpan saveDelay)
    {
        FilePath = path;
        SaveDelay = saveDelay;
        _context = SynchronizationContext.Current;
    }

    public string FilePath { get; }
    public TimeSpan SaveDelay { get; }

    /// <summary>The single live settings object. Replaced only by <see cref="Replace"/> (Preferences apply / preview).</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// The settings file exists but could not be read: defaults are active and nothing is saved (the unreadable file is kept) until
    /// the user applies Preferences (<see cref="AcceptCurrentAsReplacement"/>).
    /// </summary>
    public bool LoadFailed { get; private set; }

    /// <summary>A change was saved nowhere yet (a debounced save is pending).</summary>
    public bool HasPendingSave => _dirty;

    /// <summary>Settings changed (the source is the window or object that changed them; null: unknown).</summary>
    public event Action<object?>? Changed;
    /// <summary>A save failed (the source of the last change, the error). Raised on the store's context.</summary>
    public event Action<object?, Exception>? SaveFailed;
    /// <summary>Runs after every successful write.</summary>
    public Action<AppSettings>? AfterSave { get; set; }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath)) Settings = SettingsFileService.Load(FilePath);
        }
        catch (Exception ex)
        {
            Settings = new AppSettings();
            LoadFailed = true;
            Debug.WriteLine($"Settings file could not be loaded; defaults are active: {ex}");
        }
    }

    /// <summary>Makes <paramref name="settings"/> the live object (not saved by itself: call <see cref="MarkChanged"/> to save).</summary>
    public void Replace(AppSettings settings, object? source = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (ReferenceEquals(settings, Settings)) return;
        Settings = settings;
        Changed?.Invoke(source);
    }

    /// <summary>The user reviewed and applied settings after a failed load: from now on they replace the unreadable file.</summary>
    public void AcceptCurrentAsReplacement() => LoadFailed = false;

    /// <summary>Something changed <see cref="Settings"/>: tell the other windows and save after <see cref="SaveDelay"/>.</summary>
    public void MarkChanged(object? source = null)
    {
        _dirty = true;
        _lastSource = source is null ? null : new WeakReference<object>(source);
        Changed?.Invoke(source);
        if (SaveDelay <= TimeSpan.Zero) { Flush(); return; }
        lock (_timerGate)
        {
            _timer ??= new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer()
    {
        if (_context is { } context) context.Post(_ => Flush(), null);
        else Flush();
    }

    /// <summary>Writes a pending change now (window close, app exit, tests). True when nothing is pending afterwards.</summary>
    public bool Flush()
    {
        lock (_timerGate) _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        if (!_dirty) return true;
        if (LoadFailed) return false;   // never overwrite a settings file that could not be read (the pending change stays pending)
        try
        {
            SettingsFileService.SaveAtomic(FilePath, Settings);
            _dirty = false;
        }
        catch (Exception ex)   // as before R-09: a failed settings save is reported, never fatal
        {
            Debug.WriteLine($"Settings could not be saved: {ex}");
            SaveFailed?.Invoke(_lastSource is { } weak && weak.TryGetTarget(out var source) ? source : null, ex);
            return false;
        }
        try { AfterSave?.Invoke(Settings); }
        catch (Exception ex) { Debug.WriteLine($"After-save step failed: {ex}"); }
        return true;
    }

    public void Dispose()
    {
        Flush();
        lock (_timerGate) { _timer?.Dispose(); _timer = null; }
    }
}
