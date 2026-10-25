using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ParityRecorder;

/// <summary>
/// UI Automation helpers for the function scenarios: element lookup, driving controls through their patterns (invoke,
/// expand, toggle, select, range value), reading state, the windows of this instance, and dismissing a window a step
/// left open. No mouse or keyboard input is sent, no window is activated or focused, and no screen pixel is read.
/// Owns: the pattern helpers, the dismissal rules and the fingerprint format. Does not own: what each scenario checks (FunctionScenarios).
/// </summary>
internal sealed class UiProbe
{
    private const int SettleMs = 350;

    public UiProbe(TabForgeProcess tf, AutomationElement main, IntPtr mainHwnd)
    {
        Tf = tf;
        Main = main;
        MainHwnd = mainHwnd;
    }

    public TabForgeProcess Tf { get; }
    public AutomationElement Main { get; }
    public IntPtr MainHwnd { get; }
    public int Pid => Tf.Pid;

    /// <summary>What the dismissals of this step found and did, one line each; the step's record reports them.</summary>
    public List<string> Notes { get; } = new();

    /// <summary>Thrown when a step cannot be driven without input or a pattern; the scenario is recorded as MANUAL with this message.</summary>
    public sealed class Manual : Exception
    {
        public Manual(string message) : base(message) { }
    }

    public AutomationElement? Find(string id, AutomationElement? root = null)
    {
        try { return (root ?? Main).FindFirstDescendant(cf => cf.ByAutomationId(id)); }
        catch (Exception) { return null; } // Not logged: a missing element is recorded as <missing> by the caller.
    }

    /// <summary>The element's name, cleaned of volatile values; "&lt;missing&gt;" for null.</summary>
    public string NameOf(AutomationElement? e) => e is null ? "<missing>" : TreeDump.Clean(ElementText.NameOf(e));

    /// <summary>
    /// Drives a control through its own pattern: invoke, else expand, else toggle, else select. Expanding a menu item
    /// opens its popup; invoking it runs the command. A control with none of these patterns is MANUAL.
    /// </summary>
    public void Activate(AutomationElement e)
    {
        if (e.Patterns.Invoke.IsSupported) e.Patterns.Invoke.Pattern.Invoke();
        else if (e.Patterns.ExpandCollapse.IsSupported) e.Patterns.ExpandCollapse.Pattern.Expand();
        else if (e.Patterns.Toggle.IsSupported) e.Patterns.Toggle.Pattern.Toggle();
        else if (e.Patterns.SelectionItem.IsSupported) e.Patterns.SelectionItem.Pattern.Select();
        else throw new Manual($"\"{NameOf(e)}\" exposes no invoke, expand, toggle or select pattern");
        Thread.Sleep(SettleMs);
    }

    /// <summary>Collapses an expanded menu item (closes its popup); a no-op for an element that cannot collapse.</summary>
    public void Collapse(AutomationElement e)
    {
        if (!e.Patterns.ExpandCollapse.IsSupported) return;
        try { e.Patterns.ExpandCollapse.Pattern.Collapse(); }
        catch (Exception) { return; } // Not logged: an element that refuses to collapse is reported by the caller's popup check.
        Thread.Sleep(SettleMs);
    }

    public static bool IsExpanded(AutomationElement e) =>
        e.Patterns.ExpandCollapse.IsSupported && e.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value.ToString() == "Expanded";

    /// <summary>Sets a range (slider) value through its pattern.</summary>
    public void SetRange(AutomationElement e, double value)
    {
        if (!e.Patterns.RangeValue.IsSupported) throw new Manual("the slider exposes no range-value pattern");
        e.Patterns.RangeValue.Pattern.SetValue(value);
        Thread.Sleep(SettleMs);
    }

    /// <summary>Top-level windows of this instance now, by handle.</summary>
    public HashSet<IntPtr> WindowHandles() => Tf.Windows().Select(ElementText.HandleOf).ToHashSet();

    /// <summary>Real windows (ControlType Window) of this instance that are not in <paramref name="before"/>, waiting up to <paramref name="waitMs"/>.</summary>
    public List<AutomationElement> NewWindows(HashSet<IntPtr> before, int waitMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
        do
        {
            var fresh = Tf.Windows()
                .Where(w => !before.Contains(ElementText.HandleOf(w)) && ElementText.ControlTypeOf(w) == "Window")
                .ToList();
            if (fresh.Count > 0) return fresh;
            Thread.Sleep(200);
        } while (DateTime.UtcNow < deadline);
        return new List<AutomationElement>();
    }

    /// <summary>Whether a window with this handle is still open.</summary>
    public bool IsOpen(IntPtr hwnd) => Tf.Windows().Any(w => ElementText.HandleOf(w) == hwnd);

    /// <summary>
    /// Dismisses a window of this instance that a step opened or left open, without focus or input: its Cancel, No or Close
    /// button is invoked when it has one, otherwise the close command is posted to its own window. The window's title and
    /// text are noted first, so a prompt is on record. Returns whether the window is gone afterwards.
    /// </summary>
    public bool Dismiss(AutomationElement window, string reason)
    {
        var hwnd = ElementText.HandleOf(window);
        var title = NameOf(window);
        var text = string.Join(" | ", window.FindAllDescendants()
            .Select(e => TreeDump.Clean(ElementText.NameOf(e)))
            .Where(n => n.Length > 0 && n != title).Distinct().Take(12));
        var button = FindButton(window, "^(No|Cancel|Close)$");
        var how = "close command";
        if (button is not null)
        {
            how = $"\"{NameOf(button)}\" button";
            try { Activate(button); }
            catch (Exception) { button = null; } // Not logged: a button that cannot be invoked falls back to the close command below.
        }
        if (button is null) Native.Send(hwnd, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_CLOSE, IntPtr.Zero, out _);
        Thread.Sleep(SettleMs);
        var closed = !IsOpen(hwnd);
        Notes.Add($"{reason}: window \"{title}\" text=\"{text}\" dismissed with {how}, closed={(closed ? "y" : "n")}");
        return closed;
    }

    /// <summary>The first button under <paramref name="root"/> whose name matches the pattern, or null.</summary>
    public static AutomationElement? FindButton(AutomationElement root, string namePattern)
    {
        try
        {
            return root.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .FirstOrDefault(b => Regex.IsMatch(ElementText.NameOf(b), namePattern));
        }
        catch (Exception)
        {
            return null; // Not logged: a window whose buttons cannot be read is closed by the close command.
        }
    }

    /// <summary>The menu items of a popup (all depths), in order.</summary>
    public List<AutomationElement> MenuItemElements(AutomationElement popup) =>
        popup.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
            .Where(e => ElementText.NameOf(e).Length > 0).ToList();

    /// <summary>Popup menus of this instance: menu windows on the desktop and menus inside the main window (not the menu bar).</summary>
    public List<AutomationElement> Popups()
    {
        try
        {
            var popups = Tf.Automation.GetDesktop()
                .FindAllChildren(cf => cf.ByProcessId(Pid).And(cf.ByControlType(ControlType.Menu))).ToList();
            popups.AddRange(Main.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu))
                .Where(m => ElementText.IdOf(m) != "MainMenu"));
            return popups;
        }
        catch (Exception)
        {
            return new List<AutomationElement>(); // Not logged: an unreadable desktop is recorded as no popup.
        }
    }

    /// <summary>
    /// The menu items shown by this instance's windows, excluding the menu bar's own items. A popup menu is a window of
    /// its own, so the items of an expanded menu are found through the instance's windows.
    /// </summary>
    public List<AutomationElement> OpenMenuItems()
    {
        try
        {
            return Tf.Windows()
                .SelectMany(w => w.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)))
                .Where(e => ElementText.BoundsOf(e).Width > 0 && ElementText.NameOf(e) is not ("" or "System")
                    && !ElementText.IdOf(e).StartsWith("MainMenu.", StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception)
        {
            return new List<AutomationElement>(); // Not logged: an unreadable window list is recorded as no menu items.
        }
    }

    public static string ItemText(AutomationElement item) =>
        TreeDump.Clean(ElementText.NameOf(item)) + (ElementText.EnabledOf(item) ? "" : " (disabled)");

    /// <summary>The toggle state of an element ("On", "Off", "Indeterminate") or "n/a" when it has no toggle.</summary>
    public static string ToggleOf(AutomationElement e) =>
        e.Patterns.Toggle.IsSupported ? e.Patterns.Toggle.Pattern.ToggleState.Value.ToString() : "n/a";

    /// <summary>The value of a slider as text, or "n/a" when it is not a slider.</summary>
    public static string SliderValueOf(AutomationElement e)
    {
        try { return e.AsSlider().Value.ToString("0.###"); }
        catch (Exception) { return "n/a"; } // Not logged: the element is not a slider.
    }

    /// <summary>A short fingerprint of a text: stable across runs with the same content.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    /// <summary>Number of tracks shown in the arrangement (their Properties buttons), or -1 when the tree cannot be read.</summary>
    public int TrackCount()
    {
        try
        {
            return Main.FindAllDescendants().Count(e => Regex.IsMatch(ElementText.IdOf(e), @"^Arrangement\.Track\d+Properties$"));
        }
        catch (Exception)
        {
            return -1; // Not logged: an unreadable tree is recorded as -1 tracks.
        }
    }
}
