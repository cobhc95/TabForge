namespace TabForge.Docking;

// Owns: the instrument (fretboard / keyboard) pane's remembered size per orientation: the share
//   (fraction of its split) it keeps when it moves within the same orientation, and the size it
//   restores when it returns to an orientation it has used before. Never resets to a default after
//   the first use: once a share is remembered it is reused.
// Does not own: the dock tree (DockLayoutTree), the saved layout (DockWorkspaceState) or any UI.
// Tests: instrument dock size memory tests.
internal static class InstrumentDockSizeMemory
{
    internal const string InstrumentId = "instrument";

    /// <summary>True for a split that stacks top/bottom (the instrument's height matters).</summary>
    internal static bool IsVertical(string orientation) => orientation == "Vertical";

    /// <summary>True for a drop zone that stacks top/bottom.</summary>
    internal static bool IsVerticalZone(DockDropZone zone) => zone is DockDropZone.Top or DockDropZone.Bottom;

    /// <summary>The instrument's share of its split (0.02-0.98) from a ratio and which half holds it.</summary>
    internal static double ShareOf(double ratio, bool inFirst)
    {
        var clamped = double.IsFinite(ratio) ? Math.Clamp(ratio, 0.02, 0.98) : 0.5;
        return inFirst ? clamped : 1 - clamped;
    }

    /// <summary>The split ratio that gives the instrument <paramref name="share"/> when it lands in first or second.</summary>
    internal static double RatioForShare(double share, bool newInFirst)
    {
        var clamped = double.IsFinite(share) ? Math.Clamp(share, 0.02, 0.98) : DockLayoutTree.EdgeFraction;
        return newInFirst ? clamped : 1 - clamped;
    }

    /// <summary>The share to apply for a move: the current one when staying in the same orientation, else the remembered one for the target, else the default. Never returns the default once remembered.</summary>
    internal static double ShareToApply(double? currentWhenSameOrientation, double? rememberedTarget, double? defaultShare = null)
    {
        var fallback = defaultShare is { } d && double.IsFinite(d) ? Math.Clamp(d, 0.02, 0.98) : DockLayoutTree.EdgeFraction;
        var picked = currentWhenSameOrientation is { } c && double.IsFinite(c) ? c
            : rememberedTarget is { } r && double.IsFinite(r) ? r
            : fallback;
        return Math.Clamp(picked, 0.02, 0.98);
    }

    /// <summary>Remembers a share: the current one wins, else the old one is kept (never a default).</summary>
    internal static double? RememberShare(double? current, double? remembered)
    {
        if (current is { } c && double.IsFinite(c)) return Math.Clamp(c, 0.02, 0.98);
        if (remembered is { } r && double.IsFinite(r)) return Math.Clamp(r, 0.02, 0.98);
        return remembered;
    }

    /// <summary>Pixels for a share of an available extent.</summary>
    internal static double PixelsForShare(double share, double available) =>
        available > 0 ? Math.Clamp(share, 0.02, 0.98) * available : 0;

    /// <summary>Share for pixels of an available extent (the default when unmeasured).</summary>
    internal static double ShareForPixels(double? pixels, double available)
    {
        if (pixels is not { } p || !double.IsFinite(p) || available <= 0) return DockLayoutTree.EdgeFraction;
        return Math.Clamp(p / available, 0.02, 0.98);
    }

    /// <summary>The pixel height to apply: locked wins, then the current height, then the remembered one, then the default.</summary>
    internal static double HeightToApply(double? currentHeight, double? rememberedHeight, double defaultHeight, bool locked, double? lockedHeight)
    {
        if (locked && lockedHeight is { } l && double.IsFinite(l) && l > 0) return l;
        if (currentHeight is { } c && double.IsFinite(c) && c > 0) return c;
        if (rememberedHeight is { } r && double.IsFinite(r) && r > 0) return r;
        return double.IsFinite(defaultHeight) && defaultHeight > 0 ? defaultHeight : 150;
    }

    /// <summary>The pixel width to apply: locked wins, then the current width, then the remembered one, then the default.</summary>
    internal static double WidthToApply(double? currentWidth, double? rememberedWidth, double defaultWidth, bool locked, double? lockedWidth) =>
        HeightToApply(currentWidth, rememberedWidth, defaultWidth, locked, lockedWidth);

    /// <summary>Remembers a pixel size: the current one wins, else the old one is kept (never a default).</summary>
    internal static double? RememberPixels(double? current, double? remembered)
    {
        if (current is { } c && double.IsFinite(c) && c > 0) return c;
        return remembered is { } r && double.IsFinite(r) && r > 0 ? r : remembered;
    }
}
