using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views;

// Owns: the look of the piano roll's tool buttons: a flat icon button, transparent at rest, the hover fill under the pointer, the pressed fill
//   while held, and the active tool in the Tools pane's selected look (the same SelectedTint fill and SelectedOutline edge, reused by key).
//   The edge is 1 DIP in every state, so the active tool never changes the toolbar's size.
// Does not own: which tool is active (PianoRollView.RefreshTools sets IsChecked), the click (PianoRollView.BuildToolbar), the icon (MenuRowToggles.Icon).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests (the active tool's fill and edge).
internal static class PianoRollToolLook
{
    /// <summary>The one border template part, the outline the look paints (the fill and the edge both sit on it).</summary>
    private const string OutlinePartName = "Bd";

    /// <summary>The theme brush names: the Tools pane's active fill and edge, the hover fill and the pressed fill.</summary>
    private const string SelectedFillBrush = "SelectedTintBrush", SelectedEdgeBrush = "SelectedOutlineBrush", HoverBrush = "HoverBrush", PressBrush = "PressBrush",
        AccentBrush = "AccentBrush", TextBrush = "TextBrush";

    private const double EdgeWidth = 1;

    /// <summary>Gives a tool button its look and keeps it painted as its checked, hovered, pressed and enabled state (and the theme) change.</summary>
    public static void Apply(ToggleButton button)
    {
        // A one-border template, as the menu row toggles use: the template's own presenter would otherwise keep the app's checked fill.
        button.Template = Platform.AvaloniaButtonTemplates.SingleBorder(OutlinePartName);
        button.CornerRadius = Platform.AvaloniaFormControlStyles.ControlCornerRadius;
        button.BorderThickness = UiMetrics.Sides(EdgeWidth, EdgeWidth, EdgeWidth, EdgeWidth, 1);
        var hovered = false;
        var pressed = false;
        // The icon is the Viewbox's stroked path (MenuRowToggles.Icon): the active tool's icon takes the master accent, the rest the text colour.
        var glyph = (button.Content as Viewbox)?.Child as Path;
        void Paint()
        {
            var light = button.ActualThemeVariant == ThemeVariant.Light;
            var active = button.IsChecked == true;
            var live = button.IsEnabled;
            button.Background = active ? Brush(SelectedFillBrush, light)
                : pressed && live ? Brush(PressBrush, light)
                : hovered && live ? Brush(HoverBrush, light)
                : AvaloniaPalette.TransparentBrush;
            button.BorderBrush = active ? Brush(SelectedEdgeBrush, light) : AvaloniaPalette.TransparentBrush;
            if (glyph is not null) glyph.Stroke = Brush(active ? AccentBrush : TextBrush, light);
        }
        button.PointerEntered += (_, _) => { hovered = true; Paint(); };
        button.PointerExited += (_, _) => { hovered = false; pressed = false; Paint(); };
        button.PointerPressed += (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { pressed = true; Paint(); } };
        button.PointerReleased += (_, _) => { pressed = false; Paint(); };
        button.IsCheckedChanged += (_, _) => Paint();
        button.FollowTheme(Paint);
        Paint();
    }

    private static IBrush Brush(string brushName, bool light) =>
        new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames[brushName], light)));
}
