using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the hover and press surface contrast the owner needs to see a control hot in both themes: the minimum against the window and the Panel2
/// cards, the primary text on the hover shade, the pressed shade as one further step, and the real FX chain bar controls painting it.
/// Computed from the theme tables (AvaloniaPalette), never hexes.
/// Does not own: the Core theme values, which are shared with the app and stay as they are.
/// Tests: this class.
/// </summary>
public class HoverSurfaceContrastTests
{
    [Fact]
    public void PressedRowTextClearsAaAndDisabledInkReadsInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            var press = AvaloniaPalette.Get("PressSurface", light);
            var black = new TabForge.Platform.UiColor(0xFF, 0, 0, 0);
            if (light) Assert.True(TabForge.Platform.Theme.ThemeColours.Contrast(black, press) >= 4.5, "pressed row text");
            if (!light) Assert.True(TabForge.Platform.Theme.ThemeColours.Contrast(AvaloniaPalette.Get("TextMuted", light), AvaloniaPalette.Get("HoverSurface", light)) >= 4.5, "muted on hover");
            Assert.True(TabForge.Platform.Theme.ThemeColours.Contrast(AvaloniaPalette.Get("AccentPress", light), TabForge.Platform.Theme.ThemeColours.InkOn(AvaloniaPalette.Get("AccentPress", light))) >= 4.5, "accent pressed label");
        }
        Assert.Equal("DisabledInkBrush", AvaloniaPalette.FluentFieldBrushNames["ButtonForegroundDisabled"]);
    }

    [Fact]
    public void HoverAndPressedSurfacesMeasureUpInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            var theme = light ? "light" : "dark";
            var hover = AvaloniaPalette.Get("HoverSurface", light);
            var press = AvaloniaPalette.Get("PressSurface", light);

            AssertClears(theme, "window", hover, AvaloniaPalette.Get("WindowBackground", light), HoverSurfaces.MinimumHoverContrast);
            AssertClears(theme, "panel", hover, AvaloniaPalette.Get("PanelBackground", light), HoverSurfaces.MinimumHoverContrast);
            AssertClears(theme, "Panel2 card", hover, AvaloniaPalette.ThemeBrush("Panel2Brush", light), HoverSurfaces.MinimumHoverContrast);
            AssertClears(theme, "primary text", AvaloniaPalette.Get("TextPrimary", light), hover, HoverSurfaces.MinimumHoverTextContrast);

            Assert.NotEqual(hover, press);
            AssertClears(theme, "pressed vs hover", press, hover, HoverSurfaces.MinimumPressedContrast);

            static void AssertClears(string theme, string what, TabForge.Platform.UiColor a, TabForge.Platform.UiColor b, double minimum)
            {
                var ratio = ThemeColours.Contrast(a, b);
                Assert.True(ratio >= minimum, $"{what} ({theme}): {ratio:0.000} < {minimum:0.0}.");
            }
        }
    }

    [Fact]
    public void FxBarButtonsAndPresetComboPaintTheStrongerHoverSurface()
    {
        // The buttons of the FX bar are plain buttons, which take the Core "Button" look: its hover fill is the HoverBrush theme brush.
        var hoverSetter = LookTable.Rule("Button", "Hover").Set.Single(setter => setter.Part == "b" && setter.Property == "Border.Background");
        Assert.Equal("HoverBrush", hoverSetter.Brush);
        Assert.Equal(HoverShade.PaletteKey, AvaloniaPalette.BrushPaletteNames["HoverBrush"]);
        // The preset combo is a plain ComboBox: its pointer-over fill is the same brush, and its press the press shade, so it shows hot too.
        Assert.Equal("HoverBrush", AvaloniaPalette.FluentFieldBrushNames["ComboBoxBackgroundPointerOver"]);
        Assert.Equal("PressBrush", AvaloniaPalette.FluentFieldBrushNames["ComboBoxBackgroundPressed"]);
        foreach (var light in new[] { false, true })
            Assert.NotEqual(AvaloniaPalette.Get("WindowBackground", light), AvaloniaPalette.Get("HoverSurface", light));
    }

    /// <summary>
    /// The owner's own case: the real FX chain window bar, not a synthetic button. Every filled control must paint the HoverSurface on pointer-over
    /// (at least <see cref="HoverSurfaces.MinimumHoverContrast"/> from the window behind it), and the drawn volume knob must show its hover cue.
    /// This fails if the default look classes stop reaching the bar controls, which is the way the hover went flat before.
    /// </summary>
    [AvaloniaFact]
    public void TheRealFxBarControlsShowHoverInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = DialogTestKit.UseTheme(light);
            var themeName = light ? "light" : "dark";
            var window = FxTestKit.Window();
            window.Width = 1200;
            window.Height = 900;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var hover = AvaloniaPalette.Get("HoverSurface", light);
            var windowColour = AvaloniaPalette.Get("WindowBackground", light);

            foreach (var name in new[] { "AddPresetButton", "WiringButton", "MidiButton" })
            {
                var button = DialogTestKit.Named<Button>(window, name);
                Assert.Contains("Button", button.Classes);   // the Core "Button" look, not the bare Fluent template
                MoveOver(window, button);
                AssertHoverFill(themeName, name, NamedBorderFill(button, "b"), hover, windowColour);
            }

            var combo = DialogTestKit.Named<ComboBox>(window, "PresetBox");
            MoveOver(window, combo);
            AssertHoverFill(themeName, "PresetBox", NamedBorderFill(combo, "Background"), hover, windowColour);

            // The volume knob is drawn, not filled: its hover cue is the accent glow around the ring, so its frame must change while it is hot.
            var knob = DialogTestKit.Named<KnobControl>(window, "VolumeSlider");
            window.MouseMove(new Point(2, 2));
            Dispatcher.UIThread.RunJobs();
            var rest = Frame(window);
            MoveOver(window, knob);
            var hot = Frame(window);
            Assert.False(rest.SequenceEqual(hot), $"the volume knob draws no hover cue ({themeName}).");
            window.Close();
        }
    }

    private static void AssertHoverFill(string theme, string what, TabForge.Platform.UiColor fill, TabForge.Platform.UiColor hover, TabForge.Platform.UiColor windowColour)
    {
        Assert.True(fill == hover, $"{what} ({theme}) hovers to #{fill.R:X2}{fill.G:X2}{fill.B:X2}, not the HoverSurface.");
        var ratio = ThemeColours.Contrast(fill, windowColour);
        Assert.True(ratio >= HoverSurfaces.MinimumHoverContrast, $"{what} ({theme}) hover fill is only {ratio:0.000}:1 from the window.");
    }

    // The fill of the look template part the named control paints its hover state on.
    private static TabForge.Platform.UiColor NamedBorderFill(Control root, string partName) =>
        Colour(root.GetVisualDescendants().OfType<Border>().Single(border => border.Name == partName).Background);

    private static TabForge.Platform.UiColor Colour(IBrush? brush)
    {
        var solid = Assert.IsType<SolidColorBrush>(brush);
        return new TabForge.Platform.UiColor(solid.Color.A, solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void MoveOver(Window window, Control control)
    {
        var centre = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        window.MouseMove(control.TranslatePoint(centre, window) ?? throw new InvalidOperationException("The control is not in the window."));
        Dispatcher.UIThread.RunJobs();
    }

    private static byte[] Frame(Window window)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
        using var bytes = new MemoryStream();
        frame.Save(bytes);
        return bytes.ToArray();
    }
}
