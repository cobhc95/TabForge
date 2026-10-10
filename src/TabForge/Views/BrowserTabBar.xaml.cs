using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Shell;

namespace TabForge.Views;

/// <summary>
/// Browser-style document tab strip for the window caption. Tabs start flush at the left edge, the
/// (+) button follows the last tab, and the strip supports click-to-activate, drag-to-reorder,
/// drag-out-to-new-window, drag-onto-another-window-to-merge, middle-click, double-click-to-close and
/// a per-tab context menu. All behaviour is driven by <see cref="TabSettings"/>.
/// </summary>
/// <remarks>
/// Mouse input is handled once, at the root, using preview events. Tab elements are created from a
/// data template, and the bubbling mouse events do not survive the ScrollViewer/ItemsControl path,
/// so per-tab handlers are not reliable; the root preview handlers find the tab under the pointer
/// instead. Buttons (the close cross, the (+)) are left alone so they still click normally.
/// </remarks>
public partial class BrowserTabBar : UserControl
{
    private readonly ObservableCollection<TabItemModel> _items = new();
    private DocumentManager? _documents;
    private readonly HashSet<DocumentSession> _playing = new();
    private readonly HashSet<DocumentSession> _watched = new();
    private bool _displayRefreshQueued;

    // A tab's name, tooltip and unsaved dot follow its document through the document's own change event (no polling): this covers a
    // background tab that was saved, renamed, edited or undone while another tab is displayed.
    private void WatchDocuments(IReadOnlyList<DocumentSession> docs)
    {
        foreach (var gone in _watched.Where(w => !docs.Contains(w)).ToList())
        {
            gone.DisplayStateChanged -= Document_DisplayStateChanged;
            _watched.Remove(gone);
        }
        foreach (var doc in docs)
            if (_watched.Add(doc)) doc.DisplayStateChanged += Document_DisplayStateChanged;
    }

    private void Document_DisplayStateChanged(object? sender, EventArgs e)
    {
        if (_displayRefreshQueued) return;
        _displayRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _displayRefreshQueued = false;
            UpdateDisplayState();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Re-reads each tab's title, tooltip and unsaved dot from its document; announces only what changed.</summary>
    internal void UpdateDisplayState()
    {
        foreach (var item in _items)
        {
            var title = item.Session.DisplayName;
            var tooltip = item.Session.Tooltip;
            var dirty = item.Session.Project.IsDirty;
            if (item.ShownTitle != title || item.ShownTooltip != tooltip || item.ShownDirty != dirty)
            {
                item.ShownTitle = title; item.ShownTooltip = tooltip; item.ShownDirty = dirty;
                item.Raise(nameof(TabItemModel.Title));
                item.Raise(nameof(TabItemModel.Tooltip));
                item.Raise(nameof(TabItemModel.DirtyVisibility));
            }
        }
    }

    public TabSettings Settings { get; set; } = new();

    public event EventHandler<int>? TabActivated;
    public event EventHandler<int>? CloseRequested;
    public event EventHandler<int>? CloseOthersRequested;
    public event EventHandler<int>? CloseRightRequested;
    public event EventHandler<int>? DuplicateRequested;
    /// <summary>Move the tab into a brand-new window (drag-out / context menu).</summary>
    public event EventHandler<int>? DetachRequested;
    /// <summary>Tab crossed the tear-off boundary while still held; the shell opens and moves its new HWND.</summary>
    public event EventHandler<TabTearOffEventArgs>? HeldTearOffRequested;
    public event EventHandler<(int from, int to)>? Reordered;
    public event EventHandler? NewTabRequested;
    /// <summary>A document from another window/process was dropped here; the window adopts it.</summary>
    public event EventHandler<TabDropEventArgs>? TabDropped;
    /// <summary>Left-button pressed on empty caption space — the window should start its drag.</summary>
    public event EventHandler? CaptionDragRequested;
    public event EventHandler? CaptionDoubleClickRequested;

    public BrowserTabBar()
    {
        InitializeComponent();
        TabStrip.ItemsSource = _items;

        Root.DragOver += Root_DragOver;
        Root.Drop += Root_Drop;
        Root.DragLeave += (_, _) => DropMarker.Visibility = Visibility.Collapsed;
        Root.PreviewMouseDown += Root_PreviewMouseDown;
        Root.PreviewMouseMove += Root_PreviewMouseMove;
        Root.PreviewMouseUp += Root_PreviewMouseUp;
        Root.SizeChanged += (_, _) => { ApplyWidths(); UpdateOverflow(); };
        Scroll.ScrollChanged += (_, _) => UpdateOverflow();
    }

    public void Bind(DocumentManager documents) => _documents = documents;

    /// <summary>Moves keyboard focus to the currently selected tab after a new tab/window opens.</summary>
    public bool FocusActiveTab()
    {
        var index = _documents?.ActiveIndex ?? -1;
        var tab = index >= 0 ? ContainerAt(index) : null;
        return tab?.Focus() == true;
    }

    /// <summary>Marks the document whose score is playing (drives the per-tab playing badge).</summary>
    public void SetPlaying(DocumentSession? session)
    {
        SetPlayingDocuments(session is null ? Array.Empty<DocumentSession>() : new[] { session });
    }

    public void SetPlayingDocuments(IEnumerable<DocumentSession> sessions)
    {
        var next = sessions.ToHashSet();
        if (_playing.SetEquals(next)) return;
        _playing.Clear();
        _playing.UnionWith(next);
        foreach (var item in _items)
        {
            var playing = _playing.Contains(item.Session);
            if (item.IsPlaying == playing) continue;
            item.IsPlaying = playing;
            item.Raise(nameof(TabItemModel.IsPlaying));
            item.Raise(nameof(TabItemModel.PlayingVisibility));
        }
    }

    internal bool IsDocumentMarkedPlaying(DocumentSession session) => _playing.Contains(session);

    public void Refresh()
    {
        if (_documents is null) return;
        var docs = _documents.Documents;
        WatchDocuments(docs);

        // Update in place when the set of documents is unchanged: rebuilding the collection on every
        // activation would destroy the element the user just clicked, breaking double-click and drag.
        var sameSet = _items.Count == docs.Count;
        if (sameSet)
        {
            for (var i = 0; i < docs.Count; i++)
            {
                if (ReferenceEquals(_items[i].Session, docs[i])) continue;
                sameSet = false;
                break;
            }
        }
        if (sameSet)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                item.Index = i;
                ApplySettingsTo(item);
                var active = i == _documents.ActiveIndex;
                if (item.IsActive != active) { item.IsActive = active; item.Raise(nameof(TabItemModel.IsActive)); }
                var playing = _playing.Contains(item.Session);
                if (item.IsPlaying != playing)
                {
                    item.IsPlaying = playing;
                    item.Raise(nameof(TabItemModel.IsPlaying));
                    item.Raise(nameof(TabItemModel.PlayingVisibility));
                }
            }
            UpdateDisplayState();
            ApplyWidths();
            ScheduleWidths();
            return;
        }

        var activeIndex = _documents.ActiveIndex;
        _items.Clear();
        for (var i = 0; i < docs.Count; i++)
        {
            var item = new TabItemModel
            {
                Session = docs[i],
                Index = i,
                IsActive = i == activeIndex,
                IsPlaying = _playing.Contains(docs[i])
            };
            ApplySettingsTo(item);
            _items.Add(item);
        }
        foreach (var item in _items) item.RaiseAll();
        ApplyWidths();
        ScheduleWidths();
    }

    /// <summary>
    /// Widths can only be applied once the item containers exist, which is after the next layout pass
    /// following a rebuild. Without this the tabs collapse to their content width.
    /// </summary>
    private void ScheduleWidths() =>
        Dispatcher.BeginInvoke(new Action(() => { ApplyWidths(); UpdateOverflow(); }), System.Windows.Threading.DispatcherPriority.Loaded);

    private void ApplySettingsTo(TabItemModel item)
    {
        // Existing tabs are updated in place, so every change must be announced or the tab keeps its old look.
        var hover = Settings.CloseButton == CloseButtonModes.ActiveAndHover;
        var always = Settings.CloseButton == CloseButtonModes.Always;
        var never = Settings.CloseButton == CloseButtonModes.Never;
        if (item.ShowCloseOnHover != hover) { item.ShowCloseOnHover = hover; item.Raise(nameof(TabItemModel.ShowCloseOnHover)); }
        if (item.CloseAlways != always) { item.CloseAlways = always; item.Raise(nameof(TabItemModel.CloseAlways)); }
        if (item.CloseNever != never) { item.CloseNever = never; item.Raise(nameof(TabItemModel.CloseNever)); }
        if (item.ShowPlayingIndicator != Settings.ShowPlayingIndicator)
        {
            item.ShowPlayingIndicator = Settings.ShowPlayingIndicator;
            item.Raise(nameof(TabItemModel.PlayingVisibility));
        }
        // A uniform 8 px pill radius rather than browser-style "rounded top" corners.
        var radius = Settings.IsRounded ? new CornerRadius(8) : new CornerRadius(0);
        if (item.TabCornerRadius != radius) { item.TabCornerRadius = radius; item.Raise(nameof(TabItemModel.TabCornerRadius)); }
    }

    // ---------- layout ----------

    private void ApplyWidths()
    {
        var count = _items.Count;
        if (count == 0) return;
        double min, max;
        if (Settings.ShrinkToFit)
        {
            // Leave room for the (+) button, the 3 px inter-tab gaps and a small inset; the overflow
            // arrows take their space out of Root.ActualWidth themselves.
            var available = Math.Max(120, Root.ActualWidth - NewTabButton.ActualWidth - 12 - Math.Max(0, count - 1) * 3);
            var per = available / count;
            var floor = Math.Min(Settings.MinTabWidth, Settings.MaxTabWidth);
            min = Math.Clamp(per, floor, Math.Max(floor, Settings.MaxTabWidth));
            max = min;
        }
        else
        {
            min = 0;
            max = Settings.MaxTabWidth;
        }
        for (var i = 0; i < count; i++)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement presenter) continue;
            if (VisualTreeHelper.GetChildrenCount(presenter) == 0) continue;
            if (VisualTreeHelper.GetChild(presenter, 0) is not FrameworkElement element) continue;
            element.MinWidth = min;
            element.MaxWidth = max;
            element.Height = Settings.TabHeight;
            System.Windows.Documents.TextElement.SetFontSize(element, Settings.TabFontSize);
        }
    }

    private FrameworkElement? ContainerAt(int index) =>
        TabStrip.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement presenter &&
        VisualTreeHelper.GetChildrenCount(presenter) > 0
            ? VisualTreeHelper.GetChild(presenter, 0) as FrameworkElement
            : null;

    private double LeftOf(int index)
    {
        var c = ContainerAt(index);
        return c is null || !Root.IsAncestorOf(c) ? 0 : c.TransformToAncestor(Root).Transform(new Point(0, 0)).X;
    }

    private int InsertionIndexAt(double x)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            var c = ContainerAt(i);
            if (c is null || !Root.IsAncestorOf(c)) continue; // container recycled mid-drag
            var centre = c.TransformToAncestor(Root).Transform(new Point(c.ActualWidth / 2, 0)).X;
            if (x < centre) return i;
        }
        return _items.Count;
    }

    internal Rect GetScreenRect() => Shell.ScreenPoints.ScreenRect(this);

    internal int GetInsertionIndexAtScreen(Point screenPoint)
    {
        // Not on screen (window closing / tab strip rebuilt): append at the end.
        if (!Shell.ScreenPoints.TryFromScreen(this, screenPoint, out var local)) return InsertionIndexAt(double.MaxValue);
        if (!IsAncestorOf(Root)) return InsertionIndexAt(double.MaxValue);
        var inRoot = TransformToVisual(Root).Transform(local);
        return InsertionIndexAt(inRoot.X);
    }

    // ---------- live drag preview ----------
    //
    // While a tab is dragged the lifted tab follows the pointer and the tabs it passes slide aside
    // with a short eased animation, so the strip continuously shows where the tab will land. The
    // insertion index is computed from the layout slots captured at drag start (never from the
    // transformed live positions), so the moving tab cannot feed back into the calculation.

    private FrameworkElement? _dragSource;
    private int _dragFromIndex = -1;
    private double[] _slotLefts = System.Array.Empty<double>();
    private double[] _slotWidths = System.Array.Empty<double>();
    private double _grabDx;
    private int _pendingInsertIndex;
    private readonly Dictionary<FrameworkElement, double> _previewX = new();
    private double _savedOpacity = 1;
    private Brush? _savedBorderBrush;
    private Thickness _savedBorderThickness;
    private Effect? _savedEffect;

    private void PrepareDragPreview(FrameworkElement source, TabItemModel model, Point pointer)
    {
        _dragSource = source;
        _dragFromIndex = model.Index;
        var count = _items.Count;
        _slotLefts = new double[count];
        _slotWidths = new double[count];
        for (var i = 0; i < count; i++)
        {
            _slotLefts[i] = LeftOf(i);
            _slotWidths[i] = ContainerAt(i)?.ActualWidth ?? 0;
        }
        _pendingInsertIndex = Math.Clamp(_dragFromIndex, 0, count);
        var originLeft = count > 0 ? _slotLefts[Math.Clamp(_dragFromIndex, 0, count - 1)] : pointer.X;
        _grabDx = pointer.X - originLeft;

        _savedOpacity = source.Opacity;
        source.Opacity = 0.9;
        if (source is Border border)
        {
            _savedBorderBrush = border.BorderBrush;
            _savedBorderThickness = border.BorderThickness;
            _savedEffect = border.Effect;
            border.BorderBrush = (Brush)FindResource("AccentBrush");
            border.BorderThickness = new Thickness(1);
            border.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black };
        }
        Panel.SetZIndex(source, 60);
    }

    private void ApplyDragPreview(double pointerX)
    {
        if (_dragSource is null || _dragFromIndex < 0 || _slotLefts.Length == 0) return;
        var count = _slotLefts.Length;
        _pendingInsertIndex = InsertionIndexDuringDrag(pointerX);
        var destination = _pendingInsertIndex > _dragFromIndex ? _pendingInsertIndex - 1 : _pendingInsertIndex;
        var slot = (_slotWidths[_dragFromIndex] > 0 ? _slotWidths[_dragFromIndex] : 120) + 3;

        for (var i = 0; i < count; i++)
        {
            var container = ContainerAt(i);
            if (container is null) continue;
            if (i == _dragFromIndex)
            {
                // The lifted tab tracks the pointer one-to-one (no easing).
                SetPreviewOffset(container, pointerX - _grabDx - _slotLefts[i], animate: false);
                continue;
            }
            var newIndex = i;
            if (_dragFromIndex < i && i <= destination) newIndex = i - 1;
            else if (destination <= i && i < _dragFromIndex) newIndex = i + 1;
            SetPreviewOffset(container, (newIndex - i) * slot, animate: true);
        }
    }

    private int InsertionIndexDuringDrag(double pointerX)
    {
        for (var i = 0; i < _slotLefts.Length; i++)
        {
            if (i == _dragFromIndex) continue;
            var centre = _slotLefts[i] + _slotWidths[i] / 2;
            if (pointerX < centre) return i;
        }
        return _slotLefts.Length;
    }

    private void SetPreviewOffset(FrameworkElement element, double x, bool animate)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }
        if (!animate)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = x;
            _previewX[element] = x;
            return;
        }
        if (_previewX.TryGetValue(element, out var current) && Math.Abs(current - x) < 0.5) return;
        _previewX[element] = x;
        var duration = UiMotion.DurationMilliseconds(130);
        if (duration <= 0)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = x;
            return;
        }
        transform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(x, TimeSpan.FromMilliseconds(duration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void ClearDragPreview()
    {
        foreach (var element in _previewX.Keys.ToList())
        {
            if (element.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
            }
            element.RenderTransform = null;
        }
        _previewX.Clear();

        if (_dragSource is not null)
        {
            _dragSource.Opacity = _savedOpacity;
            if (_dragSource is Border border)
            {
                border.BorderBrush = _savedBorderBrush;
                border.BorderThickness = _savedBorderThickness;
                border.Effect = _savedEffect;
            }
            Panel.SetZIndex(_dragSource, 0);
        }
        _dragSource = null;
        _dragFromIndex = -1;
        _pendingInsertIndex = 0;
    }

    // ---------- hit testing ----------

    private static TabItemModel? ModelAt(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: TabItemModel model }) return model;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>Classifies a press (Root coordinates) with <see cref="BrowserChromeHitTest"/>; built on click only, never per frame.</summary>
    private BrowserChromeHit ClassifyPress(Point point, out int tabIndex)
    {
        tabIndex = -1;
        var tabRects = new List<Rect>();
        var tabIndices = new List<int>();
        for (var i = 0; i < _items.Count; i++)
        {
            var c = ContainerAt(i);
            if (c is null || !Root.IsAncestorOf(c) || !c.IsVisible) continue;
            tabRects.Add(c.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight)));
            tabIndices.Add(i);
        }
        var actionRects = new List<Rect>();
        CollectButtonRects(Root, actionRects);
        var hit = BrowserChromeHitTest.Classify(point, tabRects, actionRects, Array.Empty<Rect>(),
            new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
        if (hit == BrowserChromeHit.Tab)
            for (var i = 0; i < tabRects.Count; i++)
                if (tabRects[i].Contains(point)) { tabIndex = tabIndices[i]; break; }
        return hit;
    }

    private void CollectButtonRects(DependencyObject parent, List<Rect> rects)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ButtonBase { IsVisible: true } button)
                rects.Add(button.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight)));
            else CollectButtonRects(child, rects);
        }
    }

    // ---------- tab interaction (root preview handlers) ----------

    private Point _dragStart;
    private TabItemModel? _pressed;
    private bool _dragStarted;

    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // The pure classifier (BrowserChromeHitTest) decides what was pressed: a button (close cross, (+),
        // overflow arrows) keeps its own click handling, a tab starts press/drag, empty caption drags the window.
        var hit = ClassifyPress(e.GetPosition(Root), out var tabIndex);
        if (hit == BrowserChromeHit.TabAction) return;
        var model = hit == BrowserChromeHit.Tab
            ? ModelAt(e.OriginalSource as DependencyObject) ?? (tabIndex >= 0 && tabIndex < _items.Count ? _items[tabIndex] : null)
            : null;

        if (model is null)
        {
            // Empty caption space: middle-click opens a tab, left drags the window, double toggles size.
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (!Settings.MiddleClickTitleBarNewTab) return;
                e.Handled = true;
                NewTabRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            if (e.ClickCount == 2) { CaptionDoubleClickRequested?.Invoke(this, EventArgs.Empty); return; }
            CaptionDragRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            e.Handled = true;
            switch (Settings.MiddleClick)
            {
                case MiddleClickActions.Duplicate: DuplicateRequested?.Invoke(this, model.Index); break;
                case MiddleClickActions.NewTab: NewTabRequested?.Invoke(this, EventArgs.Empty); break;
                case MiddleClickActions.Nothing: break;
                default: CloseRequested?.Invoke(this, model.Index); break;
            }
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount == 2 && Settings.CloseOnDoubleClick)
        {
            e.Handled = true;
            _pressed = null;
            _dragStarted = true;   // suppress the drag a second press would otherwise start
            CloseRequested?.Invoke(this, model.Index);
            return;
        }

        e.Handled = true;          // the press must not start a window drag
        _pressed = model;
        _dragStart = e.GetPosition(this);
        _dragStarted = false;
        Root.CaptureMouse();
    }

    private void Root_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pointer = e.GetPosition(this);
        if (!_dragStarted)
        {
            var delta = pointer - _dragStart;
            if (!BrowserTabDragPolicy.CrossedThreshold(delta.X, delta.Y, 4)) return;
            _dragStarted = Root.CaptureMouse();
            if (!_dragStarted) return;
            var source = ContainerAt(_pressed.Index);
            if (source is null) { Root.ReleaseMouseCapture(); _dragStarted = false; return; }
            PrepareDragPreview(source, _pressed, pointer);
        }

        ApplyDragPreview(e.GetPosition(Root).X);
        var window = Window.GetWindow(this);
        if (window is not null)
        {
            var inWindow = e.GetPosition(window);
            var rootOrigin = Root.TranslatePoint(new Point(0, 0), window);
            if (Settings.DetachToNewWindow && BrowserTabDragPolicy.IsBeyondTearOff(inWindow.X, inWindow.Y, window.ActualWidth,
                    rootOrigin.Y, Root.ActualHeight))
            {
                var screen = Shell.ScreenPoints.TryToScreen(window, inWindow, out var converted) ? converted : Shell.ScreenPoints.Cursor();
                var model = _pressed;
                ClearDragPreview();
                Root.ReleaseMouseCapture();
                _pressed = null;
                _dragStarted = false;
                HeldTearOffRequested?.Invoke(this, new TabTearOffEventArgs(model.Index, screen));
                e.Handled = true;
                return;
            }
        }
        e.Handled = true;
    }

    private void Root_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragStarted)
        {
            var clicked = _pressed;
            _pressed = null;
            Root.ReleaseMouseCapture();
            if (clicked is not null && _documents is not null && _documents.ActiveIndex != clicked.Index)
                TabActivated?.Invoke(this, clicked.Index);
            return;
        }
        var model = _pressed;
        var from = model?.Index ?? -1;
        var insert = _pendingInsertIndex;
        Root.ReleaseMouseCapture();
        ClearDragPreview();
        _pressed = null;
        _dragStarted = false;
        var to = BrowserTabDragPolicy.ReorderDestination(from, insert, _items.Count);
        if (to >= 0 && to != from) Reordered?.Invoke(this, (from, to));
        e.Handled = true;
    }

    // ---------- drop handling (reorder + merge) ----------

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabDragService.Format))
        {
            if (DroppedSongs.In(e.Data).Length > 0) return;   // song files from Windows: the main window opens them in new tabs
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var isLocalTab = TabDragService.Current is { } state && _documents is not null && _documents.IndexOf(state.Session) >= 0;
        if (!BrowserTabDragPolicy.AcceptsDrop(Settings.MergeAcrossWindows, isLocalTab))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (isLocalTab)
        {
            ApplyDragPreview(e.GetPosition(Root).X);
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabDragService.Format) || _documents is null) return;
        e.Handled = true;
        e.Effects = DragDropEffects.Move;
        DropMarker.Visibility = Visibility.Collapsed;

        if (TabDragService.Current is { } state && _documents.IndexOf(state.Session) >= 0)
        {
            // Same-window reorder: the drag is finished here, not a transfer to another window.
            state.HandledLocally = true;
            var from = _documents.IndexOf(state.Session);
            // The live preview already resolved the drop slot; fall back to a fresh hit test only if
            // the drag preview was never prepared (e.g. a synthetic drag with no initial move).
            var insert = _dragFromIndex >= 0 ? _pendingInsertIndex : InsertionIndexAt(e.GetPosition(Root).X);
            var to = insert > from ? insert - 1 : insert;
            if (to != from) Reordered?.Invoke(this, (from, to));
            return;
        }

        if (!Settings.MergeAcrossWindows)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var index = InsertionIndexAt(e.GetPosition(Root).X);

        if (TabDragService.Current is { } incoming)
        {
            // In-process merge: adopt the live document (undo history included).
            incoming.Consumed = true;
            TabDropped?.Invoke(this, new TabDropEventArgs(incoming.Session, index));
            return;
        }

        // Cross-process merge: rebuild the document from the payload.
        var json = e.Data.GetData(TabDragService.Format) as string;
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var project = ProjectService.Restore(json);
            var doc = DocumentSession.FromProject(project, null);
            doc.Project.IsDirty = true;
            TabDropped?.Invoke(this, new TabDropEventArgs(doc, index));
        }
        catch
        {
            Services.Trace.Error(Services.Trace.Ui, "tab drop: corrupt payload ignored"); // Corrupt payload: ignore the drop rather than crashing.
        }
    }

    // ---------- overflow arrows ----------

    private void OverflowLeft_Click(object sender, RoutedEventArgs e) => ScrollTabStrip(-1);
    private void OverflowRight_Click(object sender, RoutedEventArgs e) => ScrollTabStrip(1);

    private void ScrollTabStrip(int direction)
    {
        var viewport = Math.Max(1, Scroll.ViewportWidth);
        var maxOffset = Math.Max(0, Scroll.ExtentWidth - viewport);
        var step = Math.Max(90, Math.Min(Settings.MaxTabWidth + 3, viewport * 0.65));
        var next = Math.Clamp(Scroll.HorizontalOffset + direction * step, 0, maxOffset);
        Scroll.ScrollToHorizontalOffset(next);
        UpdateOverflow();
    }

    /// <summary>Shows an overflow arrow only on the side that still has hidden tabs.</summary>
    private void UpdateOverflow()
    {
        var viewport = Math.Max(0, Scroll.ViewportWidth);
        var extent = Scroll.ExtentWidth;
        var maxOffset = Math.Max(0, extent - viewport);
        var overflow = maxOffset > 1;
        var x = Math.Clamp(Scroll.HorizontalOffset, 0, maxOffset);
        OverflowLeftButton.Visibility = overflow && x > 1 ? Visibility.Visible : Visibility.Collapsed;
        OverflowRightButton.Visibility = overflow && x < maxOffset - 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- buttons / context menu ----------

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke(this, EventArgs.Empty);

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (ModelAt(sender as DependencyObject) is not { } model) return;
        e.Handled = true;
        CloseRequested?.Invoke(this, model.Index);
    }

    private static TabItemModel? MenuModel(object sender) =>
        (sender as FrameworkElement)?.DataContext as TabItemModel;

    private void MenuNewTab_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke(this, EventArgs.Empty);
    private void MenuDuplicate_Click(object sender, RoutedEventArgs e) { if (MenuModel(sender) is { } m) DuplicateRequested?.Invoke(this, m.Index); }
    private void MenuClose_Click(object sender, RoutedEventArgs e) { if (MenuModel(sender) is { } m) CloseRequested?.Invoke(this, m.Index); }
    private void MenuDetach_Click(object sender, RoutedEventArgs e) { if (MenuModel(sender) is { } m) DetachRequested?.Invoke(this, m.Index); }
    private void MenuCloseOthers_Click(object sender, RoutedEventArgs e) { if (MenuModel(sender) is { } m) CloseOthersRequested?.Invoke(this, m.Index); }
    private void MenuCloseRight_Click(object sender, RoutedEventArgs e) { if (MenuModel(sender) is { } m) CloseRightRequested?.Invoke(this, m.Index); }
}

public sealed class TabTearOffEventArgs(int index, Point screenPoint) : EventArgs
{
    public int Index { get; } = index;
    public Point ScreenPoint { get; } = screenPoint;
}

public sealed class TabDropEventArgs : EventArgs
{
    public TabDropEventArgs(DocumentSession session, int index)
    {
        Session = session;
        Index = index;
    }

    public DocumentSession Session { get; }
    public int Index { get; }
}
