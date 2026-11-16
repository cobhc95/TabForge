// Owns: the master list of the FIXED key texts: keys that menus, tooltips and help show but that are not bindable on the Shortcuts page
//   (the piano roll's clipboard keys, Del, the arrow keys, "Esc twice" for Clear loop) and the mouse-modifier hints (Ctrl+wheel, Ctrl+click).
// Does not own: bindable commands (HotkeyCatalog: read those with HotkeyCatalog.DisplayAll / TooltipWithKey so a rebind shows everywhere).
// Tests: TabForge.Avalonia.Tests.HotkeyLiteralGuardTests (no gesture literal may appear outside this file and the HotkeyCatalog files).
namespace TabForge.Services;

/// <summary>Display texts of keys that are fixed (not on the Shortcuts page). A bindable command's key never goes here.</summary>
public static class FixedKeys
{
    // Clipboard and selection keys of views that read their own keys (the piano roll).
    public const string Cut = "Ctrl+X", Copy = "Ctrl+C", Paste = "Ctrl+V", SelectAll = "Ctrl+A";
    public const string Delete = "Del", Backspace = "Backspace", Escape = "Esc", Enter = "Enter";

    /// <summary>Clear loop: the first Esc clears the selection, the second the loop.</summary>
    public const string EscTwice = "Esc twice";

    // Arrow keys (transpose by a semitone, by an octave with Shift).
    public const string Up = "Up", Down = "Down", ShiftUp = "Shift+Up", ShiftDown = "Shift+Down";

    /// <summary>Search box of the guide window.</summary>
    public const string Find = "Ctrl+F";

    // Mouse-modifier hints for tooltips.
    public const string CtrlWheel = "Ctrl+wheel", CtrlClick = "Ctrl+click";
}
