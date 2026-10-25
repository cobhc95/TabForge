using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Docking;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Docking;

// Owns: turning a DockNodeState tree into controls (splits, tab groups, the editor node), the host lookup by host id and by point,
//   and the drop preview.
// Does not own: moving panels (DockWorkspaceController), floating windows, saving or restoring layouts, the pane content (the factory).
// Tests: TabForge.Avalonia.Tests.DockWorkspaceViewTests.
/// <summary>Renders a dock layout. The state is shown as given and edited in place by splitter drags and tab clicks.</summary>
public sealed class DockWorkspaceView : Grid
{
    /// <summary>The id the factory is asked for to get the editor node's content.</summary>
    public const string EditorContentId = "editor";
    private const string TimelinePanelId = "timeline";

    private const double TabMarkerWidth = 3;
    private const double SplitPreviewOpacity = 0.45;   // the split / body-merge rectangle lets the panes under it show through

    private readonly Func<string, Control> _contentOf;
    private readonly Dictionary<string, Control> _hosts = new(StringComparer.Ordinal);
    private readonly Dictionary<Control, double> _lockedHeights = new();   // built tab group -> its locked pixel height; cleared on every Show
    private readonly Dictionary<Control, double> _minimumHeights = new();  // built tab group holding the timeline -> its minimum pixel height; cleared on every Show
    private readonly Dictionary<DockTabSurface, IReadOnlyDictionary<string, Control>> _tabContent = new();  // built tab group -> its pane controls; cleared on every Show
    private readonly List<IDisposable> _limitSubscriptions = new();   // follows each pane's own height limits while the tree it was built for is shown
    // Perf E2b (H4/H5): caches each natural-height pane's last measured height per control instance. Reused panes
    // (Detached/Shared) skip Measure(Infinity) on later Shows while the same control stays measure-valid.
    private readonly Dictionary<string, (Control Control, double Height)> _naturalCache = new(StringComparer.Ordinal);
    private readonly Border _preview;
    private readonly Canvas _overlay = new() { Name = "DockOverlay", IsHitTestVisible = false };
    private readonly Border _tree = new() { Name = "DockTree" };

    /// <param name="contentOf">Makes the control of a pane id; called once for each pane per <see cref="Show"/>.</param>
    public DockWorkspaceView(Func<string, Control> contentOf)
    {
        _contentOf = contentOf;
        Name = "DockWorkspace";
        _preview = new Border { Name = "DockDropPreview", IsVisible = false, BorderThickness = new Thickness(2) };
        _preview[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoft");
        _preview[!Border.BorderBrushProperty] = new DynamicResourceExtension("Accent");
        _overlay.Children.Add(_preview);
        Children.Add(_tree);
        Children.Add(_overlay);
    }

    /// <summary>The rectangle the drop preview covers, in this view's coordinates; null while hidden.</summary>
    public UiRect? DropPreviewRect => _preview.IsVisible ? new UiRect(Canvas.GetLeft(_preview), Canvas.GetTop(_preview), _preview.Width, _preview.Height) : null;

    /// <summary>Makes the right-click menu of a pane's tab; null for none. Read when the tree is built.</summary>
    public Func<string, ContextMenu?>? MenuOf { get; set; }

    /// <summary>Pane ids whose tab group is locked to the content's natural height (read when the tree is built).</summary>
    public IReadOnlySet<string> NaturalHeightIds { get; set; } = new HashSet<string>();

    /// <summary>
    /// Pixel heights a tab group holding the pane id keeps (chrome included) whatever its split ratio, ahead of the natural-height lock; read when
    /// the tree is built. Key: pane id; ceiling: one entry per pane; thread: the UI thread.
    /// </summary>
    public Dictionary<string, double> HeldHeights { get; } = new(StringComparer.Ordinal);

    /// <summary>Raised when the user changed the layout in the view itself: a tab click or a splitter drag.</summary>
    public event Action? Edited;

    /// <summary>Raised for every tab group built, so a controller can follow its tab drags.</summary>
    public event Action<DockTabSurface>? SurfaceBuilt;

    /// <summary>The leaf host under a point of this view and the drop zone of that point in it; null over a splitter or outside.</summary>
    public DockDropHit? DropTargetAt(Point point)
    {
        foreach (var (id, host) in _hosts)
        {
            if (host is DockSplitPanel || host.TranslatePoint(default, this) is not { } origin) continue;
            var rect = new UiRect(origin.X, origin.Y, host.Bounds.Width, host.Bounds.Height);
            var at = new UiPoint(point.X, point.Y);
            if (!DockDropRules.Contains(rect, at)) continue;
            var local = new UiPoint(at.X - rect.X, at.Y - rect.Y);
            if (host is not DockTabSurface surface) return new DockDropHit(id, DockDropRules.ClassifyZone(local, rect.Width, rect.Height), null);
            var zone = DockDropRules.ClassifyTabGroupZone(local, rect.Width, rect.Height, surface.HeaderHeight);
            var tabs = surface.TabSpans();
            var inStrip = surface.HeaderHeight > 0 && DockDropRules.IsInStrip(local, surface.HeaderHeight);
            return new DockDropHit(id, zone, zone == DockDropZone.Center ? DockDropRules.TabInsertIndex(local.X, tabs) : null, inStrip);
        }
        return null;
    }

    /// <summary>The control of a host (tab group or editor) by host id.</summary>
    public Control? HostControl(string hostId) => _hosts.GetValueOrDefault(hostId);

    /// <summary>Builds the controls of the state's tree.</summary>
    public void Show(DockWorkspaceState state)
    {
        _hosts.Clear();
        _lockedHeights.Clear();
        _minimumHeights.Clear();
        _tabContent.Clear();
        foreach (var subscription in _limitSubscriptions) subscription.Dispose();
        _limitSubscriptions.Clear();
        HideDropPreview();
        _tree.Child = state.Root is null ? null : Build(state.Root);
    }

    private Control Build(DockNodeState node)
    {
        Control control;
        switch (node.Kind)
        {
            case "split":
                var first = Build(node.First ?? throw Invalid(node));
                var second = Build(node.Second ?? throw Invalid(node));
                control = new DockSplitPanel(node, first, second, () => Edited?.Invoke(), _lockedHeights.GetValueOrDefault(first, double.NaN), _lockedHeights.GetValueOrDefault(second, double.NaN),
                    () => MinimumHeightOf(first), () => MinimumHeightOf(second), () => MaximumHeightOf(first), () => MaximumHeightOf(second));
                break;
            case "editor":
                var editor = new Border { Name = "DockEditor", Child = _contentOf(EditorContentId) };
                editor[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
                control = editor;
                break;
            case "tabs":
                var content = new Dictionary<string, Control>(StringComparer.Ordinal);
                foreach (var id in node.Panels) content[id] = _contentOf(id);
                var surface = new DockTabSurface(node, content, MenuOf, () => Edited?.Invoke());
                SurfaceBuilt?.Invoke(surface);
                _tabContent[surface] = content;
                if ((HeldHeightOf(node) ?? LockedHeightOf(node, content)) is { } lockedHeight) _lockedHeights[surface] = lockedHeight;
                // A tab group holding the timeline keeps the timeline's minimum height (its rows and Add track strip) while its splitter is dragged.
                if (node.Panels.Contains(TimelinePanelId))
                    _minimumHeights[surface] = TabForge.Avalonia.Platform.TimelineView.MinimumPaneHeight + DockSizeRules.HostChrome(DockPanePresenter.HidesTabStrip(node.Panels), DockTabSurface.StripHeight);
                foreach (var pane in content.Values) FollowPaneLimits(pane, surface);
                control = surface;
                break;
            default:
                throw new InvalidOperationException($"Dock node '{node.HostId}' has the unknown kind '{node.Kind}'.");
        }
        _hosts[node.HostId] = control;
        return control;
    }

    /// <summary>
    /// Follows a pane's own height limits. A pane that sets them while it lays out (the arrangement's natural height, the fretboard's caps) does so
    /// after the split has arranged its halves, and the split reads them live: the host measures again when one changes, so a pane is never left
    /// taller than its content's limit (empty space under the timeline or the fretboard).
    /// </summary>
    private void FollowPaneLimits(Control pane, Control host)
    {
        void Changed(double _)
        {
            host.InvalidateMeasure();
            // The split above the host lays out again too: a changed limit changes the split's own shares, not only the host's size.
            if (host.GetVisualParent() is DockSplitPanel split) split.InvalidateMeasure();        }
        _limitSubscriptions.Add(pane.GetObservable(Layoutable.MinHeightProperty).Subscribe(new AnonymousObserver<double>(Changed)));
        _limitSubscriptions.Add(pane.GetObservable(Layoutable.MaxHeightProperty).Subscribe(new AnonymousObserver<double>(Changed)));
    }

    /// <summary>Locks the tab group that holds <paramref name="id"/> to its content's height (or unlocks it) without rebuilding the layout; the splitter beside it follows.</summary>
    public void SetNaturalHeightLock(string id, bool locked)
    {
        foreach (var (surface, content) in _tabContent)
        {
            if (!surface.Node.Panels.Contains(id, StringComparer.Ordinal)) continue;
            var lockedHeight = locked ? LockedHeightOf(surface.Node, content) : null;
            if (lockedHeight is { } height) _lockedHeights[surface] = height;
            else _lockedHeights.Remove(surface);
            if (surface.Parent is DockSplitPanel split) split.SetLockedHeight(surface, lockedHeight ?? double.NaN);
            return;
        }
    }

    /// <summary>The held pixel height of a tab group (the tallest held among its panes), or null when none of its panes is held.</summary>
    private double? HeldHeightOf(DockNodeState node)
    {
        var held = node.Panels.Where(HeldHeights.ContainsKey).Select(id => HeldHeights[id]).ToList();
        return held.Count == 0 ? null : held.Max();
    }

    /// <summary>The pixel height a tab group locked by its panes' natural heights keeps (chrome included), or null when none of its panes is locked.</summary>
    private double? LockedHeightOf(DockNodeState node, IReadOnlyDictionary<string, Control> content)
    {
        // Perf E2b (H4/H5): reused panes skip Measure(Infinity) while the same control stays measure-valid.
        var locked = node.Panels.Where(NaturalHeightIds.Contains).Select(id => MeasureNaturalHeight(id, content[id])).ToList();
        if (locked.Count == 0) return null;
        return DockSizeRules.FixedHostHeight(locked.Select(h => new DockPaneHeights(0, null, h)).ToList(), DockSizeRules.HostChrome(DockPanePresenter.HidesTabStrip(node.Panels), DockTabSurface.StripHeight));
    }

    /// <summary>
    /// Perf E2b (H4/H5): the natural height of one pane, cached per control instance. A reused pane (Detached/Shared)
    /// keeps its last measured height while it stays measure-valid; a new control or an invalidated one is measured once.
    /// </summary>
    internal double MeasureNaturalHeight(string id, Control control)
    {
        if (_naturalCache.TryGetValue(id, out var cached) && ReferenceEquals(cached.Control, control) && control.IsMeasureValid)
            return cached.Height;
        control.Measure(Size.Infinity);
        var height = control.DesiredSize.Height;
        _naturalCache[id] = (control, height);
        return height;
    }

    /// <summary>Perf E2b (H4/H5): drops the cached natural height of <paramref name="id"/> (its content changed size).</summary>
    internal void InvalidateNaturalHeight(string id) => _naturalCache.Remove(id);

    /// <summary>The shortest pixel height a tab group may take (chrome included): the timeline's minimum, or the fretboard's minimum when it is larger; read live.</summary>
    private double MinimumHeightOf(Control host)
    {
        var timelineMinimum = _minimumHeights.GetValueOrDefault(host, 0);
        if (host is not DockTabSurface surface || !_tabContent.TryGetValue(surface, out var content)) return timelineMinimum;
        // The arrangement's top band (the transport above the header labels) sits above the rows, so the timeline's minimum takes it too.
        if (timelineMinimum > 0 && content.TryGetValue(TimelinePanelId, out var timelinePane) && timelinePane is ArrangementView arrangement)
            timelineMinimum += arrangement.TopBandHeight;
        var paneMinimum = surface.Node.Panels.Select(id => content[id] is InstrumentPanelStrip strip ? strip.MinHeight : 0).DefaultIfEmpty(0).Max();
        return paneMinimum > 0 ? Math.Max(timelineMinimum, paneMinimum + TabChrome(surface.Node)) : timelineMinimum;
    }

    /// <summary>The tallest pixel height a tab group may take (chrome included), read live from its panes' own maximums; NaN when a pane has none.</summary>
    private double MaximumHeightOf(Control host)
    {
        if (host is not DockTabSurface surface || !_tabContent.TryGetValue(surface, out var content)) return double.NaN;
        var panes = surface.Node.Panels.Select(id => new DockPaneHeights(0, PaneMaximumHeight(content[id]), null)).ToList();
        return DockSizeRules.MaxHostHeight(panes, TabChrome(surface.Node)) ?? double.NaN;
    }

    /// <summary>A pane's own maximum height when it has one: the fretboard's board limit, the arrangement's content height, or any pane's own
    /// <see cref="Layoutable.MaxHeight"/>; null otherwise. Read live by the split, so a limit a pane sets while it lays out is honoured.</summary>
    private static double? PaneMaximumHeight(Control pane) => double.IsFinite(pane.MaxHeight) ? pane.MaxHeight : null;

    /// <summary>The border and tab strip a tab group adds around its panel's content.</summary>
    private static double TabChrome(DockNodeState node) => DockSizeRules.HostChrome(DockPanePresenter.HidesTabStrip(node.Panels), DockTabSurface.StripHeight);

    private static InvalidOperationException Invalid(DockNodeState node) =>
        new($"Dock split '{node.HostId}' needs both a first and a second node.");

    /// <summary>Shows where a panel dropped in <paramref name="zone"/> of the host would go, using the same maths as the real drop.</summary>
    public void ShowDropPreview(string hostId, DockDropZone zone, double fraction = DockLayoutTree.EdgeFraction, int? tabIndex = null, bool inStrip = false)
    {
        if (!_hosts.TryGetValue(hostId, out var host) || host.TranslatePoint(default, this) is not { } origin) { HideDropPreview(); return; }
        UiRect rect;
        if (inStrip && host is DockTabSurface surface && tabIndex is { } index)
        {
            // A tab merge from the header: a thin marker at the insertion position, never a box over the other tabs.
            var x = DockDropRules.TabInsertX(index, surface.TabSpans());
            rect = new UiRect(origin.X + x - TabMarkerWidth / 2, origin.Y, TabMarkerWidth, surface.HeaderHeight);
            _preview.Opacity = 1;
        }
        else
        {
            rect = DockDropRules.PreviewRect(new UiRect(origin.X, origin.Y, host.Bounds.Width, host.Bounds.Height), zone, fraction);
            _preview.Opacity = SplitPreviewOpacity;
        }
        Canvas.SetLeft(_preview, rect.X);
        Canvas.SetTop(_preview, rect.Y);
        _preview.Width = rect.Width;
        _preview.Height = rect.Height;
        _preview.IsVisible = true;
    }

    public void HideDropPreview() => _preview.IsVisible = false;
}

/// <summary>Where a dragged pane would land: the host, the zone and, for a tab merge, the position among its tabs.</summary>
/// <param name="InStrip">True when the pointer is in the header row (the preview is a thin marker there).</param>
public sealed record DockDropHit(string HostId, DockDropZone Zone, int? TabIndex, bool InStrip = false);
