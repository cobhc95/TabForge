using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll's hover / pressed / selected / disabled / focus states: every control family resolves its states to theme tokens
//   (toolbar buttons and toggles, toolbar combos, the info line fields, the lane header, the menus, the scroll bars) in the light and the dark theme.
// Does not own: the edits and gestures (PianoRollViewTests), the colour literals guard (ThemeArchitectureTests).
// Tests: PianoRollToolLook, PianoRollView, PianoRollSurface, ContextMenuBuilder.
public sealed class PianoRollHoverTests
{
    private static (PianoRollView View, Window Window) Show(SongDocument document, bool light)
    {
        // A shared snap keeps the snap-bound controls (magnet, grid) enabled, as in the app.
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Snap = () => new SnapSettings() });
        var window = new Window { Width = 900, Height = 600, Content = view, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window);
    }

    private static ToggleButton ToolButton(Window window, string tipStart) =>
        window.GetVisualDescendants().OfType<ToggleButton>().Single(b => ToolTip.GetTip(b) is string tip && tip.StartsWith(tipStart, StringComparison.Ordinal));

    private static Button ToolbarButton(Window window, string tip) =>
        window.GetVisualDescendants().OfType<Button>().First(b => (ToolTip.GetTip(b) as string)?.StartsWith(tip, StringComparison.Ordinal) == true);

    private static ComboBox ToolbarCombo(Window window, string tipStart) =>
        window.GetVisualDescendants().OfType<ComboBox>().First(c => (ToolTip.GetTip(c) as string)?.StartsWith(tipStart, StringComparison.Ordinal) == true);

    private static void Hover(Control control, bool over)
    {
        var type = over ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent;
        control.RaiseEvent(new PointerEventArgs(type, control, new Pointer(0, PointerType.Mouse, true), (Visual)control, new Point(), 0, new PointerPointProperties(), KeyModifiers.None));
    }

    private static Color BrushColour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Token(string brushName, bool light) =>
        AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames[brushName], light));

    [Fact]
    public void ButtonAndComboStateNamesUseThemeTokensOnly()
    {
        // Rest is transparent, hover and pressed take the shared fills, a lit toggle takes the Tools pane's selected fill and edge,
        // and an open popup takes the accent edge: every name must be a known theme brush (or transparent).
        var rest = PianoRollToolLook.ButtonLook(active: false, hovered: false, pressed: false, live: true, open: false);
        Assert.Null(rest.Bg); Assert.Null(rest.Edge); Assert.Equal("TextBrush", rest.Glyph);
        var hover = PianoRollToolLook.ButtonLook(active: false, hovered: true, pressed: false, live: true, open: false);
        Assert.Equal("HoverBrush", hover.Bg); Assert.Null(hover.Edge); Assert.Equal("TextBrush", hover.Glyph);
        var pressed = PianoRollToolLook.ButtonLook(active: false, hovered: true, pressed: true, live: true, open: false);
        Assert.Equal("PressBrush", pressed.Bg); Assert.Null(pressed.Edge); Assert.Equal("TextBrush", pressed.Glyph);
        var lit = PianoRollToolLook.ButtonLook(active: true, hovered: false, pressed: false, live: true, open: false);
        Assert.Equal("SelectedTintBrush", lit.Bg); Assert.Equal("SelectedOutlineBrush", lit.Edge); Assert.Equal("AccentBrush", lit.Glyph);
        var disabled = PianoRollToolLook.ButtonLook(active: false, hovered: true, pressed: true, live: false, open: false);
        Assert.Null(disabled.Bg); Assert.Null(disabled.Edge); Assert.Equal("TextBrush", disabled.Glyph);
        var open = PianoRollToolLook.ButtonLook(active: false, hovered: false, pressed: false, live: true, open: true);
        Assert.Null(open.Bg); Assert.Equal("AccentBrush", open.Edge); Assert.Equal("TextBrush", open.Glyph);
        var comboRest = PianoRollToolLook.ComboLook(hovered: false, live: true, attentive: false);
        Assert.Null(comboRest.Bg); Assert.Null(comboRest.Edge);
        var comboHot = PianoRollToolLook.ComboLook(hovered: true, live: true, attentive: true);
        Assert.Equal("HoverBrush", comboHot.Bg); Assert.Equal("AccentBrush", comboHot.Edge);
        var comboDisabled = PianoRollToolLook.ComboLook(hovered: true, live: false, attentive: true);
        Assert.Null(comboDisabled.Bg); Assert.Null(comboDisabled.Edge);
        foreach (var name in new[] { "SelectedTintBrush", "SelectedOutlineBrush", "HoverBrush", "PressBrush", "AccentBrush", "TextBrush" })
            Assert.True(AvaloniaPalette.HasBrush(name), name + " is a theme brush");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToolbarButtonsRestHoverSelectedAndDisabledResolveToTokens(bool light)
    {
        var (view, window) = Show(PianoRollSongs.Midi(), light);
        var zoomIn = ToolbarButton(window, "Zoom in");
        var scale = ToolButton(window, "Step input");

        Assert.Equal(0, BrushColour(zoomIn.Background).A);   // rest: transparent
        Assert.Null(zoomIn.Cursor);   // hover feedback is shading only (resize cursors stay on grips)
        Hover(zoomIn, over: true);
        Assert.Equal(Token("HoverBrush", light), BrushColour(zoomIn.Background));
        Hover(zoomIn, over: false);
        Assert.Equal(0, BrushColour(zoomIn.Background).A);

        scale.IsChecked = false;   // a lit toggle takes the selected fill and edge, the rest stays plain
        Assert.Equal(0, BrushColour(scale.Background).A);
        scale.IsChecked = true;
        Assert.Equal(Token("SelectedTintBrush", light), BrushColour(scale.Background));
        Assert.Equal(Token("SelectedOutlineBrush", light), BrushColour(scale.BorderBrush));
        var glyph = Assert.IsType<Path>(Assert.IsType<Viewbox>(scale.Content).Child);
        Assert.Equal(Token("AccentBrush", light), BrushColour(glyph.Stroke));

        zoomIn.IsEnabled = false;   // disabled: no fill or edge, dimmed as the menu row toggles
        Assert.Equal(0, BrushColour(zoomIn.Background).A);
        Assert.Equal(PianoRollToolLook.DisabledOpacity, zoomIn.Opacity);
        zoomIn.IsEnabled = true;
        Assert.Equal(1, zoomIn.Opacity);

        Assert.All(window.GetVisualDescendants().OfType<ToggleButton>().Where(b => b is not CheckBox && b.Name != "PianoRollKeyPiano" && b.Name != "PianoRollKeyNames"), b => Assert.False(b.Focusable));   // toolbar buttons take no focus, so no focus ring (the Piano / Names corner pair stays keyboard-reachable)
        Assert.All(window.GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button) && b.Name != "PianoRollLaneButton"), b => Assert.False(b.Focusable));   // the info line checks keep theirs; the lane button stays keyboard-reachable
        view.Presenter.SelectAll();
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToolbarCombosRestHoverOpenAndDisabledResolveToTokens(bool light)
    {
        var (view, window) = Show(PianoRollSongs.Midi(), light);
        var grid = ToolbarCombo(window, "Note value of the snap grid");

        Assert.Equal(0, BrushColour(grid.Background).A);   // rest: transparent, no edge
        Assert.Equal(0, BrushColour(grid.BorderBrush).A);
        Hover(grid, over: true);
        Assert.Equal(Token("HoverBrush", light), BrushColour(grid.Background));
        Hover(grid, over: false);

        grid.Focusable = true;   // keyboard focus takes the same attentive edge as an open dropdown
        Assert.True(grid.Focus());
        Assert.Equal(Token("AccentBrush", light), BrushColour(grid.BorderBrush));
        view.Surface.Focus();   // focus leaves: the edge goes with it
        Assert.Equal(0, BrushColour(grid.BorderBrush).A);
        grid.Focusable = false;

        grid.IsEnabled = false;
        Assert.Equal(0, BrushColour(grid.Background).A);
        Assert.Equal(PianoRollToolLook.DisabledOpacity, grid.Opacity);
        grid.IsEnabled = true;

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void InfoLineFieldsEnableWithASelectionAndDisableWithout(bool light)
    {
        var (view, window) = Show(PianoRollSongs.Midi(), light);
        var boxes = window.GetVisualDescendants().OfType<TextBox>().Where(b => (b.Name ?? "").StartsWith("PianoRoll", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(boxes);

        view.Presenter.SelectAll();   // a selection: every value box takes a value and enables
        view.RefreshInfo();
        Assert.All(boxes, box => Assert.True(box.IsEnabled));
        Assert.All(boxes, box => Assert.Equal(Token("WindowBrush", light), BrushColour(box.Background)));   // the shared field fill, never a local colour

        view.Presenter.Select(Array.Empty<int>());   // no selection: every value box disables (the template's disabled shade)
        view.RefreshInfo();
        Assert.All(boxes, box => Assert.False(box.IsEnabled));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void LaneHeaderHoverAndMenusResolveToTokens(bool light)
    {
        var (view, window) = Show(PianoRollSongs.Midi(), light);
        var surface = view.Surface;

        surface.HoverAt(new Point(surface.GridLeft + 4, surface.GridBottom + 1));   // over the lane header: hovered
        Assert.True(surface.LaneHeaderHovered);
        surface.HoverAt(new Point(surface.GridLeft + 50, surface.GridTop + 50));   // back over the grid: not hovered
        Assert.False(surface.LaneHeaderHovered);
        Assert.True(AvaloniaPalette.HasBrush("HoverBrush"));   // the header's hover fill is a theme brush, as its rest fill is
        Assert.True(AvaloniaPalette.HasBrush("Panel2Brush"));

        var rows = view.MenuRows().ToList();   // the roll's menus reuse the app's menu style: hover and pressed come from it
        Assert.NotEmpty(rows);
        Assert.Equal("HoverBrush", AvaloniaPalette.FluentMenuBrushNames["MenuFlyoutItemBackgroundPointerOver"]);
        Assert.Equal("PressBrush", AvaloniaPalette.FluentMenuBrushNames["MenuFlyoutItemBackgroundPressed"]);
        var menu = ContextMenuBuilder.Build(rows, view);
        Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void ScrollbarsAndSurfaceKeepNoLocalFillOrCursor()
    {
        var (_, window) = Show(PianoRollSongs.Midi(), light: false);
        var bars = window.GetVisualDescendants().OfType<PianoRollView>().Single().Children.OfType<ScrollBar>().ToList();
        Assert.Equal(2, bars.Count);   // the roll's own horizontal and vertical bars (combo dropdowns keep their own)
        Assert.All(bars, bar => Assert.Equal(Colors.Transparent, BrushColour(bar.Background)));   // no fill of their own: the template paints the thumb shades
        Assert.All(bars, bar => Assert.Null(bar.Cursor));
        window.Close();
    }
}
