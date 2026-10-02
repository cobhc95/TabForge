using System.Diagnostics;
using System.Windows;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: update check (Settings > General > Updates; Help > Check for updates).
public partial class MainWindow
{
    private bool _updateCheckRunning;

    /// <summary>After start-up: at most once a day, only when enabled, never in test/probe/screenshot runs.</summary>
    private void ScheduleAutomaticUpdateCheck()
    {
        if (!_settings.General.CheckForUpdates) return;
        if (Environment.GetCommandLineArgs().Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal))) return;
        if (_settings.General.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _updateCheckTimer = timer;
        timer.Tick += async (_, _) => { timer.Stop(); _updateCheckTimer = null; await CheckForUpdatesAsync(manual: false); };
        timer.Start();
    }

    private System.Windows.Threading.DispatcherTimer? _updateCheckTimer;   // stopped when the window closes (a pending timer keeps its window alive)

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckRunning) return;
        if (!manual && !_settings.General.CheckForUpdates) return;
        _updateCheckRunning = true;
        if (manual) StatusText.Text = "Checking for updates…";
        ReleaseInfo? release = null;
        var reached = true;
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            release = await UpdateService.CheckAsync(AppInfo.Version, cancel.Token);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or OperationCanceledException
                                   or System.IO.IOException or System.Security.Authentication.AuthenticationException)
        {
            reached = false; // offline or blocked: silent for the automatic check
        }
        finally { _updateCheckRunning = false; }

        _settings.General.LastUpdateCheckUtc = DateTime.UtcNow;
        SaveSettings();
        if (!IsLoaded) return;
        if (!reached)
        {
            if (manual) StatusText.Text = "Could not reach GitHub to check for updates";
            return;
        }
        if (release is null && !manual) return; // automatic check: only speak up when there is something new
        if (manual) StatusText.Text = release is null ? "TabForge is up to date" : $"TabForge {release.Version} is available";

        var result = UpdateAvailableWindow.Show(this, release, AppInfo.Version, _settings.General.CheckForUpdates);
        if (result.CheckAutomatically != _settings.General.CheckForUpdates)
        {
            _settings.General.CheckForUpdates = result.CheckAutomatically;
            SaveSettings();
            StatusText.Text = result.CheckAutomatically ? "Automatic update checks on" : "Automatic update checks off (Settings > General > Updates)";
        }
        if (result.OpenPage && release is not null) OpenReleasePage(release.Page);
    }

    /// <summary>`--probe-update <report>`: one real check against GitHub, as an old and as this version; nothing is opened.</summary>
    public void RunUpdateProbe(string reportPath)
    {
        var path = FilePathPolicy.OutputFile(reportPath, "update probe report");
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            var report = new System.Text.StringBuilder();
            foreach (var asVersion in new[] { "0.1.0-alpha.1", AppInfo.Version })
            {
                try
                {
                    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var found = await UpdateService.CheckAsync(asVersion, cancel.Token);
                    report.AppendLine($"as {asVersion}: {(found is null ? "no newer release" : $"newer {found.Version} at {found.Page}")}");
                }
                catch (Exception ex) { report.AppendLine($"as {asVersion}: {ex.GetType().Name}: {ex.GetBaseException().Message}"); }
            }
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }

    /// <summary>Opens a release page of this repository in the default browser (the address is built locally).</summary>
    private void OpenReleasePage(Uri page)
    {
        if (page.Scheme != Uri.UriSchemeHttps || !page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !page.AbsolutePath.StartsWith($"/{UpdateService.Repository}/releases", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = $"Could not open the browser: {page.AbsoluteUri}";
        }
    }
}
