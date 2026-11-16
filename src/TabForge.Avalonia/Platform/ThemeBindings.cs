// Owns: the one way code binds a control property to a theme key, plus the bindings nearly every window and label repeats
//   (a window's own background and text colour, a muted secondary line).
// Does not own: the keys or their colours (AvaloniaPalette, Palette.cs) or the looks of whole control families (the *Styles owners).
// Tests: covered by the theme and window tests (ThemeArchitectureTests, StyleKeyGuardTests and every dialog test that switches theme).
// Need a themed colour on a control? Use Themed(property, key) here instead of writing `x[!P] = new DynamicResourceExtension("Key")`.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace TabForge.Avalonia.Platform;

/// <summary>Binds control properties to theme resource keys, so they follow a theme switch.</summary>
public static class ThemeBindings
{
    /// <summary>Binds <paramref name="property"/> to the theme resource <paramref name="key"/> and returns the control.</summary>
    public static T Themed<T>(this T control, AvaloniaProperty property, string key) where T : AvaloniaObject
    {
        control[!property] = new DynamicResourceExtension(key);
        return control;
    }

    /// <summary>The muted secondary text colour (<c>TextMuted</c>) for a hint, caption or note.</summary>
    public static TextBlock UseMutedText(this TextBlock block) => block.Themed(TextBlock.ForegroundProperty, "TextMuted");

    /// <summary>A window's own colours: the <c>WindowBackground</c> fill and the <c>TextPrimary</c> text every dialog uses.</summary>
    public static T UseWindowColours<T>(this T window) where T : Window
    {
        window[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        window[!Window.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        return window;
    }
}
