using System.Linq;
using System.Windows;
using TabForge.Shell;

namespace TabForge;

// Owns: the rules of WindowFocusKeeper (TestWindowFocusKeeper): when a TabForge window closes while it had focus, the owner is activated,
//   else the most recently active TabForge window; nothing is activated when the user had already switched to another program, when the
//   candidate is minimised, or during app exit. Real windows are closed; the host seam fakes the foreground and records the activation.
// Does not own: the native focus handover itself (checked by hand on a desktop, see docs/DEBUG_SYMPTOMS.md).
public static partial class SelfTest
{
    /// <summary>A fake host: the test decides which window is the foreground one, and the posted work runs when the test says so.</summary>
    private sealed class FocusTestHost : IWindowFocusHost
    {
        public readonly List<Window> Open = new();
        public readonly List<Window> Activated = new();
        public readonly List<Action> Posted = new();
        public Window? Main;
        public bool ShuttingDown;
        public bool Foreground = true;

        public IEnumerable<Window> OpenWindows => Open.Where(w => w.IsVisible);
        Window? IWindowFocusHost.MainWindow => Main;
        bool IWindowFocusHost.ShuttingDown => ShuttingDown;
        bool IWindowFocusHost.ForegroundIsTabForge => Foreground;
        bool IWindowFocusHost.IsEnabled(Window window) => true;
        void IWindowFocusHost.Activate(Window window) => Activated.Add(window);
        void IWindowFocusHost.Post(Action action) => Posted.Add(action);

        /// <summary>Runs the posted work after the test has set the foreground window (as Windows does once a window is gone).</summary>
        public void RunPosted()
        {
            var work = Posted.ToList();
            Posted.Clear();
            foreach (var action in work) action();
        }
    }

    private static void TestWindowFocusKeeper()
    {
        // Owner alive: the tool window opened from the other tool window gets focus back.
        {
            var host = new FocusTestHost();
            var keeper = new WindowFocusKeeper(host);
            var main = FocusTestWindow(host, keeper, null, "main"); host.Main = main;
            var tool = FocusTestWindow(host, keeper, main, "tool");
            var child = FocusTestWindow(host, keeper, tool, "child");
            child.Close();
            host.Foreground = false;   // Windows gave focus to another program
            host.RunPosted();
            Check("focus: owner alive gets focus back", host.Activated.Count == 1 && ReferenceEquals(host.Activated[0], tool), host.Activated.Count.ToString());
            CloseFocusWindows(host);
        }

        // Owner gone: the most recently active other TabForge window gets it.
        {
            var host = new FocusTestHost();
            var keeper = new WindowFocusKeeper(host);
            var main = FocusTestWindow(host, keeper, null, "main"); host.Main = main;
            var owner = FocusTestWindow(host, keeper, main, "owner");
            var child = FocusTestWindow(host, keeper, owner, "child");
            var other = FocusTestWindow(host, keeper, main, "other");
            keeper.NoteActive(main);
            keeper.NoteActive(other);
            owner.Hide();   // not visible any more; hidden rather than closed so the owned child stays open for its own close
            child.Close();
            host.Foreground = false;
            host.RunPosted();
            Check("focus: owner gone gives focus to the most recent TabForge window", host.Activated.Count == 1 && ReferenceEquals(host.Activated[0], other), host.Activated.Count.ToString());
            CloseFocusWindows(host);
        }

        // The user had already switched to another program before the close: nothing is activated.
        {
            var host = new FocusTestHost();
            var keeper = new WindowFocusKeeper(host);
            var main = FocusTestWindow(host, keeper, null, "main"); host.Main = main;
            var tool = FocusTestWindow(host, keeper, main, "tool");
            host.Foreground = false;
            tool.Close();
            host.RunPosted();
            Check("focus: user switched away before the close leaves focus alone", host.Activated.Count == 0, host.Activated.Count.ToString());
            CloseFocusWindows(host);
        }

        // A minimised owner is skipped: the next candidate gets focus.
        {
            var host = new FocusTestHost();
            var keeper = new WindowFocusKeeper(host);
            var main = FocusTestWindow(host, keeper, null, "main"); host.Main = main;
            var other = FocusTestWindow(host, keeper, null, "other");   // not owned by main: an owner's minimise also minimises its owned windows
            var child = FocusTestWindow(host, keeper, main, "child");
            keeper.NoteActive(main);
            keeper.NoteActive(other);
            main.WindowState = WindowState.Minimized;
            child.Close();
            host.Foreground = false;
            host.RunPosted();
            Check("focus: minimised owner is skipped", host.Activated.Count == 1 && ReferenceEquals(host.Activated[0], other), host.Activated.Count.ToString());
            CloseFocusWindows(host);
        }

        // During app exit nothing is activated.
        {
            var host = new FocusTestHost();
            var keeper = new WindowFocusKeeper(host);
            var main = FocusTestWindow(host, keeper, null, "main"); host.Main = main;
            var tool = FocusTestWindow(host, keeper, main, "tool");
            host.ShuttingDown = true;
            tool.Close();
            host.Foreground = false;
            host.RunPosted();
            Check("focus: app exit activates nothing", host.Activated.Count == 0, host.Activated.Count.ToString());
            CloseFocusWindows(host);
        }
    }

    private static Window FocusTestWindow(FocusTestHost host, WindowFocusKeeper keeper, Window? owner, string title)
    {
        var window = new Window { Title = title, Width = 300, Height = 200, Owner = owner, ShowInTaskbar = false };
        ShowTestWindow(window);
        if (!IxPumpUntil(() => window.IsVisible, 5000)) throw new InvalidOperationException($"focus test window {title} did not show within 5 s");
        host.Open.Add(window);
        keeper.Track(window);
        return window;
    }

    private static void CloseFocusWindows(FocusTestHost host)
    {
        foreach (var window in host.Open.AsEnumerable().Reverse().ToList())
        {
            window.WindowState = WindowState.Normal;
            try { window.Close(); } catch (InvalidOperationException) { }   // already gone with its owner
        }
        host.Open.Clear();
        host.Posted.Clear();
    }
}
