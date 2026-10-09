using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;

namespace TabForge;

/// <summary>Window close, transfer, preferences and engine-chain lifetime scenarios.</summary>
public static partial class SelfTest
{
    private static void LifetimeRepeatedCloseCase(MainWindow a, LifetimeContext context)
    {
        var windows = new List<WeakReference>(); var sessions = new List<WeakReference>(); var projects = new List<WeakReference>();
        for (var i = 0; i < WindowLifetimeWarmUp; i++) CreateUseAndCloseWindow(windows, sessions, projects);
        SettleLifetimeDispatcher();
        CollectFully();
        var baseline = LifetimeSnapshot(context.Store);
        var heapBefore = CollectFully();
        var rssBefore = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        windows.Clear(); sessions.Clear(); projects.Clear();
        var perCycle = new List<string>();
        for (var i = 0; i < WindowLifetimeCycles; i++)
        {
            CreateUseAndCloseWindow(windows, sessions, projects);
            SettleLifetimeDispatcher();
            if (i is 0 or 9 or WindowLifetimeCycles - 1) perCycle.Add($"after cycle {i + 1}: {LifetimeDiff(baseline, LifetimeSnapshot(context.Store))}");
        }
        CollectUntilStable(() => LifetimeAlive(windows) + LifetimeAlive(sessions) + LifetimeAlive(projects));
        var after = LifetimeSnapshot(context.Store);
        var diff = LifetimeDiff(baseline, after);
        var heapAfter = CollectFully();
        var rssAfter = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        Log.Add($"  info  window lifetime x{WindowLifetimeCycles}: managed heap {(heapAfter - heapBefore) / 1024.0 / 1024:+0.0;-0.0} MiB after warm-up ({heapBefore / 1024.0 / 1024:0.0} -> {heapAfter / 1024.0 / 1024:0.0}), working set {(rssAfter - rssBefore) / 1024.0 / 1024:+0.0;-0.0} MiB (supporting evidence only), {LifetimeAlive(windows)}/{windows.Count} windows, {LifetimeAlive(sessions)}/{sessions.Count} documents alive");

        Check($"window lifetime: all {WindowLifetimeCycles} closed main windows are collected while window A stays open",
            LifetimeAlive(windows) == 0, $"{LifetimeAlive(windows)} of {windows.Count} closed windows still alive");
        Check($"window lifetime: the documents and projects of the {WindowLifetimeCycles} closed windows are collected",
            LifetimeAlive(sessions) == 0 && LifetimeAlive(projects) == 0,
            $"{LifetimeAlive(sessions)} of {sessions.Count} documents and {LifetimeAlive(projects)} of {projects.Count} projects still alive (cycle indexes of projects: {string.Join(",", projects.Select((p, i) => p.IsAlive ? i : -1).Where(i => i >= 0))})");
        Check($"window lifetime: after {WindowLifetimeCycles} cycles no static event handler, shared-engine handler, settings-store handler or registry entry is left over",
            diff.Length == 0, diff + (diff.Length > 0 ? " | " + string.Join(" | ", perCycle) : ""));
        Check("window lifetime: window A still works after the cycles (its media notice reacts, one handler per open window)",
            a.IsLoaded && StaticEventCount(typeof(MediaAccess), "Changed") == baseline["MediaAccess.Changed handlers"]);
        Check("window lifetime: no unhandled dispatcher exception during the cycles", context.Failures.Count == 0, string.Join(" | ", context.Failures.Take(3)));
    }

    // ---------- case 2: a cancelled close leaves the window working, wired exactly once ----------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MainWindow OpenWindowWithDirtyTab(out SortedDictionary<string, int> whileOpen, AppSettingsStore store)
    {
        var b = NewLifetimeWindow();
        whileOpen = LifetimeSnapshot(store);
        // A real edit: the song becomes unsaved, so the close asks (the dialog is cancelled by the test's capture hook).
        b.OpenDocuments[0].Project.Title = "Dirty song";
        b.OpenDocuments[0].Project.IsDirty = true;
        b.OpenDocuments[0].Project.Tracks[0].Name = "Edited";
        return b;
    }

    private static void LifetimeCancelledCloseCase(MainWindow a, LifetimeContext context)
    {
        var withoutB = LifetimeSnapshot(context.Store);
        MainWindow? b = OpenWindowWithDirtyTab(out var withB, context.Store);
        var before = context.Captured.Count;
        b.Close();   // asks to save; the dialog is cancelled
        SettleLifetimeDispatcher();
        var asked = context.Captured.Count - before;
        foreach (var dialog in context.Captured.Skip(before).ToArray()) dialog.Close();   // the captured confirmation dialog was never shown: dispose it
        var afterCancel = LifetimeSnapshot(context.Store);
        var cancelDiff = LifetimeDiff(withB, afterCancel);
        Check("window lifetime: closing a window with unsaved changes asks, and Cancel keeps the window open", asked == 1 && b.IsLoaded && b.IsVisible, $"dialogs {asked}, loaded {b.IsLoaded}, visible {b.IsVisible}");
        Check("window lifetime: a cancelled close detaches nothing and attaches nothing twice (handler and registry counts unchanged)", cancelDiff.Length == 0, cancelDiff);

        // Media review: an approval change still reaches B (its notice bar appears), once.
        AddUnapprovedClip(b);
        b.OpenDocuments[0].Project.Title = "Dirty song"; b.OpenDocuments[0].Project.IsDirty = true; b.OpenDocuments[0].Project.Tracks[0].Name = "Edited again";
        MediaAccess.RaiseChanged();   // raises MediaAccess.Changed
        SettleLifetimeDispatcher();
        var bar = MediaBar(b);
        Check("window lifetime: after a cancelled close the window's media review notice still reacts to approval changes", bar is { Visibility: Visibility.Visible }, $"bar {(bar is null ? "missing" : bar.Visibility.ToString())}");

        // Preferences: the actions still reach B (and only B).
        var overrideBefore = LtField<string>(LtPane(b), "_instrumentViewOverride");
        var overrideA = LtField<string>(LtPane(a), "_instrumentViewOverride");
        var prefs = OpenPreferencesCaptured(b, context, "fretboard.showallas");
        var pressed = prefs is not null && PressRowButton(prefs, "Apply to all tracks");
        var overrideAfter = LtField<string>(LtPane(b), "_instrumentViewOverride");
        Check("window lifetime: after a cancelled close the window's Preferences still apply their actions to it (and not to window A)",
            pressed && overrideBefore is null && overrideAfter is not null && LtField<string>(LtPane(a), "_instrumentViewOverride") == overrideA,
            $"pressed {pressed}, before '{overrideBefore}', after '{overrideAfter}', A '{LtField<string>(LtPane(a), "_instrumentViewOverride")}'");
        prefs?.Close();

        // Playback: the transport still starts and stops (the window's own playback wiring is intact). A silent output replaces the Windows synth.
        var playedOk = StartAndStopSilentPlayback(b);
        Check("window lifetime: after a cancelled close the window's playback controls still work", playedOk.Started && playedOk.Stopped, $"started {playedOk.Started}, stopped {playedOk.Stopped}");

        // Now really close it: everything B attached is gone.
        foreach (var document in b.OpenDocuments) document.MarkClean();   // nothing unsaved: this close goes through
        b.Close();
        var stillLoaded = b.IsVisible;
        b = null;
        SettleLifetimeDispatcher(); CollectFully();
        var final = LifetimeSnapshot(context.Store);
        Check("window lifetime: once the cancelled window is really closed, every handler and registry entry it held is gone", LifetimeDiff(withoutB, final).Length == 0 && !stillLoaded, LifetimeDiff(withoutB, final));
    }

    private sealed class SilentMidiOutput : IMidiOutput
    {
        public bool Disposed { get; private set; }
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() => Disposed = true;
    }

    /// <summary>Plays the window's active song through its normal transport on a silent output, then stops it.</summary>
    private static (bool Started, bool Stopped) StartAndStopSilentPlayback(MainWindow window)
    {
        var silent = new SilentMidiOutput();
        var session = new DocumentSession(new PlaybackEngine(silent)) { Project = TemplateFactory.Blank() };
        session.MarkClean();
        LtCall(window, "AdoptDroppedDocument", session, window.OpenDocuments.Count);
        LtCall(window, "StartPlayback");
        var started = session.Playback.Engine.IsPlaying;
        LtCall(window, "StopPlayback");
        var stopped = !session.Playback.Engine.IsPlaying;
        return (started, stopped);
    }

    // ---------- case 3: B closes while an approval notification and a waveform completion are queued ----------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Window, Func<bool> MediaBarCreated) QueueWorkThenClose()
    {
        var b = NewLifetimeWindow();
        AddUnapprovedClip(b);   // a clip that needs approval; B's notice bar has NOT been built yet
        var bar0 = MediaBar(b);
        if (bar0 is not null) throw new InvalidOperationException("the media bar exists before any approval change");
        // Queue: an approval change (every window's handler posts a notice refresh) and a waveform that finished reading (every timeline posts a redraw).
        MediaAccess.RaiseChanged();
        typeof(WaveformCache).GetMethod("RaiseReady", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { LifetimeUncFile });
        var weak = new WeakReference(b);
        var closedBar = new WeakReference<MainWindow>(b);
        b.Close();   // before the queue is dispatched
        return (weak, () => closedBar.TryGetTarget(out var w) && MediaBar(w) is not null);
    }

    private static void LifetimeQueuedWorkCase(MainWindow a, LifetimeContext context)
    {
        var failuresBefore = context.Failures.Count;
        var (weak, barCreated) = QueueWorkThenClose();
        SettleLifetimeDispatcher();   // the queued work is dispatched now
        var touched = barCreated();
        Check("window lifetime: an approval notification queued before the window closed does not touch the closed window", !touched, "the closed window's media notice bar was built after it closed");
        Check("window lifetime: a waveform completion and an approval notification dispatched after the close throw nothing", context.Failures.Count == failuresBefore, string.Join(" | ", context.Failures.Skip(failuresBefore).Take(3)));
        // The fixture's own list still names the Preferences windows closed by earlier cases; a closed one shares template parts with
        // other windows' controls, so the fixture lets go of them before asking whether this window is collectable.
        context.Captured.RemoveAll(w => w is PreferencesWindow && !w.IsVisible);
        CollectUntilStable(() => weak.IsAlive ? 1 : 0);
        Check("window lifetime: the window closed with queued work is collectable once the queue ran", !weak.IsAlive);
        // The surviving window still reacts.
        AddUnapprovedClip(a);
        MediaAccess.RaiseChanged();
        SettleLifetimeDispatcher();
        Check("window lifetime: the surviving window still handles approval changes after the other one closed with work queued", MediaBar(a) is { Visibility: Visibility.Visible });
        a.OpenDocuments[0].Project.Tracks[0].AudioClips.Clear();
        LtCall(a, "UpdateMediaApprovalBar");
    }

    // ---------- case 4: a playing tab moves from one window to another, then the first window closes ----------

    private sealed class CountingMidiOutput : IMidiOutput
    {
        public bool Disposed { get; private set; }
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() => Disposed = true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TransferPlayingTab(LifetimeContext context, out WeakReference sessionRef, out WeakReference projectRef, out WeakReference firstRef, out WeakReference secondRef, out CountingMidiOutput output)
    {
        output = new CountingMidiOutput();
        var first = NewLifetimeWindow();
        firstRef = new WeakReference(first);
        var project = TemplateFactory.Blank();
        var session = new DocumentSession(new PlaybackEngine(output)) { Project = project };
        session.MarkClean();
        LtCall(first, "AdoptDroppedDocument", session, 1);
        // An edit (one undo step), then playback of that tab on its own silent output; the song loops so it keeps playing.
        TabForge.Documents.DocumentEdits.Checkpoint(session);
        project.Title = "Moving song"; project.IsDirty = true;
        var title = session.Project.Title; var undoSteps = session.Undo.UndoCount;
        session.MarkClean();   // saved: closing the tab later must not ask (the dialog would be cancelled by the test's hook)
        session.LoopEnabled = true; session.LoopStartBar = 0; session.LoopEndBar = 0;
        LtCall(first, "StartPlayback");
        var playingBefore = session.Playback.Engine.IsPlaying;

        // The tab moves to a new window (the tab menu's "Move to new window").
        var windowsBefore = Application.Current.Windows.OfType<MainWindow>().ToHashSet();
        LtCall(first, "DetachDocumentToNewWindow", first.OpenDocuments.ToList().IndexOf(session));
        var second = Application.Current.Windows.OfType<MainWindow>().First(w => !windowsBefore.Contains(w));
        secondRef = new WeakReference(second);
        SettleLifetimeDispatcher();
        var movedOk = second.OpenDocuments.Contains(session) && !first.OpenDocuments.Contains(session);
        var stillPlaying = session.Playback.Engine.IsPlaying;
        first.Close();   // the window the tab came from closes
        SettleLifetimeDispatcher();
        var afterClose = (Playing: session.Playback.Engine.IsPlaying, Disposed: output.Disposed, Title: session.Project.Title, Undo: session.Undo.UndoCount, Same: ReferenceEquals(session.Project, project), InSecond: second.OpenDocuments.Contains(session));
        var uiTick = LtField<Controllers.PlaybackViewController>(second, "_playbackView")?.IsTicking == true;
        Check("window lifetime: a tab moved to another window while playing is in the new window and keeps playing", playingBefore && movedOk && stillPlaying, $"playing before {playingBefore}, moved {movedOk}, playing after move {stillPlaying}");
        Check("window lifetime: closing the window the tab came from does not stop, dispose or change the moved tab (state, undo history, playback)",
            afterClose.Playing && !afterClose.Disposed && afterClose.Title == title && afterClose.Undo == undoSteps && afterClose.Same && afterClose.InSecond,
            $"playing {afterClose.Playing}, output disposed {afterClose.Disposed}, title '{afterClose.Title}', undo {afterClose.Undo}/{undoSteps}, same project {afterClose.Same}, in new window {afterClose.InSecond}");
        Check("window lifetime: the new window's playback display follows the moved, still playing tab", uiTick, $"ui tick running {uiTick}");

        // Closing the tab for good releases it.
        LtCall(second, "CloseDocument", second.OpenDocuments.ToList().IndexOf(session));
        SettleLifetimeDispatcher();
        Check("window lifetime: closing the moved tab permanently stops its playback and disposes its output", !session.Playback.Engine.IsPlaying && output.Disposed,
            $"playing {session.Playback.Engine.IsPlaying}, output disposed {output.Disposed}");
        // This test session was built around a silent engine and has no routing to the shared audio client (production documents always have it, and
        // DocumentSession.DisposePlayback releases their engine ownership); release the ownership the window's sync gave it, as that would.
        AudioEngineClient.Instance.ReleaseOwner(session);
        sessionRef = new WeakReference(session); projectRef = new WeakReference(project);
    }

    private static void LifetimeSoleTabDragCase()
    {
        // Decision: only a secondary window's single tab moves the window; everything else tears off as before.
        Check("sole-tab drag: any window's only tab (main or secondary) moves the window (no tear-off)",
            Shell.BrowserTabDragPolicy.TearOffAction(1) == Shell.TabTearOffAction.MoveWindow);
        Check("sole-tab drag: 2+ tabs still tear off into a new window",
            Shell.BrowserTabDragPolicy.TearOffAction(2) == Shell.TabTearOffAction.NewWindow && Shell.BrowserTabDragPolicy.TearOffAction(3) == Shell.TabTearOffAction.NewWindow);

        // A sole-tab drag in any window (the main window included) creates no window and no blank replacement tab.
        var solo = NewLifetimeWindow();
        var soloDoc = solo.OpenDocuments.ToList();
        var windowsAtStart = Application.Current.Windows.OfType<MainWindow>().Count();
        LtCall(solo, "DetachHeldDocumentToNewWindow", 0, new Point(200, 200));   // no button held in a headless run: the window is not dragged, nothing else may happen
        SettleLifetimeDispatcher();
        var soloAfter = solo.OpenDocuments.ToList();
        Check("sole-tab drag: a window's only tab is not detached, no new window and no blank 'Untitled' tab appears",
            Application.Current.Windows.OfType<MainWindow>().Count() == windowsAtStart && soloAfter.Count == 1 && ReferenceEquals(soloAfter[0], soloDoc[0]),
            $"windows {Application.Current.Windows.OfType<MainWindow>().Count()}/{windowsAtStart}, tabs {soloAfter.Count}");
        solo.Close();
        SettleLifetimeDispatcher();

        // Drop over another window's tab bar: the tab merges at the position, the source window closes, document intact.
        var output = new CountingMidiOutput();
        var target = NewLifetimeWindow();
        var source = NewLifetimeWindow();
        var project = TemplateFactory.Blank();
        var session = new DocumentSession(new PlaybackEngine(output)) { Project = project };
        session.MarkClean();
        source.AdoptSession(session);
        TabForge.Documents.DocumentEdits.Checkpoint(session);
        project.Title = "Dragged sole tab"; project.IsDirty = true;
        var undoSteps = session.Undo.UndoCount;
        var targetBefore = target.OpenDocuments.Count;
        var windowsBefore = Application.Current.Windows.OfType<MainWindow>().Count();
        var merged = source.MergeSoleDocumentInto(target, 0);
        SettleLifetimeDispatcher();
        Check("sole-tab drag: dropping over another window merges the tab there at the drop position",
            merged && target.OpenDocuments.Count == targetBefore + 1 && target.OpenDocuments.ToList().IndexOf(session) == 0);
        Check("sole-tab drag: the emptied source window closes (none left behind, none created)",
            !source.IsVisible && Application.Current.Windows.OfType<MainWindow>().Count() == windowsBefore - 1);
        Check("sole-tab drag: the merged document keeps its song, undo history and dirty state",
            ReferenceEquals(session.Project, project) && project.Title == "Dragged sole tab" && session.Undo.UndoCount == undoSteps && project.IsDirty,
            $"undo {session.Undo.UndoCount}/{undoSteps}, dirty {project.IsDirty}");
        session.MarkClean();
        target.Close();
        SettleLifetimeDispatcher();
        AudioEngineClient.Instance.ReleaseOwner(session);
        GC.KeepAlive(output);
    }

    // No local of the caller may hold the window while the collector runs.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CloseIfOpen(WeakReference window)
    {
        if (window.Target is MainWindow open && open.IsVisible) open.Close();
    }

    private static void LifetimeTabTransferCase(LifetimeContext context)
    {
        var windowsBefore = Application.Current.Windows.Count;
        TransferPlayingTab(context, out var sessionRef, out var projectRef, out var firstRef, out var secondRef, out var output);
        // The second window may still be open (the last tab closed: the setting decides). Close it; then everything must be collectable.
        CloseIfOpen(secondRef);
        SettleLifetimeDispatcher();
        CollectUntilStable(() => (sessionRef.IsAlive ? 1 : 0) + (projectRef.IsAlive ? 1 : 0) + (firstRef.IsAlive ? 1 : 0) + (secondRef.IsAlive ? 1 : 0));
        Check("window lifetime: after the transfer and both closes, the moved tab's document, both windows and the output are collectable",
            !sessionRef.IsAlive && !projectRef.IsAlive && !firstRef.IsAlive && !secondRef.IsAlive && Application.Current.Windows.Count == windowsBefore,
            $"document {sessionRef.IsAlive}, project {projectRef.IsAlive}, first window {firstRef.IsAlive}, second window {secondRef.IsAlive}, windows {Application.Current.Windows.Count} (was {windowsBefore})");
        GC.KeepAlive(output);
    }

    // ---------- case 5: Preferences opened from two windows ----------

    private static PreferencesWindow? OpenPreferencesCaptured(MainWindow window, LifetimeContext context, string rowKey, string? category = null)
    {
        var before = context.Captured.Count;
        LtCall(window, "OpenSettings", category ?? SettingsCatalog.Fretboard, rowKey);
        return context.Captured.Skip(before).OfType<PreferencesWindow>().FirstOrDefault();
    }

    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Logical<T>(child)) yield return found;
    }

    private static bool PressRowButton(PreferencesWindow window, string label)
    {
        var button = Logical<Button>(window).FirstOrDefault(b => b.Content as string == label);
        if (button is null || !button.IsEnabled) return false;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PreferencesOwnerChecks(MainWindow a, MainWindow b, LifetimeContext context, string order)
    {
        // A song of its own on A only: A's linked-audio review lists its network folder, B's lists nothing.
        var pa = OpenPreferencesCaptured(a, context, "fretboard.showallas");
        var pb = OpenPreferencesCaptured(b, context, "fretboard.showallas");
        if (order == "A first") { pa?.Close(); pb?.Close(); } else { pb?.Close(); pa?.Close(); }
        LtSet(LtPane(a), "_instrumentViewOverride", null); LtSet(LtPane(b), "_instrumentViewOverride", null);

        var viewA = LtField<string>(LtPane(a), "_instrumentViewOverride"); var viewB = LtField<string>(LtPane(b), "_instrumentViewOverride");
        var again = OpenPreferencesCaptured(a, context, "fretboard.showallas");
        var applied = again is not null && PressRowButton(again, "Apply to all tracks");
        var afterA = LtField<string>(LtPane(a), "_instrumentViewOverride"); var afterB = LtField<string>(LtPane(b), "_instrumentViewOverride");
        Check($"window lifetime: Preferences opened from A and B and closed ({order}): the 'Apply to all tracks' action reaches the window that opened it (A), not B",
            applied && afterA is not null && afterB == viewB, $"applied {applied}, A '{viewA}' -> '{afterA}', B '{viewB}' -> '{afterB}'");

        var beforeReview = context.Captured.Count;
        var manage = OpenPreferencesCaptured(a, context, "audio.linkedmedia", SettingsCatalog.AudioVst);
        var pressed = manage is not null && PressRowButton(manage, "Manage approved folders…");
        var review = context.Captured.Skip(beforeReview).FirstOrDefault(w => w.Title == "Linked audio");
        var listsA = review is not null && Logical<CheckBox>(review).Any(c => (c.Content as string ?? "").StartsWith(@"\\tf-lifetime-test-server\share"));
        Check($"window lifetime: Preferences opened from A and B and closed ({order}): 'Manage approved folders' shows A's linked audio (the owning window's song), not B's",
            pressed && listsA, $"pressed {pressed}, review window {(review is null ? "missing" : "built")}, lists A's folder {listsA}");
        manage?.Close(); again?.Close();
        review?.Close();
    }

    private static void LifetimePreferencesCase(MainWindow a, LifetimeContext context)
    {
        AddUnapprovedClip(a);
        var bRef = PreferencesFromTwoWindows(a, context);
        SettleLifetimeDispatcher();
        var before = LtField<string>(LtPane(a), "_instrumentViewOverride");
        var survivor = OpenPreferencesCaptured(a, context, "fretboard.showallas");
        var applied = survivor is not null && PressRowButton(survivor, "Apply to all tracks");
        Check("window lifetime: after the other window closed, the surviving window's Preferences actions still reach it", applied && LtField<string>(LtPane(a), "_instrumentViewOverride") is not null, $"applied {applied}, was '{before}'");
        survivor?.Close();
        foreach (var dialog in context.Captured.ToArray()) { try { dialog.Close(); } catch (InvalidOperationException) { } }
        context.Captured.Clear();
        a.OpenDocuments[0].Project.Tracks[0].AudioClips.Clear();
        CollectUntilStable(() => bRef.IsAlive ? 1 : 0);
        Check("window lifetime: the window that opened Preferences and closed is collectable (no static Preferences action keeps it)", !bRef.IsAlive,
            "windows left: " + string.Join(", ", Application.Current.Windows.OfType<Window>().Select(w => w.GetType().Name + (w.Owner is { } o ? "<-" + o.GetType().Name : ""))));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PreferencesFromTwoWindows(MainWindow a, LifetimeContext context)
    {
        var b = NewLifetimeWindow();   // created after A: at the baseline its callbacks replaced A's
        foreach (var order in new[] { "A first", "B first" }) PreferencesOwnerChecks(a, b, context, order);
        // B closes: the surviving window's Preferences still act on A and nothing reaches the closed B.
        var bRef = new WeakReference(b);
        b.Close();
        foreach (var dialog in context.Captured.ToArray()) { try { dialog.Close(); } catch (InvalidOperationException) { } }
        context.Captured.Clear();   // the test must not keep the closed dialogs (they reference their owner) alive itself
        return bRef;
    }

    // ---------- case 6: closing a window releases its documents' engine chains, not the shared engine or another window's chains ----------

    private static void LifetimeEngineChainsCase(MainWindow a, LifetimeContext context)
    {
        var shared = AudioEngineClient.Instance;
        var songA = LifetimePluginSong("KeepA");
        var docA = DocumentSession.FromProject(songA, null);
        docA.MarkClean();
        LtCall(a, "AdoptDroppedDocument", docA, a.OpenDocuments.Count);   // window A shows it: its chain loads on the (fake) engine
        var trackA = songA.Tracks[0];
        var slotA = shared.SlotOf(trackA);

        var songB = LifetimePluginSong("DropB");
        var trackB = songB.Tracks[0];
        var closedB = LoadChainInWindowThenClose(songB);
        SettleLifetimeDispatcher();
        Check("window lifetime: closing a window unloads its documents' engine chains at once (not parked), while the shared engine keeps running",
            closedB.SlotBefore >= 0 && closedB.Closed && shared.SlotOf(trackB) == -1 && shared.IsRunning,
            $"B slot before {closedB.SlotBefore}, closed {closedB.Closed}, after {shared.SlotOf(trackB)}, engine running {shared.IsRunning}");
        Check("window lifetime: the other window's chain stays loaded after that close (inactive documents stay warm; only closed ones are released)",
            slotA >= 0 && shared.SlotOf(trackA) >= 0, $"A slot {slotA} -> {shared.SlotOf(trackA)}");

        // Closing that tab for good in its own window releases it too.
        LtCall(a, "CloseDocument", a.OpenDocuments.ToList().IndexOf(docA));
        SettleLifetimeDispatcher();
        Check("window lifetime: closing the tab itself unloads its chain", shared.SlotOf(trackA) == -1, $"A slot {shared.SlotOf(trackA)}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int SlotBefore, bool Closed) LoadChainInWindowThenClose(SongProject song)
    {
        var b = NewLifetimeWindow();
        var session = DocumentSession.FromProject(song, null);
        session.MarkClean();
        LtCall(b, "AdoptDroppedDocument", session, b.OpenDocuments.Count);
        var slot = AudioEngineClient.Instance.SlotOf(song.Tracks[0]);
        foreach (var document in b.OpenDocuments) document.MarkClean();
        b.Close();
        return (slot, !b.IsVisible);
    }
}
