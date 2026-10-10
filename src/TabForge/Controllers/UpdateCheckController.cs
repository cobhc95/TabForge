using System.Diagnostics;
using System.Windows.Threading;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>What the user chose in the "update available" dialog.</summary>
internal sealed record UpdateChoice(bool OpenPage, bool CheckAutomatically);

/// <summary>Looks for a release newer than the running version.</summary>
internal interface IUpdateSource
{
    Task<ReleaseInfo?> CheckAsync(string currentVersion, CancellationToken cancel);
}

/// <summary>The releases of this repository on GitHub.</summary>
internal sealed class GitHubUpdateSource : IUpdateSource
{
    public Task<ReleaseInfo?> CheckAsync(string currentVersion, CancellationToken cancel) => UpdateService.CheckAsync(currentVersion, cancel);
}

/// <summary>What the update check needs from its window.</summary>
internal interface IUpdateCheckHost
{
    GeneralSettings General { get; }
    bool IsLoaded { get; }
    void SaveSettings();
    void SetStatus(string text);
    /// <summary>Shows the dialog for <paramref name="release"/> (null: up to date) and returns the choice.</summary>
    UpdateChoice ShowUpdateAvailable(ReleaseInfo? release, bool checkAutomatically);
}

// Owns: one window's update check: the automatic check after start-up (at most daily) and the manual check.
// Does not own: the network request (UpdateService) and the version comparison.
// Tests: TestClosedDocumentChainsReleased, TestUpdateCheck.
/// <summary>
/// One window's update check (Settings > General > Updates; Help > Check for updates): the automatic check a few seconds after start-up, at most
/// once a day, and the manual check. <see cref="Dispose"/> stops a pending timer; a check that finishes after the window closed shows nothing.
/// </summary>
internal sealed class UpdateCheckController : IDisposable
{
    private readonly IUpdateCheckHost _host;
    private readonly IReadOnlyList<string> _commandLine;
    private readonly IUpdateSource _source;
    private bool _updateCheckRunning;
    private DispatcherTimer? _updateCheckTimer;   // stopped when the window closes (a pending timer keeps its window alive)
    private bool _disposed;

    /// <param name="host">The window the check belongs to.</param>
    /// <param name="commandLine">The process's arguments; a run with any option (test, probe, screenshot) never checks automatically.</param>
    /// <param name="source">Where releases come from (the network call).</param>
    public UpdateCheckController(IUpdateCheckHost host, IReadOnlyList<string>? commandLine = null, IUpdateSource? source = null)
    {
        _host = host;
        _commandLine = commandLine ?? Environment.GetCommandLineArgs();
        _source = source ?? new GitHubUpdateSource();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateCheckTimer?.Stop();
        _updateCheckTimer = null;
    }

    /// <summary>After start-up: at most once a day, only when enabled, never in test/probe/screenshot runs.</summary>
    public void ScheduleAutomaticUpdateCheck()
    {
        if (_disposed || !_host.General.CheckForUpdates) return;
        if (_commandLine.Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal))) return;
        if (_host.General.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _updateCheckTimer = timer;
        timer.Tick += async (_, _) => { timer.Stop(); _updateCheckTimer = null; await CheckForUpdatesAsync(manual: false); };
        timer.Start();
    }

    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckRunning) return;
        if (!manual && !_host.General.CheckForUpdates) return;
        _updateCheckRunning = true;
        if (manual) _host.SetStatus("Checking for updates…");
        ReleaseInfo? release = null;
        var reached = true;
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            release = await _source.CheckAsync(AppInfo.Version, cancel.Token);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or OperationCanceledException // Not logged: offline or blocked: silent for the automatic check, by design
                                   or System.IO.IOException or System.Security.Authentication.AuthenticationException)
        {
            reached = false; // offline or blocked: silent for the automatic check
        }
        finally { _updateCheckRunning = false; }

        _host.General.LastUpdateCheckUtc = DateTime.UtcNow;
        _host.SaveSettings();
        if (_disposed || !_host.IsLoaded) return;
        if (!reached)
        {
            if (manual) _host.SetStatus("Could not reach GitHub to check for updates");
            return;
        }
        if (release is null && !manual) return; // automatic check: only speak up when there is something new
        if (manual) _host.SetStatus(release is null ? "TabForge is up to date" : $"TabForge {release.Version} is available");

        var result = _host.ShowUpdateAvailable(release, _host.General.CheckForUpdates);
        if (result.CheckAutomatically != _host.General.CheckForUpdates)
        {
            _host.General.CheckForUpdates = result.CheckAutomatically;
            _host.SaveSettings();
            _host.SetStatus(result.CheckAutomatically ? "Automatic update checks on" : "Automatic update checks off (Settings > General > Updates)");
        }
        if (result.OpenPage && release is not null) OpenReleasePage(release.Page);
    }

    /// <summary>Opens a release page of this repository in the default browser (the address is built locally).</summary>
    private void OpenReleasePage(Uri page)
    {
        if (page.Scheme != Uri.UriSchemeHttps || !page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !page.AbsolutePath.StartsWith($"/{UpdateService.Repository}/releases", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Services.Trace.Error(Services.Trace.Ui, "open browser: " + ex.Message);
            _host.SetStatus($"Could not open the browser: {page.AbsoluteUri}");
        }
    }
}
