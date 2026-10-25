using System.Text;
using FlaUI.Core.AutomationElements;

namespace ParityRecorder;

/// <summary>
/// Interaction scenarios of the main window that run in the off-screen capture session, through window messages and
/// UI Automation patterns only. Each scenario either records its observed result or states MANUAL with the reason.
/// Owns: the capture-session scenario list, their order and the scenarios.txt text, and the Tab-order walk shared with the live run.
/// Does not own: the hit-test grid (HitMap), the trees (TreeDump) or the real-input scenarios (LiveScenarios).
/// Order matters: the reads come first; the resize and the tool-window close change state and come last.
/// </summary>
internal static class Scenarios
{
    private const int TabSteps = 30;

    public static string RunMain(TabForgeProcess tf, AutomationElement main, IntPtr mainHwnd, IReadOnlyList<AutomationElement> windows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# off-screen capture scenarios: window messages and UIA patterns only (no real input).");
        sb.AppendLine("# the real-input scenarios of the on-screen run are in scenarios-live.txt.");

        Manual(sb, "S05", "Tab-key focus order (WM_KEYDOWN and WM_KEYUP with VK_TAB to the window)",
            "focus results differed between runs of the same build in the off-screen run, so no order is recorded; on the owner's hand-test list");
        CtrlWheelZoom(sb, tf, main, mainHwnd);
        Manual(sb, "S08", "Windows 11 Snap Layouts flyout over the maximise button",
            "needs a human to judge the hover flyout's feel");
        ResizeLimits(sb, main);
        CloseToolWindow(sb, tf, windows);
        return sb.ToString();
    }

    internal static void Manual(StringBuilder sb, string id, string what, string reason) =>
        sb.Append(id).Append(' ').Append(what).Append(": MANUAL: ").AppendLine(reason);

    /// <summary>
    /// S05: presses Tab <see cref="TabSteps"/> times with <paramref name="pressTab"/> and records the focused element after each press.
    /// When focus never reaches an element of the app, the scenario says so as MANUAL.
    /// </summary>
    internal static void TabOrder(StringBuilder sb, TabForgeProcess tf, string how, Action pressTab)
    {
        var lines = new StringBuilder();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var inside = 0;
        for (var i = 1; i <= TabSteps; i++)
        {
            pressTab();
            Thread.Sleep(150);
            var focused = tf.FocusedInside();
            if (focused is null)
            {
                lines.Append("  ").Append(i.ToString("D2")).AppendLine(" outside the app");
                continue;
            }
            inside++;
            var line = $"{ElementText.ControlTypeOf(focused)} id=\"{TreeDump.Clean(ElementText.IdOf(focused))}\" name=\"{TreeDump.Clean(ElementText.NameOf(focused))}\"";
            distinct.Add(line);
            lines.Append("  ").Append(i.ToString("D2")).Append(' ').AppendLine(line);
        }
        if (distinct.Count <= 1)
        {
            Manual(sb, "S05", $"Tab-key focus order ({how})",
                $"focus did not move across {TabSteps} presses (at most one element of the app was focused), so no order was recorded");
            return;
        }
        sb.Append("S05 Tab-key focus order, ").Append(TabSteps).Append(" steps (").Append(how).AppendLine("):");
        sb.Append(lines);
    }

    private static void SendTab(IntPtr hwnd)
    {
        Native.Send(hwnd, Native.WM_KEYDOWN, (IntPtr)Native.VK_TAB, (IntPtr)(0x00010001 | (Native.ScanTab << 16)), out _);
        Native.Send(hwnd, Native.WM_KEYUP, (IntPtr)Native.VK_TAB, (IntPtr)(unchecked((int)0xC0010001) | (Native.ScanTab << 16)), out _);
    }

    /// <summary>S06: Ctrl+wheel as a message with MK_CONTROL over the score; the main tree is diffed before and after.</summary>
    private static void CtrlWheelZoom(StringBuilder sb, TabForgeProcess tf, AutomationElement main, IntPtr hwnd)
    {
        var score = FindScore(main);
        if (score is null)
        {
            Manual(sb, "S06", "Ctrl+wheel zoom over the score", "no element with an AutomationId containing 'score' was found");
            return;
        }
        var before = TreeDump.Body(main, hwnd);
        var b = ElementText.BoundsOf(score);
        var (cx, cy) = (b.X + b.Width / 2, b.Y + b.Height / 2);
        Native.Send(hwnd, Native.WM_MOUSEWHEEL, (IntPtr)((120 << 16) | Native.MK_CONTROL), Native.Pack(cx, cy), out _);
        Thread.Sleep(500);
        var after = TreeDump.Body(main, hwnd);
        var changed = CountChangedLines(before, after);
        if (changed == 0)
        {
            Manual(sb, "S06", "Ctrl+wheel zoom over the score",
                "no change in the element tree after a posted MK_CONTROL wheel message; zoom may read the keyboard state, which a posted message does not set");
            return;
        }
        sb.Append("S06 Ctrl+wheel zoom over the score (WM_MOUSEWHEEL, delta 120, MK_CONTROL): ")
            .Append(changed).Append(" tree lines changed; score rect before=")
            .Append(b.X).Append(',').Append(b.Y).Append(',').Append(b.Width).Append(',').Append(b.Height).AppendLine(" (px)");
    }

    /// <summary>S10: TransformPattern.Resize to a size and to an impossible minimum; the window is restored to its first size.</summary>
    private static void ResizeLimits(StringBuilder sb, AutomationElement main)
    {
        if (!main.Patterns.Transform.IsSupported)
        {
            Manual(sb, "S10", "resize through TransformPattern (size limits)", "the main window does not expose TransformPattern");
            return;
        }
        var transform = main.Patterns.Transform.Pattern;
        var first = ElementText.BoundsOf(main);
        transform.Resize(900, 600);
        Thread.Sleep(400);
        var requested = ElementText.BoundsOf(main);
        transform.Resize(10, 10);
        Thread.Sleep(400);
        var minimum = ElementText.BoundsOf(main);
        transform.Resize(first.Width, first.Height);
        Thread.Sleep(400);
        var restored = ElementText.BoundsOf(main);
        sb.Append("S10 resize through TransformPattern (px): asked 900x600, got ").Append(requested.Width).Append('x').Append(requested.Height)
            .Append("; asked 10x10, got ").Append(minimum.Width).Append('x').Append(minimum.Height)
            .Append("; restored to ").Append(restored.Width).Append('x').Append(restored.Height)
            .Append(" (first ").Append(first.Width).Append('x').Append(first.Height).AppendLine(")");
    }

    /// <summary>S11: WM_SYSCOMMAND SC_CLOSE to the Mixer tool window; records whether it is gone afterwards.</summary>
    private static void CloseToolWindow(StringBuilder sb, TabForgeProcess tf, IReadOnlyList<AutomationElement> windows)
    {
        var mixer = windows.FirstOrDefault(w => ElementText.NameOf(w).Contains("Mixer", StringComparison.OrdinalIgnoreCase));
        if (mixer is null)
        {
            Manual(sb, "S11", "close a tool window with SC_CLOSE", "the Mixer tool window was not found");
            return;
        }
        Native.Send(ElementText.HandleOf(mixer), Native.WM_SYSCOMMAND, (IntPtr)Native.SC_CLOSE, IntPtr.Zero, out _);
        Thread.Sleep(800);
        var stillOpen = tf.Windows().Any(w => ElementText.NameOf(w).Contains("Mixer", StringComparison.OrdinalIgnoreCase));
        sb.Append("S11 close a tool window (WM_SYSCOMMAND SC_CLOSE, Mixer): closed=").AppendLine(stillOpen ? "n" : "y");
    }

    private static AutomationElement? FindScore(AutomationElement main)
    {
        try
        {
            return main.FindAllDescendants().FirstOrDefault(e =>
                ElementText.IdOf(e).Contains("score", StringComparison.OrdinalIgnoreCase) && ElementText.BoundsOf(e).Width > 0);
        }
        catch (Exception)
        {
            return null; // Not logged: a tree that cannot be read has no score to aim at; the caller records MANUAL.
        }
    }

    private static int CountChangedLines(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        var changed = Math.Abs(a.Length - b.Length);
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) changed++;
        return changed;
    }
}
