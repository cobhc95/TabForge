// Owns: the "Approve all…" / "Approve selected…" logic of the plug-in browser without UI types: which listed rows are still unapproved, the confirmation
//       texts, and the batch itself (approve one plug-in, identify it in the throwaway probe, next; cancel stops between plug-ins).
// Does not own: the dialog and buttons (Views/Fx/PluginApprovalWindow, PluginBrowserWindow), the trust record (PluginTrust.Approve, the same call as approving
//       one plug-in), or the probe (PluginCatalog.Describe, which also routes 32-bit plug-ins through the bridge).
// Tests: TabForge.Avalonia.Tests.PluginApprovalPresenterTests.
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>What one finished step of the batch tells the list: the plug-in, its role and vendor, and whether the probe could read them.</summary>
public sealed record PluginApprovalStep(int Done, int Total, VstPluginInfo Plugin, string Role, string Vendor, bool Identified, string? Refusal = null);

/// <summary>Thread: set-up and callbacks on the UI thread; each approve and probe runs on the thread pool.</summary>
public static class PluginApprovalPresenter
{
    public const string Title = "Approve plug-ins?";
    /// <summary>The probe ceiling (see P1-03): the shared default; bridged plug-ins are raised to the bridged ceiling inside <see cref="PluginCatalog"/>.</summary>
    public const int ProbeTimeoutMs = PluginCatalog.DefaultProbeTimeoutMs;

    public const string NothingToApproveText = "No plug-ins here need approval.";

    /// <summary>
    /// True for a plug-in this batch may approve: not trusted yet. A file that changed since an earlier approval, or cannot be read, is left out: it needs its own look
    /// in Review plug-ins, not a batch that would silently make the changed file the new baseline.
    /// </summary>
    public static bool Approvable(TrustVerdict verdict) => !verdict.Trusted && verdict.Reason is not (PluginTrust.ReasonChanged or PluginTrust.ReasonUnreadable);

    /// <summary>
    /// The plug-ins a batch covers: "Approve all" passes the rows now listed, "Approve selected" the selected rows. One per file, in list order. <paramref name="check"/> is
    /// the trust check of a path (the role text cannot tell: a VST3 names its role while still unapproved).
    /// </summary>
    public static IReadOnlyList<VstPluginInfo> Unapproved(IEnumerable<PluginBrowserRow> rows, Func<string, TrustVerdict> check) =>
        rows.Select(r => r.Info).DistinctBy(p => p.Path, PathComparer.Default).Where(p => Approvable(check(p.Path))).ToList();

    /// <summary>The confirmation sentence for <paramref name="count"/> plug-ins.</summary>
    public static string Body(int count) =>
        $"You are about to allow {count} {(count == 1 ? "plug-in" : "plug-ins")} to run on this computer. TabForge will run each once in a separate helper to read its type and vendor. Only approve files you trust.";

    /// <summary>The confirm button text.</summary>
    public static string ApproveLabel(int count) => $"Approve {count}";

    /// <summary>One line of the plug-in list in the dialog: the name and the folder.</summary>
    public static string ItemText(VstPluginInfo plugin) => $"{plugin.Name}  ·  {System.IO.Path.GetDirectoryName(plugin.Path) ?? ""}";

    public static string RunningText(int done, int total) => $"Approving and identifying plug-ins… {done} of {total}";
    public static string DoneText(int done, int total, int failed) =>
        $"Approved {done} of {total} plug-ins" + (failed > 0 ? $" · {failed} could not be identified" : "") + (done < total ? " · stopped" : "") + ".";

    /// <summary>
    /// Approves and identifies the plug-ins one after the other. Each is approved with the same trust record as approving one plug-in (a hash of the file as it is now),
    /// then read once in the throwaway probe; a plug-in that crashes, hangs or times out is reported as not identified and the batch goes on. Cancelling stops before the
    /// next plug-in (the one being probed finishes); later ones stay unapproved. <paramref name="step"/> runs on the caller's context after each plug-in.
    /// </summary>
    /// <returns>How many plug-ins were processed.</returns>
    public static async Task<int> RunAsync(IReadOnlyList<VstPluginInfo> plugins, PluginSettings settings, PluginTrust trust, IPluginFileTrust files,
        Action<PluginApprovalStep> step, CancellationToken cancel)
    {
        var done = 0;
        foreach (var plugin in plugins)
        {
            if (cancel.IsCancellationRequested) break;
            (string Role, string Vendor, string Name, string? Refusal) described;
            try
            {
                described = await Task.Run(() =>
                {
                    trust.Approve(settings, plugin.Path, files);
                    return PluginCatalog.DescribeDetailed(plugin.Path, settings, trust, files, ProbeTimeoutMs, plugin.Format);
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // P1-01: one bad plug-in never aborts the batch; its cause is reported per row.
                if (cancel.IsCancellationRequested) break;
                Trace.Error(Trace.Engine, "plug-in approval: " + plugin.Path + ": " + ex.GetType().Name + ": " + ex.Message);
                described = ("", "", "", ex.GetBaseException().Message);
            }
            var identified = PluginCatalog.IsIdentified(plugin.Path, settings);   // not the returned role: a failed probe returns a guess from the name
            done++;
            step(new PluginApprovalStep(done, plugins.Count, plugin, described.Role, described.Vendor, identified, identified ? null : described.Refusal));
        }
        return done;
    }
}
