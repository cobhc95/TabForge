using System;
using System.Linq;
using Avalonia.Controls;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the Avalonia adapter of IMonitorWorkAreas: each screen's working area in DIPs, picked by overlap, then by distance.
// Does not own: fitting a window into the area (FloatingSessionRules, FloatingPlacementRules).
// Tests: TabForge.Avalonia.Tests.DockFloatingWindowTests.
/// <summary>Monitor work areas from the screens a top level sees; with no screens (headless) the bounds come back unchanged.</summary>
public sealed class AvaloniaMonitorWorkAreas(TopLevel topLevel) : IMonitorWorkAreas
{
    // ponytail: each area is scaled by its own monitor's scaling, so mixed-DPI layouts are approximate near monitor edges.
    public UiRect WorkAreaNear(UiRect bounds)
    {
        var areas = topLevel.Screens?.All
            .Select(s => new UiRect(s.WorkingArea.X / s.Scaling, s.WorkingArea.Y / s.Scaling, s.WorkingArea.Width / s.Scaling, s.WorkingArea.Height / s.Scaling))
            .ToList();
        if (areas is null || areas.Count == 0) return bounds;
        return areas.OrderByDescending(a => Overlap(a, bounds)).ThenBy(a => CentreDistance(a, bounds)).First();
    }

    private static double Overlap(UiRect a, UiRect b) =>
        Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));

    private static double CentreDistance(UiRect a, UiRect b) =>
        Math.Pow(a.X + a.Width / 2 - b.X - b.Width / 2, 2) + Math.Pow(a.Y + a.Height / 2 - b.Y - b.Height / 2, 2);
}
