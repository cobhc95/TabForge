using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

// Owns: the Avalonia templates of the menu items and the context menus, measured to the App.xaml menu templates: the tick column, the
//   caption padding, the gesture column and its text, the submenu arrow, the popup offsets and the popup border. The item's root border is named
//   PART_LayoutRoot so the AvaloniaLooks rules of the MenuItem look (hover, disabled) paint it.
// Does not own: the menu rows (Core), the item colours (the look table), the separator rule (AvaloniaFormControlStyles), or the menu's handlers.
// Tests: TabForge.Avalonia.Tests.ContextMenuTests and the menu-* and context-* A/B shots (ParityShotsTests).
public static class AvaloniaMenuTemplates
{
    // Tick column of a menu item (App.xaml "icon" presenter): 6 DIP left, 2 DIP right, 14 DIP minimum width.
    private static readonly Thickness TickMargin = new(6, 0, 2, 0);
    private const double TickMinWidth = 14;
    // The tick is a 12 x 10 DIP stroke path, bold enough to match the menu's tick.
    private const string CheckMarkData = "M 1,5 L 4.5,8.5 L 11,1.5";
    private const double CheckMarkWidth = 12;
    private const double CheckMarkHeight = 10;
    private const double CheckMarkStroke = 2;
    private const string RadioGlyph = "●";
    // Header padding of a menu item (App.xaml MenuItem Padding). A top-level header keeps the item's top and bottom padding and takes
    // TopLevelSideInset on both sides instead, so its label sits centred in the shade (the gesture and arrow columns hold no space for it).
    private static readonly Thickness ItemPadding = new(8, 5, 10, 5);
    private const double TopLevelSideInset = 8;
    // Gesture text (shortcut) column: 11 DIP text, 10 DIP from the right edge.
    private const double GestureFontSize = 11;
    private static readonly Thickness GestureMargin = new(0, 0, 10, 0);
    /// <summary>The gesture text a menu item shows, as the hotkey catalogue writes it ("Ctrl+T, Ctrl+Shift+Insert"); empty shows the InputGesture's text.
    /// Set by the menu builders before the item is added. Key: the item; ceiling: one value per item; read and written on the UI thread only.</summary>
    public static readonly AttachedProperty<string> GestureTextProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, string>("GestureText", typeof(AvaloniaMenuTemplates), "");
    // Submenu arrow: a 4 x 8 DIP chevron, 6 DIP from the right edge.
    private const double ArrowWidth = 4;
    private const double ArrowHeight = 8;
    private const string ArrowData = "M 0 0 L 4 4 L 0 8 Z";
    private static readonly Thickness ArrowMargin = new(0, 0, 6, 0);
    private const double ItemCornerRadius = 3;
    // Popup of a submenu or a top-level menu: 4 DIP corners, 4 DIP padding, 1 DIP border, 2 DIP below the bar, 3 DIP left for a submenu.
    private const double PopupCornerRadius = 4;
    private static readonly Thickness PopupPadding = new(4);
    private const double PopupBorderThickness = 1;
    private static readonly Thickness PopupMarginBelowBar = new(0, 2, 0, 0);
    private const double SubmenuPopupHorizontalOffset = -3;
    // Context menu (App.xaml ContextMenu): 1 DIP border, 4 DIP corners and padding, scrolls past 760 DIP.
    private static readonly Thickness ContextMenuPadding = new(4);
    private const double ContextMenuCornerRadius = 4;
    private const double ContextMenuBorderThickness = 1;
    private const double ContextMenuMaxHeight = 760;

    public static Styles Build() => new()
    {
        new Style(x => x.OfType<MenuItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new DynamicResourceExtension("DensMenuItemPad")),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate((parent, scope) => BuildItem((MenuItem)parent, scope))),
            },
        },
        HighlightStyle(":pointerover"),
        HighlightStyle(":selected"),
        HighlightStyle(":open"),
        new Style(x => x.OfType<ContextMenu>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, ContextMenuPadding),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate((parent, scope) => BuildContextMenu((ContextMenu)parent, scope))),
            },
        },
    };

    // Avalonia marks the hovered row :pointerover, the keyboard-highlighted row :selected and a parent of an open submenu :open; both show the hover shade like :pointerover does.
    private static Style HighlightStyle(string pseudoClass) =>
        new(x => x.OfType<MenuItem>().Class(pseudoClass).Template().OfType<Border>().Name("PART_LayoutRoot"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrush")) },
        };

    // A menu item: the root border, then a grid of tick, icon, caption, gesture and submenu arrow, with its popup for the children.
    private static Control BuildItem(MenuItem parent, INameScope scope)
    {
        var isTopLevel = parent.Parent is Menu;
        var tick = BuildTick(parent, isTopLevel);
        var icon = BuildIcon(parent);
        var header = BuildHeader(parent, isTopLevel);
        var gesture = BuildGesture(parent, isTopLevel);
        var arrow = BuildArrow(parent, isTopLevel);
        var popup = BuildSubmenuPopup(parent, isTopLevel);

        var grid = new Grid
        {
            // Transparent so the gaps between the columns take the pointer and the row hovers over its whole width.
            Background = Brushes.Transparent,
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            Children = { tick, icon, header, gesture, arrow, popup },
        };
        // No local background: the look table paints the hover and pressed states on this border, and a local value would override them.
        var root = new Border { Name = "PART_LayoutRoot", CornerRadius = new CornerRadius(ItemCornerRadius), Child = grid };
        root.RegisterInNameScope(scope);
        return root;
    }

    /// <summary>The tick column: the vector tick of a checked check box, or the dot of a checked radio item (a top-level header has no tick column).</summary>
    private static Border BuildTick(MenuItem parent, bool isTopLevel)
    {
        // A top-level header has no tick column (its caption sits at the header padding), so its tick host has no margin or minimum width.
        // A checked check box shows the vector tick; a checked radio item shows its dot as text.
        var radioDot = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        radioDot[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        var checkMark = new Path
        {
            Data = Geometry.Parse(CheckMarkData), Width = CheckMarkWidth, Height = CheckMarkHeight, StrokeThickness = CheckMarkStroke,
            StrokeJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        checkMark[!Shape.StrokeProperty] = new DynamicResourceExtension("TextPrimary");
        var glyph = new Grid { Children = { radioDot, checkMark } };
        void UpdateTick()
        {
            var checkedNow = parent.IsChecked;
            checkMark.IsVisible = checkedNow && parent.ToggleType == MenuItemToggleType.CheckBox;
            radioDot.Text = checkedNow && parent.ToggleType == MenuItemToggleType.Radio ? RadioGlyph : "";
        }
        UpdateTick();
        parent.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuItem.IsCheckedProperty || e.Property == MenuItem.ToggleTypeProperty) UpdateTick();
        };
        var tick = new Border
        {
            Name = "PART_ToggleIconPresenter", Margin = isTopLevel ? default : TickMargin, MinWidth = isTopLevel ? 0 : TickMinWidth,
            VerticalAlignment = VerticalAlignment.Center, Child = glyph,
        };
        Grid.SetColumn(tick, 0);
        return tick;
    }

    private static ContentPresenter BuildIcon(MenuItem parent)
    {
        var icon = new ContentPresenter { Name = "PART_IconPresenter", VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(ContentPresenter.ContentProperty, parent.GetObservable(MenuItem.IconProperty));
        Grid.SetColumn(icon, 1);
        return icon;
    }

    private static ContentPresenter BuildHeader(MenuItem parent, bool isTopLevel)
    {
        var header = new ContentPresenter { Name = "PART_HeaderPresenter", VerticalAlignment = VerticalAlignment.Center, RecognizesAccessKey = true };
        header.Bind(ContentPresenter.ContentProperty, parent.GetObservable(HeaderedItemsControl.HeaderProperty));
        void UpdateHeaderMargin()
        {
            var padding = parent.Padding;
            header.Margin = isTopLevel ? new Thickness(TopLevelSideInset, padding.Top, TopLevelSideInset, padding.Bottom) : padding;
        }
        UpdateHeaderMargin();
        parent.PropertyChanged += (_, e) =>
        {
            if (e.Property == TemplatedControl.PaddingProperty) UpdateHeaderMargin();
        };
        Grid.SetColumn(header, 2);
        return header;
    }

    private static TextBlock BuildGesture(MenuItem parent, bool isTopLevel)
    {
        var gesture = new TextBlock { Name = "PART_InputGestureText", FontSize = GestureFontSize, Margin = isTopLevel ? default : GestureMargin, VerticalAlignment = VerticalAlignment.Center };
        gesture[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        void UpdateGesture() => gesture.Text = GestureTextOf(parent);
        UpdateGesture();
        parent.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuItem.InputGestureProperty || e.Property == GestureTextProperty) UpdateGesture();
        };
        Grid.SetColumn(gesture, 3);
        return gesture;
    }

    private static Path BuildArrow(MenuItem parent, bool isTopLevel)
    {
        var arrow = new Path { Name = "PART_ChevronPath", Data = StreamGeometry.Parse(ArrowData), Width = ArrowWidth, Height = ArrowHeight, Margin = isTopLevel ? default : ArrowMargin, VerticalAlignment = VerticalAlignment.Center };
        arrow[!Shape.FillProperty] = new DynamicResourceExtension("MutedBrush");
        Grid.SetColumn(arrow, 4);
        void UpdateArrow() => arrow.IsVisible = !isTopLevel && parent.Items.Count > 0;
        UpdateArrow();
        if (parent.Items is INotifyCollectionChanged changes) changes.CollectionChanged += (_, _) => UpdateArrow();
        return arrow;
    }

    /// <summary>The popup that holds the child items of a menu item.</summary>
    private static Popup BuildSubmenuPopup(MenuItem parent, bool isTopLevel)
    {
        var popup = new Popup
        {
            Name = "PART_Popup",
            Placement = isTopLevel ? PlacementMode.BottomEdgeAlignedLeft : PlacementMode.RightEdgeAlignedTop,
            HorizontalOffset = isTopLevel ? 0 : SubmenuPopupHorizontalOffset,
            IsLightDismissEnabled = false,
            InheritsTransform = true,
        };
        popup.Bind(Popup.IsOpenProperty, new Binding { Path = nameof(MenuItem.IsSubMenuOpen), Source = parent, Mode = BindingMode.TwoWay });
        var popupBorder = new Border
        {
            CornerRadius = new CornerRadius(PopupCornerRadius), Padding = PopupPadding, Margin = PopupMarginBelowBar,
            BorderThickness = new Thickness(PopupBorderThickness),
        };
        popupBorder[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        popupBorder[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
        var childItems = new ItemsPresenter { Name = "PART_ItemsPresenter" };
        childItems.Bind(ItemsPresenter.ItemsPanelProperty, parent.GetObservable(ItemsControl.ItemsPanelProperty));
        popupBorder.Child = childItems;
        popup.Child = popupBorder;
        return popup;
    }

    // The context menu: a bordered panel that scrolls past its maximum height, with the items inside.
    private static Control BuildContextMenu(ContextMenu parent, INameScope scope)
    {
        var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
        items.Bind(ItemsPresenter.ItemsPanelProperty, parent.GetObservable(ItemsControl.ItemsPanelProperty));
        var scroller = new ScrollViewer
        {
            MaxHeight = ContextMenuMaxHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = items,
        };
        var border = new Border
        {
            Name = "PART_LayoutRoot", CornerRadius = new CornerRadius(ContextMenuCornerRadius), BorderThickness = new Thickness(ContextMenuBorderThickness),
            Child = scroller,
        };
        border.Bind(Border.PaddingProperty, parent.GetObservable(TemplatedControl.PaddingProperty));
        border.Bind(Border.BackgroundProperty, parent.GetObservable(TemplatedControl.BackgroundProperty));
        border.Bind(Border.BorderBrushProperty, parent.GetObservable(TemplatedControl.BorderBrushProperty));
        border.RegisterInNameScope(scope);
        return border;
    }

    /// <summary>Sets the gesture text an item shows; the menu builders call it with the hotkey catalogue's display text.</summary>
    public static void SetGestureText(MenuItem item, string text) => item.SetValue(GestureTextProperty, text);

    private static string GestureTextOf(MenuItem item)
    {
        var text = item.GetValue(GestureTextProperty);
        return text.Length > 0 ? text : item.InputGesture?.ToString() ?? "";
    }
}
