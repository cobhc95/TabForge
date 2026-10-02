using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Pins for the window's background services and its status-bar notices: the autosave clock and its failure notice, the update check, and the
/// order of the notice bars above the status bar. They run on a real <see cref="MainWindow"/> inside the window-lifetime fixture; the services
/// are reached through <see cref="NoticeOwner"/> so the same assertions hold wherever the services live.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The controller of the named background service (Autosave or Update), reached through the window's service holder.</summary>
    private static object NoticeOwner(MainWindow window, string service) =>
        LtField<object>(window, "_services") is { } services ? services.GetType().GetProperty(service)!.GetValue(services)! : window;

    private static object AutosaveOwner(MainWindow window) => NoticeOwner(window, "Autosave");

    private static object UpdateOwner(MainWindow window) => NoticeOwner(window, "Update");

    private static object ApprovalsOwner(MainWindow window) => NoticeOwner(window, "Approvals");

    /// <summary>The row of the linked-audio notice once it has been shown (null before).</summary>
    private static Border? MediaBar(MainWindow window) => (LtField<IStatusNotice>(ApprovalsOwner(window), "_mediaBar") as StatusNoticeBar)?.Bar;

    /// <summary>The row of the plug-in trust notice once it has been shown (null before).</summary>
    private static Border? TrustBar(MainWindow window) => (LtField<IStatusNotice>(ApprovalsOwner(window), "_trustBar") as StatusNoticeBar)?.Bar;

    // The autosave clock: a cancelled close leaves it running; a real close stops it, retires the window's documents and drops reconcile work queued before it.
    private static void LifetimeAutosaveCase(LifetimeContext context)
    {
        AutosaveRegistry.ResetForTests();
        var b = OpenWindowWithDirtyTab(out _, context.Store);
        var owner = AutosaveOwner(b);
        var doc = b.OpenDocuments[0];
        var state = AutosaveRegistry.For(doc);
        var timer = LtField<DispatcherTimer>(owner, "_autosaveTimer");
        Check("autosave lifetime: the autosave clock runs while the window is open", timer is { IsEnabled: true }, $"timer {(timer is null ? "missing" : timer.IsEnabled.ToString())}");

        var attachments = b.AttachmentCount;
        var dialogs = context.Captured.Count;
        b.Close();   // unsaved changes: the close asks, the test's dialog hook cancels
        SettleLifetimeDispatcher();
        foreach (var dialog in context.Captured.Skip(dialogs).ToArray()) dialog.Close();
        Check("autosave lifetime: a cancelled close keeps the clock running and retires nothing",
            b.IsLoaded && timer is { IsEnabled: true } && !state.Retired && b.AttachmentCount == attachments,
            $"loaded {b.IsLoaded}, timer {timer?.IsEnabled}, retired {state.Retired}, attachments {attachments} -> {b.AttachmentCount}");

        // A reconcile posted before the close, with a document no window shows (so it would start the grace-period recheck if it ran).
        var orphan = new DocumentSession();
        var orphanState = AutosaveRegistry.For(orphan);
        LtCall(owner, "ScheduleAutosaveReconcile");
        doc.MarkClean();
        b.Close();
        SettleLifetimeDispatcher();
        var recheck = LtField<DispatcherTimer>(owner, "_autosaveRecheck");
        Check("autosave lifetime: a real close stops the clock and retires the window's documents",
            timer is { IsEnabled: false } && state.Retired && AutosaveRegistry.Find(doc) is null,
            $"timer {timer?.IsEnabled}, retired {state.Retired}, registry {(AutosaveRegistry.Find(doc) is null ? "empty" : "still holds the song")}");
        Check("autosave lifetime: reconcile work queued before the close does nothing after it (no recheck timer, the other document's state untouched)",
            recheck is null && orphanState.MissingSince is null, $"recheck {(recheck is null ? "none" : "running")}, missing since {orphanState.MissingSince}");
        AutosaveRegistry.Retire(new[] { orphan });
        AutosaveRegistry.ResetForTests();
    }

    // The update check: not scheduled in a run started with a command-line option; a pending timer and nothing else is stopped by the close.
    private static void LifetimeUpdateCheckCase()
    {
        var b = NewLifetimeWindow();
        var owner = UpdateOwner(b);
        LtCall(owner, "ScheduleAutomaticUpdateCheck");
        Check("update check lifetime: no automatic check is scheduled in a run started with a command-line option (such as --selftest)",
            LtField<DispatcherTimer>(owner, "_updateCheckTimer") is null);
        var pending = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        pending.Start();
        LtSet(owner, "_updateCheckTimer", pending);
        b.Close();
        SettleLifetimeDispatcher();
        Check("update check lifetime: closing the window stops a pending check timer and lets go of it",
            !pending.IsEnabled && LtField<DispatcherTimer>(owner, "_updateCheckTimer") is null,
            $"enabled {pending.IsEnabled}, field {(LtField<DispatcherTimer>(owner, "_updateCheckTimer") is null ? "null" : "set")}");
    }

    // The bars above the status bar: each new one sits directly above the status bar, so the newest is nearest to it; hiding collapses, never removes.
    private static void LifetimeNoticeBarOrderCase()
    {
        var b = NewLifetimeWindow();
        try
        {
            var owner = AutosaveOwner(b);
            var health = LtField<AutosaveRunner>(owner, "_autosave")!.Health;
            health.Failed(DateTime.UtcNow, new IOException("The disk is not ready"));
            LtCall(owner, "UpdateAutosaveBar");
            AddUnapprovedClip(b);
            LtCall(b, "UpdateMediaApprovalBar");
            b.OpenDocuments[0].Project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Untrusted", Path = @"C:\tf-untrusted-test\plugin.dll", Type = PluginSlotType.Effect, Format = "VST3" });
            LtCall(b, "UpdatePluginTrustBar");
            SettleLifetimeDispatcher();

            var status = (FrameworkElement)b.MainStatusBar;
            var dock = (DockPanel)status.Parent;
            var media = MediaBar(b);
            var trust = TrustBar(b);
            string Label(UIElement e) => ReferenceEquals(e, media) ? "media" : ReferenceEquals(e, trust) ? "trust" : e is Border { Child: DockPanel } border && System.Windows.Automation.AutomationProperties.GetName(border) == "Autosave warning" ? "autosave" : "other";
            var after = dock.Children.Cast<UIElement>().SkipWhile(e => !ReferenceEquals(e, status)).Skip(1).Select(Label).Where(l => l != "other").ToArray();
            Check("notice bars: shown in the order autosave, media, trust they stack with the newest next to the status bar", string.Join(",", after) == "trust,media,autosave", string.Join(",", after));

            var autosaveBar = dock.Children.OfType<Border>().First(x => Label(x) == "autosave");
            var text = ((DockPanel)autosaveBar.Child).Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
            var retry = ((DockPanel)autosaveBar.Child).Children.OfType<Button>().FirstOrDefault();
            Check("notice bars: the autosave notice says why it failed and offers Retry now",
                autosaveBar.Visibility == Visibility.Visible && DockPanel.GetDock(autosaveBar) == Dock.Bottom && text.StartsWith("Autosave failed: The disk is not ready.", StringComparison.Ordinal) && retry is { Content: "Retry now" },
                $"{autosaveBar.Visibility}, '{text}', retry {retry?.Content}");

            health.Clear();
            LtCall(owner, "UpdateAutosaveBar");
            Check("notice bars: when autosave recovers its notice is collapsed, not removed",
                autosaveBar.Visibility == Visibility.Collapsed && dock.Children.Contains(autosaveBar));
            health.Failed(DateTime.UtcNow, new IOException("The disk is not ready"));
            LtCall(owner, "UpdateAutosaveBar");
            Check("notice bars: a second failure shows the same notice again (one bar, not two)",
                autosaveBar.Visibility == Visibility.Visible && dock.Children.OfType<Border>().Count(x => Label(x) == "autosave") == 1);

            // The chrome as drawn with all three notices visible; the same line from two builds on one machine shows the notices look identical.
            b.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)b.ActualWidth, (int)b.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(b);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Log.Add($"  info  notice chrome: {bitmap.PixelWidth}x{bitmap.PixelHeight} sha256 {Convert.ToHexString(SHA256.HashData(pixels))[..16]}");
        }
        finally { b.OpenDocuments[0].MarkClean(); b.Close(); SettleLifetimeDispatcher(); }
    }

    // ---------- the controllers on their own (no window) ----------

    private sealed class FakeNotice : IStatusNotice
    {
        public readonly List<string> Calls = new();
        public void Show(string text, string? toolTip = null) => Calls.Add("show:" + text);
        public void Hide() => Calls.Add("hide");
    }

    private sealed class FakeNotices : IStatusNotices
    {
        public readonly List<(string? Name, string Button, FakeNotice Notice)> Created = new();
        public IStatusNotice Create(string? automationName, string buttonText, Action onButton)
        {
            var notice = new FakeNotice();
            Created.Add((automationName, buttonText, notice));
            return notice;
        }
    }

    private sealed class FakeAutosaveHost : IAutosaveHost
    {
        public DocumentManager Documents { get; } = new();
        public int AutosaveMinutes => 5;
        public bool IsClosed { get; set; }
        public readonly List<Action> Queue = new();
        public int Posted => Queue.Count;
        public IReadOnlyList<DocumentSession> DocumentsInAllWindows() => Documents.Documents;
        public void Post(Action work, DispatcherPriority priority) => Queue.Add(work);
        public bool ConfirmRecovery(int songs) => false;
        public Task OpenDocumentAsync(string path) => Task.CompletedTask;
        public void UpdateTitle() { }
    }

    private sealed class FakeUpdateSource : IUpdateSource
    {
        private readonly Func<Task<ReleaseInfo?>> _answer;
        public FakeUpdateSource(Func<Task<ReleaseInfo?>> answer) => _answer = answer;
        public Task<ReleaseInfo?> CheckAsync(string currentVersion, CancellationToken cancel) => _answer();
    }

    private sealed class FakeUpdateHost : IUpdateCheckHost
    {
        public GeneralSettings General { get; } = new() { CheckForUpdates = true };
        public bool IsLoaded { get; set; } = true;
        public int Saves, Dialogs;
        public readonly List<string> Status = new();
        public UpdateChoice Choice = new(false, true);
        public void SaveSettings() => Saves++;
        public void SetStatus(string text) => Status.Add(text);
        public UpdateChoice ShowUpdateAvailable(ReleaseInfo? release, bool checkAutomatically) { Dialogs++; return Choice; }
    }

    private static void AutosaveControllerAloneCase()
    {
        AutosaveRegistry.ResetForTests();
        var host = new FakeAutosaveHost();
        host.Documents.AddNew();
        var notices = new FakeNotices();
        var controller = new AutosaveController(host, notices);
        Check("autosave controller: its notice is created named and with a Retry button, and nothing is shown yet",
            notices.Created is [("Autosave warning", "Retry now", { Calls.Count: 0 })]);
        controller.Start();
        host.Documents.AddNew();
        Check("autosave controller: a change of the window's songs queues one reconcile ", host.Posted == 1, $"posted {host.Posted}");
        host.Documents.AddNew();
        Check("autosave controller: while one reconcile is queued another change adds none", host.Posted == 1, $"posted {host.Posted}");
        host.Queue[0]();   // the queued reconcile runs (the window is open)
        host.Documents.AddNew();
        Check("autosave controller: once it has run, the next change queues a new one", host.Posted == 2, $"posted {host.Posted}");
        var doc = host.Documents.Documents[0];
        var state = AutosaveRegistry.For(doc);
        controller.Dispose();
        controller.Dispose();
        Check("autosave controller: Dispose retires the window's songs and can run twice", state.Retired);
        var posted = host.Posted;
        host.Documents.AddNew();
        Check("autosave controller: after Dispose a change of the songs queues nothing", host.Posted == posted, $"posted {posted} -> {host.Posted}");
        AutosaveRegistry.ResetForTests();
    }

    private static void UpdateCheckControllerAloneCase()
    {
        var host = new FakeUpdateHost();
        using (var scheduled = new UpdateCheckController(host, new[] { "TabForge.exe" }, new FakeUpdateSource(() => Task.FromResult<ReleaseInfo?>(null))))
        {
            scheduled.ScheduleAutomaticUpdateCheck();
            Check("update check controller: with no command-line option and no check in the last day the automatic check is scheduled",
                LtField<DispatcherTimer>(scheduled, "_updateCheckTimer") is { IsEnabled: true });
            scheduled.Dispose();
            Check("update check controller: Dispose stops and releases the pending timer", LtField<DispatcherTimer>(scheduled, "_updateCheckTimer") is null);
        }
        host.General.LastUpdateCheckUtc = DateTime.UtcNow.AddHours(-1);
        using (var recent = new UpdateCheckController(host, Array.Empty<string>(), new FakeUpdateSource(() => Task.FromResult<ReleaseInfo?>(null))))
        {
            recent.ScheduleAutomaticUpdateCheck();
            Check("update check controller: a check made within the last day is not repeated", LtField<DispatcherTimer>(recent, "_updateCheckTimer") is null);
        }

        // A check that finishes after the window closed records the time and shows nothing.
        host.General.LastUpdateCheckUtc = null;
        var release = new ReleaseInfo("99.0.0", new Uri("https://github.com/cobhc95/TabForge/releases/tag/v99.0.0"));
        var gate = new TaskCompletionSource<ReleaseInfo?>();
        var late = new UpdateCheckController(host, Array.Empty<string>(), new FakeUpdateSource(() => gate.Task));
        var pending = late.CheckForUpdatesAsync(manual: false);
        late.Dispose();
        gate.SetResult(release);
        SettleLifetimeDispatcher();
        Check("update check controller: a check that finishes after Dispose records the time and shows no dialog",
            pending.IsCompleted && host.Dialogs == 0 && host.General.LastUpdateCheckUtc is not null && host.Saves == 1, $"completed {pending.IsCompleted}, dialogs {host.Dialogs}, saves {host.Saves}");

        // A manual check that finds nothing says so; one that finds a release asks, and the choice about automatic checks is kept.
        var manual = new UpdateCheckController(host, Array.Empty<string>(), new FakeUpdateSource(() => Task.FromResult<ReleaseInfo?>(null)));
        host.Status.Clear(); host.Saves = 0; host.Dialogs = 0; host.Choice = new UpdateChoice(false, false);
        var upToDate = manual.CheckForUpdatesAsync(manual: true);
        SettleLifetimeDispatcher();
        Check("update check controller: a manual check with nothing new says so and shows the dialog",
            upToDate.IsCompleted && host.Status.Contains("TabForge is up to date") && host.Dialogs == 1, string.Join(" | ", host.Status));
        Check("update check controller: turning automatic checks off in the dialog is saved and announced",
            !host.General.CheckForUpdates && host.Status.Contains("Automatic update checks off (Settings > General > Updates)") && host.Saves == 2, $"checks {host.General.CheckForUpdates}, saves {host.Saves}");
        manual.Dispose();
    }

    private sealed class FakeApprovalHost : IApprovalHost
    {
        public DocumentSession ActiveDocument { get; } = new();
        public AppSettings Settings { get; } = new();
        public readonly List<Action> Queue = new();
        public readonly List<string> Calls = new();
        public void SaveSettings() => Calls.Add("save");
        public void SyncAudioEngine() => Calls.Add("sync");
        public void RefreshArrangement() => Calls.Add("arrangement");
        public void RefreshMixerWindow() => Calls.Add("mixer");
        public void Post(Action work) => Queue.Add(work);
        public void ShowLinkedAudioReview(LinkedAudioReview review) => Calls.Add("linked-review");
        public void ShowPluginReview(PluginReview review) => Calls.Add("plugin-review:" + review.Untrusted.Count);
    }

    private static void ApprovalControllerAloneCase()
    {
        var host = new FakeApprovalHost();
        var notices = new FakeNotices();
        var controller = new ApprovalNoticeController(host, notices);
        Check("approval controller: it creates two Review rows (linked audio, plug-ins) and shows neither yet",
            notices.Created is [(null, "Review…", { Calls.Count: 0 }), (null, "Review…", { Calls.Count: 0 })]);
        controller.Start();
        MediaAccess.RaiseChanged();
        Check("approval controller: a change of approvals queues one refresh for the window", host.Queue.Count == 1, $"queued {host.Queue.Count}");
        controller.Dispose();
        controller.Dispose();
        MediaAccess.RaiseChanged();
        Check("approval controller: after Dispose approval changes queue nothing (the handlers are detached, twice is fine)", host.Queue.Count == 1, $"queued {host.Queue.Count}");

        host.ActiveDocument.Project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Untrusted", Path = @"C:\tf-untrusted-test\plugin.dll", Type = PluginSlotType.Effect, Format = "VST3" });
        controller.UpdatePluginTrustBar();
        var trust = notices.Created[1].Notice.Calls;
        Check("approval controller: an unapproved plug-in shows the trust notice naming the file", trust is [var shown] && shown.StartsWith("show:This song uses 1 plug-in", StringComparison.Ordinal) && shown.Contains("plugin.dll"), string.Join(" | ", trust));
        host.ActiveDocument.Project.Tracks[0].Rig.Plugins.Clear();
        controller.UpdatePluginTrustBar();
        Check("approval controller: with no unapproved plug-in the trust notice is hidden", trust[^1] == "hide", string.Join(" | ", trust));
    }

    // The plug-in review is built from the song's unapproved plug-ins and lists each file.
    private static void LifetimePluginReviewCase(LifetimeContext context)
    {
        var b = NewLifetimeWindow();
        try
        {
            b.OpenDocuments[0].Project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Untrusted", Path = @"C:\tf-untrusted-test\plugin.dll", Type = PluginSlotType.Effect, Format = "VST3" });
            var before = context.Captured.Count;
            LtCall(ApprovalsOwner(b), "ReviewUntrustedPlugins");
            var review = context.Captured.Skip(before).FirstOrDefault(x => x.Title == "Review plug-ins");
            var box = review is null ? null : Logical<CheckBox>(review).FirstOrDefault();
            Check("approval review: the plug-in review window lists the unapproved file with its reason",
                box is not null && box.Content is string text && text.Contains("plugin.dll", StringComparison.Ordinal), $"review {review is not null}, box '{box?.Content}'");
            foreach (var dialog in context.Captured.Skip(before).ToArray()) dialog.Close();
        }
        finally { b.OpenDocuments[0].MarkClean(); b.Close(); SettleLifetimeDispatcher(); }
    }
}
