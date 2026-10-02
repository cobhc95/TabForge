using System.Windows.Threading;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>What the autosave needs from its window.</summary>
internal interface IAutosaveHost
{
    /// <summary>The songs open in this window.</summary>
    DocumentManager Documents { get; }
    int AutosaveMinutes { get; }
    bool IsClosed { get; }
    /// <summary>The songs open in every window (a song moving between windows is in none of them for a moment).</summary>
    IReadOnlyList<DocumentSession> DocumentsInAllWindows();
    /// <summary>Queues work for the window's thread; dropped when the window has closed by then.</summary>
    void Post(Action work, DispatcherPriority priority);
    /// <summary>Asks whether the songs a crashed session left behind should be opened.</summary>
    bool ConfirmRecovery(int songs);
    Task OpenDocumentAsync(string path);
    void UpdateTitle();
}

// Owns: one window's autosave: timer, retry notice and the offer to recover songs a crashed session left behind.
// Does not own: the autosave file rules (AutosaveService, AutosaveRunner) and the song model.
// Tests: TestClosedDocumentChainsReleased, TestAutosaveRecovery.
/// <summary>
/// One window's autosave: dirty songs are copied to the Recovery folder (<see cref="AutosaveService"/>, <see cref="AutosaveRunner"/>),
/// a failing autosave shows a notice with a retry, and songs a crashed session left behind are offered at start-up. The controller owns its two
/// timers; <see cref="Dispose"/> (once, when the window has really closed) stops them and retires the songs still in the window.
/// </summary>
internal sealed class AutosaveController : IDisposable
{
    private readonly IAutosaveHost _host;
    private readonly IStatusNotice _notice;
    private readonly AutosavePlanner _autosavePlanner = new(DateTime.UtcNow);
    private readonly AutosaveRunner _autosave = new();
    private DispatcherTimer? _autosaveTimer;
    private DispatcherTimer? _autosaveRecheck;
    private bool _reconcileScheduled;
    private bool _disposed;

    public AutosaveController(IAutosaveHost host, IStatusNotices notices)
    {
        _host = host;
        _notice = notices.Create("Autosave warning", "Retry now", () => { _autosavePlanner.RunSoon(); AutosaveTick(); });
    }

    /// <summary>Starts the autosave clock (a cheap tick every 20 s; the interval setting is read live).</summary>
    public void Start()
    {
        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
        _autosaveTimer.Tick += (_, _) => AutosaveTick();
        _autosaveTimer.Start();
        // A closed tab's copy goes soon after (never during a tab move: the registry waits a grace period before it deletes).
        _host.Documents.Changed += OnDocumentsChanged;
    }

    /// <summary>The documents still in this window are closed for good; a tab moved away was already handed over.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Documents.Changed -= OnDocumentsChanged;
        _autosaveTimer?.Stop();
        _autosaveRecheck?.Stop();
        _autosaveRecheck = null;
        AutosaveRegistry.Retire(_host.Documents.Documents.ToArray());
    }

    private void OnDocumentsChanged(object? sender, EventArgs e) => ScheduleAutosaveReconcile();

    private void ScheduleAutosaveReconcile()
    {
        if (_reconcileScheduled) return;
        _reconcileScheduled = true;
        _host.Post(() =>
        {
            _reconcileScheduled = false;
            ReconcileAutosaveStates();
        }, DispatcherPriority.Background);
    }

    private void ReconcileAutosaveStates()
    {
        if (_disposed || _host.IsClosed) return;   // a closed window's documents were retired when it closed; its recheck timer must not run on
        try
        {
            if (AutosaveRegistry.Reconcile(_host.DocumentsInAllWindows(), DateTime.UtcNow) > 0 && _autosaveRecheck is null)
            {
                // A document is briefly missing (a tab move in progress): look again once the grace period is over. One pending look at a
                // time, and the window stops it when it closes (a running timer keeps its window alive until it fires).
                var recheck = new DispatcherTimer(DispatcherPriority.Background) { Interval = AutosaveRegistry.MissingGrace + TimeSpan.FromMilliseconds(250) };
                _autosaveRecheck = recheck;
                recheck.Tick += (_, _) => { recheck.Stop(); _autosaveRecheck = null; ReconcileAutosaveStates(); };
                recheck.Start();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autosave reconcile failed: {ex}"); }
    }

    private void AutosaveTick()
    {
        try
        {
            ReconcileAutosaveStates();
            UpdateAutosaveBar();
            if (_autosave.Busy) return;
            var minutes = _host.AutosaveMinutes;
            // Playing (or recording) only stretches the interval; a long session must still keep a recovery copy.
            var playing = _host.Documents.Documents.Any(d => d.Playback.Engine.IsPlaying);
            if (!_autosavePlanner.Due(DateTime.UtcNow, minutes, playing, _autosave.Health.Failing)) return;
            _autosavePlanner.Ran(DateTime.UtcNow);
            _ = RunAutosavePass();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autosave failed: {ex}"); }
    }

    private async Task RunAutosavePass()
    {
        try { await _autosave.RunPassAsync(_host.Documents.Documents.ToArray(), AutosaveService.DefaultFolder); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autosave pass failed: {ex}"); }
        UpdateAutosaveBar();
    }

    /// <summary>The non-modal notice above the status bar while autosave is failing (the reason, the last good copy, the retry).</summary>
    private void UpdateAutosaveBar()
    {
        var text = _autosave.Health.Describe(DateTime.UtcNow);
        if (text is null) _notice.Hide();
        else _notice.Show(text);
    }

    /// <summary>
    /// Start-up: offers songs a crashed or killed session left in the Recovery folder. Yes opens each as an unsaved tab (Save asks
    /// for a name; the user's own files are untouched); No discards the copies.
    /// </summary>
    public void OfferRecovery()
    {
        var files = AutosaveService.FindOrphans(AutosaveService.DefaultFolder);
        if (files.Count == 0) return;
        var yes = _host.ConfirmRecovery(files.Count);
        foreach (var file in files)
        {
            if (yes) OpenRecoveredSong(file);
            else AutosaveService.Delete(file);
        }
    }

    private async void OpenRecoveredSong(string file)
    {
        try
        {
            await _host.OpenDocumentAsync(file);   // queued behind a Guitar Pro import still running (e.g. the start-up song)
            if (_disposed) return;                 // the window closed meanwhile: the copy stays for the next start
            var doc = _host.Documents.FindByPath(file);
            if (doc is null) return;   // the open failed and said so; the copy stays for the next start
            doc.Path = null;
            doc.IsNew = true;
            doc.MarkIncomplete();     // unsaved: closing asks, Save asks for a name
            _host.Documents.Replace(_host.Documents.IndexOf(doc), doc);
            _host.UpdateTitle();
            // The copy is NOT deleted yet: it is the only durable copy of this work until the song is saved or this session has
            // autosaved it under its own name (the autosave then removes it); a crash in between offers it again.
            AutosaveRegistry.Adopt(doc, file);
            _autosavePlanner.RunSoon();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Recovered song could not be opened: {ex}"); }
    }
}
