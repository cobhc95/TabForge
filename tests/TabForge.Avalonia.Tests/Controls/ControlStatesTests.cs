using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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
/// Every interactive control kind gives visual feedback in every state, in both themes. The look-table half: each listed look names a hover, a pressed, a keyboard-focus
/// and a disabled rule. The template-part half: the number box's spinner buttons (no look class) change their fill from rest to hover to pressed, show the focus ring,
/// and stay inert when disabled; a pressed enabled button shrinks.
/// Owns: the list of interactive looks and the spinner/press checks. Does not own: the look values (LookTable) or the styles (AvaloniaLooks, AvaloniaStateStyles).
/// Tests: this class.
/// </summary>
public class ControlStatesTests
{
    // The interactive looks that take the full state set. Not listed, with the reason: the caption buttons (the window shell sets their state; never focused),
    // the Preferences looks (they keep their own :focus rules and are checked by PreferencesControlHoverTests), and the tab-bar and splitter looks (custom drawn or border kinds).
    private static readonly string[] InteractiveLooks =
    {
        "Button", "AccentButton", "TransportButton", "StatusLinkButton", "TrackRowIconButton", "MixerToggle", "TrackPill", "ComboToggle",
        "Tab", "ComboBoxItem", "CheckBox", "ListBoxItem", "ListRow",
    };

    private static readonly string[] Hover = { "IsMouseOver", "IsHighlighted" };
    private static readonly string[] Pressed = { "IsPressed", "IsDragging" };
    private static readonly string[] Focus = { "IsKeyboardFocusVisible", "IsKeyboardFocused" };
    private static readonly string[] Disabled = { "IsEnabled" };

    [Fact]
    public void EveryInteractiveLookNamesHoverPressedFocusAndDisabled()
    {
        var missing = new List<string>();
        foreach (var name in InteractiveLooks)
        {
            var conditions = LookTable.Looks[name].Rules.SelectMany(rule => rule.When).Select(c => c.Property).ToHashSet();
            foreach (var (state, properties) in new[] { ("hover", Hover), ("pressed", Pressed), ("focus", Focus), ("disabled", Disabled) })
                if (!properties.Any(conditions.Contains)) missing.Add($"{name}: no {state} rule");
        }
        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [AvaloniaFact]
    public void SpinnerButtonsChangeFromRestToHoverToPressedInBothThemes()
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var (window, button, body) = Spinner(variant);
            var rest = FillOf(body);
            SetState(button, ":pointerover", true);
            var hover = FillOf(body);
            SetState(button, ":pressed", true);
            var pressed = FillOf(body);
            Assert.NotEqual(rest, hover);
            Assert.NotEqual(hover, pressed);
            Assert.NotEqual(rest, pressed);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SpinnerButtonShowsTheFocusRingOnlyForKeyboardFocus()
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var (window, button, body) = Spinner(variant);
            var restEdge = (body.BorderBrush as ISolidColorBrush)?.Color;
            SetState(button, ":focus-visible", true);
            var ring = Assert.IsAssignableFrom<ISolidColorBrush>(body.BorderBrush).Color;
            Assert.True(Application.Current!.TryGetResource("FocusRingBrush", variant, out var token));
            Assert.Equal(((ISolidColorBrush)token!).Color, ring);
            Assert.NotEqual(restEdge, ring);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DisabledSpinnerButtonIsDimmedAndIgnoresHoverAndPress()
    {
        var (window, button, body) = Spinner(ThemeVariant.Dark);
        var rest = FillOf(body);
        button.IsEnabled = false;
        SetState(button, ":pointerover", true);
        SetState(button, ":pressed", true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AvaloniaStateStyles.DisabledOpacity, button.Opacity);
        Assert.Equal(rest, FillOf(body));
        window.Close();
    }

    // Fluent animates the button transform, which a headless clock does not advance, so the rule itself is checked: it scales a pressed, enabled button.
    [AvaloniaFact]
    public void PressedEnabledButtonsShrink()
    {
        var rule = AvaloniaStateStyles.Build().OfType<Style>().First(style => style.Setters.OfType<Setter>().Any(setter => setter.Property == Visual.RenderTransformProperty));
        var scale = Assert.IsType<ScaleTransform>(rule.Setters.OfType<Setter>().Single().Value);
        Assert.Equal(AvaloniaStateStyles.PressedScale, scale.ScaleX);
        Assert.Contains(":pressed", rule.Selector!.ToString());
        Assert.Contains(":not(:disabled)", rule.Selector.ToString());
    }

    // A number box in a window of the given theme, its first spinner button and that button's body presenter.
    private static (Window Window, RepeatButton Button, ContentPresenter Body) Spinner(ThemeVariant variant)
    {
        var box = new NumericUpDown { Value = 1, Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
        var window = new Window { Width = 200, Height = 80, Content = box, RequestedThemeVariant = variant };
        window.Show();
        window.UpdateLayout();
        var button = box.GetVisualDescendants().OfType<RepeatButton>().First(b => b.Name == "PART_IncreaseButton");
        var body = button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        return (window, button, body);
    }

    private static void SetState(Control control, string pseudoClass, bool on)
    {
        ((IPseudoClasses)control.Classes).Set(pseudoClass, on);
        Dispatcher.UIThread.RunJobs();
    }

    private static Color? FillOf(ContentPresenter body) => (body.Background as ISolidColorBrush)?.Color;
}
