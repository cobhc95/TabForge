using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the palette-to-resource check and the look-to-style checks. Does not own: the palette and look values, which are read from Core as the expected values.
/// Tests: this class.
/// </summary>
public class ThemeTests
{
    [AvaloniaFact]
    public void EveryPaletteKeyResolvesToCoreValueInBothThemes()
    {
        var app = Application.Current!;
        var keys = Palette.Names.ToList();
        Assert.NotEmpty(keys);
        foreach (var (variant, light) in new[] { (ThemeVariant.Dark, false), (ThemeVariant.Light, true) })
            foreach (var key in keys)
            {
                Assert.True(app.TryGetResource(key, variant, out var resource), $"Palette key {key} has no resource in {variant}.");
                Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(key, light)), Assert.IsType<SolidColorBrush>(resource).Color);
            }
    }

    [AvaloniaFact]
    public void TrackPillPaintsLookTableBackgroundInEachState()
    {
        var app = Application.Current!;
        foreach (var light in new[] { false, true })
        {
            app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            var toggle = new StateToggle();
            toggle.Classes.Add("TrackPill");
            var window = new Window { Content = toggle };
            window.Show();

            toggle.IsChecked = false;
            toggle.SetPseudoClass(":pointerover", true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ExpectedBackground("TrackPill", "HoverUnchecked", light), BackgroundOf(toggle));

            toggle.SetPseudoClass(":pointerover", false);
            toggle.SetPseudoClass(":pressed", true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ExpectedBackground("TrackPill", "Pressed", light), BackgroundOf(toggle));

            toggle.SetPseudoClass(":pressed", false);
            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ExpectedBackground("TrackPill", "Checked", light), BackgroundOf(toggle));
            window.Close();
        }
        app.RequestedThemeVariant = null;
    }

    [AvaloniaFact]
    public void ButtonLooksDimToLookTableOpacityWhenDisabled()
    {
        foreach (var look in new[] { "Button", "AccentButton" })
        {
            var button = new Button();
            button.Classes.Add(look);
            var window = new Window { Content = button };
            window.Show();
            Assert.Equal(1.0, button.Opacity);

            button.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            var expected = double.Parse(LookTable.Rule(look, "Disabled").Set.Single().Literal!, CultureInfo.InvariantCulture);
            Assert.Equal(expected, button.Opacity);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Button", false)]
    [InlineData("PrefsActionButton", false)]
    [InlineData("PrefsPrimaryButton", false)]
    [InlineData("TrackRowIconButton", true)]
    public void TemplatePartsPaintLookTableColoursInEachState(string look, bool light)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var button = new StateButton();
        button.Classes.Add(look);
        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(TextRenderingMode.SubpixelAntialias, RenderOptions.GetTextRenderingMode(window));
        var checkedSetters = 0;
        foreach (var rule in LookTable.Looks[look].Rules)
        {
            var pseudo = rule.When.Select(c => c.Property switch
            {
                "IsMouseOver" => ":pointerover", "IsPressed" => ":pressed", "IsKeyboardFocused" => ":focus", "IsKeyboardFocusVisible" => ":focus-visible", "IsEnabled" => ":disabled", _ => null,
            }).ToList();
            if (pseudo.Contains(null)) continue;
            foreach (var p in pseudo) button.SetPseudoClass(p!, true);
            Dispatcher.UIThread.RunJobs();
            foreach (var setter in rule.Set.Where(s => s.Part is not null && (s.Property == "Border.Background" || s.Property == "Border.BorderBrush") && (s.Brush is null || AvaloniaPalette.BrushPaletteNames.ContainsKey(s.Brush))))
            {
                var border = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == setter.Part);
                var brush = setter.Property == "Border.Background" ? border.Background : border.BorderBrush;
                var expected = setter.Brush is { } name
                    ? AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames[name], light))
                    : Color.Parse(setter.Literal!);
                Assert.True(expected == Assert.IsType<SolidColorBrush>(brush).Color, $"{look}.{rule.Name} {setter.Part} {setter.Property}");
                checkedSetters++;
            }
            foreach (var p in pseudo) button.SetPseudoClass(p!, false);
        }
        Assert.True(checkedSetters >= 3, $"{look}: only {checkedSetters} part colours checked.");
        window.Close();
        app.RequestedThemeVariant = null;
    }

    private const int UnsupportedCeiling = 28;

    [AvaloniaFact]
    public void ScrollBarOrientationAndTextBoxReadOnlyAndCaptionStatesAreMapped()
    {
        var unsupported = AvaloniaLooks.Build().Unsupported;
        Assert.DoesNotContain(unsupported, line => line.StartsWith("ScrollBar.Horizontal", StringComparison.Ordinal));
        Assert.DoesNotContain(unsupported, line => line.StartsWith("PrefsTextBox.ReadOnly", StringComparison.Ordinal));
        Assert.DoesNotContain(unsupported, line => line.StartsWith("ChromeCaptionButton.Hot", StringComparison.Ordinal));
        var bar = new ScrollBar { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
        bar.Classes.Add("ScrollBar");
        var window = new Window { Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12, bar.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void ScrollViewerShowsTwelveDipBarWithThemeThumb()
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 2000, Width = 50 }, Height = 100, Width = 100 };
        var window = new Window { Content = viewer };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = viewer.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == global::Avalonia.Layout.Orientation.Vertical);
        Assert.Equal(AvaloniaScrollBarStyles.ScrollBarThickness, bar.Bounds.Width);
        var face = bar.GetVisualDescendants().OfType<Thumb>().Single().GetVisualDescendants().OfType<Border>().First();
        Assert.True(app().TryGetResource("ScrollThumbBrush", window.ActualThemeVariant, out var brush));
        Assert.Equal(((ISolidColorBrush)brush!).Color, ((ISolidColorBrush)face.Background!).Color);
        window.Close();
        static Application app() => Application.Current!;
    }

    [AvaloniaFact]
    public void ScrollBarTrackValueWritesBackToScrollViewerOffset()
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 2000, Width = 50 }, Height = 100, Width = 100 };
        var window = new Window { Content = viewer };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = viewer.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == global::Avalonia.Layout.Orientation.Vertical);
        var track = bar.GetVisualDescendants().OfType<Track>().Single();
        track.Value = 50; // What a thumb drag or a track click sets.
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, bar.Value);
        Assert.Equal(50, viewer.Offset.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void ComboBoxArrowIsTheFilledTriangle()
    {
        var combo = new ComboBox { ItemsSource = new[] { "a" }, SelectedIndex = 0 };
        var window = new Window { Content = combo };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var glyph = combo.GetVisualDescendants().OfType<PathIcon>().First(g => g.Name == "DropDownGlyph");
        Assert.Equal(8, glyph.Bounds.Width);
        Assert.Equal(4, glyph.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void UnmappedTemplatePartsAreListedAsLeftOut()
    {
        var unsupported = AvaloniaLooks.Build().Unsupported;
        Assert.DoesNotContain(unsupported, line => line.StartsWith("Button.Hover:", System.StringComparison.Ordinal));
        Assert.Contains(unsupported, line => line.StartsWith("ComboToggle.Hover: part arrow Path.Fill", System.StringComparison.Ordinal));
        // The ratchet: the number of left-out look parts only goes down. Lower it when a rule becomes mapped.
        Assert.True(unsupported.Count <= UnsupportedCeiling, $"{unsupported.Count} look parts left out, ceiling {UnsupportedCeiling}.");
    }

    /// <summary>A button whose pseudo classes the test sets the way a pointer, a press or focus would.</summary>
    private sealed class StateButton : Button
    {
        protected override System.Type StyleKeyOverride => typeof(Button);
        public void SetPseudoClass(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on);
    }
    [AvaloniaFact]
    public void FormControlStatesPaintLookTableColours()
    {
        var problems = PartColourProblems(FormStateRows, ApplyState);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [AvaloniaFact]
    public void ListTabMenuThumbStatesPaintLookTableColours()
    {
        var problems = PartColourProblems(ListTabMenuThumbStateRows, (_, control, state) => ((IPseudoClassControl)control).SetPseudoClass(state, true));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // Checks each row's template part in both themes, with the state applied by the given action; returns one line per mismatch.
    private static List<string> PartColourProblems(IEnumerable<(string Look, string Rule, string State, string Part, AvaloniaProperty Property)> rows, Action<Window, Control, string> applyState)
    {
        var app = Application.Current!;
        var problems = new List<string>();
        foreach (var light in new[] { false, true })
        {
            app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            foreach (var (look, rule, state, partName, property) in rows)
            {
                var control = NewFormControl(LookTable.Get(look).Kind);
                control.Classes.Add(look);
                var window = new Window { Width = 320, Height = 200, Content = control };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                applyState(window, control, state);
                Dispatcher.UIThread.RunJobs();

                var named = control.GetVisualDescendants().OfType<Control>().Where(c => !string.IsNullOrEmpty(c.Name)).ToList();
                var part = named.SingleOrDefault(c => c.Name == partName);
                var where = $"{look}.{rule} {property.Name} ({(light ? "light" : "dark")})";
                if (part is null)
                    problems.Add($"{where}: no template part {partName}; named parts are {string.Join(", ", named.Select(c => c.Name + ":" + c.GetType().Name))}");
                else
                {
                    var expected = CoreColour(look, rule, "Border." + property.Name, light);
                    var actual = (part.GetValue(property) as SolidColorBrush)?.Color;
                    if (actual != expected) problems.Add($"{where}: expected {expected}, got {actual}");
                }
                window.Close();
            }
        }
        app.RequestedThemeVariant = null;
        return problems;
    }

    [AvaloniaFact]
    public void ScrollBarAndSliderThumbsHaveVisibleSizeInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = DialogTestKit.UseTheme(light);
            var scroller = new ScrollViewer { Height = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, AllowAutoHide = false, Content = new Border { Height = 600, Width = 100 } };
            var slider = new Slider { Width = 200, Minimum = 0, Maximum = 10, Value = 4 };
            slider.Classes.Add("PrefsSlider");
            var window = new Window { Width = 320, Height = 220, Content = new StackPanel { Children = { scroller, slider } } };
            DialogTestKit.SavePng(window, nameof(ThemeTests), $"thumbs-{(light ? "light" : "dark")}.png");
            foreach (var owner in new Control[] { scroller, slider })
            {
                var thumb = owner.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
                Assert.True(thumb is not null && thumb.Bounds.Width >= 4 && thumb.Bounds.Height >= 4,
                    $"{owner.GetType().Name} thumb ({(light ? "light" : "dark")}) is {thumb?.Bounds.Size.ToString() ?? "missing"}.");
            }
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("PrefsSlider")]
    [InlineData("PrefsMenuItem")]
    public void DisabledSliderAndMenuItemDimToLookTableOpacity(string look)
    {
        var control = NewFormControl(LookTable.Get(look).Kind);
        control.Classes.Add(look);
        var window = new Window { Width = 320, Height = 200, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, control.Opacity);

        control.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        var expected = double.Parse(LookTable.Rule(look, "Disabled").Set.Single().Literal!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, control.Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void ListTabMenuSliderAndThumbKindsAreMappedAndTheirUnmappedPartsListed()
    {
        var unsupported = AvaloniaLooks.Build().Unsupported;
        foreach (var kind in new[] { "ListBoxItem", "ListViewItem", "MenuItem", "TabItem", "ScrollBar", "Thumb", "Slider" })
            Assert.DoesNotContain(unsupported, line => line.Contains($"control kind {kind} is not mapped", StringComparison.Ordinal));
        // Avalonia has no grid view column header in this project (its DataGrid package is not referenced), so its rules stay out.
        Assert.Contains("ListHeader: control kind GridViewColumnHeader is not mapped (all rules left out)", unsupported);
        Assert.Contains("ArrangementSlider.KeyboardFocus: part TrackBar Border.BorderBrush (the Fluent slider has no single track border, left out)", unsupported);
        Assert.Contains("ScrollBarThumbLength.Horizontal: condition Orientation|Ancestor:ScrollBar is not a plain state (rule left out)", unsupported);
    }

    [AvaloniaFact]
    public void FormControlKindsAreMappedAndTheirUnmappedPartsListed()
    {
        var unsupported = AvaloniaLooks.Build().Unsupported;
        foreach (var kind in new[] { "ComboBox", "ComboBoxItem", "CheckBox", "TextBox", "TextBlock" })
            Assert.DoesNotContain(unsupported, line => line.Contains($"control kind {kind} is not mapped", StringComparison.Ordinal));
        Assert.Contains(unsupported, line => line.StartsWith("MenuItem.Checked: part icon ContentPresenter.Content", StringComparison.Ordinal));
        Assert.DoesNotContain(unsupported, line => line.Contains("has no palette key", StringComparison.Ordinal));
    }

    // Each row: the Core look and rule, the state that applies it, the Avalonia template part, and the Avalonia property the Core setter writes.
    private static readonly (string Look, string Rule, string State, string Part, AvaloniaProperty Property)[] FormStateRows =
    {
        ("PrefsComboBox", "Focus", ":focus", "Background", Border.BorderBrushProperty),
        ("PrefsTextBox", "Focus", ":focus", "PART_BorderElement", Border.BorderBrushProperty),
        ("PrefsCheckBox", "Hover", ":pointerover", "box", Border.BorderBrushProperty),
        ("PrefsCheckBox", "Focus", ":focus", "box", Border.BorderBrushProperty),
        ("CheckBox", "Hover", ":pointerover", "box", Border.BorderBrushProperty),
    };

    // The list, tab, menu and thumb rows: the pseudo class is set directly, as a pointer, a selection or a press would set it.
    private static readonly (string Look, string Rule, string State, string Part, AvaloniaProperty Property)[] ListTabMenuThumbStateRows =
    {
        ("ListBoxItem", "Hover", ":pointerover", "bd", Border.BackgroundProperty),
        ("ListBoxItem", "Selected", ":selected", "bd", Border.BackgroundProperty),
        ("ListRow", "Hover", ":pointerover", "Bd", Border.BackgroundProperty),
        ("ListRow", "Selected", ":selected", "Bd", Border.BackgroundProperty),
        ("ScrollBarThumb", "Hover", ":pointerover", "t", Border.BackgroundProperty),
        ("Tab", "Selected", ":selected", "PART_LayoutRoot", Border.BorderBrushProperty),
        ("Tab", "HoverNotSelected", ":pointerover", "PART_LayoutRoot", Border.BackgroundProperty),
        ("MenuItem", "Highlighted", ":pointerover", "PART_LayoutRoot", Border.BackgroundProperty),
        ("ArrangementSliderThumb", "Hover", ":pointerover", "ThumbFace", Border.BackgroundProperty),
        ("ArrangementSliderThumb", "Hover", ":pointerover", "ThumbFace", Border.BorderBrushProperty),
        ("FxSplitterBar", "Hover", ":pointerover", "line", Border.BackgroundProperty),
        ("FxSplitterBar", "Dragging", ":pressed", "line", Border.BackgroundProperty),
    };

    // Puts the control in the state a user would: the check property, a focus call, or a pointer move over its centre.
    private static void ApplyState(Window window, Control control, string state)
    {
        switch (state)
        {
            case ":checked": ((ToggleButton)control).IsChecked = true; break;
            case ":focus": window.Activate(); control.Focus(); break;
            case ":pointerover":
                var centre = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
                window.MouseMove(control.TranslatePoint(centre, window) ?? default);
                break;
            default: throw new ArgumentException($"No test input for state {state}.");
        }
    }

    private static Control NewFormControl(string kind) => kind switch
    {
        "ComboBox" => new ComboBox(),
        "CheckBox" => new CheckBox(),
        "TextBox" => new TextBox(),
        "ListBoxItem" or "ListViewItem" => new StateListBoxItem(),
        "MenuItem" => new StateMenuItem(),
        "TabItem" => new StateTabItem(),
        "Thumb" => new StateThumb(),
        "Slider" => new Slider(),
        _ => throw new ArgumentException($"No test control for kind {kind}."),
    };

    /// <summary>A toggle whose pseudo classes the test sets the way a pointer or a press would.</summary>
    private sealed class StateToggle : ToggleButton
    {
        public void SetPseudoClass(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on);
    }

    /// <summary>A control whose pseudo classes the state rows set the way a pointer, a selection or a press would.</summary>
    private interface IPseudoClassControl
    {
        void SetPseudoClass(string pseudoClass, bool on);
    }

    // Each subclass keeps the Fluent template of its own type (StyleKeyOverride), so the look's border template is the only change.
    private sealed class StateListBoxItem : ListBoxItem, IPseudoClassControl
    {
        protected override Type StyleKeyOverride => typeof(ListBoxItem);
        public void SetPseudoClass(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on);
    }

    private sealed class StateMenuItem : MenuItem, IPseudoClassControl
    {
        protected override Type StyleKeyOverride => typeof(MenuItem);
        public void SetPseudoClass(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on);
    }

    private sealed class StateTabItem : TabItem, IPseudoClassControl
    {
        protected override Type StyleKeyOverride => typeof(TabItem);
        public void SetPseudoClass(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on);
    }

    private sealed class StateThumb : Thumb, IPseudoClassControl
    {
        protected override Type StyleKeyOverride => typeof(Thumb);
        public void SetPseudoClass(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on);
    }

    private static Color ExpectedBackground(string look, string rule, bool light) => CoreColour(look, rule, "Background", light);

    // The colour a Core setter writes: the theme brush painted for the theme, or the literal colour.
    private static Color CoreColour(string look, string rule, string property, bool light)
    {
        var setter = LookTable.Rule(look, rule).Set.Single(s => s.Property == property);
        return setter.Brush is not null
            ? AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames[setter.Brush], light))
            : Color.Parse(setter.Literal!);
    }

    private static Color BackgroundOf(Control control) => Assert.IsType<SolidColorBrush>(((TemplatedControl)control).Background).Color;
}
