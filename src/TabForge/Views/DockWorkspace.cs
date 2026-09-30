using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TabForge.Docking;

namespace TabForge.Views;

/// <summary>
/// Reusable WPF split/tab workspace. Registered content has one owner and is moved between
/// ContentPresenters; only the lightweight split/tab chrome is rebuilt after a completed drop.
/// </summary>
public sealed class DockWorkspace : Grid
{
    private const double SplitterSize = 5;
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
    private const double EdgeFraction = 0.28;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private readonly Window _owner;
    private readonly Dictionary<string, DockPanelRegistration> _panels = new(StringComparer.Ordinal);
    private readonly List<DockHostView> _hosts = new();
    private readonly List<DockSlot> _slots = new();
    private readonly Dictionary<string, DockFloatingWindow> _floatWindows = new(StringComparer.Ordinal);
    private DockWorkspaceState _state = new();
    private DockTabSurface? _dragSource;
    private string? _dragPanelId;
    private Point _dragStart;
    private bool _dragArmed;
    private bool _dragging;
    private DockDropDestination? _destination;
    private DockDragGhostWindow? _ghost;
    private readonly DispatcherTimer _geometrySaveTimer;
    private bool _restoring;

    public DockWorkspace(Window owner)
    {
        _owner = owner;
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

    public static DockNodeState EditorNode(string hostId = "score-editor") =>
        new() { Kind = "editor", HostId = hostId };

    public static DockNodeState Tabs(string hostId, params string[] panelIds) =>
        new()
        {
            Kind = "tabs",
            HostId = hostId,
            Panels = panelIds.ToList(),
            SelectedPanel = panelIds.FirstOrDefault()
        };

    public static DockNodeState Split(string orientation, double ratio, DockNodeState first, DockNodeState second) =>
        new()
        {
            Kind = "split",
            Orientation = orientation,
            Ratio = ratio,
            First = first,
            Second = second
        };

    public void RegisterPanel(string id, string title, FrameworkElement content,
        double minWidth = 190, double minHeight = 120, string defaultHost = "side", string defaultAnchor = "tools")
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A dock panel needs a stable id.", nameof(id));
        if (_panels.ContainsKey(id)) throw new InvalidOperationException($"Dock panel '{id}' is already registered.");
        DetachFromParent(content);
        _panels.Add(id, new DockPanelRegistration(id, title, content,
            Math.Max(120, minWidth), Math.Max(40, minHeight), defaultHost, defaultAnchor));
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

    /// <summary>Pixel height of a tab host holding a size-locked panel (chrome included), or null.</summary>
    private double? FixedHostHeight(DockNodeState node)
    {
        if (node.Kind != "tabs") return null;
        double? fixedContent = null;
        foreach (var id in node.Panels)
            if (_panels.TryGetValue(id, out var p) && p.FixedHeight is { } f) fixedContent = Math.Max(fixedContent ?? 0, f);
        if (fixedContent is null) return null;
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
            if (!ValidateState(_state)) _state = CreateDefaultState();
            _state.ClosedPanels = _state.ClosedPanels
                .Where(_panels.ContainsKey).Distinct(StringComparer.Ordinal).ToList();

            var present = EnumerateAllPanels(_state).ToHashSet(StringComparer.Ordinal);
            foreach (var id in _panels.Keys)
            {
                if (present.Contains(id) || _state.ClosedPanels.Contains(id, StringComparer.Ordinal)) continue;
                RestorePanelToDefault(id, notify: false);
            }
            RebuildVisualTree();
        }
        finally { _restoring = false; }
    }

    public DockWorkspaceState CaptureLayout()
    {
        var result = Clone(_state);
        foreach (var floating in result.Floating)
        {
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

    private DockWorkspaceState CreateDefaultState()
    {
        var toolHost = Tabs("default-tool-palette", "tools", "structure", "rhythm", "layout");
        toolHost.SelectedPanel = "tools";
        var sideHost = Tabs("default-sections-practice-playback", "sections", "practice", "playback");
        sideHost.SelectedPanel = "sections";
        var right = Split("Vertical", 0.44, toolHost, sideHost);
        var instrumentAndScore = Split("Vertical", 0.26, Tabs("default-instrument", "instrument"), EditorNode());
        var upper = Split("Horizontal", 0.79, instrumentAndScore, right);
        var root = Split("Vertical", 0.74, upper, Tabs("default-timeline", "timeline"));
        root.Second!.SelectedPanel = "timeline";
        var state = new DockWorkspaceState { Root = root };
        return state;
    }

    private void RestorePanelToDefault(string id, bool notify)
    {
        RemovePanelFromAllRoots(id);
        _state.ClosedPanels.Remove(id);
        if (!_panels.TryGetValue(id, out var panel)) return;

        // Palette and right-side utility panels restore into their original shared tab host.
        var host = FindPanelHost(_state.Root, panel.DefaultAnchor) ??
                   _state.Floating.Select(f => FindPanelHost(f.Root, panel.DefaultAnchor)).FirstOrDefault(h => h is not null);
        if (panel.DefaultHost == "tools" || panel.DefaultHost == "side")
        {
            if (host is not null)
            {
                AddToTabs(host, id);
            }
            else
            {
                InsertAtRightOfEditor(id);
            }
        }
        else if (panel.DefaultHost == "instrument")
        {
            InsertAroundEditor(id, DockDropZone.Top);
        }
        else if (panel.DefaultHost == "timeline")
        {
            InsertAtRootEdge(id, DockDropZone.Bottom);
        }
        else
        {
            InsertAtRightOfEditor(id);
        }

        RebuildVisualTree();
        if (notify) NotifyLayoutChanged();
    }

    private void InsertAtRightOfEditor(string id)
    {
        var target = FindNode(_state.Root, "score-editor");
        if (target is null) return;
        ReplaceNode(_state, target.HostId,
            Split("Horizontal", 0.70, CloneNode(target)!, Tabs("restore-" + Guid.NewGuid().ToString("N"), id)));
    }

    private void InsertAroundEditor(string id, DockDropZone zone)
    {
        var target = FindNode(_state.Root, "score-editor");
        if (target is null) return;
        var panelNode = Tabs("restore-" + Guid.NewGuid().ToString("N"), id);
        var split = zone is DockDropZone.Top or DockDropZone.Bottom ? "Vertical" : "Horizontal";
        var before = zone is DockDropZone.Left or DockDropZone.Top;
        ReplaceNode(_state, target.HostId, before
            ? Split(split, EdgeFraction, panelNode, CloneNode(target)!)
            : Split(split, 1 - EdgeFraction, CloneNode(target)!, panelNode));
    }

    private void InsertAtRootEdge(string id, DockDropZone zone)
    {
        var root = _state.Root;
        if (root is null) return;
        var first = zone is DockDropZone.Left or DockDropZone.Top;
        var vertical = zone is DockDropZone.Top or DockDropZone.Bottom;
        var panel = Tabs("restore-" + Guid.NewGuid().ToString("N"), id);
        _state.Root = first
            ? Split(vertical ? "Vertical" : "Horizontal", EdgeFraction, panel, CloneNode(root)!)
            : Split(vertical ? "Vertical" : "Horizontal", 1 - EdgeFraction, CloneNode(root)!, panel);
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

    private static DockNodeState? RemovePanel(DockNodeState? node, string id)
    {
        if (node is null) return null;
        if (node.Kind == "tabs")
        {
            node.Panels.RemoveAll(p => p == id);
            if (node.SelectedPanel == id) node.SelectedPanel = node.Panels.FirstOrDefault();
            return node.Panels.Count == 0 ? null : node;
        }
        if (node.Kind == "split")
        {
            node.First = RemovePanel(node.First, id);
            node.Second = RemovePanel(node.Second, id);
            if (node.First is null) return node.Second;
            if (node.Second is null) return node.First;
        }
        return node;
    }

    private void RebuildVisualTree()
    {
        EndDrag();
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
                    splitGrid.RowDefinitions.Add(new RowDefinition { Height = fixedFirst is { } f1 ? new GridLength(f1) : fixedSecond is not null ? new GridLength(1, GridUnitType.Star) : new GridLength(Math.Clamp(node.Ratio, 0.02, 0.98), GridUnitType.Star), MinHeight = minFirst.Height });
                    splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
                    splitGrid.RowDefinitions.Add(new RowDefinition { Height = fixedSecond is { } f2 ? new GridLength(f2) : fixedFirst is not null ? new GridLength(1, GridUnitType.Star) : new GridLength(Math.Clamp(1 - node.Ratio, 0.02, 0.98), GridUnitType.Star), MinHeight = minSecond.Height });
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
                    EnforceSplitMinimums(splitter);
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
            node.Ratio = Math.Clamp(inFirst ? height / available : (available - height) / available, 0.02, 0.98);
            if (inFirst) split.RowDefinitions[0].MinHeight = height;
            else split.RowDefinitions[2].MinHeight = height;
            split.RowDefinitions[0].Height = new GridLength(node.Ratio, GridUnitType.Star);
            split.RowDefinitions[2].Height = new GridLength(1 - node.Ratio, GridUnitType.Star);
            NotifyLayoutChanged();
            return true;
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
    }

    /// <summary>
    /// Hard clamp for an interactive splitter drag: neither side may go below its pane minimum (GridSplitter
    /// alone let a star row shrink under its MinHeight). Rewrites both star sizes from the clamped size.
    /// </summary>
    private void EnforceSplitMinimums(GridSplitter splitter)
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
        var clamped = minA + minB <= total ? Math.Clamp(sizeA, minA, total - minB) : minA;
        var rest = Math.Max(minB, total - clamped);
        if (horizontal)
        {
            split.ColumnDefinitions[0].MinWidth = minA; split.ColumnDefinitions[2].MinWidth = minB;
            split.ColumnDefinitions[0].Width = new GridLength(clamped, GridUnitType.Star);
            split.ColumnDefinitions[2].Width = new GridLength(rest, GridUnitType.Star);
        }
        else
        {
            split.RowDefinitions[0].MinHeight = minA; split.RowDefinitions[2].MinHeight = minB;
            split.RowDefinitions[0].Height = new GridLength(clamped, GridUnitType.Star);
            split.RowDefinitions[2].Height = new GridLength(rest, GridUnitType.Star);
        }
    }

    private void ArmDrag(DockTabSurface source, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || source.IsClosed) return;
        SelectPanel(source.PanelId);
        _dragSource = source;
        _dragPanelId = source.PanelId;
        _dragStart = e.GetPosition(source);
        _dragArmed = true;
        source.CaptureMouse();
        e.Handled = true;
    }

    private void MoveDrag(DockTabSurface source, MouseEventArgs e)
    {
        if (!_dragArmed || !ReferenceEquals(source, _dragSource) || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(source);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            _ghost = new DockDragGhostWindow(_owner, _panels[_dragPanelId!].Title);
            _ghost.Show();
        }
        // The dragged tab can leave its window mid-drag (re-docked, closed): then use the real cursor.
        var screenPoint = Shell.ScreenPoints.TryToScreen(source, point, out var converted) ? converted : Shell.ScreenPoints.Cursor();
        UpdateDestination(screenPoint);
        _ghost?.MoveTo(screenPoint);
        e.Handled = true;
    }

    private void EndTabDrag(DockTabSurface source, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(source, _dragSource)) return;
        if (_dragging && _dragPanelId is not null)
        {
            var finalPoint = Shell.ScreenPoints.TryToScreen(source, e.GetPosition(source), out var converted)
                ? converted : Shell.ScreenPoints.Cursor();
            UpdateDestination(finalPoint);
            if (_destination is { } destination)
                DropOn(destination, _dragPanelId);
            else
                FloatAt(_dragPanelId, finalPoint);
            e.Handled = true;
        }
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragSource is { IsMouseCaptured: true } source) source.ReleaseMouseCapture();
        _dragSource = null;
        _dragPanelId = null;
        _dragArmed = false;
        _dragging = false;
        _destination = null;
        _ghost?.Close();
        _ghost = null;
        ClearPreview();
    }

    private void UpdateDestination(Point screenPoint)
    {
        _destination = null;
        ClearPreview();
        if (_dragPanelId is null) return;
        foreach (var host in _hosts)
        {
            // Hosts rebuilt or closed during the drag are not on screen: skip them instead of throwing.
            if (host.Surface is null || !host.Surface.IsLoaded || !Shell.ScreenPoints.IsOnScreen(host.Surface)) continue;
            var targetRect = ScreenRect(host.Surface);
            if (!targetRect.Contains(screenPoint)) continue;
            if (!Shell.ScreenPoints.TryFromScreen(host.Surface, screenPoint, out var local)) continue;
            if (host.Node.Kind == "editor" && ClassifyZone(local, host.Surface.RenderSize) == DockDropZone.Center)
                return;
            if (host.Node.Kind == "tabs" && host.Node.Panels.Count == 1 && host.Node.Panels[0] == _dragPanelId)
                return;

            var overTabHeader = host.IsOverTabHeader(screenPoint);
            var zone = overTabHeader
                ? DockDropZone.Center
                : ClassifyZone(local, host.Surface.RenderSize);
            if (!CanDropOn(host, zone, host.Surface.RenderSize)) return;
            var fraction = zone == DockDropZone.Center ? 0 : SplitFractionFor(host.Surface, host.Node, zone,
                zone is DockDropZone.Left or DockDropZone.Right);
            var preview = PreviewRect(host.Surface, host.Slot.Root, zone, host.Node, fraction);
            ShowPreview(host.Slot, preview);
            _destination = new DockDropDestination(host.Node.HostId, zone, host.Slot.IsMain ? null :
                _state.Floating.FirstOrDefault(f => RootBelongsToSlot(host.Node, f.Root))?.Id,
                preview, fraction, zone == DockDropZone.Center && overTabHeader ? host.InsertionIndexAt(screenPoint) : null);
            return;
        }
    }

    private bool CanDropOn(DockHostView host, DockDropZone zone, Size size)
    {
        if (host.Node.Kind == "editor" && zone == DockDropZone.Center) return false;
        if (host.Node.Kind == "tabs" && host.Node.Panels.Count == 1 && host.Node.Panels[0] == _dragPanelId)
            return false;
        if (zone == DockDropZone.Center) return host.Node.Kind == "tabs";
        var panel = _panels[_dragPanelId!];
        var existingMin = MinimumSize(host.Node);
        var horizontal = zone is DockDropZone.Left or DockDropZone.Right;
        var available = (horizontal ? size.Width : size.Height) - SplitterSize;
        var needed = (horizontal ? panel.MinWidth + existingMin.Width : SoloHostMinHeight(panel.Id) + existingMin.Height);
        return available >= needed;
    }

    private static DockDropZone ClassifyZone(Point point, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return DockDropZone.Center;
        var x = point.X / size.Width;
        var y = point.Y / size.Height;
        var candidates = new List<(DockDropZone Zone, double Distance)>();
        if (x <= EdgeFraction) candidates.Add((DockDropZone.Left, x / EdgeFraction));
        if (x >= 1 - EdgeFraction) candidates.Add((DockDropZone.Right, (1 - x) / EdgeFraction));
        if (y <= EdgeFraction) candidates.Add((DockDropZone.Top, y / EdgeFraction));
        if (y >= 1 - EdgeFraction) candidates.Add((DockDropZone.Bottom, (1 - y) / EdgeFraction));
        return candidates.Count == 0 ? DockDropZone.Center : candidates.MinBy(c => c.Distance).Zone;
    }

    private Rect PreviewRect(FrameworkElement host, Grid slotRoot, DockDropZone zone, DockNodeState node, double fraction)
    {
        var origin = host.TranslatePoint(new Point(0, 0), slotRoot);
        var width = host.ActualWidth;
        var height = host.ActualHeight;
        var horizontal = zone is DockDropZone.Left or DockDropZone.Right;
        if (zone == DockDropZone.Center) return new Rect(origin, new Size(width, height));
        if (horizontal)
        {
            var newWidth = Math.Max(0, (width - SplitterSize) * fraction);
            return zone == DockDropZone.Left
                ? new Rect(origin, new Size(newWidth, height))
                : new Rect(origin.X + width - newWidth, origin.Y, newWidth, height);
        }
        var newHeight = Math.Max(0, (height - SplitterSize) * fraction);
        return zone == DockDropZone.Top
            ? new Rect(origin, new Size(width, newHeight))
            : new Rect(origin.X, origin.Y + height - newHeight, width, newHeight);
    }

    private double SplitFractionFor(FrameworkElement host, DockNodeState node, DockDropZone zone, bool horizontal)
    {
        var existing = MinimumSize(node);
        var newPanel = _panels[_dragPanelId!];
        var available = (horizontal ? host.ActualWidth : host.ActualHeight) - SplitterSize;
        var newMinHeight = SoloHostMinHeight(newPanel.Id);
        var total = horizontal ? newPanel.MinWidth + existing.Width : newMinHeight + existing.Height;
        if (available <= 0 || total <= 0) return EdgeFraction;
        var minFraction = (horizontal ? newPanel.MinWidth : newMinHeight) / available;
        var maxFraction = 1 - (horizontal ? existing.Width : existing.Height) / available;
        return minFraction <= maxFraction ? Math.Clamp(EdgeFraction, minFraction, maxFraction) : EdgeFraction;
    }

    private static Rect ScreenRect(FrameworkElement element) => Shell.ScreenPoints.ScreenRect(element);

    private static bool RootBelongsToSlot(DockNodeState host, DockNodeState? root) =>
        root is not null && FindNode(root, host.HostId) is not null;

    private void ShowPreview(DockSlot slot, Rect rect)
    {
        var border = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            BorderThickness = new Thickness(2),
            Background = Brush("AccentSoftBrush", Color.FromArgb(56, 76, 154, 255)),
            CornerRadius = new CornerRadius(5),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(76, 154, 255),
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.95
            }
        };
        border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        slot.Preview.Children.Add(border);
    }

    private void ClearPreview()
    {
        foreach (var slot in _slots) slot.Preview.Children.Clear();
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

    private static void AddToTabs(DockNodeState host, string panelId, int? index = null)
    {
        if (host.Kind != "tabs") return;
        host.Panels.RemoveAll(id => id == panelId);
        host.Panels.Insert(Math.Clamp(index ?? host.Panels.Count, 0, host.Panels.Count), panelId);
        host.SelectedPanel = panelId;
    }

    private bool RemovePanelFromRootsOnly(string id)
    {
        var wasPresent = EnumerateAllPanels(_state).Contains(id, StringComparer.Ordinal);
        if (!wasPresent) return false;
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

    private static void ClampBounds(Window window, DockFloatingState state)
    {
        // Keep finite, usable DIP values until the HWND is available. The actual target monitor's
        // pixel work area is applied immediately after Show, which also handles secondary displays.
        window.Width = Math.Max(Math.Min(240, window.MinWidth),
            double.IsFinite(state.Width) ? state.Width : Math.Max(320, window.MinWidth));
        window.Height = Math.Max(Math.Min(150, window.MinHeight),
            double.IsFinite(state.Height) ? state.Height : Math.Max(220, window.MinHeight));
        window.Left = double.IsFinite(state.Left) ? state.Left : 180;
        window.Top = double.IsFinite(state.Top) ? state.Top : 140;
    }

    private static void ClampToMonitorWorkArea(Window window, DockFloatingState state)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var work = info.WorkArea;
        var workWidth = Math.Max(1, work.Right - work.Left);
        var workHeight = Math.Max(1, work.Bottom - work.Top);
        var source = PresentationSource.FromVisual(window);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice;
        var workDip = fromDevice.HasValue
            ? fromDevice.Value.Transform(new Vector(workWidth, workHeight))
            : new Vector(workWidth, workHeight);

        // Window.Width/Height exclude the non-client frame. Leave a small frame allowance so the
        // outer rectangle fits the work area while respecting panel minimums whenever possible.
        const double frameAllowanceDip = 20;
        var maxWidth = Math.Max(180, workDip.X - frameAllowanceDip);
        var maxHeight = Math.Max(120, workDip.Y - frameAllowanceDip);
        window.MinWidth = Math.Min(window.MinWidth, maxWidth);
        window.MinHeight = Math.Min(window.MinHeight, maxHeight);
        if (window.Width > maxWidth) window.Width = maxWidth;
        if (window.Height > maxHeight) window.Height = maxHeight;
        window.UpdateLayout();

        if (!GetWindowRect(handle, out bounds)) return;
        var width = Math.Min(bounds.Right - bounds.Left, workWidth);
        var height = Math.Min(bounds.Bottom - bounds.Top, workHeight);
        var left = Math.Clamp(bounds.Left, work.Left, work.Right - width);
        var top = Math.Clamp(bounds.Top, work.Top, work.Bottom - height);
        if (left != bounds.Left || top != bounds.Top || width != bounds.Right - bounds.Left || height != bounds.Bottom - bounds.Top)
            SetWindowPos(handle, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);

        // WM_WINDOWPOSCHANGED updates WPF's DIP location; copy that normalized geometry for saves.
        state.Left = window.Left;
        state.Top = window.Top;
        state.Width = window.Width;
        state.Height = window.Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);

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
        var reset = new MenuItem { Header = "Reset this panel to original position" };
        reset.Click += (_, _) => ContextReset(panelId);
        menu.Items.Add(reset);
        var close = new MenuItem { Header = "Close panel" };
        close.Click += (_, _) => ClosePanel(panelId);
        menu.Items.Add(close);
        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = "Reset all panels to original positions" };
        all.Click += (_, _) => ResetAllPanels();
        menu.Items.Add(all);
        return menu;
    }

    private void OnHostSelectionChanged(DockNodeState node, string panelId)
    {
        node.SelectedPanel = panelId;
        NotifyLayoutChanged();
    }

    private void OnTabDragStarted(DockTabSurface source, MouseButtonEventArgs e) => ArmDrag(source, e);
    private void OnTabDragMoved(DockTabSurface source, MouseEventArgs e) => MoveDrag(source, e);
    private void OnTabDragEnded(DockTabSurface source, MouseButtonEventArgs e) => EndTabDrag(source, e);

    private DockWorkspaceState Clone(DockWorkspaceState state) => new()
    {
        Version = Math.Max(1, state.Version),
        Root = CloneNode(state.Root),
        Floating = (state.Floating ?? new()).Select(f => new DockFloatingState
        {
            Id = string.IsNullOrWhiteSpace(f.Id) ? Guid.NewGuid().ToString("N") : f.Id,
            Root = CloneNode(f.Root),
            Left = f.Left,
            Top = f.Top,
            Width = f.Width,
            Height = f.Height
        }).ToList(),
        ClosedPanels = (state.ClosedPanels ?? new()).ToList()
    };

    private static DockNodeState? CloneNode(DockNodeState? node) => node is null ? null : new DockNodeState
    {
        Kind = node.Kind,
        HostId = node.HostId,
        Panels = (node.Panels ?? new()).ToList(),
        SelectedPanel = node.SelectedPanel,
        Orientation = node.Orientation,
        Ratio = double.IsFinite(node.Ratio) ? Math.Clamp(node.Ratio, 0.02, 0.98) : 0.5,
        First = CloneNode(node.First),
        Second = CloneNode(node.Second)
    };

    private bool ValidateState(DockWorkspaceState state)
    {
        if (state.Root is null || _editorContent is null) return false;
        var ids = new List<string>();
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        bool Walk(DockNodeState? node, bool isMain)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.HostId) || !hosts.Add(node.HostId)) return false;
            if (node.Kind == "editor")
            {
                ids.Add("$editor");
                return isMain;
            }
            if (node.Kind == "tabs")
            {
                foreach (var id in node.Panels)
                {
                    if (!_panels.ContainsKey(id)) return false;
                    ids.Add(id);
                }
                return true;
            }
            if (node.Kind != "split" || node.First is null || node.Second is null) return false;
            if (node.Orientation is not ("Horizontal" or "Vertical")) return false;
            if (!double.IsFinite(node.Ratio) || node.Ratio is < 0.02 or > 0.98) return false;
            return Walk(node.First, isMain) && Walk(node.Second, isMain);
        }
        if (!Walk(state.Root, true)) return false;
        foreach (var floating in state.Floating)
            if (floating.Root is null || !Walk(floating.Root, false)) return false;
        if (ids.Count(i => i == "$editor") != 1) return false;
        var panels = ids.Where(i => i != "$editor").ToList();
        if (panels.Count != panels.Distinct(StringComparer.Ordinal).Count()) return false;
        foreach (var id in state.ClosedPanels)
            if (!_panels.ContainsKey(id) || panels.Contains(id, StringComparer.Ordinal)) return false;
        return true;
    }

    private static IEnumerable<string> EnumerateAllPanels(DockWorkspaceState state) =>
        EnumeratePanels(state.Root).Concat(state.Floating.SelectMany(f => EnumeratePanels(f.Root)));

    private static IEnumerable<string> EnumeratePanels(DockNodeState? node)
    {
        if (node is null) yield break;
        if (node.Kind == "tabs")
        {
            foreach (var id in node.Panels) yield return id;
        }
        if (node.Kind == "split")
        {
            foreach (var id in EnumeratePanels(node.First)) yield return id;
            foreach (var id in EnumeratePanels(node.Second)) yield return id;
        }
    }

    private static bool ContainsPanel(DockNodeState node, string id) => EnumeratePanels(node).Contains(id, StringComparer.Ordinal);

    private static DockNodeState? FindPanelHost(DockNodeState? node, string id)
    {
        if (node is null) return null;
        if (node.Kind == "tabs" && node.Panels.Contains(id, StringComparer.Ordinal)) return node;
        return FindPanelHost(node.First, id) ?? FindPanelHost(node.Second, id);
    }

    private static DockNodeState? FindNode(DockNodeState? node, string hostId)
    {
        if (node is null) return null;
        if (node.HostId == hostId) return node;
        return FindNode(node.First, hostId) ?? FindNode(node.Second, hostId);
    }

    private static DockNodeState? ReplaceNode(DockNodeState? root, string hostId, DockNodeState replacement)
    {
        if (root is null) return null;
        if (root.HostId == hostId) return replacement;
        if (root.First is not null) root.First = ReplaceNode(root.First, hostId, replacement);
        if (root.Second is not null) root.Second = ReplaceNode(root.Second, hostId, replacement);
        return root;
    }

    private void ReplaceNode(DockWorkspaceState state, string hostId, DockNodeState replacement)
        => state.Root = ReplaceNode(state.Root, hostId, replacement);

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

    private static void DetachFromParent(FrameworkElement element)
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element);
        switch (parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element): decorator.Child = null; break;
            case ContentControl control when ReferenceEquals(control.Content, element): control.Content = null; break;
            case ContentPresenter presenter when ReferenceEquals(presenter.Content, element): presenter.Content = null; break;
            case Border border when ReferenceEquals(border.Child, element): border.Child = null; break;
        }
    }

    /// <param name="ContentSized">MinHeight is the content's own full-draw height (set by <see cref="SetPanelContentMinHeight"/>); host chrome is added.</param>
    private sealed record DockPanelRegistration(string Id, string Title, FrameworkElement Content,
        double MinWidth, double MinHeight, string DefaultHost, string DefaultAnchor, bool ContentSized = false,
        double? FixedHeight = null);

    private sealed record DockDropDestination(string HostId, DockDropZone Zone, string? FloatingId,
        Rect Preview, double Fraction, int? TabIndex);

    private sealed class DockSlot(Grid root, Canvas preview, Window window, bool isMain)
    {
        public Grid Root { get; } = root;
        public Canvas Preview { get; } = preview;
        public Window Window { get; } = window;
        public bool IsMain { get; } = isMain;
    }

    private sealed class DockHostView
    {
        private readonly DockWorkspace? _workspace;
        private readonly DockSlot _slot;
        private readonly ContentControl? _body;
        private readonly ScrollViewer? _tabScroller;
        private readonly List<DockTabSurface> _tabSurfaces = new();

        // Groups that only ever host a single, fixed panel: their tab header strip is pure
        // chrome (one button, nothing to switch between), so it is collapsed and the panel's
        // right-click menu is mirrored onto the content area instead.
        private static readonly HashSet<string> SoloPanelIds = new(StringComparer.Ordinal) { "instrument", "timeline" };
        public static bool IsSoloPanel(string id) => SoloPanelIds.Contains(id);
        public const double TabHeaderMinHeight = 36;

        public DockHostView(string hostId, DockNodeState node, FrameworkElement? surface,
            DockWorkspace? workspace, bool isEditor, DockSlot slot)
        {
            HostId = hostId;
            Node = node;
            _workspace = workspace;
            IsEditor = isEditor;
            _slot = slot;
            if (surface is null)
            {
                var border = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    ClipToBounds = true
                };
                border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
                var layout = new DockPanel();
                // Tabs wrap onto extra rows when the panel is narrow; a horizontal scrollbar here
                // overlapped the tab labels and buried them.
                var header = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0),
                    MinHeight = 31
                };
                header.SetResourceReference(Panel.BackgroundProperty, "Panel2Brush");
                var tabScroller = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MinHeight = TabHeaderMinHeight,
                    Content = header
                };
                layout.Children.Add(tabScroller);
                DockPanel.SetDock(tabScroller, Dock.Top);
                _tabScroller = tabScroller;
                _body = new ContentControl
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    ClipToBounds = true
                };
                _body.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
                layout.Children.Add(_body);
                border.Child = layout;
                Surface = border;
                Header = header;
            }
            else
            {
                Surface = surface;
                if (surface is Border border && border.Child is TextBlock) _body = null;
            }
        }

        public string HostId { get; }
        public DockNodeState Node { get; }
        public FrameworkElement Surface { get; }
        public Panel? Header { get; }
        public bool IsEditor { get; }
        public DockSlot Slot => _slot;

        public void BuildTabs(List<string> ids, IReadOnlyDictionary<string, DockPanelRegistration> panels)
        {
            if (Header is null || _body is null || _workspace is null) return;
            Header.Children.Clear();
            _tabSurfaces.Clear();
            foreach (var id in ids)
            {
                var registration = panels[id];
                var tab = new DockTabSurface(_workspace, HostId, id, registration.Title);
                _workspace.RenderContextMenu(tab);
                tab.MouseLeftButtonDown += (_, e) => _workspace.OnTabDragStarted(tab, e);
                tab.MouseMove += (_, e) => _workspace.OnTabDragMoved(tab, e);
                tab.MouseLeftButtonUp += (_, e) => _workspace.OnTabDragEnded(tab, e);
                tab.Selected += (_, _) => Select(id, notify: true);
                Header.Children.Add(tab);
                _tabSurfaces.Add(tab);
            }
            Select(Node.SelectedPanel ?? ids[0], notify: false);

            var collapseHeader = ids.Count == 1 && SoloPanelIds.Contains(ids[0]);
            if (_tabScroller is not null) _tabScroller.Visibility = collapseHeader ? Visibility.Collapsed : Visibility.Visible;
            _body.ContextMenu = collapseHeader ? _workspace.BuildPanelContextMenu(ids[0]) : null;
            // A header-less fretboard may paint its top-string markers upward over the menu bar.
            // Clip geometry (not ClipToBounds) keeps the sides and bottom clipped; no per-frame cost.
            // Overhang removed: the fretboard now reserves room for its top markers inside the pane.
            SetTopOverhang(0);
        }

        public const double OverhangPx = 22;

        private double _overhang;

        private void SetTopOverhang(double overhang)
        {
            _overhang = overhang;
            foreach (var element in new FrameworkElement?[] { Surface, _body })
            {
                if (element is null) continue;
                element.SizeChanged -= OnOverhangSizeChanged;
                if (overhang <= 0)
                {
                    element.Clip = null;
                    element.ClipToBounds = true;
                    continue;
                }
                element.ClipToBounds = false;
                element.SizeChanged += OnOverhangSizeChanged;
                ApplyOverhangClip(element);
            }
        }

        private void OnOverhangSizeChanged(object sender, SizeChangedEventArgs e) => ApplyOverhangClip((FrameworkElement)sender);

        private void ApplyOverhangClip(FrameworkElement element)
        {
            var overhang = _overhang;
            var clip = new RectangleGeometry(new Rect(0, -overhang, element.ActualWidth, element.ActualHeight + overhang));
            clip.Freeze();
            element.Clip = clip;
        }

        public void Select(string panelId, bool notify)
        {
            if (_body is null || _workspace is null || !_workspace._panels.TryGetValue(panelId, out var panel)) return;
            Node.SelectedPanel = panelId;
            _body.Content = panel.Content;
            foreach (var tab in _tabSurfaces) tab.SetSelected(tab.PanelId == panelId);
            if (notify) _workspace.OnHostSelectionChanged(Node, panelId);
        }

        public void ReleaseContent()
        {
            if (_body is not null) _body.Content = null;
            if (IsEditor && Surface is Border border) border.Child = null;
            foreach (var tab in _tabSurfaces) tab.IsClosed = true;
        }

        public int InsertionIndexAt(Point screenPoint)
        {
            for (var i = 0; i < _tabSurfaces.Count; i++)
            {
                var bounds = ScreenRect(_tabSurfaces[i]);
                if (screenPoint.X < bounds.Left + bounds.Width / 2) return i;
            }
            return _tabSurfaces.Count;
        }

        public bool IsOverTabHeader(Point screenPoint) =>
            _tabSurfaces.Any(tab => ScreenRect(tab).Contains(screenPoint));
    }

    private sealed class DockTabSurface : Border
    {
        private readonly DockWorkspace _workspace;
        private bool _selected;

        public DockTabSurface(DockWorkspace workspace, string hostId, string panelId, string title)
        {
            _workspace = workspace;
            HostId = hostId;
            PanelId = panelId;
            // Browser-style dock tabs remain ordinary pointer targets even though dragging them
            // rearranges the workspace.
            Cursor = Cursors.Arrow;
            Padding = new Thickness(9, 5, 9, 5);
            Margin = new Thickness(3, 3, 0, 3);
            CornerRadius = new CornerRadius(4, 4, 0, 0);
            BorderThickness = new Thickness(1);
            Child = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip = $"{title} · drag to move · right-click for panel options";
            MouseRightButtonUp += (_, e) =>
            {
                if (ContextMenu is not null) ContextMenu.IsOpen = true;
                e.Handled = true;
            };
            MouseEnter += (_, _) => { if (!_selected) SetHover(true); };
            MouseLeave += (_, _) => { if (!_selected) SetHover(false); };
        }

        public string HostId { get; }
        public string PanelId { get; }
        public bool IsClosed { get; set; }
        public event EventHandler? Selected;

        public void SetSelected(bool selected)
        {
            _selected = selected;
            if (selected)
            {
                SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            }
            else
            {
                ClearValue(Border.BackgroundProperty);
                ClearValue(Border.BorderBrushProperty);
                Background = Brushes.Transparent;
            }
            if (Child is TextBlock label)
            {
                if (selected) label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                else label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_workspace._dragging) Selected?.Invoke(this, EventArgs.Empty);
        }

        private void SetHover(bool hover)
        {
            if (hover) SetResourceReference(Border.BackgroundProperty, "TabHoverBrush");
            else
            {
                ClearValue(Border.BackgroundProperty);
                Background = Brushes.Transparent;
            }
        }
    }

    private sealed class DockFloatingWindow : Window
    {
        private bool _closingFromWorkspace;

        public DockFloatingWindow(string id)
        {
            Id = id;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            SetResourceReference(Window.BackgroundProperty, "WindowBrush");
            SetResourceReference(Window.ForegroundProperty, "TextBrush");
            ContentRoot = new Grid();
            ContentRoot.SetResourceReference(Panel.BackgroundProperty, "WindowBrush");
            Content = ContentRoot;
            Closing += (_, e) =>
            {
                if (_closingFromWorkspace) return;
                e.Cancel = true;
                Hide();
                ClosingByUser?.Invoke(this, EventArgs.Empty);
            };
        }

        public string Id { get; }
        public Grid ContentRoot { get; }
        public event EventHandler? ClosingByUser;

        public void AttachOwner(Window owner)
        {
            if (Owner is null && owner.IsVisible) Owner = owner;
        }

        public void CloseFromWorkspace()
        {
            _closingFromWorkspace = true;
            if (IsVisible) Close();
            else
            {
                Content = null;
                if (Owner is { IsVisible: true }) Close();
            }
        }
    }

    private sealed class DockDragGhostWindow : Window
    {
        private readonly Window _coordinateWindow;

        public DockDragGhostWindow(Window owner, string title)
        {
            _coordinateWindow = owner;
            Owner = owner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            IsHitTestVisible = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = new Border
            {
                Background = Brush("Panel2Brush", Color.FromRgb(31, 34, 40)),
                BorderBrush = Brush("AccentBrush", Color.FromRgb(76, 154, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8),
                Opacity = 0.9,
                Effect = new DropShadowEffect { Color = Color.FromRgb(76, 154, 255), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 },
                Child = new TextBlock { Text = title, Foreground = Brush("TextBrush", Colors.White), FontWeight = FontWeights.SemiBold }
            };
        }

        public void MoveTo(Point screenPoint)
        {
            var position = screenPoint;
            try
            {
                var transform = PresentationSource.FromVisual(_coordinateWindow)?.CompositionTarget?.TransformFromDevice;
                if (transform.HasValue) position = transform.Value.Transform(screenPoint);
            }
            catch (InvalidOperationException) { } // window closing: its presentation source is gone
            Left = position.X + 10;
            Top = position.Y + 12;
        }
    }
}
