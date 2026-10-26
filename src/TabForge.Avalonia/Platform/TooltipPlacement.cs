using Avalonia.Controls;

namespace TabForge.Avalonia.Platform;

// Owns: the one tooltip placement the settings rows use (pointer placement).
// Does not own: the tooltip popup's own screen-edge handling (Avalonia).
// Tests: TabForge.Avalonia.Tests (settings tooltip tests).
/// <summary>
/// The one tooltip placement the settings rows use: <see cref="PlacementMode.Pointer"/>, so a tip near a window edge is moved back on
/// screen by the tooltip popup's own constraint handling instead of crossing the edge. Avalonia 11.3.20's <see cref="ToolTip"/> has no
/// PlacementConstraintAdjustment attached property; the popup keeps its default adjustment.
/// </summary>
public static class TooltipPlacement
{
    public static void SetTip(Control control, object? tip)
    {
        ToolTip.SetTip(control, tip);
        ToolTip.SetPlacement(control, PlacementMode.Pointer);
    }
}
