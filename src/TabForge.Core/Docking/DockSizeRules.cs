namespace TabForge.Docking;

// Owns: the height maths of the dock in plain pixels: a tab host's maximum and locked height, the split ratio of a fit,
//   the clamp of a splitter drag (pane minimums, maximums and a lower pane's height limits) and the main dock's overflow.
// Does not own: measuring or sizing controls, the splitters and scroll bar (DockWorkspace in the app), or the saved layout.
// Tests: no dedicated test class.
/// <summary>The minimum, maximum and locked content heights of one registered dock panel, in pixels.</summary>
internal readonly record struct DockPaneHeights(double Min, double? Max, double? Fixed);

/// <summary>Toolkit-free height rules of the dock; the host measures its controls and applies the results.</summary>
internal static class DockSizeRules
{
    /// <summary>Border plus, when the tab strip shows, its header height.</summary>
    internal static double HostChrome(bool headerCollapsed, double tabHeaderHeight) => 2 + (headerCollapsed ? 0 : tabHeaderHeight);

    /// <summary>Tallest height of a tab host whose every panel has a maximum (chrome included), or null.</summary>
    internal static double? MaxHostHeight(IReadOnlyList<DockPaneHeights> panes, double chrome)
    {
        if (panes.Count == 0 || panes.Any(p => p.Max is null)) return null;
        return Math.Max(panes.Max(p => p.Max!.Value), panes.Max(p => p.Min)) + chrome;
    }

    /// <summary>Height of a tab host holding a size-locked panel (chrome included), or null.</summary>
    internal static double? FixedHostHeight(IReadOnlyList<DockPaneHeights> panes, double chrome)
    {
        double? fixedContent = null;
        foreach (var p in panes)
            if (p.Fixed is { } f) fixedContent = Math.Max(fixedContent ?? 0, f);
        if (fixedContent is null) return null;
        foreach (var q in panes)   // a locked height never leaves empty space around a panel with a maximum
            if (q.Max is { } cap) fixedContent = Math.Max(Math.Min(fixedContent.Value, cap), q.Min);
        return fixedContent.Value + chrome;
    }

    /// <summary>The share of the dock the track list may take at most (the rest stays for the score).</summary>
    internal const double TrackListMaxShare = 0.45;

    /// <summary>The track list's wanted height capped at <paramref name="maxShare"/> of the <paramref name="shared"/> dock height and leaving the score
    /// <paramref name="minScoreHeight"/>; never below <paramref name="collapsedHeight"/>. Unmeasured inputs (shared or minScore not above 0) leave it uncapped.
    /// The default share is the rule; a share of 1 leaves only the score's minimum as the cap.</summary>
    internal static double TrackListHeight(double want, double shared, double minScoreHeight, double collapsedHeight, double maxShare = TrackListMaxShare)
    {
        if (shared <= 1 || minScoreHeight <= 0) return want;
        var cap = Math.Min(shared * maxShare, shared - minScoreHeight);
        return Math.Min(want, Math.Max(collapsedHeight, cap));
    }

    /// <summary>The pane height (never below its minimum) and split ratio that give the pane in the first or second row that height.</summary>
    internal static (double Height, double Ratio) Fit(double requested, double minimum, double available, bool inFirst)
    {
        var height = Math.Max(Math.Max(0, requested), minimum);
        return (height, Math.Clamp(inFirst ? height / available : (available - height) / available, 0.02, 0.98));
    }

    /// <summary>
    /// Clamps the first side of a split to <paramref name="sizeA"/> within <paramref name="total"/>: neither side below its minimum,
    /// a vertical split's lower pane within its height limits (when the other minimums leave room) and no side past its maximum.
    /// Returns both sides' sizes, the first side's lowest size while dragging and the second side's effective minimum.
    /// </summary>
    internal static (double First, double Second, double FirstLowerBound, double SecondMin) ClampSplit(double sizeA, double total, double minA, double minB,
        bool vertical, IEnumerable<(double Min, double Max)> lowerLimits, double? maxA, double? maxB)
    {
        var lowerBound = minA;
        if (vertical)
            foreach (var limit in lowerLimits)
            {
                minB = Math.Max(minB, Math.Min(limit.Min, Math.Max(0, total - minA)));
                lowerBound = Math.Max(minA, total - Math.Max(minB, limit.Max));
            }
        var upperBound = total - minB;
        if (vertical)
        {
            if (maxA is { } a) upperBound = Math.Min(upperBound, Math.Max(a, minA));
            if (maxB is { } b) lowerBound = Math.Max(lowerBound, Math.Min(total - Math.Max(b, minB), upperBound));
        }
        var clamped = minA + minB <= total ? Math.Clamp(sizeA, Math.Min(lowerBound, upperBound), upperBound) : minA;
        return (clamped, Math.Max(minB, total - clamped), lowerBound, minB);
    }

    /// <summary>Pixels the main dock must scroll when the panes' minimum height exceeds the workspace (0 when it fits or is unmeasured).</summary>
    internal static double MainOverflow(double height, double needed) => height > 0 ? Math.Max(0, Math.Ceiling(needed - height)) : 0;
}
