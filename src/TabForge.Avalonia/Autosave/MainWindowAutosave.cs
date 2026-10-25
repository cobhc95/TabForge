using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Autosave;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: one window's autosave: the clock (a cheap tick; the interval setting is read live), the pass over the window's songs through AutosaveRunner,
//     the notice bar that says while autosave is failing (with a retry), and the start-up offer to reopen the songs a crashed session left behind
//     (the first window that asks gets the offer; the copies stay until the reopened song is saved or its session has its own copy).
// Does not own: the copy rules and the registry (TabForge.Autosave), the tabs (MainWindowTabs, reached through the host), the settings.
// Tests: TabForge.Avalonia.Tests.MainWindowAutosaveTests.
/// <summary>The window's autosave controller. UI thread only.</summary>
public sealed class MainWindowAutosave : IDisposable
{
    /// <summary>How often the clock looks at the autosave interval.</summary>
    public static readonly TimeSpan ClockInterval = TimeSpan.FromMilliseconds(UiTiming.AutosaveClockIntervalMs);
    /// <summary>A missing song is looked for again this long after it went missing: the registry's grace period plus a short margin.</summary>
    public static readonly TimeSpan RecheckDelay = AutosaveRegistry.MissingGrace + TimeSpan.FromMilliseconds(UiTiming.AutosaveRecheckDelayMs);
    /// <summary>The notice bar's name for assistive technology.</summary>
    public const string NoticeName = "Autosave warning";
    /// <summary>The notice's retry button text.</summary>
    public const string RetryButtonText = "Retry now";

    /// <summary>What the controller needs from its window and the process.</summary>
    public interface IHost
    {
        /// <summary>The songs open in this window.</summary>
        IReadOnlyList<SongDocument> Songs { get; }
        /// <summary>The songs open in every window of the process (a song moving between windows is in none of them for a moment).</summary>
        IReadOnlyList<SongDocument> SongsInAllWindows { get; }
        /// <summary>The autosave interval in minutes (0: off).</summary>
        int AutosaveMinutes { get; }
        /// <summary>True while the song plays or records (the interval is stretched, never stopped).</summary>
        bool IsPlaying(SongDocument song);
        /// <summary>Asks whether the songs a crashed session left behind should be opened.</summary>
        Task<bool> ConfirmRecoveryAsync(int songCount);
        /// <summary>Shows a recovered song as a new unsaved tab.</summary>
        void OpenRecoveredSong(SongDocument song);
        /// <summary>Shows a line to the person in the window's status notice.</summary>
        void Report(string text);
    }

    private readonly IHost _host;
    private readonly AutosaveRegistry _registry;
    private readonly string _folder;
    private readonly IUiThread _ui;
    private readonly AutosaveRunner _runner;
    private readonly AutosavePlanner _planner = new(DateTime.UtcNow);
    private readonly ScoreOpenFlow _scores = new(new DocumentController());
    private readonly IUiTimer _clock;
    private IUiTimer? _recheck;
    private NotificationBar? _topBar;
    private bool _disposed;

    /// <param name="host">The window's songs, settings and notices.</param>
    /// <param name="registry">The process's autosave registry, shared by every window.</param>
    /// <param name="folder">The Recovery folder (the user's folder in the app; a test folder in tests).</param>
    /// <param name="ui">The UI thread the clock runs on.</param>
    public MainWindowAutosave(IHost host, AutosaveRegistry registry, string folder, IUiThread ui)
    {
        _host = host;
        _registry = registry;
        _folder = folder;
        _ui = ui;
        _runner = new AutosaveRunner(registry, RecoveryCopyWriter.Write);
        Notice = new StatusNoticeBarView(NoticeName, RetryButtonText, Retry);
        _clock = ui.CreateTimer(ClockInterval, UiPriority.Background);
        _clock.Tick += (_, _) => AutosaveTick();
        _clock.Start();
    }

    /// <summary>The autosave health (the last attempt, the last success and the failure).</summary>
    public AutosaveHealth Health => _runner.Health;

    /// <summary>The notice bar above the status bar: shown while autosave is failing (kept for tests; the app routes to the top bar).</summary>
    public StatusNoticeBarView Notice { get; }

    /// <summary>Routes the failing-autosave notice to the unified top bar (sticky Warn with Retry); null keeps the bottom row (tests).</summary>
    public void AttachNotificationBar(NotificationBar? bar)
    {
        _topBar = bar;
        UpdateNotice();
    }

    /// <summary>The notice's retry button: re-probes the Recovery folder first (reporting the probe's detail), else copies now.</summary>
    public void Retry()
    {
        if (!Services.FilePathPolicy.TryProbeWritableFolder(_folder, out var failure) && failure is not null)
        {
            _runner.Health.Failed(DateTime.UtcNow, failure, _folder, failure.Message);
            UpdateNotice();
            return;
        }
        _planner.RunSoon();
        AutosaveTick();
    }

    /// <summary>Copies the window's dirty songs now (the clock does this when the interval is due); null when a pass is already running.</summary>
    public async Task<AutosavePassResult?> CopyDirtySongsAsync()
    {
        var result = await _runner.RunPassAsync(_host.Songs.ToArray(), _folder);
        UpdateNotice();
        return result;
    }

    /// <summary>The window closed: its songs are closed for good (a tab moved away was already handed over), so their copies go and the clock stops.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Stop();
        _recheck?.Stop();
        _recheck = null;
        _registry.Retire(_host.Songs.ToArray());
        _registry.RetireMissing(_host.SongsInAllWindows.ToArray());
    }

    private void AutosaveTick()
    {
        if (_disposed) return;
        try
        {
            ReconcileRegistry();
            UpdateNotice();
            if (_runner.Busy) return;
            var playing = _host.Songs.Any(_host.IsPlaying);
            if (!_planner.Due(DateTime.UtcNow, _host.AutosaveMinutes, playing, _runner.Health.Failing)) return;
            _planner.Ran(DateTime.UtcNow);
            _ = CopyDirtySongsAsync();
        }
        catch (Exception ex) { Services.Trace.Error(Services.Trace.Ui, "autosave: tick: " + ex.Message); }
    }

    /// <summary>Retires the copies of songs that left every window; a song that went missing just now is looked for again after its grace period.</summary>
    private void ReconcileRegistry()
    {
        if (_registry.Reconcile(_host.SongsInAllWindows.ToArray(), DateTime.UtcNow) == 0 || _recheck is not null) return;
        // One pending look at a time; the window stops it when it closes (a running timer keeps its window alive until it fires).
        var recheck = _ui.CreateTimer(RecheckDelay, UiPriority.Background);
        _recheck = recheck;
        recheck.Tick += (_, _) => { recheck.Stop(); _recheck = null; ReconcileRegistry(); };
        recheck.Start();
    }

    private void UpdateNotice()
    {
        var text = _runner.Health.Describe(DateTime.UtcNow);
        if (_topBar is not null)
        {
            // UI thread only (the clock ticks there; CopyDirtySongsAsync resumes there via the runner): marshal the engine/autosave callbacks.
            if (!_ui.CheckAccess()) { _ui.Post(UpdateNotice); return; }
            if (text is null) _topBar.HideSticky(NotificationBar.AutosaveSource);
            else _topBar.Show(text, NotificationKind.Warn, (RetryButtonText, Retry), source: NotificationBar.AutosaveSource);
            // The bottom row stays hidden in the app (one top bar); tests without a top bar use Notice below.
            Notice.Hide();
            return;
        }
        if (text is null) Notice.Hide();
        else Notice.Show(text);
    }

    /// <summary>
    /// Start-up: offers the songs a crashed or killed session left in the Recovery folder. Yes opens each as an unsaved tab (Save asks for a name;
    /// the user's own files are untouched); No discards the copies. Only the first call per process makes the offer.
    /// </summary>
    public async Task OfferRecoveryAsync()
    {
        if (!_registry.TryClaimRecoveryOffer()) return;
        var files = AutosaveService.FindOrphans(_folder);
        if (files.Count == 0) return;
        var yes = await _host.ConfirmRecoveryAsync(files.Count);
        foreach (var file in files)
        {
            if (yes) await OpenRecoveredCopyAsync(file);
            else AutosaveService.Delete(file);
        }
    }

    private async Task OpenRecoveredCopyAsync(string file)
    {
        try
        {
            var opened = await _scores.OpenAsync(file);
            var song = new SongDocument(() => null) { Project = opened.Project, Path = null, IsNew = true };
            song.MarkIncomplete();   // unsaved: closing asks, Save asks for a name
            // The copy is NOT deleted yet: it is the only durable copy of this work until the song is saved or this session has autosaved it under its
            // own name (the autosave then removes it); a crash in between offers it again.
            _registry.Adopt(song, file);
            _host.OpenRecoveredSong(song);
            _planner.RunSoon();
        }
        catch (Exception ex)
        {
            // Reported to the person and the trace: the copy stays on disk, so the next start offers it again.
            _host.Report($"Could not reopen {Path.GetFileName(file)}: {ex.Message}");
            Services.Trace.Error(Services.Trace.Ui, "autosave: recovered song: " + ex.Message);
        }
    }
}
