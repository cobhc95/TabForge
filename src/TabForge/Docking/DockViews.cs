using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TabForge.Docking;

/// <summary>What a dock host view, tab and drag need from the workspace that owns them.</summary>
internal interface IDockHost
{
    bool TryGetPanel(string id, out DockPanelRegistration panel);
    bool IsDragging { get; }
    void RenderContextMenu(DockTabSurface surface);
    ContextMenu BuildPanelContextMenu(string panelId);
    void OnHostSelectionChanged(DockNodeState node, string panelId);
    void OnTabDragStarted(DockTabSurface source, MouseButtonEventArgs e);
    void OnTabDragMoved(DockTabSurface source, MouseEventArgs e);
    void OnTabDragEnded(DockTabSurface source, MouseButtonEventArgs e);
}

/// <param name="ContentSized">MinHeight is the content's own full-draw height (set by <c>DockWorkspace.SetPanelContentMinHeight</c>); host chrome is added.</param>
internal sealed record DockPanelRegistration(string Id, string Title, FrameworkElement Content,
    double MinWidth, double MinHeight, string DefaultHost, string DefaultAnchor, bool ContentSized = false,
    double? FixedHeight = null, double? MaxHeight = null);

internal sealed record DockDropDestination(string HostId, DockDropZone Zone, string? FloatingId,
    Rect Preview, double Fraction, int? TabIndex);

internal sealed class DockSlot(Grid root, Canvas preview, Window window, bool isMain)
{
    public Grid Root { get; } = root;
    public Canvas Preview { get; } = preview;
    public Window Window { get; } = window;
    public bool IsMain { get; } = isMain;
}

internal sealed class DockHostView
{
    private readonly IDockHost? _workspace;
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
        IDockHost? workspace, bool isEditor, DockSlot slot)
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
        if (_body is null || _workspace is null || !_workspace.TryGetPanel(panelId, out var panel)) return;
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
            var bounds = Shell.ScreenPoints.ScreenRect(_tabSurfaces[i]);
            if (screenPoint.X < bounds.Left + bounds.Width / 2) return i;
        }
        return _tabSurfaces.Count;
    }

    public bool IsOverTabHeader(Point screenPoint) =>
        _tabSurfaces.Any(tab => Shell.ScreenPoints.ScreenRect(tab).Contains(screenPoint));
}

internal sealed class DockTabSurface : Border
{
    private readonly IDockHost _workspace;
    private bool _selected;

    public DockTabSurface(IDockHost workspace, string hostId, string panelId, string title)
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
            FontSize = 12.65,
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
            else label.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_workspace.IsDragging) Selected?.Invoke(this, EventArgs.Empty);
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

internal sealed class DockFloatingWindow : Window
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

internal sealed class DockDragGhostWindow : Window
{
    private readonly Window _coordinateWindow;

    private static Brush ResourceBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

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
            Background = ResourceBrush("Panel2Brush", Color.FromRgb(31, 34, 40)),
            BorderBrush = ResourceBrush("AccentBrush", Color.FromRgb(76, 154, 255)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 8),
            Opacity = 0.9,
            Effect = new DropShadowEffect { Color = Color.FromRgb(76, 154, 255), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 },
            Child = new TextBlock { Text = title, Foreground = ResourceBrush("TextBrush", Colors.White), FontWeight = FontWeights.SemiBold }
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

/// <summary>Helpers for moving registered panel content between hosts.</summary>
internal static class DockVisuals
{
    internal static void DetachFromParent(FrameworkElement element)
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
}
