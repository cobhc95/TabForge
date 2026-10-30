using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views;

// Owns: the look of every piano roll toolbar button (tools, commands, toggles, dropdowns): a flat icon button, transparent at rest, the hover
//   fill under the pointer, the pressed fill while held, and a checked toggle in the Tools pane's selected look (the same SelectedTint fill and
//   SelectedOutline edge, reused by key). A dropdown keeps the same height, corner, transparent rest and hover fill; its edge turns to the accent
//   while it is open or focused. A popup's button (the quantize options) keeps the accent edge while its popup is open. A disabled button or
//   dropdown is dimmed (the menu row's 0.45) with no fill or edge. The edge is 1 DIP in every state, so a lit button never changes the toolbar's size.
// Does not own: which tool is active (PianoRollView.RefreshTools sets IsChecked), the click (PianoRollView.BuildToolbar), the icon (MenuRowToggles.Icon).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests (the active tool's fill and edge), PianoRollHoverTests (every state in both themes).
internal static class PianoRollToolLook
{
    /// <summary>The one border template part, the outline the look paints (the fill and the edge both sit on it).</summary>
    private const string OutlinePartName = "Bd";

    /// <summary>The theme brush names: the Tools pane's active fill and edge, the hover fill and the pressed fill.</summary>
    private const string SelectedFillBrush = "SelectedTintBrush", SelectedEdgeBrush = "SelectedOutlineBrush", HoverBrush = "HoverBrush", PressBrush = "PressBrush",
        AccentBrush = "AccentBrush", TextBrush = "TextBrush", FieldFillBrush = "WindowBrush", FieldEdgeBrush = "BorderBrush";

    /// <summary>A disabled button or dropdown's opacity (the menu row toggles' dim), so the disabled shade follows the rest of the app.</summary>
    internal const double DisabledOpacity = 0.45;

    private const double EdgeWidth = 1;

    /// <summary>A button's brushes by state, as theme brush names (null: transparent): the lit toggle takes the selected fill and edge, else the
    /// pressed fill while held, the hover fill under the pointer, and the accent edge while <paramref name="open"/> (its popup is open).
    /// <paramref name="field"/> gives a dropdown-style button (the field fill and the general border at rest, the accent edge while open or
    /// <paramref name="focused"/>), as the app's combo boxes look.</summary>
    internal static (string? Bg, string? Edge, string Glyph) ButtonLook(bool active, bool hovered, bool pressed, bool live, bool open, bool field = false, bool focused = false)
    {
        if (active) return (SelectedFillBrush, SelectedEdgeBrush, AccentBrush);
        if (!live) return (null, null, TextBrush);
        if (field)
            return (pressed ? PressBrush : hovered ? HoverBrush : FieldFillBrush, open || focused ? AccentBrush : FieldEdgeBrush, TextBrush);
        var edge = open ? AccentBrush : null;
        if (pressed) return (PressBrush, edge, TextBrush);
        if (hovered) return (HoverBrush, edge, TextBrush);
        return (null, edge, TextBrush);
    }

    /// <summary>A dropdown's brushes by state, as theme brush names (null: transparent): the hover fill under the pointer, the accent edge while
    /// <paramref name="attentive"/> (open or keyboard-focused).</summary>
    internal static (string? Bg, string? Edge) ComboLook(bool hovered, bool live, bool attentive) =>
        !live ? (null, null) : (hovered ? HoverBrush : null, attentive ? AccentBrush : null);

    /// <summary>Gives a toolbar button its look and keeps it painted as its checked, hovered, pressed, opened, enabled state (and the theme) change.</summary>
    public static void Apply(Button button, Popup? openPopup = null)
    {
        var repaint = Apply(button, () => openPopup?.IsOpen == true);
        if (openPopup is not null) { openPopup.Opened += (_, _) => repaint(); openPopup.Closed += (_, _) => repaint(); }
    }

    /// <summary>The same look for a button whose "open" state is any menu or list (<paramref name="isOpen"/>); <paramref name="isLit"/> (optional) paints it
    /// as a checked toggle while true (the lane button while a controller lane is chosen). <paramref name="field"/> gives it the dropdown look at rest
    /// (a filled, bordered field) with the accent edge while focused. Returns the repaint to call when those states change.</summary>
    public static Action Apply(Button button, Func<bool> isOpen, Func<bool>? isLit = null, bool field = false)
    {
        // A one-border template, as the menu row toggles use: the template's own presenter would otherwise keep the app's checked fill.
        button.Template = Platform.AvaloniaButtonTemplates.SingleBorder(OutlinePartName);
        button.CornerRadius = Platform.AvaloniaFormControlStyles.ControlCornerRadius;
        button.BorderThickness = UiMetrics.Sides(EdgeWidth, EdgeWidth, EdgeWidth, EdgeWidth, 1);
        var toggle = button as ToggleButton;
        var hovered = false;
        var pressed = false;
        var focused = false;   // keyboard-focused (the field look only)
        // The icon is the Viewbox's stroked path (MenuRowToggles.Icon): a checked button's icon takes the master accent, the rest the text colour.
        void Paint()
        {
            var light = button.ActualThemeVariant == ThemeVariant.Light;
            var (bg, edge, glyph) = ButtonLook(toggle?.IsChecked == true || isLit?.Invoke() == true, hovered, pressed, button.IsEnabled, isOpen(), field, focused);
            button.Background = bg is null ? AvaloniaPalette.TransparentBrush : Brush(bg, light);
            button.BorderBrush = edge is null ? AvaloniaPalette.TransparentBrush : Brush(edge, light);
            button.Opacity = button.IsEnabled ? 1 : DisabledOpacity;
            if ((button.Content as Viewbox)?.Child is Path path) path.Stroke = Brush(glyph, light);
        }
        button.PointerEntered += (_, _) => { hovered = true; Paint(); };
        button.PointerExited += (_, _) => { hovered = false; pressed = false; Paint(); };
        button.PointerPressed += (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { pressed = true; Paint(); } };
        button.PointerReleased += (_, _) => { pressed = false; Paint(); };
        button.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsEnabledProperty) Paint(); };
        if (field)
        {
            button.GotFocus += (_, _) => { focused = true; Paint(); };
            button.LostFocus += (_, _) => { focused = false; Paint(); };
        }
        if (toggle is not null) toggle.IsCheckedChanged += (_, _) => Paint();
        button.FollowTheme(Paint);
        Paint();
        return Paint;
    }

    private static IBrush Brush(string brushName, bool light) =>
        new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames[brushName], light)));

    /// <summary>Gives a toolbar dropdown its look and keeps it painted as its hovered, opened, focused and enabled state (and the theme) change.</summary>
    public static void Apply(ComboBox combo)
    {
        combo.CornerRadius = Platform.AvaloniaFormControlStyles.ControlCornerRadius;
        combo.BorderThickness = UiMetrics.Sides(EdgeWidth, EdgeWidth, EdgeWidth, EdgeWidth, 1);
        var hovered = false;
        var focused = false;   // keyboard-focused
        var open = false;   // dropdown open (kept apart from focus, so the edge stays while open even as focus moves into the list)
        void Paint()
        {
            var light = combo.ActualThemeVariant == ThemeVariant.Light;
            var (bg, edge) = ComboLook(hovered, combo.IsEnabled, focused || open);
            combo.Background = bg is null ? AvaloniaPalette.TransparentBrush : Brush(bg, light);
            combo.BorderBrush = edge is null ? AvaloniaPalette.TransparentBrush : Brush(edge, light);
            combo.Opacity = combo.IsEnabled ? 1 : DisabledOpacity;
        }
        combo.PointerEntered += (_, _) => { hovered = true; Paint(); };
        combo.PointerExited += (_, _) => { hovered = false; Paint(); };
        combo.GotFocus += (_, _) => { focused = true; Paint(); };
        combo.LostFocus += (_, _) => { focused = false; Paint(); };
        combo.DropDownOpened += (_, _) => { open = true; Paint(); };
        combo.DropDownClosed += (_, _) => { open = false; Paint(); };
        combo.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsEnabledProperty) Paint(); };
        combo.FollowTheme(Paint);
        Paint();
    }
}
