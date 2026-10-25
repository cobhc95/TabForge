using System.Drawing;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ParityRecorder;

/// <summary>
/// Scenarios that need real mouse and keyboard input, run on an on-screen TabForge the recorder started itself
/// (scratch profile). Only this process is driven; the owner's own TabForge is never touched.
/// Owns: the live scenario list, the caption-point search and the real-input steps. Does not own: the off-screen scenarios (Scenarios).
/// Each step waits a bounded time; the caller disposes the process, which ends it.
/// </summary>
internal static class LiveScenarios
{
    private const int SettleMs = 900;

    /// <summary>Runs the live scenarios in order. The hit-test map is written into <paramref name="files"/> first, while the window is still in its start state.</summary>
    public static string Run(TabForgeProcess tf, IDictionary<string, string> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# live scenarios: real mouse and keyboard input to an on-screen TabForge started by the recorder with a scratch profile.");
        sb.AppendLine("# the states are read from the window's own flags (IsZoomed, IsIconic); a MANUAL line names what a human must judge.");
        var hwnd = tf.MainHandle();
        var main = tf.Windows().FirstOrDefault(w => ElementText.HandleOf(w) == hwnd);
        if (main is null)
        {
            sb.AppendLine("MANUAL: the on-screen main window was not found; no live scenario ran.");
            return sb.ToString();
        }

        EnsureNormal(hwnd);
        WaitForStableRect(hwnd);
        if (Native.StateOf(hwnd) == "Normal") HitMap.Record(hwnd, files);
        else sb.AppendLine("MANUAL: the live window could not be restored to its normal state, so the hit-test map was not taken.");
        DoubleClickCaption(sb, hwnd);
        WinArrows(sb, hwnd);
        Activate(hwnd);
        DuplicateTab(sb);
        Scenarios.TabOrder(sb, tf, "real Tab key", () => Keyboard.Type(VirtualKeyShort.TAB));
        TabDrag(sb, main);
        ScreenEdgeContextMenu(sb, tf, main, hwnd);
        AltSpaceMenu(sb, tf, main);
        CtrlWheel(sb, main);
        return sb.ToString();
    }

    /// <summary>Setup for the tab scenarios: Ctrl+Shift+D duplicates the active tab, giving a second document without a dialog.</summary>
    private static void DuplicateTab(StringBuilder sb)
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, (VirtualKeyShort)0x44);
        Thread.Sleep(SettleMs);
    }

    /// <summary>S01 and S02: a real double-click on a point the window itself reports as HTCAPTION, then again to restore.</summary>
    private static void DoubleClickCaption(StringBuilder sb, IntPtr hwnd)
    {
        var start = Native.StateOf(hwnd);
        var point = TitleBarFreePoint(hwnd);
        if (point is null)
        {
            sb.AppendLine("S01 double-click title bar: MANUAL: no free title-bar point answered WM_NCHITTEST with HTCLIENT");
            return;
        }
        Mouse.DoubleClick(point.Value);
        Thread.Sleep(SettleMs);
        var maximised = Native.StateOf(hwnd);
        sb.Append("S01 double-click title bar (real input at a free title-bar point): ").Append(start).Append(" -> ").AppendLine(maximised);

        var again = TitleBarFreePoint(hwnd);
        if (again is null)
        {
            sb.AppendLine("S02 double-click title bar to restore: MANUAL: no free title-bar point after maximise");
            return;
        }
        Mouse.DoubleClick(again.Value);
        Thread.Sleep(SettleMs);
        sb.Append("S02 double-click title bar to restore: ").Append(maximised).Append(" -> ").AppendLine(Native.StateOf(hwnd));
    }

    /// <summary>S03: Win+Up, Win+Down, Win+Down with real keys; the state after each.</summary>
    private static void WinArrows(StringBuilder sb, IntPtr hwnd)
    {
        var states = new List<string> { Native.StateOf(hwnd) };
        foreach (var key in new[] { VirtualKeyShort.UP, VirtualKeyShort.DOWN, VirtualKeyShort.DOWN })
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, key);
            Thread.Sleep(SettleMs);
            states.Add(Native.StateOf(hwnd));
        }
        sb.Append("S03 Win+Up, Win+Down, Win+Down (real keys): ").AppendLine(string.Join(" -> ", states));
        // Leave the window restored so the rest of the run starts from a normal window.
        if (Native.StateOf(hwnd) != "Normal") Native.Send(hwnd, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_RESTORE, IntPtr.Zero, out _);
        Thread.Sleep(SettleMs);
    }

    /// <summary>S04: drag the first document tab onto the second with real pointer input; the tab order before and after.</summary>
    private static void TabDrag(StringBuilder sb, AutomationElement main)
    {
        var tabs = TabItems(main);
        if (tabs.Count < 2)
        {
            sb.Append("S04 tab drag: MANUAL: ").Append(tabs.Count).AppendLine(" document tab(s) in the live run; a drag needs two");
            return;
        }
        var before = string.Join(", ", tabs.Select(TitleOf));
        var from = Center(tabs[0]);
        var to = Center(tabs[1]);
        Mouse.MoveTo(from);
        Mouse.Down(MouseButton.Left);
        Mouse.MoveTo(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        Mouse.MoveTo(new Point(to.X + 4, to.Y));
        Thread.Sleep(200);
        Mouse.Up(MouseButton.Left);
        Thread.Sleep(SettleMs);
        var after = string.Join(", ", TabItems(main).Select(TitleOf));
        sb.Append("S04 tab drag (real pointer, first tab onto second): before [").Append(TreeDump.Clean(before))
            .Append("] after [").Append(TreeDump.Clean(after)).AppendLine("]");
    }

    /// <summary>S07: move the window to the right screen edge, right-click the score there, record whether the menu stays on screen.</summary>
    private static void ScreenEdgeContextMenu(StringBuilder sb, TabForgeProcess tf, AutomationElement main, IntPtr hwnd)
    {
        var original = ElementText.BoundsOf(main);
        var screenWidth = Native.GetSystemMetrics(Native.SM_CXSCREEN);
        if (!main.Patterns.Transform.IsSupported)
        {
            sb.AppendLine("S07 context menu at a screen edge: MANUAL: the main window does not expose TransformPattern to move it to the edge");
            return;
        }
        var transform = main.Patterns.Transform.Pattern;
        transform.Move(screenWidth - original.Width, original.Y);
        Thread.Sleep(SettleMs);
        var score = main.FindAllDescendants().FirstOrDefault(e => ElementText.IdOf(e).Contains("score", StringComparison.OrdinalIgnoreCase) && ElementText.BoundsOf(e).Width > 0);
        if (score is null)
        {
            sb.AppendLine("S07 context menu at a screen edge: MANUAL: no score element to right-click");
            transform.Move(original.X, original.Y);
            return;
        }
        var b = ElementText.BoundsOf(score);
        var point = new Point(Math.Min(b.Right - 2, screenWidth - 2), b.Y + b.Height / 2);
        Mouse.RightClick(point);
        Thread.Sleep(SettleMs);
        var menu = PopupMenus(tf, main).FirstOrDefault();
        if (menu is null)
        {
            sb.AppendLine("S07 right-click at the right screen edge: no menu popup was found");
        }
        else
        {
            var r = ElementText.BoundsOf(menu);
            var inside = r.Left >= 0 && r.Right <= screenWidth;
            sb.Append("S07 right-click at the right screen edge: menu items=").Append(MenuItemCount(menu))
                .Append(" menu fully on screen=").AppendLine(inside ? "y" : "n");
        }
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Thread.Sleep(400);
        transform.Move(original.X, original.Y);
        Thread.Sleep(SettleMs);
    }

    /// <summary>S09: Alt+Space with real keys; the system menu items that appear.</summary>
    private static void AltSpaceMenu(StringBuilder sb, TabForgeProcess tf, AutomationElement main)
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
        Thread.Sleep(SettleMs);
        var menu = PopupMenus(tf, main).FirstOrDefault();
        if (menu is null)
        {
            sb.AppendLine("S09 Alt+Space (real keys): no system menu popup appeared");
        }
        else
        {
            var names = menu.FindAllDescendants().Where(e => e.ControlType == ControlType.MenuItem)
                .Select(e => TreeDump.Clean(ElementText.NameOf(e)));
            sb.Append("S09 Alt+Space (real keys): system menu items=").AppendLine(string.Join(" | ", names));
        }
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Thread.Sleep(400);
    }

    /// <summary>S06: real Ctrl+wheel up over the score, then down; the zoom box text after each.</summary>
    private static void CtrlWheel(StringBuilder sb, AutomationElement main)
    {
        var score = main.FindAllDescendants().FirstOrDefault(e => ElementText.IdOf(e).Contains("score", StringComparison.OrdinalIgnoreCase) && ElementText.BoundsOf(e).Width > 0);
        var zoom = main.FindFirstDescendant(cf => cf.ByAutomationId("ZoomCombo"));
        if (score is null || zoom is null)
        {
            sb.AppendLine("S06 Ctrl+wheel zoom (real input): MANUAL: no score or zoom box element");
            return;
        }
        string Zoom()
        {
            try { return TreeDump.Clean(zoom.AsComboBox().Value); }
            catch (Exception) { return "n/a"; } // Not logged: a zoom box without a readable value is recorded as n/a.
        }
        var before = Zoom();
        Mouse.MoveTo(Center(score));
        Thread.Sleep(200);
        var states = new List<string> { before };
        foreach (var delta in new[] { 1.0, -1.0 })
        {
            Keyboard.Press(VirtualKeyShort.CONTROL);
            try { Mouse.Scroll(delta); Thread.Sleep(SettleMs); }
            finally { Keyboard.Release(VirtualKeyShort.CONTROL); }
            Thread.Sleep(300);
            states.Add(Zoom());
        }
        sb.Append("S06 Ctrl+wheel zoom (real input, up then down): ").AppendLine(string.Join(" -> ", states.Select(z => "[" + z + "]")));
    }

    /// <summary>Waits (bounded, about 10 s) until the window rectangle has not moved for about a second, so the map is taken from a settled window.</summary>
    private static void WaitForStableRect(IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var last);
        var stable = 0;
        for (var i = 0; i < 40 && stable < 3; i++)
        {
            Thread.Sleep(300);
            Native.GetWindowRect(hwnd, out var now);
            stable = now.Left == last.Left && now.Top == last.Top && now.Right == last.Right && now.Bottom == last.Bottom ? stable + 1 : 0;
            last = now;
        }
    }

    /// <summary>
    /// The first point of the title bar, in its right half (the tabs sit at the left), that the window answers with
    /// HTCLIENT. This app does not report HTCAPTION for its custom title bar; its double-click and drag are its own.
    /// </summary>
    private static Point? TitleBarFreePoint(IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var rect);
        var dpi = Native.GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        var start = rect.Left + (rect.Right - rect.Left) / 2;
        var end = rect.Right - (int)(200 * scale);
        for (var dy = 4; dy < 48 * scale; dy += 4)
        for (var x = start; x < end; x += 8)
        {
            var y = rect.Top + dy;
            Native.SetCursorPos(x, y);
            if (Native.Send(hwnd, Native.WM_NCHITTEST, IntPtr.Zero, Native.Pack(x, y), out var result)
                && unchecked((short)result.ToInt64()) == 1)
                return new Point(x, y);
        }
        return null;
    }

    /// <summary>The tab's title text (the item's own name is generic).</summary>
    private static string TitleOf(AutomationElement tab)
    {
        try
        {
            return TreeDump.Clean(tab.FindFirstDescendant(cf => cf.ByAutomationId("TabTitle"))?.Name);
        }
        catch (Exception)
        {
            return ""; // Not logged: a tab that vanished mid-read has no title in the record.
        }
    }

    /// <summary>The document tabs: the items of the tab strip list, left to right.</summary>
    private static List<AutomationElement> TabItems(AutomationElement main)
    {
        try
        {
            var strip = main.FindFirstDescendant(cf => cf.ByAutomationId("TabStrip"));
            return strip is null
                ? new List<AutomationElement>()
                : strip.FindAllChildren(cf => cf.ByControlType(ControlType.DataItem))
                    .Where(e => ElementText.BoundsOf(e).Width > 0)
                    .OrderBy(e => ElementText.BoundsOf(e).X).ToList();
        }
        catch (Exception)
        {
            return new List<AutomationElement>(); // Not logged: no tabs readable; the scenario records how many.
        }
    }

    /// <summary>Restores a maximised start state, so the caption and resize tests begin from a normal window.</summary>
    private static void EnsureNormal(IntPtr hwnd)
    {
        if (Native.StateOf(hwnd) == "Normal") return;
        Native.Send(hwnd, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_RESTORE, IntPtr.Zero, out _);
        Thread.Sleep(SettleMs);
    }

    /// <summary>A single click on the caption (no double-click, so no maximise) makes the window the active one for keyboard input.</summary>
    private static void Activate(IntPtr hwnd)
    {
        var point = TitleBarFreePoint(hwnd);
        if (point is null) return;
        Mouse.Click(point.Value);
        Thread.Sleep(SettleMs);
    }

    /// <summary>Menus of the app: popup windows (desktop children of its process) and menus inside its main window.</summary>
    private static List<AutomationElement> PopupMenus(TabForgeProcess tf, AutomationElement main)
    {
        try
        {
            var popups = tf.Automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(tf.Pid).And(cf.ByControlType(ControlType.Menu))).ToList();
            popups.AddRange(main.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)));
            return popups;
        }
        catch (Exception)
        {
            return new List<AutomationElement>(); // Not logged: an unreadable desktop is recorded as no popup found.
        }
    }

    private static int MenuItemCount(AutomationElement menu) =>
        menu.FindAllDescendants().Count(e => e.ControlType == ControlType.MenuItem);

    private static Point Center(AutomationElement e)
    {
        var b = ElementText.BoundsOf(e);
        return new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
    }
}
