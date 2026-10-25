using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Docking;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the live dock layout of an Avalonia DockWorkspaceView: restore from a saved state, the edits a tab drag asks for
//   (DockPaneDragFlow: dock, float at the drop point), the drop that moves a panel in the tree, close, reset panel, reset all, the tab menu and the debounced
//   LayoutChanged event that carries DockWorkspaceRules.Capture output, and the floating windows: float a panel, show the saved
//   ones fitted onto the work areas, follow their moves, and the user's X (FloatingSessionRules).
// Does not own: the floating window itself (DockFloatingWindow), the pane content (DockPaneSetup), saving the layout, size and fit maths, the rendering.
// Tests: TabForge.Avalonia.Tests.DockWorkspaceControllerTests.
/// <summary>Edits the dock tree through DockLayoutTree and DockWorkspaceRules (the same calls as the WPF dock) and re-shows the view.</summary>
public sealed class DockWorkspaceController : IDockPaneDragHost, IDisposable
{
    private static readonly TimeSpan ChangeDelay = TimeSpan.FromMilliseconds(UiTiming.LayoutChangeDebounceMs);

    private readonly DockWorkspaceView _view;
    private readonly DockPaneSetup _setup;
    /// <summary>The pane height when the user last let go of its moved splitter, per pane id; thread: the UI thread.</summary>
    private readonly Dictionary<string, double?> _draggedHeights = new(StringComparer.Ordinal);
    /// <summary>The split ratio <see cref="StartPanelAtHeight"/> last set, per pane id; key: pane id; thread: the UI thread.</summary>
    private readonly Dictionary<string, double> _startRatios = new(StringComparer.Ordinal);
    /// <summary>The split ratio <see cref="FitPanelHeight"/> last set, per panel id, so a later fit can tell its own splitter from one the user dragged; key: panel id; ceiling: one entry per fitted panel; thread: the UI thread.</summary>
    private readonly Dictionary<string, double> _fittedRatios = new(StringComparer.Ordinal);
    private readonly IUiTimer _timer = new AvaloniaUiTimer(ChangeDelay);
    private readonly DockPaneDragFlow _drag;
    private bool _changePending;
    // Perf E2b (H4/H5): coalesces per-panel rebuilds. While _batchDepth > 0, Changed only marks _batchDirty;
    // the outermost EndBatch runs one Show. _isShowing guards re-entrant Changed during Show itself.
    private int _batchDepth;
    private bool _batchDirty;
    private bool _isShowing;
    private readonly Dictionary<string, DockFloatingWindow> _floating = new(StringComparer.Ordinal);   // floating id -> its open window
    private readonly IMonitorWorkAreas? _workAreas;
    private Window? _owner;   // the window the view sits in; floating windows are owned by it and shown once it is shown
    private const double MinFloatingWidth = 240;
    private const double MinFloatingHeight = 150;

    /// <summary>The dock pane id of the instrument (fretboard / keyboard) panel.</summary>
    private const string InstrumentPanelId = "instrument";

    /// <param name="saved">The saved layout; null or an invalid one gives the default layout.</param>
    /// <param name="workAreas">The monitors saved floating windows are fitted onto; null asks the screens of the view's window.</param>
    public DockWorkspaceController(DockWorkspaceView view, DockPaneSetup setup, DockWorkspaceState? saved = null, IMonitorWorkAreas? workAreas = null)
    {
        _view = view;
        _workAreas = workAreas;
        _setup = setup;
        _drag = new DockPaneDragFlow(this, view);
        var emptiedFloating = new List<string>();
        State = DockWorkspaceRules.Restore(saved, hasEditorContent: true, setup.PaneIds,
            id => Row(id).StartsClosed, id => (Row(id).DefaultHost, Row(id).DefaultAnchor), emptiedFloating);
        view.NaturalHeightIds = setup.NaturalHeightIds;
        view.MenuOf = id => DockPanelMenu.Build(id, ResetPanel, id => ClosePanel(id), ResetAllPanels, id => FloatPanel(id));
        view.SurfaceBuilt += _drag.Hook;
        view.Edited += NotifyChanged;
        view.AttachedToVisualTree += (_, _) =>
        {
            TopLevel.GetTopLevel(view)?.AddHandler(InputElement.KeyDownEvent, _drag.OnKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            if (_owner is not null || TopLevel.GetTopLevel(view) is not Window owner) return;
            _owner = owner;
            if (owner.IsVisible) ShowSavedFloating();
            else owner.Opened += (_, _) => ShowSavedFloating();
        };
        _timer.Tick += (_, _) => FlushLayoutChanged();
        view.Show(State);
    }

    /// <summary>The live layout. Read it; change it only through the controller's methods.</summary>
    public DockWorkspaceState State { get; private set; }

    /// <summary>While keyboard mode shows its layout, the instrument (fretboard) panel stays closed: showing, resetting,
    /// floating and layout requests for it are refused and an applied layout keeps it closed.</summary>
    public bool SuppressInstrumentPanel { get; set; }

    public bool IsDragging => _drag.IsDragging;

    /// <summary>The drag ghost while a tab is dragged outside the main dock, else null.</summary>
    public DockDragGhost? DragGhost => _drag.Ghost;

    /// <summary>Raised a moment after the last change with the state to save (<see cref="DockWorkspaceRules.Capture"/>).</summary>
    public event Action<DockWorkspaceState>? LayoutChanged;

    /// <summary>Raised before a change rebuilds the layout, while the controls still show the old one (a pane's height can still be read).</summary>
    public event Action? LayoutRebuilding;

    /// <summary>Raised after a change rebuilt the layout; the new panes are not laid out yet.</summary>
    public event Action? LayoutRebuilt;

    private static DockPaneRow Row(string id) => DockPaneTable.Rows.First(r => r.Id == id);

    private bool IsRegistered(string id) => _setup.PaneIds.Contains(id);

    /// <summary>True for the instrument panel while keyboard mode keeps it closed.</summary>
    private bool IsInstrumentSuppressed(string id) => SuppressInstrumentPanel && id == InstrumentPanelId;

    /// <summary>Moves a visible panel next to or into a host. False, with the tree untouched, when the drop is not allowed.</summary>
    public bool DockPanelTo(string panelId, string targetHostId, DockDropZone zone, double fraction = DockLayoutTree.EdgeFraction, int? tabIndex = null)
    {
        if (!CanDrop(panelId, targetHostId, zone)
            || !DockWorkspaceRules.DropPanel(State, panelId, targetHostId, zone, floatingId: null, fraction, tabIndex, new List<string>()))
            return false;
        Changed();
        return true;
    }

    /// <summary>False when a left or right split would leave the target column narrower than two minimum panes; true before the first layout.</summary>
    private bool SplitKeepsWidth(string hostId, DockDropZone zone)
    {
        if (!DockDropRules.IsHorizontal(zone) || _view.HostControl(hostId) is not { Bounds.Width: > 0 } host) return true;
        return DockDropRules.SplitFits(host.Bounds.Width, DockDropRules.MinPaneWidth, DockDropRules.MinPaneWidth);
    }

    /// <summary>Whether <see cref="DockPanelTo"/> would move the panel: it exists, can move, the host is a leaf, the editor takes no tabs,
    /// and a lone panel (or a same-group tab drop) has nowhere to go.</summary>
    public bool CanDrop(string panelId, string targetHostId, DockDropZone zone)
    {
        if (!IsRegistered(panelId) || !DockPanePresenter.CanMove(panelId) || !DockWorkspaceRules.IsVisible(State, panelId)) return false;
        var target = DockLayoutTree.FindNode(State.Root, targetHostId);
        if (target is null || target.Kind == "split") return false;
        if (target.Kind == "editor") return zone != DockDropZone.Center;
        if (!target.Panels.Contains(panelId, StringComparer.Ordinal)) return zone == DockDropZone.Center || SplitKeepsWidth(targetHostId, zone);
        return target.Panels.Count > 1;   // within its own group: Center reorders, an edge splits it out; a lone pane has nowhere to go
    }

    /// <summary>
    /// Sizes the lower pane of the vertical split that ends in <paramref name="panelId"/>'s tab host to <paramref name="height"/> (the Core fit rule), as the
    /// WPF dock does: the live ratio changes, the user's ratio is kept in UserRatio, and nothing is saved. False when no such split is shown yet, or when
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
            var (_, ratio) = DockSizeRules.Fit(height, 0, available, inFirst: false);
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

    /// <summary>Takes the panel out of the layout; it comes back with <see cref="ResetPanel"/>.</summary>
    public bool ClosePanel(string id)
    {
        if (!IsRegistered(id)) return false;
        DockWorkspaceRules.Close(State, id);
        Changed();
        return true;
    }

    /// <summary>Puts the panel back at its default place, also when it was closed.</summary>
    public void ResetPanel(string id)
    {
        if (!IsRegistered(id)) return;
        if (IsInstrumentSuppressed(id)) { ClosePanel(id); return; }
        var row = Row(id);
        DockWorkspaceRules.PlaceAtDefault(State, id, row.DefaultHost, row.DefaultAnchor);
        Changed();
    }

    /// <summary>The layout the window starts with and "Reset all panels" restores: the WPF default (the fretboard above the score, the palettes at the right, the timeline below).</summary>
    public static DockWorkspaceState DefaultLayout() => DockLayoutTree.CreateDefaultState();

    /// <summary>The layout the window starts with: the saved one when it is usable (every required pane shown), otherwise the default.</summary>
    public static DockWorkspaceState StartupLayout(DockWorkspaceState? saved) => DockLayoutTree.IsUsableAtStart(saved) ? saved! : DefaultLayout();

    /// <summary>A copy of the current layout, with every panel's place and size; <see cref="ApplyLayout"/> restores it as it was taken.</summary>
    public DockWorkspaceState SnapshotLayout() => DockLayoutTree.Clone(State);

    public void ResetAllPanels()
    {
        _drag.Cancel();
        State = DefaultLayout();
        if (SuppressInstrumentPanel) DockWorkspaceRules.Close(State, InstrumentPanelId);
        Changed();
    }

    /// <summary>True when the panel is in the layout (shown or docked; a closed panel is not).</summary>
    public bool IsPanelVisible(string id) => DockWorkspaceRules.IsVisible(State, id);

    /// <summary>View > Panels: shows a closed panel at its default place, or closes a shown one.</summary>
    public void SetPanelVisible(string id, bool visible)
    {
        if (visible && IsInstrumentSuppressed(id)) return;
        if (visible) ResetPanel(id);
        else ClosePanel(id);
    }

    /// <summary>View > Layouts: replaces the panels, their places and sizes with a saved or built-in layout, restored the way the window starts.</summary>
    public void ApplyLayout(DockWorkspaceState layout)
    {
        var emptiedFloating = new List<string>();
        State = DockWorkspaceRules.Restore(layout, hasEditorContent: true, _setup.PaneIds,
            id => Row(id).StartsClosed, id => (Row(id).DefaultHost, Row(id).DefaultAnchor), emptiedFloating);
        if (SuppressInstrumentPanel) DockWorkspaceRules.Close(State, InstrumentPanelId);
        Changed();
    }

    /// <summary>The open floating windows by floating id.</summary>
    public IReadOnlyDictionary<string, DockFloatingWindow> FloatingWindows => _floating;

    /// <summary>Takes a visible, movable panel out of the layout into a new floating window near the main window.</summary>
    public bool FloatPanel(string id)
    {
        if (!IsRegistered(id) || IsInstrumentSuppressed(id) || !DockPanePresenter.CanMove(id) || !DockWorkspaceRules.IsVisible(State, id)) return false;
        var origin = _owner is null ? new PixelPoint() : _owner.Position;
        var scaling = _owner?.DesktopScaling ?? 1;
        return FloatPanelAt(id, new Point(origin.X / scaling + 180, origin.Y / scaling + 140));
    }

    /// <summary>Opens a visible, movable panel (docked or floating) in a new floating window whose top-left is at this DIP screen point.</summary>
    public bool FloatPanelAt(string id, Point dipTopLeft)
    {
        if (!IsRegistered(id) || IsInstrumentSuppressed(id) || !DockPanePresenter.CanMove(id) || !DockWorkspaceRules.IsVisible(State, id)) return false;
        var row = Row(id);
        var bounds = new UiRect(dipTopLeft.X, dipTopLeft.Y, Math.Max(360, row.MinWidth), Math.Max(260, row.MinHeight));
        FloatingSessionRules.OpenFloating(State, id, Guid.NewGuid().ToString("N"), bounds, out _);
        Changed();
        return true;
    }

    global::Avalonia.Styling.ThemeVariant IDockPaneDragHost.Theme => _owner?.ActualThemeVariant ?? global::Avalonia.Styling.ThemeVariant.Dark;

    /// <summary>True while a layout change waits for its delay.</summary>
    public bool IsLayoutTimerRunning => _timer.IsEnabled;

    /// <summary>Stops the layout timer, cancels a running tab drag and closes the ghost and every floating window (the main window closed).</summary>
    public void Dispose()
    {
        _timer.Stop();
        _drag.Cancel();
        _drag.CloseGhost();
        foreach (var window in _floating.Values.ToList()) window.CloseFromWorkspace();
        _floating.Clear();
    }

    /// <summary>Raises <see cref="LayoutChanged"/> now when a change waits for its delay.</summary>
    public void FlushLayoutChanged()
    {
        _timer.Stop();
        if (!_changePending) return;
        _changePending = false;
        LayoutChanged?.Invoke(DockWorkspaceRules.Capture(State));
    }

    private void Changed()
    {
        // Perf E2b (H4/H5): batch sequential Changed calls (band/compact entry hides N side panels) into one Show.
        if (_batchDepth > 0 || _isShowing) { _batchDirty = true; return; }
        _isShowing = true;
        try
        {
            LayoutRebuilding?.Invoke();
            _view.Show(State);
            SyncFloatingWindows();
            LayoutRebuilt?.Invoke();
        }
        finally { _isShowing = false; }
        NotifyChanged();
        if (_batchDirty) { _batchDirty = false; Changed(); }
    }

    /// <summary>
    /// Perf E2b (H4/H5): defers <see cref="Changed"/> rebuilds until the returned scope ends, so a multi-panel
    /// entry (Band/compact hides N side panels + applies a layout) pays one Show instead of N. Re-entrant and nestable; UI thread only.
    /// </summary>
    public IDisposable DeferRefresh()
    {
        _batchDepth++;
        return new RefreshScope(this);
    }

    /// <summary>Perf E2b (H4/H5): runs <paramref name="action"/> with rebuilds deferred (see <see cref="DeferRefresh"/>).</summary>
    public void Batch(Action action)
    {
        using (DeferRefresh()) action();
    }

    /// <summary>
    /// Perf E2b (H4/H5): shows or hides several panels with one rebuild. False when an id is unknown (the tree is untouched).
    /// </summary>
    public bool SetPanelsVisible(IEnumerable<string> ids, bool visible)
    {
        var list = ids.ToList();
        if (list.Any(id => !IsRegistered(id))) return false;
        using (DeferRefresh())
        {
            foreach (var id in list) SetPanelVisible(id, visible);
        }
        return true;
    }

    private sealed class RefreshScope : IDisposable
    {
        private readonly DockWorkspaceController _owner;
        private bool _done;
        public RefreshScope(DockWorkspaceController owner) => _owner = owner;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _owner._batchDepth--;
            if (_owner._batchDepth <= 0)
            {
                _owner._batchDepth = 0;
                if (_owner._batchDirty) { _owner._batchDirty = false; _owner.Changed(); }
            }
        }
    }

    private void ShowSavedFloating()
    {
        FloatingSessionRules.FitToWorkAreas(State, _workAreas ?? new AvaloniaMonitorWorkAreas(_owner!), MinFloatingWidth, MinFloatingHeight);
        SyncFloatingWindows();
    }

    /// <summary>Closes the windows whose floating root is gone, re-shows the others and opens the new ones (only once the owner is shown).</summary>
    private void SyncFloatingWindows()
    {
        if (_owner is not { IsVisible: true } owner) return;
        foreach (var (id, window) in _floating.ToList())
        {
            if (State.Floating.Any(f => f.Id == id)) continue;
            window.CloseFromWorkspace();
            _floating.Remove(id);
        }
        foreach (var floating in State.Floating)
        {
            if (!_floating.TryGetValue(floating.Id, out var window))
            {
                window = new DockFloatingWindow(floating.Id, new DockWorkspaceView(_setup.ContentOf) { NaturalHeightIds = _setup.NaturalHeightIds, MenuOf = _view.MenuOf });
                window.View.Edited += NotifyChanged;
                window.View.SurfaceBuilt += _drag.Hook;
                window.AddHandler(InputElement.KeyDownEvent, _drag.OnKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
                window.RequestedThemeVariant = owner.ActualThemeVariant;
                // The floating window follows the owner's theme for its whole life (a light/dark switch repaints it), then drops the hook.
                void FollowTheme(object? sender, EventArgs e) => window.RequestedThemeVariant = owner.ActualThemeVariant;
                owner.ActualThemeVariantChanged += FollowTheme;
                window.Closed += (_, _) => owner.ActualThemeVariantChanged -= FollowTheme;
                window.Place(floating);
                _floating[floating.Id] = window;
                window.Show(owner);
                window.ClosingByUser += OnFloatingClosedByUser;
                window.BoundsChanged += OnFloatingMoved;   // after Show, so opening does not read a half-made window back into the state
            }
            window.Title = DockPanePresenter.FloatingTitle(DockWorkspaceState.EnumeratePanelIds(floating.Root), id => Row(id).PaneTitle);
            window.View.Show(new DockWorkspaceState { Root = floating.Root });
        }
    }

    private void OnFloatingClosedByUser(DockFloatingWindow window)
    {
        FloatingSessionRules.CloseByUser(State, window.Id, id => (Row(id).DefaultHost, Row(id).DefaultAnchor));
        Changed();
    }

    private void OnFloatingMoved(DockFloatingWindow window)
    {
        if (State.Floating.FirstOrDefault(f => f.Id == window.Id) is not { } floating) return;
        var bounds = window.DipBounds;
        (floating.Left, floating.Top, floating.Width, floating.Height) = (bounds.X, bounds.Y, bounds.Width, bounds.Height);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        foreach (var id in _startRatios.Keys) _draggedHeights[id] = MovedHeight(id) is null ? null : RatioHeight(id);   // the drag's end, before any window resize
        _changePending = true;
        _timer.Stop();
        _timer.Start();
    }
}
