using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace TabForge.Shell;

/// <summary>
/// Screen/element coordinate conversion that never throws. WPF's PointToScreen/PointFromScreen throw when
/// a visual is not currently in a window (e.g. a tab or panel re-parented or closed in the middle of a
/// drag), which would crash drag-and-drop. Every drag and hit test goes through these instead.
/// </summary>
internal static class ScreenPoints
{
    public static bool IsOnScreen(Visual visual) => PresentationSource.FromVisual(visual) is not null;

    public static bool TryToScreen(Visual visual, Point local, out Point screen)
    {
        screen = default;
        if (!IsOnScreen(visual)) return false;
        try { screen = visual.PointToScreen(local); return true; }
        catch (InvalidOperationException) { return false; } // detached between the check and the call // Not logged: pointer or hit-test path: no logging per pointer move
    }

    public static bool TryFromScreen(Visual visual, Point screen, out Point local)
    {
        local = default;
        if (!IsOnScreen(visual)) return false;
        try { local = visual.PointFromScreen(screen); return true; }
        catch (InvalidOperationException) { return false; } // Not logged: pointer or hit-test path: no logging per pointer move
    }

    /// <summary>The element's bounds on screen, or <see cref="Rect.Empty"/> when it is not shown.</summary>
    public static Rect ScreenRect(FrameworkElement element) =>
        TryToScreen(element, new Point(0, 0), out var a) &&
        TryToScreen(element, new Point(element.ActualWidth, element.ActualHeight), out var b)
            ? new Rect(a, b)
            : Rect.Empty;

    /// <summary>The mouse position in screen pixels, independent of any element (works mid-re-parenting).</summary>
    public static Point Cursor() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
