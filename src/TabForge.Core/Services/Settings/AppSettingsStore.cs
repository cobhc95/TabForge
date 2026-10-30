using System.Diagnostics;
using System.IO;

namespace TabForge.Services;

// Owns: the one live settings object of the running app: debounced atomic save and the change notification every window uses.
// Does not own: the file format rules (SettingsFileService, SettingsMigration) and the settings UI.
// Tests: TestSettingsChangeDuringSaveStaysPending.
/// <summary>
/// The one <see cref="AppSettings"/> of the running app. The app owns the shared store (created at startup, flushed on
/// exit); every window reads and writes <see cref="Settings"/> through it and none keeps or saves its own copy, so a change made in one
/// window (a plug-in approval, a hotkey, the scan cache) is never overwritten by another window's stale copy.
/// Saves are debounced (<see cref="SaveDelay"/>) and atomic (<see cref="SettingsFileService.SaveAtomic"/>). UI-framework free: the
/// debounced save runs on the <see cref="SynchronizationContext"/> the store was created on (the UI thread), or inline without one.
/// </summary>
public sealed class AppSettingsStore : IDisposable
{
    public static string DefaultPath => UserPaths.SettingsFile;

    // Key: none (one store per process). Ceiling: one instance. Thread: created at start-up on the UI thread; tests swap it on the
    // test thread. Reads and the first creation are guarded by _sharedGate so two threads never create two stores.
    // The mutable part lives in an instance: the static facade only holds a readonly slot.
    private sealed class SharedSlot { public AppSettingsStore? Current; }
    private static readonly SharedSlot SharedState = new();
    private static readonly object _sharedGate = new();

    /// <summary>The app-wide store. The app creates it at startup; code that runs without it gets it on first use.</summary>
    public static AppSettingsStore Shared => InitializeShared();

    /// <summary>App start: loads the settings file once for the whole app.</summary>
    internal static AppSettingsStore InitializeShared()
    {
        lock (_sharedGate) return SharedState.Current ??= CreateDefault();
    }

    /// <summary>Test seam: real main windows read the shared store; a test points it at its own scratch store so no user file is read or written. Dispose puts the previous one back.</summary>
    internal static IDisposable OverrideSharedForTest(AppSettingsStore store)
    {
        lock (_sharedGate)
        {
            var previous = SharedState.Current;
            SharedState.Current = store;
            return new SharedRestore(previous);
        }
    }

    private sealed class SharedRestore(AppSettingsStore? previous) : IDisposable
    {
        public void Dispose() { lock (_sharedGate) SharedState.Current = previous; }
    }

    /// <summary>App exit: writes a pending debounced save now. Does nothing when the store was never created (headless runs).</summary>
    internal static void FlushShared() => SharedState.Current?.Flush();

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
    private volatile bool _disposed;   // set under _timerGate by Dispose: later changes are dropped, never re-arm the timer
    // A change bumps _changeVersion; a save records the version it wrote. A change made while a save is writing keeps the versions
    // apart, so it stays pending. _saveGate keeps two saves (timer thread and UI thread) from writing at once.
    private readonly object _saveGate = new();
    private long _changeVersion;
    private long _savedVersion;
    private long _pendingSince;   // Stopwatch timestamp of the first change not yet saved (0: none), under _timerGate
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
    public bool HasPendingSave => Interlocked.Read(ref _changeVersion) != Interlocked.Read(ref _savedVersion);

    /// <summary>Settings changed (the source is the window or object that changed them; null: unknown).</summary>
    public event Action<object?>? Changed;
    /// <summary>A save failed (the source of the last change, the error). Raised on the store's context.</summary>
    public event Action<object?, Exception>? SaveFailed;
    /// <summary>Runs after every successful write.</summary>
    public Action<AppSettings>? AfterSave { get; set; }
    /// <summary>The longest a change waits for its save while more changes keep arriving (a resize or a drag restarts the debounce on every step): then it is written anyway.</summary>
    internal TimeSpan MaxPendingDelay { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Tests only: runs inside a save just before the file is written (simulates a slow write).</summary>
    internal Action? BeforeWriteForTest { get; set; }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath)) Settings = SettingsFileService.Load(FilePath);
        }
        catch (Exception ex)
        {
            Services.Trace.Error(Services.Trace.Ui, "settings: load: " + ex.Message);
            Settings = new AppSettings();
            LoadFailed = true;
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

    /// <summary>
    /// Something changed <see cref="Settings"/>: save after <see cref="SaveDelay"/>, and tell the other windows unless <paramref name="notifyObservers"/> is false
    /// (a change only the source tracks, such as a window's position while it is dragged, so the others do not re-apply the settings on every move).
    /// </summary>
    public void MarkChanged(object? source = null, bool notifyObservers = true)
    {
        if (_disposed)
        {
            Services.Trace.Error(Services.Trace.Ui, "settings: change after the store was closed is dropped");
            return;
        }
        Interlocked.Increment(ref _changeVersion);
        _lastSource = source is null ? null : new WeakReference<object>(source);
        if (notifyObservers && ChangedSinceNotified()) Changed?.Invoke(source);
        if (SaveDelay <= TimeSpan.Zero) { Flush(); return; }
        lock (_timerGate)
        {
            if (_disposed) return;
            _timer ??= new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
            var now = Stopwatch.GetTimestamp();
            if (_pendingSince == 0) _pendingSince = now;
            var waited = TimeSpan.FromSeconds((now - _pendingSince) / (double)Stopwatch.Frequency);
            _timer.Change(Min(SaveDelay, Max(TimeSpan.Zero, MaxPendingDelay - waited)), Timeout.InfiniteTimeSpan);
        }
    }

    // The settings as the observers were last told them: one copy of the file's bytes, written by the thread that marks changes.
    // A change that leaves these bytes as they were notifies nobody, as the observers already hold the same settings.
    private byte[]? _notifiedBytes;

    private bool ChangedSinceNotified()
    {
        var bytes = SettingsFileService.SerializeToBytes(Settings);
        if (_notifiedBytes is not null && bytes.AsSpan().SequenceEqual(_notifiedBytes)) return false;
        _notifiedBytes = bytes;
        return true;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private void OnTimer()
    {
        if (_context is { } context) context.Post(_ => Flush(), null);
        else Flush();
    }

    /// <summary>Writes a pending change now (window close, app exit, tests). True when nothing is pending afterwards.</summary>
    public bool Flush()
    {
        lock (_timerGate) { _timer?.Change(Timeout.Infinite, Timeout.Infinite); _pendingSince = 0; }
        lock (_saveGate)
        {
            var version = Interlocked.Read(ref _changeVersion);
            if (version == Interlocked.Read(ref _savedVersion)) return true;
            if (LoadFailed) return false;   // never overwrite a settings file that could not be read (the pending change stays pending)
            try
            {
                BeforeWriteForTest?.Invoke();
                SettingsFileService.SaveAtomic(FilePath, Settings);
                Interlocked.Exchange(ref _savedVersion, version);
            }
            catch (Exception ex)   // a failed settings save is reported, never fatal
            {
                Services.Trace.Error(Services.Trace.Ui, "settings: save: " + ex.Message);
                SaveFailed?.Invoke(_lastSource is { } weak && weak.TryGetTarget(out var source) ? source : null, ex);
                return false;
            }
            try { AfterSave?.Invoke(Settings); }
            catch (Exception ex) { Services.Trace.Error(Services.Trace.Ui, "settings: after-save step: " + ex.Message); }
            return !HasPendingSave;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Flush();
        lock (_timerGate) { _disposed = true; _timer?.Dispose(); _timer = null; }
    }
}
