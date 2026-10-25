using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: the Windows calls that dock a plug-in editor: the plain child window the engine process parents the editor to, finding the engine editor
//   popup of a window, placing that popup over the host area, showing and hiding it.
// Does not own: when any of it happens (FxChainWindow, FxEditorArea), the engine editor window itself.
// Tests: none headless (needs a real window and an engine); TabForge.Avalonia.Tests.FxEditorFitTests covers the arithmetic.
// Threads: UI thread only. Windows only; every call returns at once when not on Windows.
public static class PluginEditorWin32
{
    public const string EditorPopupClass = "TabForgePluginEditor";
    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000;
    private const uint GwOwner = 4, GwChild = 5;
    private const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpAsyncWindowPos = 0x4000;
    private const int ClassNameChars = 64, SwHide = 0, SwShowNoActivate = 8;

    [StructLayout(LayoutKind.Sequential)] public struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int ex, string cls, string? title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out NativeRect r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out NativeRect r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref NativePoint p);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr l);

    /// <summary>Creates the plain child window of <paramref name="parent"/> the engine parents an editor to; zero off Windows or on failure.</summary>
    public static IntPtr CreateArea(IntPtr parent) =>
        OperatingSystem.IsWindows() ? CreateWindowEx(0, "STATIC", null, WsChild | WsVisible | WsClipChildren, 0, 0, 100, 100, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) : IntPtr.Zero;

    public static void DestroyArea(IntPtr area) { if (OperatingSystem.IsWindows() && area != IntPtr.Zero) DestroyWindow(area); }

    /// <summary>The engine editor popup owned by <paramref name="owner"/> (the cached <paramref name="known"/> when it is still valid); zero when there is none.</summary>
    public static IntPtr FindEditorPopup(IntPtr owner, IntPtr known)
    {
        if (!OperatingSystem.IsWindows() || owner == IntPtr.Zero) return IntPtr.Zero;
        if (known != IntPtr.Zero && IsWindow(known) && GetWindow(known, GwOwner) == owner) return known;
        var found = IntPtr.Zero;
        var name = new StringBuilder(ClassNameChars);
        EnumWindows((h, _) =>
        {
            if (GetWindow(h, GwOwner) != owner) return true;
            name.Clear();
            GetClassName(h, name, name.Capacity);
            if (name.ToString() != EditorPopupClass) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Places the popup exactly over the host area, clipped to the owner client area. Asynchronous: the popup belongs to the engine process.</summary>
    public static void FollowArea(IntPtr owner, IntPtr area, IntPtr popup)
    {
        if (popup == IntPtr.Zero || !IsWindowVisible(popup)) return;
        if (!VisibleAreaRect(owner, area, out var r)) return;
        SetWindowPos(popup, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos);
    }

    /// <summary>Hides or shows the popup at once (without activating it); the engine's own placement agrees with it a tick later.</summary>
    public static void SetPopupVisible(IntPtr popup, bool visible)
    {
        if (OperatingSystem.IsWindows() && popup != IntPtr.Zero) ShowWindow(popup, visible ? SwShowNoActivate : SwHide);
    }

    /// <summary>The visible part of the host area in screen pixels, clipped to the owner client area; false when none of it shows.</summary>
    public static bool VisibleAreaRect(IntPtr owner, IntPtr area, out NativeRect rect)
    {
        rect = default;
        if (!OperatingSystem.IsWindows() || area == IntPtr.Zero || !IsWindowVisible(area) || !GetWindowRect(area, out rect)) return false;
        if (GetClientRect(owner, out var client))
        {
            var origin = new NativePoint();
            ClientToScreen(owner, ref origin);
            rect.Left = Math.Max(rect.Left, origin.X); rect.Top = Math.Max(rect.Top, origin.Y);
            rect.Right = Math.Min(rect.Right, origin.X + client.Right); rect.Bottom = Math.Min(rect.Bottom, origin.Y + client.Bottom);
        }
        return rect.Right > rect.Left && rect.Bottom > rect.Top;
    }

    /// <summary>Moves the view of the plug-in inside the editor popup to (<paramref name="x"/>, <paramref name="y"/>) pixels (scrolling an editor larger than its area).</summary>
    public static void MoveEditorView(IntPtr popup, int x, int y)
    {
        if (!OperatingSystem.IsWindows() || popup == IntPtr.Zero) return;
        var view = GetWindow(popup, GwChild);
        if (view != IntPtr.Zero) SetWindowPos(view, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos);
    }
}
