using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using TabForge.Audio;
using TabForge.Documents;

namespace TabForge;

/// <summary>
/// Tab transfer between windows (Controllers/TabTransferController): a tear-off whose source or target closes meanwhile, a merge that must leave the
/// source holding nothing, the engine following the moved song, and repeated tear-off / merge cycles that must release every window they create.
/// The held drag itself needs the mouse button; its transfer is the same entry points called here.
/// </summary>
public static partial class SelfTest
{
    private static HashSet<MainWindow> TransferWindowsNow() => Application.Current.Windows.OfType<MainWindow>().ToHashSet();

    private static MainWindow TransferNewWindow(HashSet<MainWindow> before) =>
        Application.Current.Windows.OfType<MainWindow>().First(w => !before.Contains(w));

    /// <summary>A window whose only tab is <paramref name="session"/> (as a torn-off window starts), with the song's engine chain loaded by the window's sync.</summary>
    private static MainWindow TransferSoleWindow(DocumentSession session)
    {
        var window = NewLifetimeWindow();
        window.AdoptSession(session);
        SettleLifetimeDispatcher();
        return window;
    }

    private static void CloseTransferWindow(MainWindow? window)
    {
        if (window is null || !window.IsVisible) return;
        foreach (var document in window.OpenDocuments) document.MarkClean();
        window.Close();
        SettleLifetimeDispatcher();
    }

    /// <summary>TearOffCloseSourceFirst: the window a tab was torn from closes while the torn-off window is still held; the song survives in the new window and then in the window it merges into.</summary>
    private static void TearOffCloseSourceFirstCase(LifetimeContext context)
    {
        var failures = context.Failures.Count;
        var (source, a) = EngineSyncOpen("TearSrc");
        MainWindow? held = null, target = null;
        try
        {
            var before = TransferWindowsNow();
            LtCall(source, "DetachDocumentToNewWindow", source.OpenDocuments.ToList().IndexOf(a));
            held = TransferNewWindow(before);
            SettleLifetimeDispatcher();
            var project = a.Project; var revision = project.TimelineRevision; var track = project.Tracks[0];
            CloseTransferWindow(source);
            var attachments = source.AttachmentCount;
            Check("tab transfer: the source window closing during a held tear-off leaves the song in the torn-off window, intact and still loaded in the engine",
                !source.IsVisible && held.OpenDocuments.Contains(a) && ReferenceEquals(a.Project, project) && project.TimelineRevision == revision && AudioEngineClient.Instance.SlotOf(track) >= 0 && ReferenceEquals(IxSlotOwner(track), a),
                $"source visible {source.IsVisible}, in held {held.OpenDocuments.Contains(a)}, slot {AudioEngineClient.Instance.SlotOf(track)}");
            Check("tab transfer: the closed source window holds no attachment and no song", attachments == 0 && source.OpenDocuments.Count == 0, $"attachments {attachments}, songs {source.OpenDocuments.Count}");

            target = NewLifetimeWindow();
            var merged = held.MergeSoleDocumentInto(target, 0);
            SettleLifetimeDispatcher();
            Check("tab transfer: the held window then merges into another window (the song is not copied) and nothing was posted to the closed source",
                merged && !held.IsVisible && target.OpenDocuments.ToList().IndexOf(a) == 0 && ReferenceEquals(target.OpenDocuments[0].Project, project) && context.Failures.Count == failures,
                $"merged {merged}, held visible {held.IsVisible}, failures {context.Failures.Count - failures}");
        }
        finally
        {
            CloseTransferWindow(source); CloseTransferWindow(held); CloseTransferWindow(target);
            AudioEngineClient.Instance.ReleaseOwner(a);
        }
    }

    /// <summary>TearOffCloseTargetFirst: the window the tab was about to merge into has closed; the merge is refused and the held window keeps its song.</summary>
    private static void TearOffCloseTargetFirstCase(LifetimeContext context)
    {
        var failures = context.Failures.Count;
        var a = DocumentSession.FromProject(LifetimePluginSong("TearTarget"), null);
        a.MarkClean();
        var held = TransferSoleWindow(a);
        var target = NewLifetimeWindow();
        try
        {
            CloseTransferWindow(target);
            var merged = held.MergeSoleDocumentInto(target, 0);
            SettleLifetimeDispatcher();
            Check("tab transfer: a merge into a window that has closed meanwhile is refused; the held window stays open with its song",
                !merged && held.IsVisible && held.OpenDocuments.Count == 1 && ReferenceEquals(held.OpenDocuments[0], a) && !target.OpenDocuments.Contains(a) && context.Failures.Count == failures,
                $"merged {merged}, held visible {held.IsVisible}, songs in held {held.OpenDocuments.Count}, song in closed target {target.OpenDocuments.Contains(a)}");
            Check("tab transfer: the song of a refused merge is still loaded in the engine and not disposed", AudioEngineClient.Instance.SlotOf(a.Project.Tracks[0]) >= 0 && ReferenceEquals(IxSlotOwner(a.Project.Tracks[0]), a),
                $"slot {AudioEngineClient.Instance.SlotOf(a.Project.Tracks[0])}");
        }
        finally
        {
            CloseTransferWindow(held); CloseTransferWindow(target);
            AudioEngineClient.Instance.ReleaseOwner(a);
        }
    }

    /// <summary>A target that says it is open but then refuses the song.</summary>
    private sealed class RefusingTarget : TabForge.Shell.ITabTransferTarget
    {
        public bool IsOpen => true;
        public bool CanAcceptAttachAt(Point screenPoint) => true;
        public int AttachInsertIndexAt(Point screenPoint) => 0;
        public void SetAttachHighlight(bool highlighted) { }
        public bool TryAdopt(DocumentSession session, int index) => false;
    }

    /// <summary>A merge refused after the open check rolls back: the song stays and its tab is shown again in the window it went back to.</summary>
    private static void MergeRollbackShowsSongAgainCase()
    {
        var a = DocumentSession.FromProject(LifetimePluginSong("Rollback"), null);
        a.MarkClean();
        var held = TransferSoleWindow(a);
        try
        {
            var status = LtField<System.Windows.Controls.TextBlock>(held, "StatusText")!;
            status.Text = "before";
            var merged = LtField<Controllers.TabTransferController>(held, "_tabTransfer")!.MergeSoleDocumentInto(new RefusingTarget(), 0);
            SettleLifetimeDispatcher();
            Check("tab transfer: a merge refused after the check rolls back and shows the song again in its window",
                !merged && held.IsVisible && held.OpenDocuments.Count == 1 && ReferenceEquals(held.OpenDocuments[0], a) && status.Text.StartsWith("Switched to", StringComparison.Ordinal),
                $"merged {merged}, songs {held.OpenDocuments.Count}, status '{status.Text}'");
        }
        finally { CloseTransferWindow(held); AudioEngineClient.Instance.ReleaseOwner(a); }
    }

    /// <summary>AttachTargetClearedOnClose: a window closing while it marks another window as its drop target takes the mark away again, and leaves the registry.</summary>
    private static void AttachTargetClearedOnCloseCase()
    {
        var source = NewLifetimeWindow();
        var target = NewLifetimeWindow();
        try
        {
            var sourceTransfer = LtField<Controllers.TabTransferController>(source, "_tabTransfer")!;
            var targetTransfer = LtField<Controllers.TabTransferController>(target, "_tabTransfer")!;
            var strip = LtField<System.Windows.Controls.Border>(target, "TitleBar")!;
            var plain = strip.BorderThickness;
            LtSet(sourceTransfer, "_attachTarget", targetTransfer);
            targetTransfer.SetAttachHighlight(true);
            var marked = strip.BorderThickness != plain && ReferenceEquals(sourceTransfer.AttachTarget, targetTransfer);
            var registered = StaticCollectionCount(typeof(TabForge.Shell.TabWindowRegistry), "Windows");
            CloseTransferWindow(source);
            Check("tab transfer: a window closing mid tear-off clears the highlight it put on the target and its attach target, and leaves the registry",
                marked && strip.BorderThickness == plain && sourceTransfer.AttachTarget is null && StaticCollectionCount(typeof(TabForge.Shell.TabWindowRegistry), "Windows") == registered - 1,
                $"marked {marked}, thickness restored {strip.BorderThickness == plain}, target {(sourceTransfer.AttachTarget is null ? "none" : "kept")}");
        }
        finally { CloseTransferWindow(source); CloseTransferWindow(target); }
    }

    /// <summary>MergeIntoOtherWindowReleasesSource: the source window holds nothing of the moved song once it merged, and is collectable while the song lives on in the target.</summary>
    private static void MergeIntoOtherWindowReleasesSourceCase()
    {
        var a = DocumentSession.FromProject(LifetimePluginSong("MergeRel"), null);
        a.MarkClean();
        var target = NewLifetimeWindow();
        MainWindow? other = null;
        try
        {
            var sourceRef = MergeSoleWindowInto(a, target);
            SettleLifetimeDispatcher();
            CollectUntilStable(() => sourceRef.IsAlive ? 1 : 0);
            Check("tab transfer: after a merge the emptied source window is released (collectable) while the song lives on in the target", !sourceRef.IsAlive && target.OpenDocuments.Contains(a),
                $"source alive {sourceRef.IsAlive}, in target {target.OpenDocuments.Contains(a)}");
            Check("tab transfer: the target is the only window listening to the moved song", PlaybackHandlers(a) == 2, $"handlers {PlaybackHandlers(a)}");
            // The song moves on from the target to a new window: the target lets go of it as well.
            var before = TransferWindowsNow();
            var second = DocumentSession.FromProject(LifetimePluginSong("MergeRel2"), null);
            second.MarkClean();
            LtCall(target, "AdoptDroppedDocument", second, target.OpenDocuments.Count);
            LtCall(target, "DetachDocumentToNewWindow", target.OpenDocuments.ToList().IndexOf(a));
            other = TransferNewWindow(before);
            SettleLifetimeDispatcher();
            Check("tab transfer: a song torn off again is held by the new window only (not by the window it left)", other.OpenDocuments.Contains(a) && !target.OpenDocuments.Contains(a) && PlaybackHandlers(a) == 2 && PlaybackHandlers(second) == 2,
                $"in other {other.OpenDocuments.Contains(a)}, in target {target.OpenDocuments.Contains(a)}, a handlers {PlaybackHandlers(a)}, second handlers {PlaybackHandlers(second)}");
            CloseTransferWindow(target); CloseTransferWindow(other);
            AudioEngineClient.Instance.ReleaseOwner(second);
        }
        finally
        {
            CloseTransferWindow(target); CloseTransferWindow(other);
            AudioEngineClient.Instance.ReleaseOwner(a);
        }
    }

    /// <summary>Opens a window that shows only <paramref name="session"/>, merges it into <paramref name="target"/> and returns a weak handle on the emptied source (no local keeps it alive).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MergeSoleWindowInto(DocumentSession session, MainWindow target)
    {
        var source = TransferSoleWindow(session);
        var merged = source.MergeSoleDocumentInto(target, 0);
        Check("tab transfer: a window's only song merges into another window at the drop position and the source closes", merged && !source.IsVisible && target.OpenDocuments.ToList().IndexOf(session) == 0, $"merged {merged}, source visible {source.IsVisible}");
        return new WeakReference(source);
    }

    /// <summary>TransferKeepsIdentityAndRevision plus the engine: the engine's slot, owner and chain follow the song across tear-off and merge; the document is the same object throughout.</summary>
    private static void EngineSyncFollowsMovedDocumentCase()
    {
        var a = DocumentSession.FromProject(LifetimePluginSong("FollowMove"), null);
        a.MarkClean();
        var client = AudioEngineClient.Instance;
        var first = TransferSoleWindow(a);
        MainWindow? second = null, third = null;
        try
        {
            var project = a.Project; var track = project.Tracks[0]; var revision = project.TimelineRevision;
            var ownerBefore = IxSlotOwner(track);
            var slotBefore = client.SlotOf(track);
            Check("tab transfer: the window showing a song owns its engine chain", slotBefore >= 0 && ReferenceEquals(ownerBefore, a) && ReferenceEquals(client.CurrentOwner, a), $"slot {slotBefore}, current owner {client.CurrentOwner?.GetType().Name}");

            third = NewLifetimeWindow();   // a window to merge into: it has its own blank tab, the engine owner stays with the moved song's window until it is shown there
            var merged = first.MergeSoleDocumentInto(third, 1);
            SettleLifetimeDispatcher();
            Check("tab transfer: the engine keeps the song's chain (same slot, same owner) when its window merges into another, and the new window answers for it",
                merged && client.SlotOf(track) == slotBefore && ReferenceEquals(IxSlotOwner(track), a) && ReferenceEquals(client.CurrentOwner, a) && ReferenceEquals(third.OpenDocuments[1], a),
                $"merged {merged}, slot {client.SlotOf(track)}/{slotBefore}, owner {IxSlotOwner(track)?.GetType().Name}");
            Check("tab transfer: the merged song is the same document, project and revision", ReferenceEquals(third.OpenDocuments[1].Project, project) && project.TimelineRevision == revision && !a.Project.IsDirty,
                $"revision {project.TimelineRevision}/{revision}, dirty {a.Project.IsDirty}");
            Check("tab transfer: the closed source window holds no engine attachment", first.AttachmentCount == 0, $"attachments {first.AttachmentCount}");

            // The new window now syncs the song: a sync after the move reaches the engine under the song as owner.
            var before = TransferWindowsNow();
            LtCall(third, "DetachDocumentToNewWindow", third.OpenDocuments.ToList().IndexOf(a));
            second = TransferNewWindow(before);
            SettleLifetimeDispatcher();
            Check("tab transfer: after a tear-off the engine still holds the song's chain once, owned by the song, and the new window shows it",
                client.SlotOf(track) == slotBefore && ReferenceEquals(IxSlotOwner(track), a) && second.OpenDocuments.Contains(a), $"slot {client.SlotOf(track)}/{slotBefore}");
            CloseTransferWindow(second);
            Check("tab transfer: closing the window that finally shows the song unloads its chain", client.SlotOf(track) == -1, $"slot {client.SlotOf(track)}");
        }
        finally
        {
            CloseTransferWindow(first); CloseTransferWindow(second); CloseTransferWindow(third);
            client.ReleaseOwner(a);
        }
    }

    /// <summary>RepeatedTearOffAndMergeReleasesWindows (area leaks): torn-off windows that merge back are collected, and no registry entry, handler or window is left behind.</summary>
    private static void TestRepeatedTearOffAndMergeReleasesWindows() => RunInWindowFixture((main, context) =>
    {
        const int cycles = 12;
        var a = DocumentSession.FromProject(LifetimePluginSong("CycleA"), null);
        a.MarkClean();
        LtCall(main, "AdoptDroppedDocument", a, main.OpenDocuments.Count);
        SettleLifetimeDispatcher();
        var baseline = LifetimeSnapshot(context.Store);
        var torn = new List<WeakReference>();
        for (var i = 0; i < cycles; i++) TearOffAndMergeBack(main, a, torn);
        CollectUntilStable(() => torn.Count(r => r.IsAlive));
        var after = LifetimeSnapshot(context.Store);
        var diff = LifetimeDiff(baseline, after);
        Check($"tear-off/merge x{cycles}: every torn-off window is collected after it merged back", LifetimeAlive(torn) == 0, $"{LifetimeAlive(torn)} of {torn.Count} still alive");
        Check($"tear-off/merge x{cycles}: no handler, registry entry or window is left behind", diff.Length == 0, diff);
        Check($"tear-off/merge x{cycles}: the song is still in its window, listened to once, and loaded in the engine once",
            main.OpenDocuments.Contains(a) && PlaybackHandlers(a) is 0 or 2 && AudioEngineClient.Instance.SlotOf(a.Project.Tracks[0]) >= 0 && context.Failures.Count == 0,
            $"in window {main.OpenDocuments.Contains(a)}, handlers {PlaybackHandlers(a)}, failures {context.Failures.Count}");
        LtCall(main, "CloseDocument", main.OpenDocuments.ToList().IndexOf(a));
        SettleLifetimeDispatcher();
        Check($"tear-off/merge x{cycles}: closing the song afterwards unloads its chain", AudioEngineClient.Instance.SlotOf(a.Project.Tracks[0]) == -1);
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TearOffAndMergeBack(MainWindow main, DocumentSession a, List<WeakReference> torn)
    {
        var before = TransferWindowsNow();
        LtCall(main, "DetachDocumentToNewWindow", main.OpenDocuments.ToList().IndexOf(a));
        var window = TransferNewWindow(before);
        torn.Add(new WeakReference(window));
        SettleLifetimeDispatcher();
        window.MergeSoleDocumentInto(main, main.OpenDocuments.Count);
        SettleLifetimeDispatcher();
    }
}
