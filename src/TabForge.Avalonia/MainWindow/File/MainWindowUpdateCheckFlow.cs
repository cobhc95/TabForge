using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: one window's update check run: the automatic check a few seconds after start-up (at most once a day, only when enabled, never in a
//   run started with an option), the manual check, the status texts, the saved opt-out and opening the release page.
// Does not own: the request and the version rules (UpdateService in Core), the answer dialog (Views/UpdateAvailableWindow), the menu entry
//   (MainWindowUpdateCheck).
// Tests: TabForge.Avalonia.Tests.HelpMenuTests.
/// <summary>The update check of one Avalonia window; a check that finishes after the window closed shows nothing.</summary>
internal sealed class MainWindowUpdateCheckFlow
{
    private static readonly TimeSpan AutomaticCheckDelay = TimeSpan.FromMilliseconds(UiTiming.UpdateCheckStartDelayMs);
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromMilliseconds(UiTiming.UpdateCheckIntervalMs);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(UiTiming.UpdateCheckTimeoutMs);
    private const string CheckingText = "Checking for updates…";
    private const string UpToDateText = "TabForge is up to date";
    private const string UnreachableText = "Could not reach GitHub to check for updates";
    private const string AutomaticChecksOnText = "Automatic update checks on";
    private const string AutomaticChecksOffText = "Automatic update checks off (Settings > General > Updates)";
    private const string BrowserFailedText = "Could not open the browser: ";

    private readonly Window _owner;
    private readonly MainWindowSettings? _settings;
    private readonly Action<string> _status;
    private bool _running;
    private bool _closed;
    private AvaloniaUiTimer? _timer;

    public MainWindowUpdateCheckFlow(Window owner, MainWindowSettings? settings, Action<string> status)
    {
        _owner = owner;
        _settings = settings;
        _status = status;
        owner.Closed += (_, _) =>
        {
            _closed = true;
            _timer?.Stop(); // A pending timer must not fire into a closed window.
            _timer = null;
        };
    }

    /// <summary>After start-up: at most once a day, only when enabled, never in a run started with an option.</summary>
    public void ScheduleAutomaticCheck()
    {
        if (_settings is null || _closed || !_settings.Settings.General.CheckForUpdates) return;
        if (Environment.GetCommandLineArgs().Skip(1).Any(argument => argument.StartsWith("--", StringComparison.Ordinal))) return;
        if (_settings.Settings.General.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < AutomaticCheckInterval) return;
        var timer = new AvaloniaUiTimer(AutomaticCheckDelay);
        _timer = timer;
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            _timer = null;
            await RunAsync(manual: false);
        };
        timer.Start();
    }

    /// <summary>One check: the request, the status texts, the update dialog and the saved opt-out. A second run while one is open does nothing.</summary>
    public async Task RunAsync(bool manual)
    {
        if (_running) return;
        var enabled = _settings?.Settings.General.CheckForUpdates ?? false;
        if (!manual && !enabled) return;
        _running = true;
        if (manual) _status(CheckingText);
        ReleaseInfo? release = null;
        var reached = true;
        try
        {
            using var cancel = new CancellationTokenSource(RequestTimeout);
            release = await UpdateService.CheckAsync(AppInfo.Version, cancel.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                   or IOException or AuthenticationException)
        {
            reached = false; // Not logged: offline or blocked is reported as "could not reach" (silent for the automatic check)
        }
        finally { _running = false; }

        if (_settings is not null)
        {
            _settings.Settings.General.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.SaveSettings();
        }
        if (_closed) return;
        if (!reached)
        {
            if (manual) _status.Error(UnreachableText);
            return;
        }
        if (release is null && !manual) return; // Automatic check: only speak up when there is something new
        if (manual) _status(release is null ? UpToDateText : $"TabForge {release.Version} is available");

        var answer = await DialogLauncher.Ask<UpdateAvailableWindow, UpdateAvailableWindow.Answer?>(_owner,
            () => new UpdateAvailableWindow(new UpdateAvailablePresenter(release, AppInfo.Version, enabled), previewOnly: false));
        var checkAutomatically = answer?.CheckAutomatically ?? enabled;
        if (_settings is not null && checkAutomatically != enabled)
        {
            _settings.Settings.General.CheckForUpdates = checkAutomatically;
            _settings.SaveSettings();
            _status(checkAutomatically ? AutomaticChecksOnText : AutomaticChecksOffText);
        }
        if (answer?.OpenPage == true && release is not null) OpenReleasePage(release.Page);
    }

    /// <summary>Opens the release page in the default browser; the address is built by UpdateService from a validated version.</summary>
    private void OpenReleasePage(Uri page)
    {
        if (!UpdateService.IsReleasePage(page)) { Services.Trace.Error(Services.Trace.Ui, "open browser: refused a release page outside GitHub"); return; }
        if (!TabForge.Platform.ShellLauncher.Current.OpenUrl(page.AbsoluteUri))
        {
            Services.Trace.Error(Services.Trace.Ui, "open browser: no handler");
            _status.Error(BrowserFailedText + page.AbsoluteUri);
        }
    }
}
