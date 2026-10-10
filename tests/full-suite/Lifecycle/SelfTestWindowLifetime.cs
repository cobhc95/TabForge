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

/// <summary>
/// Lifetime of REAL <see cref="MainWindow"/> instances. Window A stays open while window B is created and closed;
/// everything B attached to a longer-lived object (static events and callbacks, the shared audio engine client, the shared settings store,
/// registries, queued dispatcher work) must be gone when B has really closed, B and its closed documents must be collectable, a cancelled
/// close must leave B working (wired exactly once), a tab moved to another window keeps its document, undo history and playback, and the
/// Preferences actions reach the window that opened them.
/// Minimal seams only: a scratch settings store and profile (no user file is read or written), the audio engine client is a "running" fake
/// (no process, no device), modal dialogs go to <see cref="DialogHost.Capture"/>, the automatic update check is already off in --selftest
/// runs, and the production subscriptions are exercised as they are (windows are built, shown and closed through the normal paths).
/// </summary>
public static partial class SelfTest
{
    // Registered in SelfTest.Run as GuardGroup("window-lifetime", TestWindowLifetime) (group definition and minimum: SelfTestRequirements.cs, area: AreaOf).
    private const int WindowLifetimeCycles = 24;
    private const int WindowLifetimeWarmUp = 3;
    private const string LifetimeUncFile = @"\\tf-lifetime-test-server\share\take.wav";

    private static void TestWindowLifetime() => RunInWindowFixture((a, context) =>
    {
        LifetimeFixtureCleanupOnBodyFailureCase(a);
        RunLifetimeCase("owned subscriptions", LifetimeOwnedSubscriptionsCase);
        RunLifetimeCase("repeated create/close", () => LifetimeRepeatedCloseCase(a, context));
        RunLifetimeCase("cancelled close", () => LifetimeCancelledCloseCase(a, context));
        RunLifetimeCase("queued work after close", () => LifetimeQueuedWorkCase(a, context));
        RunLifetimeCase("tab transfer during playback", () => LifetimeTabTransferCase(context));
        RunLifetimeCase("sole-tab drag: window moves, drop merges", LifetimeSoleTabDragCase);
        RunLifetimeCase("tab transfer: tear-off, source closes first", () => TearOffCloseSourceFirstCase(context));
        RunLifetimeCase("tab transfer: tear-off, target closes first", () => TearOffCloseTargetFirstCase(context));
        RunLifetimeCase("tab transfer: rollback shows the song again", MergeRollbackShowsSongAgainCase);
        RunLifetimeCase("engine sync: one plug-in crash syncs only the windows using it", PluginCrashSyncsOnlyUsingWindowsCase);
        RunLifetimeCase("tab transfer: attach target cleared on close", AttachTargetClearedOnCloseCase);
        RunLifetimeCase("tab transfer: merge releases the source", MergeIntoOtherWindowReleasesSourceCase);
        RunLifetimeCase("tab transfer: engine follows the document", EngineSyncFollowsMovedDocumentCase);
        RunLifetimeCase("Preferences from two windows", () => LifetimePreferencesCase(a, context));
        RunLifetimeCase("closed window's engine chains", () => LifetimeEngineChainsCase(a, context));
        RunLifetimeCase("engine hooks released on close", EngineHooksReleasedOnCloseCase);
        RunLifetimeCase("engine restart syncs every window", EngineRestartResyncsEveryWindowCase);
        RunLifetimeCase("playback tick allocation budget", PlaybackTickAllocationBudgetCase);
        RunLifetimeCase("app options: no shared default instance", AppOptionsHaveNoSharedDefaultCase);
        RunLifetimeCase("app options: a settings change reaches every open song", AppOptionsReachEverySongCase);
        RunLifetimeCase("playback events detached on tab switch and close", PlaybackEventsDetachedOnTabSwitchAndCloseCase);
        RunLifetimeCase("closure (c): another window's close keeps this window's approval and playback", () => ClosureOtherWindowKeepsApprovalAndPlaybackCase(a));
        RunLifetimeCase("pending metronome save on close", () => LifetimePendingMetronomeSaveCase(context));
        RunLifetimeCase("closed window with live automation peers", () => LifetimeAutomationPeerCase());
        RunLifetimeCase("autosave clock", () => LifetimeAutosaveCase(context));
        RunLifetimeCase("update check", LifetimeUpdateCheckCase);
        RunLifetimeCase("notice bar order", LifetimeNoticeBarOrderCase);
        RunLifetimeCase("autosave controller on its own", AutosaveControllerAloneCase);
        RunLifetimeCase("update check controller on its own", UpdateCheckControllerAloneCase);
        RunLifetimeCase("approval controller on its own", ApprovalControllerAloneCase);
        RunLifetimeCase("plug-in review window", () => LifetimePluginReviewCase(context));
    });

    private static void LifetimeFixtureCleanupOnBodyFailureCase(MainWindow outerWindow)
    {
        MainWindow? fixtureWindow = null;
        MainWindow? extraWindow = null;
        var sentinel = new Exception("sentinel for fixture cleanup");
        try
        {
            RunInWindowFixture((a, _) =>
            {
                fixtureWindow = a;
                extraWindow = NewLifetimeWindow();
                throw sentinel;
            });
        }
        catch (Exception ex) when (ReferenceEquals(ex, sentinel)) { }

        var windows = AllMainWindows();
        Check("window lifetime fixture cleanup runs when its body throws",
            fixtureWindow is { IsVisible: false } && !windows.Contains(fixtureWindow),
            $"fixture window visible {fixtureWindow?.IsVisible}, still registered {fixtureWindow is not null && windows.Contains(fixtureWindow)}");
        Check("window lifetime fixture cleanup closes windows created by a throwing body",
            extraWindow is { IsVisible: false } && !windows.Contains(extraWindow),
            $"extra window visible {extraWindow?.IsVisible}, still registered {extraWindow is not null && windows.Contains(extraWindow)}");
        Check("window lifetime fixture cleanup preserves its enclosing fixture",
            outerWindow.IsVisible && windows.Contains(outerWindow),
            $"outer window visible {outerWindow.IsVisible}, still registered {windows.Contains(outerWindow)}");
    }

    /// <summary>The seams every real-window test needs (scratch settings store and profile, fake engine client, captured modal dialogs, no UI Automation work), with window A open.</summary>
    private static void RunInWindowFixture(Action<MainWindow, LifetimeContext> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-window-lifetime-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(folder);
        var previousProfile = UserPaths.ProfileRoot;
        var previousPlayAll = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        IDisposable? alive = null;
        try { alive = KeepAlive(); } catch (InvalidOperationException) { }
        var shared = AudioEngineClient.Instance;
        var fakeEngine = !shared.IsRunning;
        var captured = new List<Window>();
        var failures = new List<string>();
        var previousCapture = DialogHost.Capture;
        var previousListener = TabEditorControl.AutomationListenerOverride;
        var (structureBuilds, announceBuilds) = (TabEditorControl.StructureBuilds, TabEditorControl.AnnounceBuilds);   // global counters other tests assert on
        void OnUnhandled(object? s, DispatcherUnhandledExceptionEventArgs e) { failures.Add(e.Exception.GetType().Name + ": " + e.Exception.Message + " @ " + string.Join(" < ", (e.Exception.StackTrace ?? "").Split((char)10).Take(3).Select(x => x.Trim()))); e.Handled = true; }
        MainWindow? a = null;
        var windowsBefore = AllMainWindows();
        AppSettingsStore? store = null;
        IDisposable? storeScope = null;
        try
        {
            UserPaths.SetProfile(folder);
            store = AppSettingsStore.Open(Path.Combine(folder, "settings.json"), TimeSpan.FromMinutes(30));   // never written during the test
            storeScope = AppSettingsStore.OverrideSharedForTest(store);
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = false;
            if (fakeEngine) shared.AttachFakeForTest();
            DialogHost.Capture = dialog => { captured.Add(dialog); return false; };   // every modal dialog is "cancelled"; the test inspects what was built
            TabEditorControl.AutomationListenerOverride = false;   // no UI Automation client work in these windows (and no global build counters touched)
            Dispatcher.CurrentDispatcher.UnhandledException += OnUnhandled;

            var before = LifetimeSnapshot(store);
            Log.Add($"  info  window lifetime: application shutting down = {typeof(Application).GetProperty("IsShuttingDown", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)}, shutdown mode {Application.Current.ShutdownMode}, windows {Application.Current.Windows.Count}");
            a = NewLifetimeWindow();
            Log.Add($"  info  window lifetime: after window A: shutting down = {typeof(Application).GetProperty("IsShuttingDown", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)}, windows {Application.Current.Windows.Count}");
            var context = new LifetimeContext(store, captured, failures, before);
            SettleLifetimeDispatcher();

            body(a, context);
        }
        finally
        {
            Dispatcher.CurrentDispatcher.UnhandledException -= OnUnhandled;
            var closeFailures = new List<string>();
            try { a?.Close(); } catch (Exception ex) { closeFailures.Add($"window A close threw ({ex.GetType().Name}: {ex.Message})"); }
            closeFailures.AddRange(CloseWindowsOpenedSince(windowsBefore));
            DialogHost.Capture = previousCapture;
            TabEditorControl.AutomationListenerOverride = previousListener;
            // A UI Automation client on the machine can walk these windows' editor peers; that is not what the structure tests count.
            (TabEditorControl.StructureBuilds, TabEditorControl.AnnounceBuilds) = (structureBuilds, announceBuilds);
            foreach (var dialog in captured)
            {
                try { dialog.Close(); }
                catch (Exception ex) { closeFailures.Add($"captured {dialog.GetType().Name} dialog close threw ({ex.GetType().Name}: {ex.Message})"); }
            }
            Check("window lifetime fixture closes every window it created", closeFailures.Count == 0, string.Join("; ", closeFailures));
            if (fakeEngine) shared.Stop();
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previousPlayAll;
            storeScope?.Dispose();
            UserPaths.SetProfile(previousProfile);
            try { alive?.Dispose(); } catch (InvalidOperationException) { }
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static HashSet<MainWindow> AllMainWindows() => Application.Current?.Windows.OfType<MainWindow>().ToHashSet() ?? new HashSet<MainWindow>();

    /// <summary>Closes every main window opened since <paramref name="before"/>; their documents are marked clean first so no save prompt shows.</summary>
    private static string[] CloseWindowsOpenedSince(HashSet<MainWindow> before)
    {
        var failures = new List<string>();
        var previousMessage = DialogHost.MessageCapture;
        DialogHost.MessageCapture = (_, _) => { };
        try
        {
            foreach (var w in Application.Current?.Windows.OfType<MainWindow>().Where(w => !before.Contains(w)).ToList() ?? new List<MainWindow>())
            {
                try { foreach (var session in w.OpenDocuments.ToList()) session.MarkClean(); }
                catch (Exception ex) { failures.Add($"{w.Title}: could not clear documents before close ({ex.GetType().Name}: {ex.Message})"); }
                try { w.Close(); }
                catch (Exception ex) { failures.Add($"{w.Title}: close threw ({ex.GetType().Name}: {ex.Message})"); }
            }
        }
        finally { DialogHost.MessageCapture = previousMessage; }
        try { SettleLifetimeDispatcher(); }
        catch (Exception ex) { failures.Add($"dispatcher did not settle after window cleanup ({ex.GetType().Name}: {ex.Message})"); }
        var leaked = Application.Current?.Windows.OfType<MainWindow>().Where(w => !before.Contains(w)).ToArray() ?? Array.Empty<MainWindow>();
        failures.AddRange(leaked.Select(w => $"{w.Title}: window remains in Application.Windows after close"));
        return failures.ToArray();
    }

    // A metronome change waits 280 ms for its debounced save; closing the window inside that wait still saves it (the close stops the timer).
    private static void LifetimePendingMetronomeSaveCase(LifetimeContext context)
    {
        var b = NewLifetimeWindow();
        var saves = 0;
        void OnChanged(object? source) { if (ReferenceEquals(source, b)) saves++; }
        context.Store.Changed += OnChanged;
        try
        {
            LtCall(b, "QueueMetronomeSettingsSave");
            var pending = LtField<TabForge.Controllers.TransportControlsController>(b, "_transport")!.SavePending;
            b.Close();
            Check("window lifetime: a metronome change still waiting for its debounced save is saved when the window closes",
                pending && saves == 1, $"timer pending {pending}, saves from the window {saves}");
        }
        finally { context.Store.Changed -= OnChanged; }
    }

    private sealed record LifetimeContext(AppSettingsStore Store, List<Window> Captured, List<string> Failures, SortedDictionary<string, int> Empty);

    private static void RunLifetimeCase(string name, Action body)
    {
        var windowsBefore = AllMainWindows();
        try { body(); }
        catch (Exception ex) { Check($"window lifetime: {name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message} at {string.Join(" <- ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()))}"); }
        finally
        {
            var closeFailures = CloseWindowsOpenedSince(windowsBefore);
            Check($"window lifetime: {name} closes every window it created", closeFailures.Length == 0, string.Join("; ", closeFailures));
        }
    }

    // ---------- helpers ----------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MainWindow NewLifetimeWindow()
    {
        var window = new MainWindow(AudioEngineClient.Instance);
        window.WindowState = WindowState.Normal;   // the main window starts maximised; a window shown without activation cannot be (as for a torn-off tab's window)
        window.Width = 1000; window.Height = 700;
        ShowTestWindow(window);
        if (!IxPumpUntil(() => window.IsLoaded && PresentationSource.FromVisual(window) is not null, 5000))
            throw new InvalidOperationException("test window did not become ready within 5 s");
        return window;
    }

    private static void SettleLifetimeDispatcher()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        for (var i = 0; i < 4; i++) dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static object? LtCall(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .First(x => x.Name == method && x.GetParameters().Length == args.Length);
        try { return m.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static T? LtField<T>(object target, string name) =>
        (T?)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target);

    /// <summary>The window's instrument panel controller (it holds the view choice).</summary>
    private static object LtPane(object window) =>
        window.GetType().GetProperty("InstrumentPane", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    /// <summary>The window's tool actions (the palette tools that edit through a dialog or a bar property).</summary>
    private static object LtTools(object window) =>
        window.GetType().GetProperty("ToolActions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void LtSet(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    private static int StaticEventCount(Type type, string name) =>
        (type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;

    private static int InstanceEventCount(object target, string name) =>
        (target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) as Delegate)?.GetInvocationList().Length ?? 0;

    private static int StaticCollectionCount(Type type, string name) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) switch
        {
            System.Collections.ICollection c => c.Count,
            _ => -1,
        };

    /// <summary>What longer-lived objects currently hold about windows: handler counts of the static and shared events, registry sizes, open windows.</summary>
    private static SortedDictionary<string, int> LifetimeSnapshot(AppSettingsStore store)
    {
        var s = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["MediaAccess.Changed handlers"] = StaticEventCount(typeof(MediaAccess), "Changed"),
            ["MediaAccess.Resolved handlers"] = StaticEventCount(typeof(MediaAccess), "Resolved"),
            ["WaveformCache.Ready handlers"] = StaticEventCount(typeof(WaveformCache), "Ready"),
            ["PlaybackEngine.LoopCompleted handlers"] = StaticEventCount(typeof(PlaybackEngine), "LoopCompleted"),
            ["settings store Changed handlers"] = InstanceEventCount(store, "Changed"),
            ["settings store SaveFailed handlers"] = InstanceEventCount(store, "SaveFailed"),
            ["TabWindowRegistry entries"] = StaticCollectionCount(typeof(TabWindowRegistry), "Windows"),
            ["AutosaveRegistry documents"] = StaticCollectionCount(typeof(AutosaveRegistry), "States"),
            ["Application.Windows"] = Application.Current.Windows.Count,
        };
        var engine = AudioEngineClient.Instance;
        foreach (var name in new[] { "StatusChanged", "ChainLoaded", "ChainAcknowledged", "PluginCrashed", "PluginFailed", "PluginMisbehaved", "DeviceError", "PluginSlow", "PluginEdited", "Recorded", "InputError", "RecordingLoss" })
            s[$"audio engine {name} handlers"] = InstanceEventCount(engine, name);
        return s;
    }

    private static string LifetimeDiff(SortedDictionary<string, int> expected, SortedDictionary<string, int> actual)
    {
        var parts = new List<string>();
        foreach (var (key, value) in expected)
            if (!actual.TryGetValue(key, out var now) || now != value) parts.Add($"{key}: {value} -> {(actual.TryGetValue(key, out var n) ? n : -1)}");
        return parts.Count == 0 ? "" : string.Join("; ", parts);
    }

    private static int LifetimeAlive(List<WeakReference> references) => references.Count(r => r.IsAlive);

    private static void CollectUntilStable(Func<int> alive)
    {
        for (var round = 0; round < 6; round++)
        {
            SettleLifetimeDispatcher();
            CollectFully();
            if (alive() == 0) return;
        }
    }

    private static SongProject LifetimePluginSong(string name)
    {
        var song = TemplateFactory.Blank();
        song.Tracks[0].SoundSource = SoundSources.Plugins;
        song.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = name, Path = $@"C:\NoSuch\{name}.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
        return song;
    }

    /// <summary>A clip on a network location nobody approved: the media notice bar of a window shows it. Nothing on that path is ever touched.</summary>
    private static void AddUnapprovedClip(MainWindow window, int document = 0)
    {
        var session = window.OpenDocuments[document];
        session.Project.Tracks[0].AudioClips.Add(new AudioClip { File = LifetimeUncFile, Name = "take", StartSec = 0, SourceLengthSec = 1, FileLengthSec = 1 });
        session.MarkClean();   // adding the clip must not make the window ask to save on close
    }

    // ---------- the owned-subscription helper itself ----------

    private static void LifetimeOwnedSubscriptionsCase()
    {
        var owned = new OwnedSubscriptions();
        var log = new List<string>();
        owned.Add(() => log.Add("first"));
        owned.Attach(() => log.Add("attach"), () => log.Add("detach"));
        var countBefore = owned.Count;
        owned.Dispose();
        owned.Dispose();   // again: nothing runs twice
        owned.Add(() => log.Add("late"));   // after disposal: undone at once, never kept
        Check("window lifetime: owned subscriptions run each detach exactly once, newest first, and a second dispose does nothing",
            countBefore == 2 && string.Join(",", log) == "attach,detach,first,late" && owned.IsDisposed && owned.Count == 0, $"count {countBefore}, log {string.Join(",", log)}");
    }

    // ---------- case 1: A stays open, B is created and closed many times ----------

    // Everything one cycle creates lives in this frame, so no local keeps a closed window reachable when the collection runs.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateUseAndCloseWindow(List<WeakReference> windows, List<WeakReference> sessions, List<WeakReference> projects)
    {
        var b = NewLifetimeWindow();
        windows.Add(new WeakReference(b));
        LtCall(b, "NewTab");                         // a second tab
        LtCall(b, "ActivateTabAt", 0);
        AddUnapprovedClip(b);                        // the media notice bar gets built
        LtCall(b, "UpdateMediaApprovalBar");
        foreach (var session in b.OpenDocuments) { sessions.Add(new WeakReference(session)); projects.Add(new WeakReference(session.Project)); }
        b.UpdateLayout();
        b.Close();
    }

    // ---------- an external UI Automation client keeps peers of a closed window's controls alive ----------

    /// <summary>
    /// A UI Automation client (a screen reader, an inspector) holds ElementProxy objects that reference the controls' automation peers, so a peer can outlive
    /// the window. The peer reaches its control; the control's event handlers must not reach the closed window (and through it the song).
    /// Simulated here by creating a peer for every element of the window (and the editor's bar peers) and keeping them alive in the test.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<System.Windows.Automation.Peers.AutomationPeer> CloseWindowHoldingPeers(out WeakReference window, out WeakReference session, out WeakReference project)
    {
        var b = NewLifetimeWindow();
        window = new WeakReference(b);
        LtCall(b, "NewTab");
        LtCall(b, "ActivateTabAt", 0);
        var doc = b.OpenDocuments[0];
        session = new WeakReference(doc); project = new WeakReference(doc.Project);
        b.UpdateLayout();
        var peers = new List<System.Windows.Automation.Peers.AutomationPeer>();
        void Walk(DependencyObject node)
        {
            if (node is UIElement element && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(element) is { } peer)
            {
                peers.Add(peer);
                // A client asks every peer for its children: items controls answer with item peers that hold the data items (tab models, documents).
                var pending = new Queue<System.Windows.Automation.Peers.AutomationPeer>(); pending.Enqueue(peer);
                for (var guard = 0; pending.Count > 0 && guard < 400; guard++)
                    foreach (var child in pending.Dequeue().GetChildren() ?? new List<System.Windows.Automation.Peers.AutomationPeer>())
                        if (!peers.Contains(child)) { peers.Add(child); pending.Enqueue(child); }
            }
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
        }
        Walk(b);
        b.Close();
        return peers;
    }

    /// <summary>Failure diagnostics: the shortest field path from the held peers to <paramref name="target"/> (breadth-first over instance fields and arrays).</summary>
    private static string ReferencePathFrom(IEnumerable<object> roots, object target)
    {
        var parent = new Dictionary<object, (object? From, string Via)>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>();
        foreach (var r in roots) if (parent.TryAdd(r, (null, "peer"))) queue.Enqueue(r);
        var budget = 3_000_000;
        while (queue.Count > 0 && budget-- > 0)
        {
            var o = queue.Dequeue();
            if (ReferenceEquals(o, target))
            {
                var path = new List<string>();
                for (object? n = o; n is not null; n = parent[n].From) path.Add($"{n.GetType().Name}[{parent[n].Via}]");
                path.Reverse();
                return " | path: " + string.Join(" -> ", path);
            }
            var t = o.GetType();
            if (t.IsPrimitive || t == typeof(string) || o is Type || o is System.Reflection.MemberInfo) continue;
            if (o is Array array)
            {
                if (t.GetElementType()!.IsValueType) continue;
                foreach (var item in array) if (item is not null && parent.TryAdd(item, (o, "[]"))) queue.Enqueue(item);
                continue;
            }
            for (var c = t; c is not null; c = c.BaseType)
                foreach (var f in c.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPointer || (f.FieldType.IsValueType && f.FieldType.IsPrimitive)) continue;
                    object? v; try { v = f.GetValue(o); } catch (Exception) { continue; }
                    if (v is not null && !v.GetType().IsPrimitive && parent.TryAdd(v, (o, f.Name))) queue.Enqueue(v);
                }
        }
        return " | no path found" + (budget <= 0 ? " (search budget used up)" : "");
    }

    private static void LifetimeAutomationPeerCase()
    {
        var peers = CloseWindowHoldingPeers(out var window, out var session, out var project);
        CollectUntilStable(() => (window.IsAlive ? 1 : 0) + (session.IsAlive ? 1 : 0) + (project.IsAlive ? 1 : 0));
        var held = peers.Count;
        var why = project.IsAlive ? ReferencePathFrom(peers, project.Target!) : "";
        Check("window lifetime: a closed window's song is collectable while a UI Automation client still holds peers of its controls (the window's own peer may keep the small window object)",
            !session.IsAlive && !project.IsAlive && held > 50,
            $"window alive {window.IsAlive}, document alive {session.IsAlive}, project alive {project.IsAlive}, peers held {held}{why}");
        GC.KeepAlive(peers);
    }
}
