// Owns: the one popup and density-size entry point: bar and row heights and pads by density, and the popup placement
//   clamp to the owner screen working area.
// Does not own: the density factors (UiDensity), the working area (WindowMetrics), or any view layout (each window keeps its own).
// Tests: UiMetricsTests.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia;

/// <summary>
/// The one popup and density-size entry point. Bar, row and pad take their existing base and scale it by density, so at
/// Comfortable they return the base exactly (Compact shrinks and Spacious enlarges it). The popup clamp keeps a
/// popup inside the owner working area for the supported placements; other placements are left alone. Nothing calls it yet.
/// </summary>
public static class UiMetrics
{
    /// <summary>The square of a transport bar button at full width (DIPs; it shrinks to the transport minimum when the bar is narrow).</summary>
    public const double TransportButtonSize = 34;
    /// <summary>The square of a preset row action button (DIPs): the pencil and the bin, a little wider than the 14 DIP icon so they are easy to hit.</summary>
    public const double PresetRowButtonSize = 20;
    /// <summary>The square of a Tools pane tool button (DIPs).</summary>
    public const double ToolButtonSize = 36;

    /// <summary>The quick actions group's (Open / Save) height of one button (DIPs): the menu row's icon buttons.</summary>
    public const double QuickButtonHeight = 27;
    /// <summary>The quick actions group's least button width (DIPs).</summary>
    public const double QuickButtonMinWidth = 30;
    /// <summary>The gap between the Open and Save buttons (DIPs).</summary>
    public const double QuickGroupGap = 2;
    /// <summary>The group's inset from the Tab pill and the divider on each side (DIPs): the same left and right.</summary>
    public const double QuickGroupSide = 6;
    /// <summary>The group's rounded corner (DIPs): the grey capsule's corner, clipped to its bounds.</summary>
    public const double QuickGroupCorner = 6;
    /// <summary>The group's edge (DIPs): the one inset on every side, so the group has no inner padding and its buttons sit inside the edge evenly.</summary>
    public const double QuickGroupEdge = 1;

    /// <summary>The quick actions group's (Open / Save) frame: the grey capsule clipped to its rounded bounds, an edge as the even inset on all sides,
    /// and the same left and right margin.</summary>
    public static void StyleQuickGroup(Border group)
    {
        group.CornerRadius = new CornerRadius(QuickGroupCorner);
        group.BorderThickness = new Thickness(QuickGroupEdge);
        group.Padding = default;
        group.Margin = new Thickness(QuickGroupSide, 0, QuickGroupSide, 0);
        group.ClipToBounds = true;
    }

    /// <summary>The size of a quick action button (a menu row icon button): the row's icon height, no margin, its own hover and pressed fills clipped to its bounds.</summary>
    public static void StyleQuickCell(TemplatedControl button)
    {
        button.Height = QuickButtonHeight;
        button.MinWidth = QuickButtonMinWidth;
        button.Padding = new Thickness(4, 0);
        button.Margin = default;
        button.ClipToBounds = true;
    }

    /// <summary>The bar height for the base at the appearance density.</summary>
    public static double Bar(double baseDips, AppearanceSettings appearance) =>
        baseDips * UiDensity.Of(appearance).Bar;

    /// <summary>The row height for the base at the appearance density.</summary>
    public static double Row(double baseDips, AppearanceSettings appearance) =>
        baseDips * UiDensity.Of(appearance).Row;

    /// <summary>The uniform pad for the base at the appearance density.</summary>
    public static Thickness Pad(double baseDips, AppearanceSettings appearance) =>
        new Thickness(baseDips * UiDensity.Of(appearance).Pad);

    /// <summary>Sets the outer margin of <paramref name="control"/> (device-independent pixels) and returns it, so a view can place a part without a literal thickness.</summary>
    public static T WithMargin<T>(this T control, double left, double top, double right, double bottom) where T : Layoutable
    {
        control.Margin = new Thickness(left, top, right, bottom);
        return control;
    }

    /// <summary>Removes the outer margin of <paramref name="control"/> (a control a canvas places, which the button look's margin would shift) and returns it.</summary>
    public static T WithNoMargin<T>(this T control) where T : Layoutable
    {
        control.Margin = default;
        return control;
    }

    /// <summary>Sets the padding of <paramref name="control"/> (device-independent pixels, <paramref name="x"/> left and right, <paramref name="y"/> top and bottom) and returns it.</summary>
    public static T WithPadding<T>(this T control, double x, double y) where T : global::Avalonia.Controls.Primitives.TemplatedControl
    {
        control.Padding = new Thickness(x, y, x, y);
        return control;
    }

    /// <summary>Sets the padding of a border (<paramref name="x"/> left and right, <paramref name="y"/> top and bottom) and returns it.</summary>
    public static Border WithPadding(this Border border, double x, double y)
    {
        border.Padding = new Thickness(x, y, x, y);
        return border;
    }

    /// <summary>A thickness of the four sides each multiplied by <paramref name="factor"/> (the density factor).</summary>
    public static Thickness Sides(double left, double top, double right, double bottom, double factor) =>
        new Thickness(left * factor, top * factor, right * factor, bottom * factor);

    /// <summary>The asymmetric pad at the appearance density: the height scales, the width stays as set.</summary>
    public static Thickness Pad(double x, double y, AppearanceSettings appearance)
    {
        var scaled = y * UiDensity.Of(appearance).Pad;
        return new Thickness(x, scaled, x, scaled);
    }

    /// <summary>
    /// The top-left clamped inside the working area, in the same unit for all three. A popup larger than the area sits at the origin.
    /// </summary>
    public static PixelPoint ClampPopup(PixelPoint desired, Size popupSize, Size workingArea)
    {
        double x = desired.X;
        double y = desired.Y;
        if (popupSize.Width >= workingArea.Width)
            x = 0;
        else
            x = Math.Clamp(x, 0, workingArea.Width - popupSize.Width);
        if (popupSize.Height >= workingArea.Height)
            y = 0;
        else
            y = Math.Clamp(y, 0, workingArea.Height - popupSize.Height);
        return new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
    }

    /// <summary>Opens <paramref name="menu"/> under its button, left edges aligned (the dropdown buttons' menus).</summary>
    public static void PlaceUnderLeft(ContextMenu menu) => menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
}

/// <summary>
/// The sizes and spacing of the status bar scale popup (ScalePanel): its width, the gaps between its sections, the segmented buttons, the chips
/// and the confidence bar. Kept here so the popup file holds no literal layout numbers.
/// </summary>
public static class ScalePopupLayout
{
    /// <summary>The popup panel width (DIPs): the three radio cards, the pick-another drop-downs and the chips fit on it.</summary>
    public const double PanelSpan = 340;

    /// <summary>The fretboard popup when the window is wide: the scale column and the look column side by side; the window width from which it is used; the full list height; the gap between columns.</summary>
    /// <summary>Margins and gaps of the fretboard popup.</summary>
    public static global::Avalonia.Thickness ColumnMargin => new(0, 0, ColumnGap, 0);
    public static global::Avalonia.Thickness ButtonRowMargin => new(0, 12, 0, 0);
    public static global::Avalonia.Thickness BadgeLinkMargin => new(8, 0, 0, 0);
    public static global::Avalonia.Thickness LinkRowMargin => new(0, 4, 0, 0);
    public const double ButtonGap = 8;
    public const double WidePanelSpan = 700, WideThreshold = 900, ListHeight = 260, PopupMaxHeight = 760, ColumnGap = 16, PercentSpan = 40, GreyedOpacity = 0.55;

    /// <summary>The colour swatches of the fretboard look: the circle size, the ring of the selected one and the gap between them.</summary>
    public const double SwatchSize = 26, SwatchRing = 2.5, SwatchEdge = 1, SwatchGap = 8, SwatchCorner = 13;

    private const double SectionTopGap = 10, SectionBottomGap = 3, CardGap = 4, ChipGap = 4, ConfidenceBarThickness = 4, DropDownSpan = 120;
    private static readonly Thickness SegmentPad = new(8, 3);
    private static readonly Thickness ChipPad = new(8, 2);

    /// <summary>A small caps muted section label (DETECTED, READ FROM...) with room above it.</summary>
    public static TextBlock SectionLabel(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, SectionTopGap, 0, SectionBottomGap) };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return label;
    }

    /// <summary>The favourite star button of a scale list row (filled when the scale is a favourite).</summary>
    public static Button FavouriteStar(bool favourite) =>
        new() { Content = favourite ? "★" : "☆", Name = "ScaleFavourite", Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Right };

    /// <summary>The gap above a card after the first one.</summary>
    public static T CardSpacing<T>(this T control) where T : Control
    {
        control.Margin = new Thickness(0, CardGap, 0, 0);
        return control;
    }

    /// <summary>A segmented button: a toggle that sizes to its text; the selected one is accent filled, the others plain with a hover fill.</summary>
    public static ToggleButton Segment(string name, string text, bool on) =>
        Skin(new ToggleButton { Name = name, Content = text, IsChecked = on, Padding = SegmentPad, Margin = new Thickness(0, 0, ChipGap, ChipGap), Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Center }, chip: false);

    /// <summary>A scale-family chip: on is an accent tint with a check mark, off is an outline only.</summary>
    public static ToggleButton Chip(string name, string text, bool on) =>
        Skin(new ToggleButton { Name = name, Content = text, IsChecked = on, Padding = ChipPad, Margin = new Thickness(0, 0, ChipGap, ChipGap), Focusable = false }, chip: true);

    /// <summary>The brush of a saved custom scale colour (the Custom swatch fill).</summary>
    public static IBrush CustomFill(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    /// <summary>A colour swatch: a theme-colour circle; the selected one has the accent ring. Custom shows the saved custom colour.</summary>
    public static ToggleButton Swatch(string name, string label, bool on, string brushKey, IBrush? customFill = null)
    {
        var dot = new Border
        {
            Width = SwatchSize,
            Height = SwatchSize,
            CornerRadius = new CornerRadius(SwatchCorner),
            BorderThickness = new Thickness(on ? SwatchRing : SwatchEdge),
        };
        if (customFill is not null) dot.Background = customFill;
        else dot[!Border.BackgroundProperty] = new DynamicResourceExtension(brushKey);
        dot[!Border.BorderBrushProperty] = new DynamicResourceExtension(on ? "AccentBrush" : "BorderSoftBrush");
        var button = new ToggleButton
        {
            Name = name,
            Content = dot,
            IsChecked = on,
            Width = SwatchSize,
            Height = SwatchSize,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, SwatchGap, SwatchGap),
            Focusable = false,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Template = new global::Avalonia.Controls.Templates.FuncControlTemplate<ToggleButton>((_, _) => dot);
        button.IsCheckedChanged += (_, _) =>
        {
            var selected = button.IsChecked == true;
            dot.BorderThickness = new Thickness(selected ? SwatchRing : SwatchEdge);
            dot[!Border.BorderBrushProperty] = new DynamicResourceExtension(selected ? "AccentBrush" : "BorderSoftBrush");
        };
        return button;
    }

    /// <summary>A wrapping row of segmented buttons, so none is clipped.</summary>
    public static WrapPanel SegmentRow(IReadOnlyList<ToggleButton> buttons)
    {
        var row = new WrapPanel();
        foreach (var button in buttons) row.Children.Add(button);
        return row;
    }

    private static ToggleButton Skin(ToggleButton button, bool chip)
    {
        var face = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        face.Child = text;
        var plain = chip ? button.Content : null;
        var over = false;
        void Paint()
        {
            var on = button.IsChecked == true;
            if (chip) button.Content = on ? "✓ " + plain : plain;
            face.Padding = button.Padding;
            text.Text = button.Content as string;
            face[!Border.BackgroundProperty] = new DynamicResourceExtension(chip ? (on ? "AccentSoftBrush" : over ? "HoverBrush" : "PanelBackground") : (on ? "AccentBrush" : over ? "HoverBrush" : "PanelBackground"));
            face[!Border.BorderBrushProperty] = new DynamicResourceExtension(on ? "AccentBrush" : "BorderBrush");
            text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(on ? (chip ? "TextBrush" : "OnAccentBrush") : (chip ? "TextMuted" : "TextBrush"));
        }
        button.Template = new global::Avalonia.Controls.Templates.FuncControlTemplate<ToggleButton>((_, _) => face);
        button.IsCheckedChanged += (_, _) => Paint();
        button.PointerEntered += (_, _) => { over = true; Paint(); };
        button.PointerExited += (_, _) => { over = false; Paint(); };
        Paint();
        return button;
    }

    /// <summary>The thin confidence bar under a candidate card (0 to 1).</summary>
    public static ProgressBar ConfidenceBar(double fraction) =>
        new() { Minimum = 0, Maximum = 1, Value = Math.Clamp(fraction, 0, 1), Height = ConfidenceBarThickness, MinHeight = ConfidenceBarThickness, ShowProgressText = false };

    /// <summary>A key or scale drop-down of the pick-another card.</summary>
    public static ComboBox DropDown(string name, IEnumerable<string> items, int selected) =>
        new() { Name = name, ItemsSource = items.ToList(), SelectedIndex = selected, MinWidth = DropDownSpan, Margin = new Thickness(0, 0, CardGap, 0) };

    /// <summary>Wires the pick-another card shared by the scale popups: pressing the card applies the pick of its drop-downs, and changing either drop-down
    /// selects the card and applies the new pick at once (directly when the card is already selected).</summary>
    public static void WirePickAnother(TabForge.Avalonia.Platform.Controls.RadioCard card, ComboBox key, ComboBox scale, Action applyPick)
    {
        card.Radio.IsCheckedChanged += (_, _) => { if (card.IsSelected) applyPick(); };
        void Changed() { if (card.IsSelected) applyPick(); else card.Select(); }
        key.SelectionChanged += (_, _) => Changed();
        scale.SelectionChanged += (_, _) => Changed();
    }
}
