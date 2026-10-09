using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TabForge.Docking;
using static TabForge.Docking.DockLayoutTree;
using static TabForge.Docking.DockVisuals;
using static TabForge.Docking.FloatingWindowPlacement;

namespace TabForge.Views;

/// <summary>
/// Reusable WPF split/tab workspace. Registered content has one owner and is moved between
/// ContentPresenters; only the lightweight split/tab chrome is rebuilt after a completed drop.
/// </summary>
public sealed class DockWorkspace : Grid, IDockHost, IDockDragHost
{
    private const double SplitterSize = DockDragController.SplitterSize;
    private const double GrabPad = 2;

    /// <summary>Splitter look: a transparent hit area (SplitterSize + 2 x GrabPad) with the visible line inset to SplitterSize.</summary>
    private static ControlTemplate CreateSplitterTemplate(bool horizontal)
    {
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.MarginProperty, horizontal ? new Thickness(GrabPad, 0, GrabPad, 0) : new Thickness(0, GrabPad, 0, GrabPad));
        line.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        root.AppendChild(line);
        return new ControlTemplate(typeof(GridSplitter)) { VisualTree = root };
    }
    private const double EdgeFraction = DockDragController.EdgeFraction;
    private readonly Window _owner;
    private readonly Dictionary<string, DockPanelRegistration> _panels = new(StringComparer.Ordinal);
    private readonly List<DockHostView> _hosts = new();
    private readonly List<DockSlot> _slots = new();
    private readonly Dictionary<string, DockFloatingWindow> _floatWindows = new(StringComparer.Ordinal);
    private DockWorkspaceState _state = new();
    private readonly DockDragController _drag;
    private readonly DispatcherTimer _geometrySaveTimer;
    private bool _restoring;

    public DockWorkspace(Window owner)
    {
        _owner = owner;
        _drag = new DockDragController(this);
        _geometrySaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _geometrySaveTimer.Tick += (_, _) =>
        {
            _geometrySaveTimer.Stop();
            NotifyLayoutChanged();
        };
        SetResourceReference(BackgroundProperty, "WindowBrush");
        // Clip like ClipToBounds, but leave a thin band above the top edge so a header-less fretboard
        // docked at the top can overhang its markers onto the menu bar. Recomputed only on resize.
        SizeChanged += (_, _) => UpdateMainOverflow();
        SizeChanged += (_, _) => ClampFloatingWindows();
        Loaded += (_, _) => ShowFloatingWindows();
        owner.Closed += (_, _) => CloseFloatWindows();
    }

    public event EventHandler? LayoutChanged;

    public static DockNodeState EditorNode(string hostId = "score-editor") => DockLayoutTree.EditorNode(hostId);

    public static DockNodeState Tabs(string hostId, params string[] panelIds) => DockLayoutTree.Tabs(hostId, panelIds);

    public static DockNodeState Split(string orientation, double ratio, DockNodeState first, DockNodeState second) =>
        DockLayoutTree.Split(orientation, ratio, first, second);

    bool IDockHost.TryGetPanel(string id, out DockPanelRegistration panel) => _panels.TryGetValue(id, out panel!);
    Window IDockDragHost.Owner => _owner;
    IReadOnlyList<DockHostView> IDockDragHost.Hosts => _hosts;
    IReadOnlyList<DockSlot> IDockDragHost.Slots => _slots;
    IReadOnlyList<DockFloatingState> IDockDragHost.Floating => _state.Floating;
    DockPanelRegistration IDockDragHost.Panel(string id) => _panels[id];
    void IDockDragHost.SelectPanel(string id) => SelectPanel(id);
    Size IDockDragHost.MinimumSize(DockNodeState node) => MinimumSize(node);
    double IDockDragHost.SoloHostMinHeight(string panelId) => SoloHostMinHeight(panelId);
    void IDockDragHost.DropOn(DockDropDestination destination, string panelId) => DropOn(destination, panelId);
    void IDockDragHost.FloatAt(string panelId, Point screenPoint) => FloatAt(panelId, screenPoint);
    bool IDockHost.IsDragging => _drag.IsDragging;
    void IDockHost.RenderContextMenu(DockTabSurface surface) => RenderContextMenu(surface);
    ContextMenu IDockHost.BuildPanelContextMenu(string panelId) => BuildPanelContextMenu(panelId);
    void IDockHost.OnHostSelectionChanged(DockNodeState node, string panelId) => OnHostSelectionChanged(node, panelId);
    void IDockHost.OnTabDragStarted(DockTabSurface source, MouseButtonEventArgs e) => OnTabDragStarted(source, e);
    void IDockHost.OnTabDragMoved(DockTabSurface source, MouseEventArgs e) => OnTabDragMoved(source, e);
    void IDockHost.OnTabDragEnded(DockTabSurface source, MouseButtonEventArgs e) => OnTabDragEnded(source, e);

    private bool ValidateState(DockWorkspaceState state) => Validate(state, _editorContent is not null, _panels.ContainsKey);


    public void RegisterPanel(string id, string title, FrameworkElement content,
        double minWidth = 190, double minHeight = 120, string defaultHost = "side", string defaultAnchor = "tools", bool startsClosed = false)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A dock panel needs a stable id.", nameof(id));
        if (_panels.ContainsKey(id)) throw new InvalidOperationException($"Dock panel '{id}' is already registered.");
        DetachFromParent(content);
        _panels.Add(id, new DockPanelRegistration(id, title, content,
            Math.Max(120, minWidth), Math.Max(40, minHeight), defaultHost, defaultAnchor, StartsClosed: startsClosed));
    }

    /// <summary>
    /// Sets the height a panel's content needs to draw completely (the tab host's border and, when shown,
    /// its tab header are added on top). It is a hard minimum: splitters, saved layouts, docking and window
    /// resizing cannot make the pane smaller; when the window is too short the dock scrolls instead.
    /// Updates the live layout in place (no rebuild); cheap to call with an unchanged value.
    /// </summary>
    public void SetPanelContentMinHeight(string id, double contentHeight)
    {
        if (!_panels.TryGetValue(id, out var panel) || !double.IsFinite(contentHeight)) return;
        var minHeight = Math.Max(40, Math.Ceiling(contentHeight));
        if (panel.ContentSized && Math.Abs(panel.MinHeight - minHeight) < 0.5) return;
        _panels[id] = panel with { MinHeight = minHeight, ContentSized = true };
        RefreshMinimums();
    }

    /// <summary>
    /// Locks a panel's content height (null unlocks). While locked, the split beside its tab host gives that
    /// row a fixed pixel height and its splitter cannot be dragged; the neighbouring panes absorb resizes.
    /// Rebuilds the layout (lock changes are rare, never per frame).
    /// </summary>
    public void SetPanelFixedHeight(string id, double? contentHeight)
    {
        if (!_panels.TryGetValue(id, out var panel)) return;
        double? value = contentHeight is { } h && double.IsFinite(h) && h > 0 ? Math.Ceiling(h) : null;
        if (Nullable.Equals(panel.FixedHeight, value)) return;
        _panels[id] = panel with { FixedHeight = value };
        RebuildVisualTree();
    }

    /// <summary>
    /// Sets the tallest content height a panel is useful at (the fretboard at its maximum stretch); null = no limit.
    /// The row of its tab host cannot grow past it (splitter drags and window resizes alike); the neighbour takes the rest.
    /// </summary>
    public void SetPanelContentMaxHeight(string id, double? contentHeight)
    {
        if (!_panels.TryGetValue(id, out var panel)) return;
        double? value = contentHeight is { } h && double.IsFinite(h) && h > 0 ? Math.Ceiling(h) : null;
        if (Nullable.Equals(panel.MaxHeight, value)) return;
        _panels[id] = panel with { MaxHeight = value };
        RefreshMinimums();
    }

    /// <summary>Tallest pixel height of a tab host whose every panel has a maximum (chrome included), or null.</summary>
    private double? MaxHostHeight(DockNodeState node)
    {
        if (node.Kind != "tabs" || node.Panels.Count == 0) return null;
        double max = 0;
        foreach (var id in node.Panels)
        {
            if (!_panels.TryGetValue(id, out var p) || p.MaxHeight is not { } m) return null;
            max = Math.Max(max, m);
        }
        var registrations = node.Panels.Where(_panels.ContainsKey).ToArray();
        var headerCollapsed = registrations.Length == 1 && DockHostView.IsSoloPanel(registrations[0]);
        var chrome = 2 + (headerCollapsed ? 0 : DockHostView.TabHeaderMinHeight);
        var min = node.Panels.Where(_panels.ContainsKey).Max(id => _panels[id].MinHeight);
        return Math.Max(max, min) + chrome;
    }

    /// <summary>Pixel height of a tab host holding a size-locked panel (chrome included), or null.</summary>
    private double? FixedHostHeight(DockNodeState node)
    {
        if (node.Kind != "tabs") return null;
        double? fixedContent = null;
        foreach (var id in node.Panels)
            if (_panels.TryGetValue(id, out var p) && p.FixedHeight is { } f) fixedContent = Math.Max(fixedContent ?? 0, f);
        if (fixedContent is null) return null;
        foreach (var id in node.Panels)   // a locked height never leaves empty space around a panel with a maximum
            if (_panels.TryGetValue(id, out var q) && q.MaxHeight is { } cap) fixedContent = Math.Max(Math.Min(fixedContent.Value, cap), q.MinHeight);
        var registrations = node.Panels.Where(_panels.ContainsKey).ToArray();
        var headerCollapsed = registrations.Length == 1 && DockHostView.IsSoloPanel(registrations[0]);
        var chrome = 2 + (headerCollapsed ? 0 : DockHostView.TabHeaderMinHeight);
        return fixedContent.Value + chrome;
    }

    public double PanelMinHeight(string id) => _panels.TryGetValue(id, out var panel) ? panel.MinHeight : 0;

    /// <summary>Re-applies every split's row/column minimums from the registrations (after a minimum changed).</summary>
    private void RefreshMinimums()
    {
        var splitters = FindSplitters(this)
            .Concat(_floatWindows.Values.SelectMany(w => FindSplitters(w.ContentRoot))).ToList();
        foreach (var splitter in splitters)
        {
            if (splitter.Tag is not DockNodeState { First: { } first, Second: { } second } node ||
                VisualTreeHelper.GetParent(splitter) is not Grid split) continue;
            var a = MinimumSize(first);
            var b = MinimumSize(second);
            if (string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase))
            {
                if (split.ColumnDefinitions.Count < 3) continue;
                split.ColumnDefinitions[0].MinWidth = a.Width;
                split.ColumnDefinitions[2].MinWidth = b.Width;
            }
            else
            {
                if (split.RowDefinitions.Count < 3) continue;
                split.RowDefinitions[0].MinHeight = a.Height;
                split.RowDefinitions[2].MinHeight = b.Height;
                split.RowDefinitions[0].MaxHeight = MaxHostHeight(first) ?? double.PositiveInfinity;
                split.RowDefinitions[2].MaxHeight = MaxHostHeight(second) ?? double.PositiveInfinity;
            }
        }
        foreach (var floating in _state.Floating)
        {
            if (floating.Root is null || !_floatWindows.TryGetValue(floating.Id, out var window)) continue;
            var min = MinimumSize(floating.Root);
            window.MinWidth = min.Width;
            window.MinHeight = min.Height;
        }
        if (_mainSlotRoot is not null) _mainSlotRoot.MinHeight = _state.Root is null ? 0 : MinimumSize(_state.Root).Height;
        UpdateMainOverflow();
    }

    // ---------- main dock overflow: scroll rather than squeeze a pane below its minimum ----------

    private Grid? _mainSlotRoot;
    private Canvas? _mainSlotPreview;
    private System.Windows.Controls.Primitives.ScrollBar? _overflowBar;
    private double _overflowOffset;

    /// <summary>When the panes' minimum heights exceed the workspace, the dock scrolls vertically (resize only, not per frame).</summary>
    private void UpdateMainOverflow()
    {
        var height = ActualHeight;
        var needed = _mainSlotRoot?.MinHeight ?? 0;
        var overflow = height > 0 ? Math.Max(0, Math.Ceiling(needed - height)) : 0;
        if (overflow > 0)
        {
            if (_overflowBar is null)
            {
                _overflowBar = new System.Windows.Controls.Primitives.ScrollBar
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Width = SystemParameters.VerticalScrollBarWidth,
                    SmallChange = 24,
                    ToolTip = "The window is too short for every panel's minimum height: scroll the workspace"
                };
                _overflowBar.ValueChanged += (_, e) => { _overflowOffset = e.NewValue; ApplyMainOffset(); };
                Panel.SetZIndex(_overflowBar, 2000);
            }
            if (!Children.Contains(_overflowBar)) Children.Add(_overflowBar);
            _overflowBar.Minimum = 0;
            _overflowBar.Maximum = overflow;
            _overflowBar.ViewportSize = height;
            _overflowBar.LargeChange = Math.Max(24, height * 0.8);
            _overflowOffset = Math.Clamp(_overflowBar.Value, 0, overflow);
            _overflowBar.Visibility = Visibility.Visible;
        }
        else
        {
            _overflowOffset = 0;
            if (_overflowBar is not null) { _overflowBar.Value = 0; _overflowBar.Visibility = Visibility.Collapsed; }
        }
        ApplyMainOffset();
    }

    private void ApplyMainOffset()
    {
        var scrolling = _overflowBar is { Visibility: Visibility.Visible };
        var margin = scrolling ? new Thickness(0, -_overflowOffset, _overflowBar!.Width, 0) : new Thickness(0);
        if (_mainSlotRoot is not null && _mainSlotRoot.Margin != margin) _mainSlotRoot.Margin = margin;
        if (_mainSlotPreview is not null && _mainSlotPreview.Margin != margin) _mainSlotPreview.Margin = margin;
        // Clip like ClipToBounds, but leave a thin band above the top edge so a header-less fretboard
        // docked at the top can overhang its markers onto the menu bar (not while scrolled: that band
        // would show the panes scrolled out above). Recomputed only on resize / minimum changes.
        const double overhang = 0;   // the fretboard keeps its top markers inside its pane
        var clip = new RectangleGeometry(new Rect(0, -overhang, ActualWidth, ActualHeight + overhang));
        clip.Freeze();
        Clip = clip;
    }

    /// <summary>Registers the protected score surface. It can be split around, but never moved or hidden.</summary>
    public void SetEditorContent(FrameworkElement content, double minWidth = 460, double minHeight = 320)
    {
        DetachFromParent(content);
        _editorContent = content;
        _editorMinSize = new Size(Math.Max(360, minWidth), Math.Max(80, minHeight));
    }

    private FrameworkElement? _editorContent;
    private Size _editorMinSize = new(460, 320);

    public void RestoreLayout(DockWorkspaceState? saved)
    {
        _restoring = true;
        try
        {
            _state = saved?.Root is null ? CreateDefaultState() : Clone(saved);
            DropPanels(_state, _panels.ContainsKey);
            if (!ValidateState(_state)) _state = CreateDefaultState();
            _state.ClosedPanels = _state.ClosedPanels
                .Where(_panels.ContainsKey).Distinct(StringComparer.Ordinal).ToList();

            var present = EnumerateAllPanels(_state).ToHashSet(StringComparer.Ordinal);
            foreach (var id in _panels.Keys)
            {
                if (present.Contains(id) || _state.ClosedPanels.Contains(id, StringComparer.Ordinal) || _panels[id].StartsClosed) continue;
                RestorePanelToDefault(id, notify: false);
            }
            RebuildVisualTree();
        }
        finally { _restoring = false; }
    }

    public DockWorkspaceState CaptureLayout()
    {
        var result = Clone(_state);
        UseUserRatios(result.Root);
        foreach (var floating in result.Floating)
        {
            UseUserRatios(floating.Root);
            if (!_floatWindows.TryGetValue(floating.Id, out var window)) continue;
            floating.Left = window.Left;
            floating.Top = window.Top;
            floating.Width = Math.Max(window.MinWidth, window.Width);
            floating.Height = Math.Max(window.MinHeight, window.Height);
        }
        return result;
    }


    /// <summary>Every panel in a captured layout (docked and floating).</summary>
    public static IEnumerable<string> PanelsOf(DockWorkspaceState state) => EnumerateAllPanels(state);

    public bool IsPanelVisible(string id) => EnumerateAllPanels(_state).Contains(id, StringComparer.Ordinal);

    public bool IsPanelFloating(string id) => _state.Floating.Any(f => f.Root is not null && ContainsPanel(f.Root, id));

    public void SetPanelVisible(string id, bool visible)
    {
        if (!_panels.ContainsKey(id) || IsPanelVisible(id) == visible) return;
        if (visible) RestorePanelToDefault(id, notify: true);
        else ClosePanel(id);
    }

    /// <summary>Hides several panels with one layout rebuild (closing them one by one rebuilt the tree each time).</summary>
    public void HidePanels(IEnumerable<string> ids)
    {
        var any = false;
        foreach (var id in ids)
        {
            if (!_panels.ContainsKey(id) || !IsPanelVisible(id)) continue;
            RemovePanelFromAllRoots(id);
            if (!_state.ClosedPanels.Contains(id, StringComparer.Ordinal)) _state.ClosedPanels.Add(id);
            any = true;
        }
        if (!any) return;
        RebuildVisualTree();
        NotifyLayoutChanged();
    }

    /// <summary>Brings back an earlier <see cref="CaptureLayout"/> exactly (placement, tab order and sizes) in one rebuild.</summary>
    public void ApplyLayout(DockWorkspaceState saved)
    {
        RestoreLayout(saved);
        NotifyLayoutChanged();
    }

    public void SelectPanel(string id)
    {
        if (!IsPanelVisible(id)) return;
        var host = FindPanelHost(_state.Root, id) ?? _state.Floating
            .Select(f => FindPanelHost(f.Root, id)).FirstOrDefault(h => h is not null);
        if (host is null || host.SelectedPanel == id) return;
        host.SelectedPanel = id;
        var view = _hosts.FirstOrDefault(h => h.HostId == host.HostId);
        view?.Select(id, notify: false);
        NotifyLayoutChanged();
    }

    public void ResetPanel(string id)
    {
        if (!_panels.ContainsKey(id)) return;
        RestorePanelToDefault(id, notify: true);
    }

    public void ResetAllPanels()
    {
        _state = CreateDefaultState();
        RebuildVisualTree();
        NotifyLayoutChanged();
    }

    /// <summary>
    /// Programmatic equivalent of a drag/drop operation, useful to host panels from commands and
    /// to validate layout operations without synthesizing pointer input.
    /// </summary>
    public bool DockPanelTo(string panelId, string targetHostId, DockDropZone zone,
        string? floatingId = null, double splitFraction = EdgeFraction, int? tabIndex = null)
    {
        if (!_panels.ContainsKey(panelId) || !IsPanelVisible(panelId)) return false;
        var initialTarget = floatingId is null
            ? FindNode(_state.Root, targetHostId)
            : _state.Floating.FirstOrDefault(f => f.Id == floatingId)?.Root is { } initialFloatRoot
                ? FindNode(initialFloatRoot, targetHostId)
                : null;
        if (initialTarget is null || initialTarget.Kind == "editor" && zone == DockDropZone.Center) return false;
        if (zone == DockDropZone.Center && initialTarget.Kind != "tabs") return false;
        if (initialTarget.Kind == "tabs" && initialTarget.Panels.Count == 1 && initialTarget.Panels[0] == panelId)
            return false;
        if (initialTarget.Kind == "tabs" && initialTarget.Panels.Contains(panelId, StringComparer.Ordinal))
        {
            var oldIndex = initialTarget.Panels.IndexOf(panelId);
            if (tabIndex.HasValue && tabIndex.Value > oldIndex) tabIndex--;
        }

        splitFraction = double.IsFinite(splitFraction) ? Math.Clamp(splitFraction, 0.02, 0.98) : EdgeFraction;
        if (!RemovePanelFromRootsOnly(panelId)) return false;
        _state.ClosedPanels.Remove(panelId);
        var target = floatingId is null
            ? FindNode(_state.Root, targetHostId)
            : _state.Floating.FirstOrDefault(f => f.Id == floatingId)?.Root is { } targetFloatRoot
                ? FindNode(targetFloatRoot, targetHostId)
                : null;
        if (target is null) return false;
        if (zone == DockDropZone.Center)
        {
            AddToTabs(target, panelId, tabIndex);
        }
        else
        {
            var panelNode = Tabs("dock-" + Guid.NewGuid().ToString("N"), panelId);
            var horizontal = zone is DockDropZone.Left or DockDropZone.Right;
            var before = zone is DockDropZone.Left or DockDropZone.Top;
            var split = before
                ? Split(horizontal ? "Horizontal" : "Vertical", splitFraction, panelNode, CloneNode(target)!)
                : Split(horizontal ? "Horizontal" : "Vertical", 1 - splitFraction, CloneNode(target)!, panelNode);
            if (floatingId is null) ReplaceNode(_state, target.HostId, split);
            else
            {
                var floatState = _state.Floating.First(f => f.Id == floatingId);
                floatState.Root = ReplaceNode(floatState.Root, target.HostId, split);
            }
        }
        RebuildVisualTree();
        NotifyLayoutChanged();
        return true;
    }

    public void ShowFloatingWindows()
    {
        foreach (var floating in _state.Floating)
            if (_floatWindows.TryGetValue(floating.Id, out var window) && !window.IsVisible)
            {
                ClampBounds(window, floating);
                window.AttachOwner(_owner);
                window.Show();
                ClampToMonitorWorkArea(window, floating);
            }
        ClampFloatingWindows();
    }

    /// <summary>Compatibility migration for the pre-workspace floating controller setting.</summary>
    public void FloatPanelAt(string id, Point position, double width = 340, double height = 180)
    {
        if (!_panels.ContainsKey(id)) return;
        RemovePanelFromAllRoots(id);
        _state.ClosedPanels.Remove(id);
        var floatState = new DockFloatingState
        {
            Root = Tabs("float-" + Guid.NewGuid().ToString("N"), id),
            Left = position.X,
            Top = position.Y,
            Width = width,
            Height = height
        };
        _state.Floating.Add(floatState);
        RebuildVisualTree();
        NotifyLayoutChanged();
    }

    /// <summary>Where the fretboard / keyboard pane returns to when it is opened or the layout is rebuilt: below the score (above the timeline) instead of above it.</summary>
    public bool InstrumentAtBottom { get; set; }

    /// <summary>Moves the instrument pane above or below the score now (when it is shown) and remembers the side for later openings.</summary>
    public void SetInstrumentPosition(bool bottom)
    {
        InstrumentAtBottom = bottom;
        if (_panels.ContainsKey("instrument") && IsPanelVisible("instrument") && !IsPanelFloating("instrument")) RestorePanelToDefault("instrument", notify: true);
    }

    private void RestorePanelToDefault(string id, bool notify)
    {
        RemovePanelFromAllRoots(id);
        _state.ClosedPanels.Remove(id);
        if (!_panels.TryGetValue(id, out var panel)) return;

        PlaceAtDefault(_state, id, id == "instrument" && InstrumentAtBottom ? "instrument-bottom" : panel.DefaultHost, panel.DefaultAnchor);
        RebuildVisualTree();
        if (notify) NotifyLayoutChanged();
    }

    private void ClosePanel(string id)
    {
        if (!_panels.ContainsKey(id)) return;
        RemovePanelFromAllRoots(id);
        if (!_state.ClosedPanels.Contains(id, StringComparer.Ordinal)) _state.ClosedPanels.Add(id);
        RebuildVisualTree();
        NotifyLayoutChanged();
    }

    private void RemovePanelFromAllRoots(string id)
    {
        _state.Root = RemovePanel(_state.Root, id);
        foreach (var floating in _state.Floating.ToArray())
        {
            floating.Root = RemovePanel(floating.Root, id);
            if (floating.Root is null)
            {
                _state.Floating.Remove(floating);
                if (_floatWindows.Remove(floating.Id, out var window)) window.CloseFromWorkspace();
            }
        }
    }

    private void RebuildVisualTree()
    {
        _drag.EndDrag();
        foreach (var host in _hosts) host.ReleaseContent();
        _hosts.Clear();
        foreach (var slot in _slots) slot.Root.Children.Clear();
        _slots.Clear();

        Children.Clear();
        _floatWindows.Keys.Except(_state.Floating.Select(f => f.Id), StringComparer.Ordinal).ToList()
            .ForEach(id => { var old = _floatWindows[id]; _floatWindows.Remove(id); old.CloseFromWorkspace(); });

        var mainSlot = CreateSlot(this, _owner, isMain: true);
        if (_state.Root is not null) RenderNode(_state.Root, mainSlot.Root, mainSlot);
        _mainSlotRoot = mainSlot.Root;
        _mainSlotPreview = mainSlot.Preview;
        _mainSlotRoot.MinHeight = _state.Root is null ? 0 : MinimumSize(_state.Root).Height;
        UpdateMainOverflow();

        foreach (var floating in _state.Floating.Where(f => f.Root is not null))
        {
            if (!_floatWindows.TryGetValue(floating.Id, out var window))
            {
                window = new DockFloatingWindow(floating.Id);
                window.ClosingByUser += (_, _) => FloatingWindowClosing(floating.Id);
                window.LocationChanged += (_, _) => OnFloatingGeometryChanged(floating.Id);
                window.SizeChanged += (_, _) => OnFloatingGeometryChanged(floating.Id);
                _floatWindows[floating.Id] = window;
            }
            window.Title = FloatingTitle(floating.Root!);
            window.MinWidth = MinimumSize(floating.Root!).Width;
            window.MinHeight = MinimumSize(floating.Root!).Height;
            window.Width = Math.Max(window.MinWidth, double.IsFinite(floating.Width) ? floating.Width : 360);
            window.Height = Math.Max(window.MinHeight, double.IsFinite(floating.Height) ? floating.Height : 260);
            window.Left = double.IsFinite(floating.Left) ? floating.Left : 180;
            window.Top = double.IsFinite(floating.Top) ? floating.Top : 140;
            window.ContentRoot.Children.Clear();
            var slot = CreateSlot(window.ContentRoot, window, isMain: false);
            RenderNode(floating.Root!, slot.Root, slot);
        }
    }

    private DockSlot CreateSlot(Grid parent, Window window, bool isMain)
    {
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "WindowBrush");
        var preview = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
        parent.Children.Add(root);
        Panel.SetZIndex(root, 0);
        Panel.SetZIndex(preview, 1000);
        parent.Children.Add(preview);
        var slot = new DockSlot(root, preview, window, isMain);
        _slots.Add(slot);
        return slot;
    }

    private void RenderNode(DockNodeState node, Grid parent, DockSlot slot)
    {
        switch (node.Kind)
        {
            case "editor":
            {
                var editorFrame = new Border
                {
                    BorderThickness = new Thickness(1),
                    MinWidth = _editorMinSize.Width,
                    MinHeight = _editorMinSize.Height,
                    Child = _editorContent
                };
                editorFrame.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
                editorFrame.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
                parent.Children.Add(editorFrame);
                _hosts.Add(new DockHostView(node.HostId, node, editorFrame, null, isEditor: true, slot));
                break;
            }
            case "tabs":
            {
                var activeIds = node.Panels.Where(_panels.ContainsKey).ToList();
                if (activeIds.Count == 0)
                {
                    var empty = new Border
                    {
                        BorderThickness = new Thickness(1),
                        Child = new TextBlock
                        {
                            Text = "Drop a panel here",
                            Foreground = Brush("MutedBrush", Color.FromRgb(152, 161, 174)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    empty.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
                    empty.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
                    parent.Children.Add(empty);
                    _hosts.Add(new DockHostView(node.HostId, node, empty, null, isEditor: false, slot));
                    break;
                }

                if (!activeIds.Contains(node.SelectedPanel, StringComparer.Ordinal)) node.SelectedPanel = activeIds[0];
                var host = new DockHostView(node.HostId, node, null, this, isEditor: false, slot);
                _hosts.Add(host);
                parent.Children.Add(host.Surface);
                host.BuildTabs(activeIds, _panels);
                break;
            }
            case "split" when node.First is not null && node.Second is not null:
            {
                var horizontal = string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);
                var splitGrid = new Grid();
                splitGrid.SetResourceReference(Panel.BackgroundProperty, "WindowBrush");
                var minFirst = MinimumSize(node.First);
                var minSecond = MinimumSize(node.Second);
                var lockedSplit = false;
                if (horizontal)
                {
                    splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(node.Ratio, 0.02, 0.98), GridUnitType.Star), MinWidth = minFirst.Width });
                    splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterSize) });
                    splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(1 - node.Ratio, 0.02, 0.98), GridUnitType.Star), MinWidth = minSecond.Width });
                }
                else
                {
                    // A size-locked pane (fretboard) gets a fixed pixel row; the other side takes the rest.
                    var fixedFirst = FixedHostHeight(node.First);
                    var fixedSecond = fixedFirst is null ? FixedHostHeight(node.Second) : null;
                    lockedSplit = fixedFirst is not null || fixedSecond is not null;
                    splitGrid.RowDefinitions.Add(new RowDefinition { Height = fixedFirst is { } f1 ? new GridLength(f1) : fixedSecond is not null ? new GridLength(1, GridUnitType.Star) : new GridLength(Math.Clamp(node.Ratio, 0.02, 0.98), GridUnitType.Star), MinHeight = minFirst.Height, MaxHeight = MaxHostHeight(node.First) ?? double.PositiveInfinity });
                    splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
                    splitGrid.RowDefinitions.Add(new RowDefinition { Height = fixedSecond is { } f2 ? new GridLength(f2) : fixedFirst is not null ? new GridLength(1, GridUnitType.Star) : new GridLength(Math.Clamp(1 - node.Ratio, 0.02, 0.98), GridUnitType.Star), MinHeight = minSecond.Height , MaxHeight = MaxHostHeight(node.Second) ?? double.PositiveInfinity });
                }
                var first = new Grid();
                var second = new Grid();
                splitGrid.Children.Add(first);
                splitGrid.Children.Add(second);
                if (horizontal) Grid.SetColumn(second, 2); else Grid.SetRow(second, 2);
                var splitter = new GridSplitter
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ShowsPreview = false,
                    ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
                    ToolTip = "Drag to resize docked panels",
                    Tag = node
                };
                splitter.SetResourceReference(Control.BackgroundProperty, "BorderSoftBrush");
                // Comfortable grab area: the hit area extends GrabPad px over each neighbour (transparent), the drawn line stays SplitterSize.
                splitter.Margin = horizontal ? new Thickness(-GrabPad, 0, -GrabPad, 0) : new Thickness(0, -GrabPad, 0, -GrabPad);
                splitter.Template = CreateSplitterTemplate(horizontal);
                Panel.SetZIndex(splitter, 5);
                if (lockedSplit)
                {
                    // Not draggable while the fretboard size is locked; the tooltip says how to unlock.
                    splitter.IsEnabled = false;
                    splitter.Cursor = Cursors.Arrow;
                    ToolTipService.SetShowOnDisabled(splitter, true);
                    splitter.ToolTip = "Fretboard size locked: right-click the fretboard and untick \"Lock fretboard size\" to resize it";
                }
                splitter.DragDelta += (_, _) =>
                {
                    // GridSplitter has just rewritten the row/column sizes; lay out first so the clamp reads the
                    // new sizes. Reading the stale ActualHeight would put the old size back and undo every drag.
                    (VisualTreeHelper.GetParent(splitter) as Grid)?.UpdateLayout();
                    EnforceSplitMinimums(splitter, dragging: true);
                    RaiseSplitter(splitter, DockSplitterPhase.Delta);
                };
        splitter.DragStarted += (_, _) => { EnforceSplitMinimums(splitter, dragging: true); RaiseSplitter(splitter, DockSplitterPhase.Started); };
        splitter.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || !splitter.IsEnabled) return;
            e.Handled = true;   // a double-click is a command on the split, not the start of a drag
            RaiseSplitter(splitter, DockSplitterPhase.DoubleClick);
        };
        splitter.DragCompleted += Splitter_DragCompleted;
                splitGrid.Children.Add(splitter);
                if (horizontal) Grid.SetColumn(splitter, 1); else Grid.SetRow(splitter, 1);
                parent.Children.Add(splitGrid);
                RenderNode(node.First, first, slot);
                RenderNode(node.Second, second, slot);
                break;
            }
        }
    }

    /// <summary>
    /// Resizes the vertical split whose lower pane is exactly <paramref name="panelId"/>'s tab host so that
    /// pane gets <paramref name="height"/> pixels (clamped to the split's limits).
    /// </summary>
    public bool FitPanelHeight(string panelId, double height)
    {
        UpdateLayout();
        foreach (var splitter in FindSplitters(this))
        {
            if (splitter.Tag is not DockNodeState node || !splitter.IsEnabled ||
                string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase) ||
                VisualTreeHelper.GetParent(splitter) is not Grid split) continue;
            var inFirst = node.First is { Kind: "tabs" } first && first.Panels.Contains(panelId, StringComparer.Ordinal);
            var inSecond = node.Second is { Kind: "tabs" } second && second.Panels.Contains(panelId, StringComparer.Ordinal);
            if (!inFirst && !inSecond) continue;
            var available = split.ActualHeight - SplitterSize;
            if (available <= 0) return false;
            // Never below the pane's own minimum (e.g. the fretboard's full-draw height).
            height = Math.Max(Math.Max(0, height), MinimumSize(inFirst ? node.First! : node.Second!).Height);
            // The fit follows this window's size, so it changes the live split only: the user's ratio is what gets saved.
            node.UserRatio ??= node.Ratio;
            node.Ratio = Math.Clamp(inFirst ? height / available : (available - height) / available, 0.02, 0.98);
            if (inFirst) split.RowDefinitions[0].MinHeight = height;
            else split.RowDefinitions[2].MinHeight = height;
            split.RowDefinitions[0].Height = new GridLength(node.Ratio, GridUnitType.Star);
            split.RowDefinitions[2].Height = new GridLength(1 - node.Ratio, GridUnitType.Star);
            return true;   // no LayoutChanged: nothing the user did, so nothing is saved
        }
        return false;
    }

    private static IEnumerable<GridSplitter> FindSplitters(DependencyObject? root)
    {
        if (root is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is GridSplitter splitter) yield return splitter;
            foreach (var nested in FindSplitters(child)) yield return nested;
        }
    }

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (sender is not GridSplitter { Tag: DockNodeState node } splitter) return;
        var split = VisualTreeHelper.GetParent(splitter) as Grid;
        if (split is null) return;
        EnforceSplitMinimums(splitter);
        split.UpdateLayout();
        node.UserRatio = null;   // the user's drag is now the ratio to save
        if (string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase))
        {
            var available = split.ActualWidth - SplitterSize;
            if (available > 0) node.Ratio = Math.Clamp(split.ColumnDefinitions[0].ActualWidth / available, 0.02, 0.98);
        }
        else
        {
            var available = split.ActualHeight - SplitterSize;
            if (available > 0) node.Ratio = Math.Clamp(split.RowDefinitions[0].ActualHeight / available, 0.02, 0.98);
        }
        NotifyLayoutChanged();
        RaiseSplitter(splitter, DockSplitterPhase.Completed);
    }

    /// <summary>A splitter between two pane groups was dragged (start, each move, end) or double-clicked.</summary>
    public event EventHandler<DockSplitterEventArgs>? SplitterInteraction;

    private readonly Dictionary<string, Func<(double Min, double Max)?>> _heightLimits = new();

    /// <summary>Limits (min, max px) for the height of the pane group holding <paramref name="panelId"/> while its splitter is dragged; null from the function = no limit.</summary>
    public void SetPanelHeightLimits(string panelId, Func<(double Min, double Max)?>? limits)
    {
        if (limits is null) _heightLimits.Remove(panelId); else _heightLimits[panelId] = limits;
    }

    private void RaiseSplitter(GridSplitter splitter, DockSplitterPhase phase)
    {
        if (SplitterInteraction is null || splitter.Tag is not DockNodeState node) return;
        static string[] Panels(DockNodeState? n) => n is { Kind: "tabs" } ? n.Panels.ToArray() : Array.Empty<string>();
        SplitterInteraction(this, new DockSplitterEventArgs(phase,
            !string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase), Panels(node.First), Panels(node.Second)));
    }

    /// <summary>Test hook: does what dragging the splitter above <paramref name="panelId"/> to <paramref name="height"/> px does (live layout, minimum clamp, events).</summary>
    internal bool SimulateSplitterDrag(string panelId, double height, bool complete)
    {
        foreach (var splitter in FindSplitters(this))
        {
            if (splitter.Tag is not DockNodeState { Second: { Kind: "tabs" } second } node || !splitter.IsEnabled ||
                string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase) ||
                !second.Panels.Contains(panelId, StringComparer.Ordinal) || VisualTreeHelper.GetParent(splitter) is not Grid split) continue;
            var available = split.ActualHeight - SplitterSize;
            split.RowDefinitions[0].MinHeight = 0; split.RowDefinitions[2].MinHeight = 0;
            split.RowDefinitions[0].Height = new GridLength(Math.Max(0, available - height), GridUnitType.Star);
            split.RowDefinitions[2].Height = new GridLength(Math.Max(0, height), GridUnitType.Star);
            split.UpdateLayout();
            EnforceSplitMinimums(splitter, dragging: true);
            split.UpdateLayout();
            RaiseSplitter(splitter, DockSplitterPhase.Delta);
            if (complete) Splitter_DragCompleted(splitter, null!);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Hard clamp for an interactive splitter drag: neither side may go below its pane minimum (GridSplitter
    /// alone let a star row shrink under its MinHeight). Rewrites both star sizes from the clamped size.
    /// </summary>
    private void EnforceSplitMinimums(GridSplitter splitter, bool dragging = false)
    {
        if (splitter.Tag is not DockNodeState { First: { } first, Second: { } second } node ||
            VisualTreeHelper.GetParent(splitter) is not Grid split) return;
        var horizontal = string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);
        if (horizontal ? split.ColumnDefinitions.Count < 3 : split.RowDefinitions.Count < 3) return;
        var minA = horizontal ? MinimumSize(first).Width : MinimumSize(first).Height;
        var minB = horizontal ? MinimumSize(second).Width : MinimumSize(second).Height;
        var available = (horizontal ? split.ActualWidth : split.ActualHeight) - SplitterSize;
        if (available <= 0) return;
        var sizeA = horizontal ? split.ColumnDefinitions[0].ActualWidth : split.RowDefinitions[0].ActualHeight;
        var sizeB = horizontal ? split.ColumnDefinitions[2].ActualWidth : split.RowDefinitions[2].ActualHeight;
        var total = sizeA + sizeB > 0 ? sizeA + sizeB : available;
        // A lower pane with its own height limits (the track list, with auto-fit on) may not go below the height that shows
        // all its rows at the smallest row height, nor above the height that shows them at the largest. When the other
        // panes' minimums leave no room for the lower limit, the lower pane gets what they allow (its content scrolls).
        var lowerBound = minA;
        if (!horizontal && second is { Kind: "tabs" } lowerTabs)
            foreach (var id in lowerTabs.Panels)
                if (_heightLimits.TryGetValue(id, out var limits) && limits() is { } limit)
                {
                    minB = Math.Max(minB, Math.Min(limit.Min, Math.Max(0, total - minA)));
                    lowerBound = Math.Max(minA, total - Math.Max(minB, limit.Max));
                }
        var upperBound = total - minB;
        if (!horizontal)
        {
            // A pane with a maximum (fretboard at full stretch) may not grow past it: the neighbour takes the rest.
            if (MaxHostHeight(first) is { } maxA) upperBound = Math.Min(upperBound, Math.Max(maxA, minA));
            if (MaxHostHeight(second) is { } maxB) lowerBound = Math.Max(lowerBound, Math.Min(total - Math.Max(maxB, minB), upperBound));
        }
        var clamped = minA + minB <= total ? Math.Clamp(sizeA, Math.Min(lowerBound, upperBound), upperBound) : minA;
        var rest = Math.Max(minB, total - clamped);
        if (horizontal)
        {
            split.ColumnDefinitions[0].MinWidth = minA; split.ColumnDefinitions[2].MinWidth = minB;
            split.ColumnDefinitions[0].Width = new GridLength(clamped, GridUnitType.Star);
            split.ColumnDefinitions[2].Width = new GridLength(rest, GridUnitType.Star);
        }
        else
        {
            // Only write what differs: an unchanged row definition still dirties the grid and costs a second layout pass per drag step.
            var row0 = split.RowDefinitions[0]; var row2 = split.RowDefinitions[2];
            var min0 = dragging ? lowerBound : minA;
            if (Math.Abs(row0.MinHeight - min0) > 0.001) row0.MinHeight = min0;
            if (Math.Abs(row2.MinHeight - minB) > 0.001) row2.MinHeight = minB;
            if (!(row0.Height.IsStar && Math.Abs(row0.Height.Value - clamped) < 0.001)) row0.Height = new GridLength(clamped, GridUnitType.Star);
            if (!(row2.Height.IsStar && Math.Abs(row2.Height.Value - rest) < 0.001)) row2.Height = new GridLength(rest, GridUnitType.Star);
        }
    }


    private void DropOn(DockDropDestination destination, string panelId)
    {
        DockPanelTo(panelId, destination.HostId, destination.Zone, destination.FloatingId,
            destination.Fraction, destination.TabIndex);
    }

    private void FloatAt(string panelId, Point screenPoint)
    {
        if (!RemovePanelFromRootsOnly(panelId)) return;
        _state.ClosedPanels.Remove(panelId);
        var size = _panels[panelId];
        var (left, top) = ScreenPointToDips(screenPoint);
        var floating = new DockFloatingState
        {
            Root = Tabs("float-" + Guid.NewGuid().ToString("N"), panelId),
            Left = left - 90,
            Top = top - 22,
            Width = Math.Max(320, size.MinWidth),
            Height = Math.Max(220, size.MinHeight)
        };
        _state.Floating.Add(floating);
        RebuildVisualTree();
        ShowFloatingWindows();
        NotifyLayoutChanged();
    }

    private (double Left, double Top) ScreenPointToDips(Point point)
    {
        try
        {
            var transform = PresentationSource.FromVisual(_owner)?.CompositionTarget?.TransformFromDevice;
            if (transform.HasValue)
            {
                var dips = transform.Value.Transform(point);
                return (dips.X, dips.Y);
            }
        }
        catch (InvalidOperationException) { } // window closing: its presentation source is gone
        return (point.X, point.Y);
    }

    private bool RemovePanelFromRootsOnly(string id)
    {
        if (!EnumerateAllPanels(_state).Contains(id, StringComparer.Ordinal)) return false;
        RemovePanelFromAllRoots(id);
        return true;
    }

    private void FloatingWindowClosing(string id)
    {
        if (_restoring || !_state.Floating.Any(f => f.Id == id)) return;
        var floating = _state.Floating.First(f => f.Id == id);
        var panels = EnumeratePanels(floating.Root).ToArray();
        _state.Floating.Remove(floating);
        _floatWindows.Remove(id);
        foreach (var panel in panels) RestorePanelToDefault(panel, notify: false);
        RebuildVisualTree();
        NotifyLayoutChanged();
    }

    private void OnFloatingGeometryChanged(string id)
    {
        if (_restoring || !_floatWindows.TryGetValue(id, out var window)) return;
        var state = _state.Floating.FirstOrDefault(f => f.Id == id);
        if (state is null || !window.IsVisible || window.WindowState != WindowState.Normal) return;
        state.Left = window.Left;
        state.Top = window.Top;
        state.Width = window.Width;
        state.Height = window.Height;
        _geometrySaveTimer.Stop();
        _geometrySaveTimer.Start();
    }

    private void ClampFloatingWindows()
    {
        foreach (var floating in _state.Floating)
            if (_floatWindows.TryGetValue(floating.Id, out var window) && window.WindowState == WindowState.Normal)
                ClampToMonitorWorkArea(window, floating);
    }

    private void CloseFloatWindows()
    {
        foreach (var window in _floatWindows.Values.ToArray()) window.CloseFromWorkspace();
        _floatWindows.Clear();
    }

    private void NotifyLayoutChanged()
    {
        if (!_restoring) LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ContextReset(string id) => ResetPanel(id);

    private void RenderContextMenu(DockTabSurface surface) => surface.ContextMenu = BuildPanelContextMenu(surface.PanelId);

    /// <summary>The pane's own menu items (reset / close panel), for panels that extend the menu with items of their own.</summary>
    internal List<Control> PanelMenuItems(string panelId)
    {
        var menu = BuildPanelContextMenu(panelId);
        var items = menu.Items.Cast<Control>().ToList();
        menu.Items.Clear();
        return items;
    }

    private ContextMenu BuildPanelContextMenu(string panelId)
    {
        var menu = new ContextMenu
        {
            Background = Brush("Panel2Brush", Color.FromRgb(30, 33, 39)),
            Foreground = Brush("TextBrush", Color.FromRgb(231, 234, 239))
        };
        var reset = new MenuItem { Header = "Reset this panel to default position" };
        reset.Click += (_, _) => ContextReset(panelId);
        menu.Items.Add(reset);
        var close = new MenuItem { Header = "Close panel" };
        close.Click += (_, _) => ClosePanel(panelId);
        menu.Items.Add(close);
        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = "Reset all panels to default positions" };
        all.Click += (_, _) => ResetAllPanels();
        menu.Items.Add(all);
        return menu;
    }

    private void OnHostSelectionChanged(DockNodeState node, string panelId)
    {
        node.SelectedPanel = panelId;
        NotifyLayoutChanged();
    }

    private void OnTabDragStarted(DockTabSurface source, MouseButtonEventArgs e) => _drag.ArmDrag(source, e);
    private void OnTabDragMoved(DockTabSurface source, MouseEventArgs e) => _drag.MoveDrag(source, e);
    private void OnTabDragEnded(DockTabSurface source, MouseButtonEventArgs e) => _drag.EndTabDrag(source, e);

    private Size MinimumSize(DockNodeState node)
    {
        if (node.Kind == "editor") return _editorMinSize;
        if (node.Kind == "tabs")
        {
            if (node.Panels.Count == 0) return new Size(170, 100);
            var registrations = node.Panels.Where(_panels.ContainsKey).Select(id => _panels[id]).ToArray();
            if (registrations.Length == 0) return new Size(170, 100);
            // Content-sized panels (the fretboard) give the content height: add the host's 1 px border
            // top and bottom, and the tab header row unless the header is collapsed (solo panel).
            var headerCollapsed = registrations.Length == 1 && DockHostView.IsSoloPanel(registrations[0].Id);
            var chrome = 2 + (headerCollapsed ? 0 : DockHostView.TabHeaderMinHeight);
            var minHeight = registrations.Max(panel => panel.MinHeight + (panel.ContentSized ? chrome : 0));
            if (FixedHostHeight(node) is { } fixedHeight) minHeight = Math.Max(minHeight, fixedHeight);
            return new Size(registrations.Max(panel => panel.MinWidth), minHeight);
        }
        var first = MinimumSize(node.First!);
        var second = MinimumSize(node.Second!);
        return string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase)
            ? new Size(first.Width + SplitterSize + second.Width, Math.Max(first.Height, second.Height))
            : new Size(Math.Max(first.Width, second.Width), first.Height + SplitterSize + second.Height);
    }

    /// <summary>Minimum height of a new tab host holding only this panel (chrome included for content-sized panels).</summary>
    private double SoloHostMinHeight(string panelId) => MinimumSize(Tabs("probe", panelId)).Height;

    private string FloatingTitle(DockNodeState root) =>
        string.Join(" · ", EnumeratePanels(root).Select(id => _panels.TryGetValue(id, out var p) ? p.Title : id).Take(3));

    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

}

public enum DockSplitterPhase { Started, Delta, Completed, DoubleClick }

/// <summary>What happened to a dock splitter, and which panels sit above/left (<see cref="First"/>) and below/right (<see cref="Second"/>) of it.</summary>
public sealed class DockSplitterEventArgs : EventArgs
{
    public DockSplitterEventArgs(DockSplitterPhase phase, bool vertical, IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        (Phase, Vertical, First, Second) = (phase, vertical, first, second);
    public DockSplitterPhase Phase { get; }
    public bool Vertical { get; }
    public IReadOnlyList<string> First { get; }
    public IReadOnlyList<string> Second { get; }
}
