using System.Text;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;

namespace ParityRecorder;

/// <summary>
/// Element tree of one window as text: ControlType, AutomationId, Name, IsEnabled, IsKeyboardFocusable and bounds
/// relative to the window's top-left. Volatile values are replaced with fixed tokens.
/// Owns: the line format and the volatile-value rules. Does not own: which windows are dumped (Recorder).
/// </summary>
internal static class TreeDump
{
    private const int MaxDepth = 40;
    private const int MaxNodes = 4000;

    private static readonly (Regex Pattern, string Token)[] Volatile =
    {
        (new Regex(@"[A-Za-z]:\\[^""'\r\n]*"), "<path>"),
        (new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b"), "<guid>"),
        (new Regex(@"\b\d{1,2}:\d{2}(:\d{2})?(\s*[AaPp][Mm])?\b"), "<time>"),
        (new Regex(@"\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b"), "<date>"),
        (new Regex(@"0x[0-9A-Fa-f]{4,}"), "<handle>"),
        (new Regex(@"\b\d{6,}\b"), "<num>"),
    };

    /// <summary>Whole file for one window: a header, the tree and the node count.</summary>
    public static string File(string role, AutomationElement window, IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var rect);
        var dpi = Native.GetDpiForWindow(hwnd);
        var sb = new StringBuilder();
        sb.Append("# TabForge element tree, role=").Append(role).AppendLine();
        sb.Append("# control view; rect = x,y,w,h relative to the window top-left in physical pixels at dpi=").Append(dpi).AppendLine();
        sb.Append("# window size=").Append(rect.Width).Append('x').Append(rect.Height).AppendLine();
        sb.Append("# stripped: paths, GUIDs, times, dates, handles, numbers of 6+ digits").AppendLine();
        var count = 0;
        Walk(window, 0, rect.Left, rect.Top, sb, ref count);
        sb.Append("# nodes=").Append(count).AppendLine();
        return sb.ToString();
    }

    /// <summary>The tree lines only, relative to the window origin; used to diff two states of one window.</summary>
    public static string Body(AutomationElement window, IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var rect);
        var sb = new StringBuilder();
        var count = 0;
        Walk(window, 0, rect.Left, rect.Top, sb, ref count);
        return sb.ToString();
    }

    /// <summary>Replaces volatile fragments with fixed tokens and removes line breaks and double quotes.</summary>
    public static string Clean(string? text)
    {
        var s = (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace('"', '\'');
        foreach (var (pattern, token) in Volatile) s = pattern.Replace(s, token);
        return s;
    }

    private static void Walk(AutomationElement element, int depth, int originX, int originY, StringBuilder sb, ref int count)
    {
        if (depth > MaxDepth || count >= MaxNodes) return;
        // Tooltips come and go with the pointer position, so they are not part of the recorded tree.
        if (IsToolTip(element) || (depth > 0 && ElementText.ChildrenOf(element).Any(IsToolTip))) return;
        count++;
        var bounds = ElementText.BoundsOf(element);
        sb.Append(' ', depth * 2)
            .Append(ElementText.ControlTypeOf(element))
            .Append(" id=\"").Append(Clean(ElementText.IdOf(element)))
            .Append("\" name=\"").Append(Clean(ElementText.NameOf(element)))
            .Append("\" enabled=").Append(Flag(ElementText.EnabledOf(element)))
            .Append(" focusable=").Append(Flag(ElementText.FocusableOf(element)))
            .Append(" rect=").Append(bounds.X - originX).Append(',').Append(bounds.Y - originY).Append(',')
            .Append(bounds.Width).Append(',').Append(bounds.Height)
            .AppendLine();
        foreach (var child in ElementText.ChildrenOf(element)) Walk(child, depth + 1, originX, originY, sb, ref count);
    }

    private static string Flag(bool value) => value ? "y" : "n";

    private static bool IsToolTip(AutomationElement e) => ElementText.ControlTypeOf(e) == "ToolTip";
}

/// <summary>Property reads that return a neutral value instead of throwing when an element vanishes mid-read.</summary>
internal static class ElementText
{
    public static string NameOf(AutomationElement e) => Read(() => e.Name ?? "", "");
    public static string IdOf(AutomationElement e) => Read(() => e.AutomationId ?? "", "");
    public static string ControlTypeOf(AutomationElement e) => Read(() => e.ControlType.ToString(), "?");
    public static bool EnabledOf(AutomationElement e) => Read(() => e.IsEnabled, false);
    public static bool FocusableOf(AutomationElement e) => Read(() => e.Properties.IsKeyboardFocusable.ValueOrDefault, false);
    public static System.Drawing.Rectangle BoundsOf(AutomationElement e) => Read(() => e.BoundingRectangle, System.Drawing.Rectangle.Empty);
    public static IReadOnlyList<AutomationElement> ChildrenOf(AutomationElement e) => Read(() => (IReadOnlyList<AutomationElement>)e.FindAllChildren(), Array.Empty<AutomationElement>());
    public static IntPtr HandleOf(AutomationElement e) => Read(() => (IntPtr)e.Properties.NativeWindowHandle.ValueOrDefault, IntPtr.Zero);

    private static T Read<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception)
        {
            return fallback; // Not logged: a property of an element that vanished is recorded as its neutral value.
        }
    }
}
