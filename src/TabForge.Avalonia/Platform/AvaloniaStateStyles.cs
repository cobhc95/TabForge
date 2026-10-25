using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The state rules that no Core look names: the press offset every button shares, and the hover, pressed, disabled and keyboard-focus surfaces of the number box's spinner
/// buttons (template parts that carry no look class). Colours are theme brushes only (HoverBrush, PressBrush, FocusRingBrush).
/// Owns: the press scale and the spinner button states. Does not own: the look rules (Core looks, AvaloniaLooks) or the brushes (AvaloniaPalette).
/// Tests: TabForge.Avalonia.Tests.ControlStatesTests.
/// </summary>
public static class AvaloniaStateStyles
{
    /// <summary>The scale a pressed button shrinks to, so a click moves the control as well as recolours it.</summary>
    public const double PressedScale = 0.97;

    /// <summary>The opacity of a disabled spinner button, as the Core looks' disabled rules.</summary>
    public const double DisabledOpacity = 0.45;

    private const string BodyPart = "PART_ContentPresenter";
    private static readonly Thickness FocusRingThickness = new(1);

    public static Styles Build() => new()
    {
        // Every enabled button, toggle and repeat button that is held down shrinks a little; the fill change comes from its look.
        new Style(x => Enabled(x.Is<Button>().Class(":pressed")))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, new ScaleTransform(PressedScale, PressedScale)) },
        },
        // The number box's spinner buttons (inside the ButtonSpinner of the NumericUpDown template): flat at rest, lifted on hover, darker when pressed.
        SpinnerButton(button => button, Bound(TemplatedControl.BackgroundProperty, "Panel2Brush")),
        SpinnerButton(button => Enabled(button.Class(":pointerover")).Template().OfType<ContentPresenter>().Name(BodyPart), Bound(ContentPresenter.BackgroundProperty, "HoverBrush")),
        SpinnerButton(button => Enabled(button.Class(":pressed")).Template().OfType<ContentPresenter>().Name(BodyPart), Bound(ContentPresenter.BackgroundProperty, "PressBrush")),
        SpinnerButton(button => button.Class(":focus-visible").Template().OfType<ContentPresenter>().Name(BodyPart),
            Bound(ContentPresenter.BorderBrushProperty, "FocusRingBrush"), new Setter(ContentPresenter.BorderThicknessProperty, FocusRingThickness)),
        SpinnerButton(button => button.Class(":disabled"), new Setter(Visual.OpacityProperty, DisabledOpacity)),
        // Fluent paints a disabled presenter transparent; the dimmed button keeps its resting fill.
        SpinnerButton(button => button.Class(":disabled").Template().OfType<ContentPresenter>().Name(BodyPart), Bound(ContentPresenter.BackgroundProperty, "Panel2Brush")),
        // A disabled slider, drop-down or text box reads dimmed in every window, not only through its own colours.
        Dimmed<Slider>(), Dimmed<ComboBox>(), Dimmed<TextBox>(), Dimmed<RadioButton>(),
    };

    private static Style Dimmed<T>() where T : Control =>
        new(x => x.OfType<T>().Class(":disabled")) { Setters = { new Setter(Visual.OpacityProperty, DisabledOpacity) } };

    // A style on the spinner buttons of every number box.
    private static Style SpinnerButton(Func<Selector, Selector> part, params Setter[] setters)
    {
        var style = new Style(x => part(x.OfType<NumericUpDown>().Template().OfType<ButtonSpinner>().Template().OfType<RepeatButton>()));
        foreach (var setter in setters) style.Setters.Add(setter);
        return style;
    }

    // A pointer-over or pressed state paints only while the control is enabled.
    private static Selector Enabled(Selector selector) => Selectors.Not(selector, x => Selectors.Class(x, ":disabled"));

    private static Setter Bound(AvaloniaProperty property, string brushName) => new(property, new DynamicResourceExtension(brushName));
}
