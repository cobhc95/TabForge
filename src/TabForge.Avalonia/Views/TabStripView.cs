using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Shell;

// Owns: building the document tabs and their layout; the press, move past TabDragRules.DragThreshold and release that make a tab drag; the
//   drop preview over the strip (the accent highlight and the insertion marker), the neighbour slide of an in-strip reorder and the tab hidden
//   while it is torn off.
// Does not own: names and tooltips (TabLabelPresenter), width, close-button and corner rules (TabStripPresenter), the order (the caller's
//   list), what a tab drag does (DocumentTabDragFlow) or the tab context menu (MainWindowTabMenu).
// Tests: TabForge.Avalonia.Tests.PanelBarsTests, TabForge.Avalonia.Tests.TabTearOffTests, TabForge.Avalonia.Tests.TabStripDragVisualTests.

namespace TabForge.Avalonia.Views;

/// <summary>
/// One document as the tab strip shows it: the file its tab names (the save file, else the imported file; null for a new song), its song title, whether it has unsaved changes (the dot)
/// and whether it is playing (the speaker; the strip shows it only when the tab settings show the playing indicator).
/// </summary>
public sealed record TabStripDocument(string? LabelPath, string SongTitle, bool Unsaved = false, bool Playing = false, bool HasSaveFile = true);

/// <summary>
/// The document tab strip: one tab per document in the caller's order (name, tooltip, a close cross as the setting says), the
/// active tab on the active-tab colour, then the 36 px (+) button, sized.
/// </summary>
public sealed partial class TabStripView : UserControl
{
    /// <summary>The gap between tabs, in DIPs; the strip's stack spacing and the reorder slide both use it.</summary>
    public const double TabSpacing = 3;
    /// <summary>How long a neighbouring tab eases into its reorder slot, in milliseconds.</summary>
    public const double SlideMilliseconds = UiTiming.TabReorderSlideMs;
    /// <summary>How long a dropped tab eases from where it was released into its slot, in milliseconds.</summary>
    public const double DropSettleMilliseconds = UiTiming.TabDropSettleMs;
    /// <summary>The opacity of the tab while it is lifted by a drag.</summary>
    public const double DragLiftOpacity = 0.9;
    /// <summary>The blur radius of the shadow under a lifted tab, in DIPs.</summary>
    public const double DragShadowBlur = 16;
    /// <summary>How long an unselected tab's fill takes to fade to and from its hover level, in milliseconds.</summary>
    internal const double TabHoverFadeMilliseconds = 100;
    /// <summary>The tab's border width: drawn always (transparent when idle), so lifting a tab does not change its size.</summary>
    private const double TabBorderWidth = 1;
    private const double TabHorizontalPadding = 9;
    private const double NewTabButtonWidth = 36;
    private const double DropHighlightBorder = 2;
    private const double DropMarkerWidth = 2;
    private readonly StackPanel _tabs = new() { Name = "Tabs", Orientation = Orientation.Horizontal, Spacing = TabSpacing };
    private readonly Border _dropHighlight;
    private readonly Rectangle _insertMarker;
    private readonly TabStripMotion _motion;
    private readonly Button _add;
    private int? _hiddenTab;
    // The tab a drag moves (null when none): it follows the pointer, is lifted and is left out of the neighbour slides.
    private int? _draggedTab;
    private double _draggedOffset;
    // A dropped tab's offset, applied to the tab at its new index by the next Show, which then eases it into place.
    private (int Index, double Offset)? _carry;
    // The tab titles of the last Show, to find a tab closed by the next one.
    private IReadOnlyList<string> _shownNames = Array.Empty<string>();
    // The tab widths kept after a close while the pointer stays over the strip (null otherwise), and the widths the tabs resize to after.
    private double[]? _frozenWidths;
    private double _naturalMinWidth;
    private double _naturalMaxWidth;
    private TopLevel? _top;

    public TabStripView(TabSettings settings)
    {
        Name = "TabStrip";
        Settings = settings;
        _motion = new TabStripMotion(_tabs);
        // The (+) button rounds with the tabs: square while the saved tab style is square, pill-rounded otherwise.
        var add = GlyphButton("NewTab", "TabBarPlusButton", NewTabButtonWidth, TabStripPresenter.CornerRadius(Settings), Glyph(IconGeometry.Plus, 14));
        _add = add;
        add.Margin = new Thickness(6, 0, 0, 0);
        TabForge.Avalonia.Platform.Controls.HotkeyTip.Set(add, "Tab.New", "New tab");
        AutomationProperties.SetName(add, "New tab");
        add.Click += (_, _) => NewTabRequested?.Invoke();
        // The strip is transparent so the title row's chrome colour shows behind it; tabs start 8 px in and hang from the top edge.
        BuildScroller();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0), Children = { _left, _scroll, _right, add } };
        _dropHighlight = new Border { Name = "TabDropHighlight", IsVisible = false, IsHitTestVisible = false, BorderThickness = new Thickness(DropHighlightBorder), CornerRadius = new CornerRadius(8) };
        _dropHighlight[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        _dropHighlight[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
        _insertMarker = new Rectangle { Name = "TabInsertMarker", IsVisible = false, IsHitTestVisible = false, Width = DropMarkerWidth, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _insertMarker[!Shape.FillProperty] = new DynamicResourceExtension("AccentBrush");
        Content = new Grid { Children = { row, _dropHighlight, _insertMarker } };
        // The three levels of a tab's fill: rest, hover, and the selected tab's own local fill (TabActiveBrush), which wins over both.
        Styles.Add(new Style(x => x.OfType<Border>().Class("DocumentTab"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("TabRestSurfaceBrush")) },
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("DocumentTab").Class(":pointerover"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("TabHoverSurfaceBrush")) },
        });
        // Escape cancels a tab drag while the strip holds the press, whichever child has the keyboard focus.
        AddHandler(KeyDownEvent, OnStripKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnStripKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _press is not { Dragging: true }) return;
        e.Handled = true;
        CancelDrag();
    }

    // The drag stops from the strip's side too: its press, lift, follow and marker end here, then the flow is told to revert.
    private void CancelDrag()
    {
        EndDrag();
        TabDragCancelled?.Invoke();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        // Bubbling: a tab's own release handler runs first. This one ends a press whose capture was lost or released off the tab.
        _top?.AddHandler(InputElement.PointerReleasedEvent, OnWindowPointerReleased);
        if (_top is Window window) window.Deactivated += OnWindowDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _top?.RemoveHandler(InputElement.PointerReleasedEvent, OnWindowPointerReleased);
        if (_top is Window window) window.Deactivated -= OnWindowDeactivated;
        _top = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        // The strip left: the tabs kept through a close now take their natural widths.
        if (_frozenWidths is null) return;
        _frozenWidths = null;
        foreach (var child in _tabs.Children)
        {
            if (child is not Control tab) continue;
            tab.MinWidth = _naturalMinWidth;
            tab.MaxWidth = _naturalMaxWidth;
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e) => ReleasePress(e);

    // Losing activation ends a press that was not dragged; a running drag goes to its owner, so a tear-off can keep the pointer.
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_press is not { } press) return;
        if (press.Dragging) TabDragInterrupted?.Invoke();
        else LosePress();
    }

    // The press ends without a release (its capture was lost, or a move came with no button held): a dragged tab cancels, a click just clears.
    private void LosePress()
    {
        if (_press is not { } held) return;
        _press = null;
        held.Pointer.Capture(null);
        if (held.Dragging) CancelDrag();
        else HideDropPreview();
    }

    // Ends the press on a release: a dragged tab drops, a click only clears the press. The insertion marker hides on every end.
    private void ReleasePress(PointerReleasedEventArgs e)
    {
        if (_press is not { } press) return;
        _press = null;
        e.Pointer.Capture(null);
        if (press.Dragging) TabDragEnded?.Invoke(press.Index, e);
        HideDropPreview();
    }

    // A transparent square button with a stroked glyph centred in it; the look class gives it the hover border.
    private static Button GlyphButton(string name, string look, double size, double corner, Control glyph)
    {
        var button = new Button
        {
            Name = name, Width = size, Height = size, Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(corner),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Background = AvaloniaPalette.TransparentBrush, Focusable = false, Content = glyph,
        };
        button.Classes.Add(look);
        return button;
    }

    private static Path Glyph(string geometry, double size)
    {
        var path = new Path
        {
            Name = "Glyph", Data = Geometry.Parse(geometry), Width = size, Height = size, Stretch = Stretch.Uniform, StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false,
        };
        path[!Shape.StrokeProperty] = new DynamicResourceExtension("TabIdleGlyph");
        return path;
    }

    public IReadOnlyList<Control> Tabs => _tabs.Children;

    /// <summary>The style, width, close-button and height rules the tabs are drawn with; the owner sets it and calls Show again.</summary>
    public TabSettings Settings { get; set; }

    /// <summary>Raised with the tab index when a tab is pressed.</summary>
    public event Action<int>? TabPressed;

    /// <summary>Raised with the tab index when its close mark is pressed.</summary>
    public event Action<int>? CloseRequested;

    /// <summary>Raised with the tab index while a pressed tab is dragged past the threshold.</summary>
    public event Action<int, PointerEventArgs>? TabDragMoved;

    /// <summary>Raised on release after a drag started.</summary>
    public event Action<int, PointerReleasedEventArgs>? TabDragEnded;

    /// <summary>Raised when a running tab drag is cancelled by Escape or by losing the pointer capture (the strip has already ended its visuals).</summary>
    public event Action? TabDragCancelled;

    /// <summary>Raised when the window loses activation while a tab is dragged; the drag's owner decides whether it ends.</summary>
    public event Action? TabDragInterrupted;

    // a press redraws the strip (the tab switches), so the pointer is kept to capture the new tab; InTab is the grabbed point in the tab's own coordinates
    private (int Index, Point At, bool Dragging, IPointer Pointer, Point InTab)? _press;

    /// <summary>Raised when the new-tab button is pressed.</summary>
    public event Action? NewTabRequested;

    /// <summary>Raised with the tab index when the middle-click setting duplicates that tab.</summary>
    public event Action<int>? DuplicateRequested;

    /// <summary>Rebuilds the tabs; called when the documents or the active one change, never per frame.</summary>
    public void Show(IReadOnlyList<TabStripDocument> documents, int activeIndex, double stripWidth)
    {
        // A rebuild ends a running drag, and stops every tab's motion on the tabs it is about to remove.
        if (_press is { Dragging: true }) CancelDrag();
        _motion.ResetAll();
        // The (+) button is built once: its corners still follow the saved tab style on every redraw.
        _add.CornerRadius = new CornerRadius(TabStripPresenter.CornerRadius(Settings));
        // A rebuilt strip has no drop preview: a marker left showing by an earlier drag would stay stuck until the next drag move.
        HideDropPreview();
        var names = documents.Select(document => TabLabelPresenter.Name(document.LabelPath, document.SongTitle)).ToList();
        FreezeWidthsOnClose(names);
        _shownNames = names;
        _tabs.Children.Clear();
        _initialWidth = stripWidth;
        _activeIndex = activeIndex;
        _revealPending = true;
        var (min, max) = FitWidths(documents.Count);
        var close = TabStripPresenter.CloseButton(Settings);
        for (var i = 0; i < documents.Count; i++)
        {
            var (tabMin, tabMax) = _frozenWidths is { } frozen ? (frozen[i], frozen[i]) : (min, max);
            _tabs.Children.Add(BuildTab(documents[i], i, i == activeIndex, names[i], tabMin, tabMax, close));
        }
        if (_carry is { } carry)
        {
            _carry = null;
            if (carry.Index < _tabs.Children.Count) _motion.Settle(_tabs.Children[carry.Index], carry.Offset);
        }
        if (_press is { } held && held.Index < _tabs.Children.Count) held.Pointer.Capture(_tabs.Children[held.Index]);
    }

    /// <summary>One tab: its title, close cross, playing icon and unsaved dot, with the hover, press, drag and middle-click handlers.</summary>
    private Border BuildTab(TabStripDocument document, int i, bool active, string name, double tabMin, double tabMax, TabCloseButton close)
    {
        var title = new TextBlock { Name = "TabTitle", Text = name, FontSize = Settings.TabFontSize, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(active ? "TextStrong" : "TextMuted");
        var cross = GlyphButton("TabClose", "TabBarCloseButton", 20, 10, Glyph(IconGeometry.Close, 10));
        cross.Margin = new Thickness(6, 0, 0, 0);
        cross.IsVisible = close.Always || ((close.OnHover || close.ActiveOnly) && active);
        TabForge.Avalonia.Platform.Controls.HotkeyTip.Set(cross, "Tab.Close", "Close tab");
        AutomationProperties.SetName(cross, "Close tab");
        DockPanel.SetDock(cross, Dock.Right);
        var speaker = NewPlayingIcon(document);
        var dot = NewUnsavedDot(document);
        var tab = new Border
        {
            MinWidth = tabMin, MaxWidth = tabMax, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Height = Settings.TabHeight, IsVisible = i != _hiddenTab,
            // The border is always one DIP wide (transparent until lifted) and the padding gives back its width, so the tab's size never changes.
            BorderThickness = new Thickness(TabBorderWidth), BorderBrush = AvaloniaPalette.TransparentBrush,
            // The top corners stay square: a tab's fill reaches the window's top edge, so only its bottom corners round (the top rounded notched the chrome).
            Padding = new Thickness(TabHorizontalPadding - TabBorderWidth, 0), CornerRadius = new CornerRadius(0, 0, TabStripPresenter.CornerRadius(Settings), TabStripPresenter.CornerRadius(Settings)),
            // The fill comes from the strip's styles (rest, hover); a tab takes no focus, so it never draws a focus outline.
            Focusable = false,
            Transitions = new Transitions { new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(UiMotion.DurationMilliseconds(TabHoverFadeMilliseconds)) } },
            Child = new DockPanel { LastChildFill = true, Children = { cross, dot, speaker, title } },
        };
        tab.Classes.Add("DocumentTab");
        if (active) tab[!Border.BackgroundProperty] = new DynamicResourceExtension("TabActiveBrush");
        ToolTip.SetTip(tab, TabLabelPresenter.Tooltip(document.LabelPath, name, document.SongTitle, document.HasSaveFile));
        var index = i;
        if (close.OnHover && !active)   // Active and hover: a tab that is not shown gets its cross only while the pointer is over it
        {
            tab.PointerEntered += (_, _) => cross.IsVisible = true;
            tab.PointerExited += (_, _) => cross.IsVisible = false;
        }
        WireTabPointer(tab, cross, index);
        return tab;
    }

    private Path NewPlayingIcon(TabStripDocument document)
    {
        var speaker = new Path
        {
            Name = "PlayingIcon", Data = Geometry.Parse(IconGeometry.Speaker), Width = 13, Height = 13, Stretch = Stretch.Uniform, StrokeThickness = 1.2,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
            IsVisible = document.Playing && Settings.ShowPlayingIndicator,
        };
        speaker[!Shape.FillProperty] = new DynamicResourceExtension("TextStrongBrush");
        speaker[!Shape.StrokeProperty] = new DynamicResourceExtension("TextStrongBrush");
        ToolTip.SetTip(speaker, "Playing");
        AutomationProperties.SetName(speaker, "Playing");
        DockPanel.SetDock(speaker, Dock.Left);
        return speaker;
    }

    private static Ellipse NewUnsavedDot(TabStripDocument document)
    {
        var dot = new Ellipse
        {
            Name = "UnsavedDot", Width = 8, Height = 8, StrokeThickness = 1, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            IsVisible = document.Unsaved,
        };
        dot[!Shape.FillProperty] = new DynamicResourceExtension("CursorBrush");
        dot[!Shape.StrokeProperty] = new DynamicResourceExtension("MutedBrush");
        ToolTip.SetTip(dot, "Unsaved changes");
        DockPanel.SetDock(dot, Dock.Right);
        return dot;
    }

    /// <summary>The cross, press, move, release and capture-lost handlers of a tab: click, middle-click, double-click close and the drag of the tab.</summary>
    private void WireTabPointer(Border tab, Button cross, int index)
    {
        cross.Click += (_, _) => CloseRequested?.Invoke(index);
        tab.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(tab).Properties;
            if (point.IsMiddleButtonPressed)
            {
                e.Handled = true;
                RunMiddleClick(index);
                return;
            }
            if (point.IsLeftButtonPressed && e.ClickCount == 2 && Settings.CloseOnDoubleClick)
            {
                e.Handled = true;
                CloseRequested?.Invoke(index);
                return;
            }
            if (!point.IsLeftButtonPressed) return;
            _press = (index, e.GetPosition(this), false, e.Pointer, e.GetPosition(tab));
            e.Pointer.Capture(tab);
            TabPressed?.Invoke(index);
        };
        tab.PointerMoved += (_, e) =>
        {
            if (_press is not { } press || press.Index != index) return;
            // A move with no button held means the release went unseen: the press ends here instead of dragging on.
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                LosePress();
                return;
            }
            var pointer = e.GetPosition(this);
            var delta = pointer - press.At;
            if (!press.Dragging && !TabDragRules.CrossedThreshold(delta.X, delta.Y)) return;
            if (!press.Dragging)
            {
                _press = press with { Dragging = true };
                BeginDrag(index, tab);
            }
            FollowPointer(tab, pointer, press.InTab);
            TabDragMoved?.Invoke(index, e);
        };
        tab.PointerReleased += (_, e) =>
        {
            if (_press is not { } press || press.Index != index) return;
            ReleasePress(e);
        };
        // Losing the capture ends the press: a dragged tab cancels its drag, a click just clears. A tab removed by a rebuild is no
        // longer in the strip, so its loss is not an end.
        tab.PointerCaptureLost += (_, _) =>
        {
            if (_press is not { } held || held.Index != index || !_tabs.Children.Contains(tab)) return;
            LosePress();
        };
    }

    // A tab closed while the pointer is over the strip keeps the other tabs' widths, so the next × lands under the pointer; the widths
    // come back when the pointer leaves (OnPointerExited). Widths are read only once laid out (none is zero).
    private void FreezeWidthsOnClose(IReadOnlyList<string> names)
    {
        var widths = _tabs.Children.Select(tab => tab.Bounds.Width).ToArray();
        var laidOut = widths.Length == _shownNames.Count && widths.All(width => width > 0);
        if (IsPointerOver && laidOut && names.Count < _shownNames.Count)
            _frozenWidths = widths.Where((_, index) => index != ClosedIndex(_shownNames, names)).ToArray();
        else if (_frozenWidths is { } frozen && frozen.Length != names.Count)
            _frozenWidths = null;
    }

    // The index of the tab that a rebuild removed: the first position where the titles differ, else the last tab.
    private static int ClosedIndex(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        for (var index = 0; index < after.Count; index++)
            if (before[index] != after[index]) return index;
        return before.Count - 1;
    }

}
