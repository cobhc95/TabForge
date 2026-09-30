using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// Where the pointer is, for drag handlers. Normally the real mouse position; self-tests set <see cref="Simulated"/> (a screen point)
/// and raise synthetic routed mouse events, so tests drive the real handlers without ever moving the user's cursor.
/// </summary>
internal static class PointerSource
{
    /// <summary>Screen position (device pixels) the pointer is pretended to be at; null = use the real mouse.</summary>
    internal static Point? Simulated;

    internal static Point Position(MouseEventArgs e, IInputElement relativeTo) =>
        Simulated is { } screen && relativeTo is Visual visual ? visual.PointFromScreen(screen) : e.GetPosition(relativeTo);
}
