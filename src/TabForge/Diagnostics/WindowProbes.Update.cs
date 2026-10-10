using System.Windows;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Owns: the `--probe-update` check: one real update check against GitHub, as an old and as the current version, writing the report without opening anything.
// Does not own: the update check logic itself.
// Tests: TestUpdateCheck.

// Window probes, update probe.
internal sealed partial class WindowProbes : MainWindow.ProbeAccess
{
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
                catch (Exception ex) { report.AppendLine($"as {asVersion}: {ex.GetType().Name}: {ex.GetBaseException().Message}"); } // Not logged: diagnostic probe: the failure goes to its report, not errors.log
            }
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }
}
