using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.VisualTree;
using TabForge.Docking;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the dock controller's panel heights and widths: fitting a pane to a pixel height (FitPanelHeight), starting it at one
//   (StartPanelAtHeight/RestartPanelAtHeight, and the width twins for left/right docks), holding it through rebuilds (HoldPanelHeight),
//   putting back a replaced ratio (RestorePanelHeight), and reading the ratio's, the moved and the shown heights and widths.
// Does not own: the fit maths (DockSizeRules), the tree (DockLayoutTree) or the layoutChanged debounce (DockWorkspaceController).
// Tests: TabForge.Avalonia.Tests.DockWorkspaceControllerTests.
public sealed partial class DockWorkspaceController
{
    /// <summary>The split ratio <see cref="StartPanelAtWidth"/> last set, per pane id; kept apart from the height fits so a vertical drag never looks like a horizontal one.</summary>
    private readonly Dictionary<string, double> _startWidthRatios = new(StringComparer.Ordinal);
    /// <summary>The pane width when the user last let go of its moved horizontal splitter, per pane id.</summary>
    private readonly Dictionary<string, double?> _draggedWidths = new(StringComparer.Ordinal);
    /// <summary>
    /// Sizes the lower pane of the vertical split that ends in <paramref name="panelId"/>'s tab host to <paramref name="height"/> (the Core fit rule), as the
    /// the dock does: the live ratio changes, the user's ratio is kept in UserRatio, and nothing is saved. False when no such split is shown yet, or when
    /// the user dragged the splitter since the last fit and <paramref name="replaceUserSplit"/> is false. A saved ratio at launch is not a user drag, so the
    /// first fit of a session always applies.
    /// </summary>
    public bool FitPanelHeight(string panelId, double height, bool replaceUserSplit = false)
    {
        foreach (var split in _view.GetVisualDescendants().OfType<DockSplitPanel>())
        {
            var node = split.Node;
            if (node.Orientation != "Vertical" || node.Second is not { Kind: "tabs" } lower || !lower.Panels.Contains(panelId, StringComparer.Ordinal)) continue;
            var available = split.AvailableNow;
            if (available <= 0) return false;
            // A drag clears UserRatio; a ratio other than the last fit's then belongs to the user, until a new song replaces it.
            if (!replaceUserSplit && node.UserRatio is null && _fittedRatios.TryGetValue(panelId, out var fitted) && Math.Abs(node.Ratio - fitted) > 1e-9) return false;
            // The split snaps its first half to whole pixels (DockSplitPanel.FirstShare), so a fractional height (density-scaled rows) would land up to half a pixel short and scroll the rows: round the first half down so the pane keeps at least the height asked.
            var (_, ratio) = DockSizeRules.Fit(available - Math.Floor(available - height), 0, available, inFirst: false);
            node.UserRatio ??= node.Ratio;
            node.Ratio = ratio;
            _fittedRatios[panelId] = ratio;
            split.Rearrange();
            return true;
        }
        return false;
    }

    /// <summary>Locks a pane's row to its content height, or unlocks it, while the window is open; the splitter beside it follows at once.</summary>
    public void SetNaturalHeightLock(string id, bool locked)
    {
        _setup.SetKeepsNaturalHeight(id, locked);
        _view.SetNaturalHeightLock(id, locked);
    }

    /// <summary>Sizes the vertical split beside a pane so the pane is <paramref name="height"/> tall, as a fresh default layout starts it. Unlike
    /// <see cref="FitPanelHeight"/> it works for the pane in either half. It refits on each call while the ratio it last set is still in place,
    /// so the pane holds its height as the window resizes; once the user moves that splitter the ratio no longer matches and it is left alone.
    /// False until the split is laid out.</summary>
    public bool StartPanelAtHeight(string panelId, double height)
    {
        if (StartSplitOf(panelId) is not { } start) return false;
        var (split, inFirst) = start;
        var node = split.Node;
        if (_startRatios.TryGetValue(panelId, out var fitted) && Math.Abs(node.Ratio - fitted) > 1e-9) return true;   // the user moved the splitter
        var available = split.AvailableNow;
        if (available <= 0) return false;
        var ratio = DockSizeRules.Fit(height, 0, available, inFirst).Ratio;
        node.Ratio = ratio;
        _startRatios[panelId] = ratio;
        split.Rearrange();
        return true;
    }

    /// <summary>
    /// Starts the pane at <paramref name="height"/> as <see cref="StartPanelAtHeight"/> does, also when the user moved its splitter before: the user's
    /// ratio is dropped and the split is fitted again. False until the split is laid out.
    /// </summary>
    public bool RestartPanelAtHeight(string panelId, double height)
    {
        _startRatios.Remove(panelId);
        _draggedHeights.Remove(panelId);
        return StartPanelAtHeight(panelId, height);
    }

    /// <summary>
    /// The pixel height of a pane whose splitter the user moved since <see cref="StartPanelAtHeight"/> last fitted it; null while the splitter is
    /// where the fit put it, or when no fit is held.
    /// </summary>
    public double? UserSetPanelHeight(string panelId) =>
        MovedHeight(panelId) is null ? null : _draggedHeights.GetValueOrDefault(panelId) ?? PanelHeight(panelId);

    // The height the split's ratio gives the pane now, in the terms StartPanelAtHeight fits (the layout pass may not have run yet).
    private double? RatioHeight(string panelId)
    {
        if (StartSplitOf(panelId) is not { } start) return null;
        var (split, inFirst) = start;
        return (inFirst ? split.Node.Ratio : 1 - split.Node.Ratio) * split.AvailableNow;
    }

    private double? MovedHeight(string panelId)
    {
        if (!_startRatios.TryGetValue(panelId, out var fitted) || StartSplitOf(panelId) is not { } start) return null;
        return Math.Abs(start.Split.Node.Ratio - fitted) > 1e-9 ? PanelHeight(panelId) : null;
    }

    /// <summary>The first vertical split shown with the pane in one of its tab halves, and whether the pane is in the first half; null when none shows it.</summary>
    private (DockSplitPanel Split, bool InFirst)? StartSplitOf(string panelId)
    {
        foreach (var split in _view.GetVisualDescendants().OfType<DockSplitPanel>())
        {
            var node = split.Node;
            if (node.Orientation != "Vertical") continue;
            if (node.First is { Kind: "tabs" } upper && upper.Panels.Contains(panelId, StringComparer.Ordinal)) return (split, true);
            if (node.Second is { Kind: "tabs" } lower && lower.Panels.Contains(panelId, StringComparer.Ordinal)) return (split, false);
        }
        return null;
    }

    /// <summary>The pixel height of the tab group showing <paramref name="panelId"/>, chrome included; null while it is not shown.</summary>
    public double? PanelHeight(string panelId) =>
        _view.GetVisualDescendants().OfType<DockTabSurface>().FirstOrDefault(s => s.Node.Panels.Contains(panelId, StringComparer.Ordinal)) is { Bounds.Height: > 0 } surface
            ? surface.Bounds.Height : null;

    /// <summary>The pixel width of the tab group showing <paramref name="panelId"/>; null while it is not shown. Used to keep the instrument pane's width across left/right moves.</summary>
    public double? PanelWidth(string panelId) =>
        _view.GetVisualDescendants().OfType<DockTabSurface>().FirstOrDefault(s => s.Node.Panels.Contains(panelId, StringComparer.Ordinal)) is { Bounds.Width: > 0 } surface
            ? surface.Bounds.Width : null;

    /// <summary>Sizes the horizontal split beside a pane so the pane is <paramref name="width"/> wide. Like <see cref="StartPanelAtHeight"/> but for left/right docks; a dragged vertical splitter never counts as a width drag.</summary>
    public bool StartPanelAtWidth(string panelId, double width)
    {
        if (WidthSplitOf(panelId) is not { } start) return false;
        var (split, inFirst) = start;
        var node = split.Node;
        if (_startWidthRatios.TryGetValue(panelId, out var fitted) && Math.Abs(node.Ratio - fitted) > 1e-9) return true;
        var available = split.AvailableNow;
        if (available <= 0) return false;
        var ratio = DockSizeRules.Fit(width, 0, available, inFirst).Ratio;
        node.Ratio = ratio;
        _startWidthRatios[panelId] = ratio;
        split.Rearrange();
        return true;
    }

    /// <summary>Starts the pane at <paramref name="width"/> as <see cref="StartPanelAtWidth"/> does, also when the user moved its splitter before.</summary>
    public bool RestartPanelAtWidth(string panelId, double width)
    {
        _startWidthRatios.Remove(panelId);
        _draggedWidths.Remove(panelId);
        return StartPanelAtWidth(panelId, width);
    }

    /// <summary>The pixel width of a pane whose horizontal splitter the user moved since <see cref="StartPanelAtWidth"/> last fitted it; null otherwise.</summary>
    public double? UserSetPanelWidth(string panelId) =>
        MovedWidth(panelId) is null ? null : _draggedWidths.GetValueOrDefault(panelId) ?? PanelWidth(panelId);

    private double? RatioWidth(string panelId)
    {
        if (WidthSplitOf(panelId) is not { } start) return null;
        var (split, inFirst) = start;
        return (inFirst ? split.Node.Ratio : 1 - split.Node.Ratio) * split.AvailableNow;
    }

    private double? MovedWidth(string panelId)
    {
        if (!_startWidthRatios.TryGetValue(panelId, out var fitted) || WidthSplitOf(panelId) is not { } start) return null;
        return Math.Abs(start.Split.Node.Ratio - fitted) > 1e-9 ? PanelWidth(panelId) : null;
    }

    /// <summary>The first horizontal split shown with the pane in one of its tab halves, and whether the pane is in the first half; null when none shows it.</summary>
    private (DockSplitPanel Split, bool InFirst)? WidthSplitOf(string panelId)
    {
        foreach (var split in _view.GetVisualDescendants().OfType<DockSplitPanel>())
        {
            var node = split.Node;
            if (node.Orientation != "Horizontal") continue;
            if (node.First is { Kind: "tabs" } left && left.Panels.Contains(panelId, StringComparer.Ordinal)) return (split, true);
            if (node.Second is { Kind: "tabs" } right && right.Panels.Contains(panelId, StringComparer.Ordinal)) return (split, false);
        }
        return null;
    }

    /// <summary>
    /// Holds the tab group of <paramref name="panelId"/> at <paramref name="height"/> pixels (chrome included) through every rebuild, whatever its split
    /// ratio, or releases it with null. The split ratios stay as they were, so releasing gives the ratio's height back. Takes effect at the next rebuild.
    /// </summary>
    public void HoldPanelHeight(string panelId, double? height)
    {
        if (height is { } held) _view.HeldHeights[panelId] = held;
        else _view.HeldHeights.Remove(panelId);
    }

    /// <summary>Puts back the ratio <see cref="FitPanelHeight"/> replaced on the split that ends in <paramref name="panelId"/>'s tab host; false when no fit is held.</summary>
    public bool RestorePanelHeight(string panelId)
    {
        foreach (var split in _view.GetVisualDescendants().OfType<DockSplitPanel>())
        {
            var node = split.Node;
            if (node.Second is not { Kind: "tabs" } lower || !lower.Panels.Contains(panelId, StringComparer.Ordinal) || node.UserRatio is not { } user) continue;
            node.Ratio = user;
            node.UserRatio = null;
            split.Rearrange();
            return true;
        }
        return false;
    }
}
