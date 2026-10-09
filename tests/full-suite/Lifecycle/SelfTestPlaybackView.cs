using System.Reflection;
using System.Windows;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Shell;

namespace TabForge;

/// <summary>
/// The playback view of a window (Controllers/PlaybackViewController): the per-frame tick allocates no more than the recorded budget, and the
/// window listens to the playback events of the song on show only, until its tab switches or the window closes.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Bytes the 200 measured ticks (one position report plus one ApplyPendingPlayhead each) may allocate on this thread.</summary>
    private const long PlaybackTickAllocationBudgetBytes = 528_000;   // about 526 KB measured, plus headroom for runtime jitter

    private static bool PlaybackTickRunning(MainWindow window) => LtField<Controllers.PlaybackViewController>(window, "_playbackView")?.IsTicking == true;

    private static int PlaybackHandlers(DocumentSession session) =>
        InstanceEventCount(session.Playback, "TimelineChanged") + InstanceEventCount(session.Playback, "TimelineRevised");

    /// <summary>PlaybackTickAllocationBudget: 200 ticks over a playing song stay inside the recorded allocation budget (the measured figure is in the message).</summary>
    private static void PlaybackTickAllocationBudgetCase()
    {
        var (window, session) = EngineSyncOpen("TickBudget");
        try
        {
            var playback = session.Playback;
            playback.IsPlayingVisual = true;
            LtField<TabForge.Views.TabEditorControl>(window, "Editor")!.Playback.Active = true;
            var tick = (Action)Delegate.CreateDelegate(typeof(Action), window, typeof(MainWindow).GetMethod("ApplyPendingPlayhead", BindingFlags.Instance | BindingFlags.NonPublic)!);
            var positions = new PlaybackPosition[260];
            for (var i = 0; i < positions.Length; i++)
                positions[i] = new PlaybackPosition { Bar = i / 20, Cell = i % 4, BarFraction = (i % 20) / 20.0, ElapsedMs = i * 16.0 };
            for (var i = 0; i < 60; i++) { playback.ReportPosition(positions[i]); tick(); }   // warm-up: jitted, caches filled
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 60; i < 260; i++) { playback.ReportPosition(positions[i]); tick(); }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check($"playback view: 200 playback ticks allocate no more than the recorded budget -> {allocated:N0} bytes, budget {PlaybackTickAllocationBudgetBytes:N0}",
                allocated <= PlaybackTickAllocationBudgetBytes, $"{allocated:N0} bytes");
            playback.IsPlayingVisual = false;
        }
        finally
        {
            foreach (var document in window.OpenDocuments) document.MarkClean();
            window.Close();
            SettleLifetimeDispatcher();
        }
    }

    /// <summary>PlaybackEventsDetachedOnTabSwitchAndClose: only the song on show is listened to; a tab that left, a tab switched away from and a closed window leave no handler and no running tick.</summary>
    private static void PlaybackEventsDetachedOnTabSwitchAndCloseCase()
    {
        var (window, a) = EngineSyncOpen("ViewA");
        var b = DocumentSession.FromProject(LifetimePluginSong("ViewB"), null);
        b.MarkClean();
        LtCall(window, "AdoptDroppedDocument", b, window.OpenDocuments.Count);
        SettleLifetimeDispatcher();
        MainWindow? second = null;
        try
        {
            Check("playback view: the song on show is listened to, the other is not", PlaybackHandlers(b) == 2 && PlaybackHandlers(a) == 0, $"shown {PlaybackHandlers(b)}, other {PlaybackHandlers(a)}");
            IxActivate(window, a);
            Check("playback view: switching tabs moves the listening to the new song and detaches the old one", PlaybackHandlers(a) == 2 && PlaybackHandlers(b) == 0, $"a {PlaybackHandlers(a)}, b {PlaybackHandlers(b)}");
            a.Playback.IsPlayingVisual = true;
            IxActivate(window, b);
            Check("playback view: the tick stops when a tab that is not playing is shown", !PlaybackTickRunning(window) && PlaybackHandlers(a) == 0, $"tick {PlaybackTickRunning(window)}");
            IxActivate(window, a);
            a.Playback.IsPlayingVisual = false;

            // A tab that moves to another window is listened to by that window only; closing the window it left changes nothing.
            var windowsBefore = Application.Current.Windows.OfType<MainWindow>().ToHashSet();
            LtCall(window, "DetachDocumentToNewWindow", window.OpenDocuments.ToList().IndexOf(a));
            second = Application.Current.Windows.OfType<MainWindow>().First(w => !windowsBefore.Contains(w));
            SettleLifetimeDispatcher();
            Check("playback view: a tab moved to another window is listened to once, by that window", second.OpenDocuments.Contains(a) && PlaybackHandlers(a) == 2, $"a {PlaybackHandlers(a)}");
            foreach (var document in window.OpenDocuments) document.MarkClean();
            window.Close();
            SettleLifetimeDispatcher();
            Check("playback view: closing the window a tab came from leaves the moved tab's listener alone and stops the old window's tick", PlaybackHandlers(a) == 2 && !PlaybackTickRunning(window), $"a {PlaybackHandlers(a)}, tick {PlaybackTickRunning(window)}");
            foreach (var document in second.OpenDocuments) document.MarkClean();
            second.Close();
            SettleLifetimeDispatcher();
            Check("playback view: closing the last window leaves no handler on its songs and no running tick", PlaybackHandlers(a) == 0 && !PlaybackTickRunning(second), $"a {PlaybackHandlers(a)}, tick {PlaybackTickRunning(second)}");
        }
        finally
        {
            foreach (var w in new[] { window, second }.Where(w => w is not null && w.IsLoaded))
            {
                foreach (var document in w!.OpenDocuments) document.MarkClean();
                w.Close();
            }
            SettleLifetimeDispatcher();
        }
    }
}
