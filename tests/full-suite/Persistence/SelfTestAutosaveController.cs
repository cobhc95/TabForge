using System.IO;
using System.Reflection;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Services;

namespace TabForge;

// Owns: one window's autosave controller run on its own: the copies its pass writes (dirty songs only), the failure notice it shows and hides,
//     Dispose stopping its timer and retiring its songs, and a closed tab losing its copy once the window has reconciled.
// Does not own: the song rules of a pass (TestAutosaveCopies), the copy writer and the failure wording (TestAutosaveRecovery), and the window's
//     wiring of the controller (TestNoticeLifetime, TestWindowLifetime).
// Tests: TestAutosaveController.
public static partial class SelfTest
{
    private static void TestAutosaveController()
    {
        AutosaveRegistry.ResetForTests();
        var realWriter = AutosaveRunner.WriteCopy;
        var grace = AutosaveRegistry.MissingGrace;
        try
        {
            ControllerCopiesAndNotices();
            ControllerClosedTabLosesItsCopy();
        }
        finally
        {
            AutosaveRunner.WriteCopy = realWriter;
            AutosaveRegistry.MissingGrace = grace;
            AutosaveRegistry.ResetForTests();
        }
    }

    /// <summary>One autosave tick, as the timer or the notice's Retry button runs it: the planner is made due, then the controller's own tick runs.</summary>
    private static void ControllerTick(AutosaveController controller)
    {
        LtField<AutosavePlanner>(controller, "_autosavePlanner")!.RunSoon();
        typeof(AutosaveController).GetMethod("AutosaveTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
    }

    private static void ControllerCopiesAndNotices()
    {
        var host = new FakeAutosaveHost();
        var notices = new FakeNotices();
        var controller = new AutosaveController(host, notices);
        var notice = notices.Created[0].Notice;
        var real = AutosaveRunner.WriteCopy;
        var dirty = DirtyDocument(2, 10, 31, "Controller dirty");
        var clean = DirtyDocument(2, 10, 32, "Controller clean");
        clean.Project.IsDirty = false;
        host.Documents.Add(dirty, activate: false);
        host.Documents.Add(clean, activate: false);
        controller.Start();
        try
        {
            // Pass 1: nothing failing, so the notice is hidden before the pass and again after it.
            ControllerTick(controller);
            var passed = SmUntil(() => notice.Calls.Count >= 2);
            var copy = AutosavePathOf(dirty);
            Check("autosave controller: a pass gives the dirty song its recovery copy and the clean song none",
                passed && copy is not null && File.Exists(copy) && AutosavePathOf(clean) is null, $"calls {string.Join(",", notice.Calls)}");
            Check("autosave controller: a successful pass keeps the notice hidden", notice.Calls.All(c => c == "hide"), string.Join(",", notice.Calls));

            // A failing write (full disk): the notice shows the reason, and the previous copy is still there.
            dirty.Project.Tracks[0].Measures[0].Cells[0].Text = "edited while the disk is full";
            AutosaveRunner.WriteCopy = (_, _) => throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
            ControllerTick(controller);
            var shown = SmUntil(() => notice.Calls.Count >= 4);
            Check("autosave controller: a failing write shows the notice with the reason and keeps the previous copy",
                shown && notice.Calls[^1].StartsWith("show:Autosave failed", StringComparison.Ordinal) && notice.Calls[^1].Contains("disk is full", StringComparison.Ordinal)
                && AutosavePathOf(dirty) == copy && copy is not null && File.Exists(copy),
                notice.Calls.Count > 0 ? notice.Calls[^1] : "no call");

            // The next pass succeeds: the notice goes again.
            AutosaveRunner.WriteCopy = real;
            ControllerTick(controller);
            var cleared = SmUntil(() => notice.Calls.Count >= 6);
            Check("autosave controller: the next successful pass hides the notice again", cleared && notice.Calls[^1] == "hide", string.Join(",", notice.Calls));

            // Dispose: the timer stops, and the window's songs lose their copies.
            var timer = LtField<DispatcherTimer>(controller, "_autosaveTimer");
            var wasRunning = timer is { IsEnabled: true };
            controller.Dispose();
            controller.Dispose();
            Check("autosave controller: Dispose stops the autosave timer (it ran before) and can run twice",
                wasRunning && timer is { IsEnabled: false }, $"ran before {wasRunning}");
            Check("autosave controller: Dispose removes the copies of the songs still in its window", AutosavePathOf(dirty) is null && copy is not null && !File.Exists(copy));
        }
        finally
        {
            AutosaveRunner.WriteCopy = real;
            AutosaveRegistry.Retire(host.Documents.Documents.ToArray());
        }
    }

    private static void ControllerClosedTabLosesItsCopy()
    {
        var host = new FakeAutosaveHost();
        var notices = new FakeNotices();
        var controller = new AutosaveController(host, notices);
        var notice = notices.Created[0].Notice;
        var closed = DirtyDocument(1, 4, 33, "Closed tab");
        var open = DirtyDocument(1, 4, 34, "Open tab");
        host.Documents.Add(closed, activate: false);
        host.Documents.Add(open, activate: false);
        controller.Start();
        try
        {
            ControllerTick(controller);
            SmUntil(() => notice.Calls.Count >= 2);
            var closedCopy = AutosavePathOf(closed);
            var openCopy = AutosavePathOf(open);
            var hadCopy = closedCopy is not null && File.Exists(closedCopy);

            // Closing the tab changes the window's songs; the queued reconcile then finds it gone. The grace period is zero for this check only.
            AutosaveRegistry.MissingGrace = TimeSpan.Zero;
            host.Documents.Close(host.Documents.IndexOf(closed));
            foreach (var work in host.Queue.ToArray()) work();
            Check("autosave controller: a closed tab's recovery copy is removed after the window's reconcile; the open song keeps its copy",
                hadCopy && !File.Exists(closedCopy!) && AutosavePathOf(closed) is null && openCopy is not null && File.Exists(openCopy),
                $"had copy {hadCopy}, queued {host.Queue.Count}");
        }
        finally
        {
            controller.Dispose();
            AutosaveRegistry.Retire(host.Documents.Documents.ToArray());
        }
    }
}
