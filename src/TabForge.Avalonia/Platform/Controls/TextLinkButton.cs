using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the look of a text link button: transparent at rest, the soft hover shade under the pointer (HoverShade), and the rounded corner. The pointer shape never changes on hover.
// Does not own: the text, the click handler (the caller), the foreground colour (the caller's theme key) or the shade's colour (HoverShade).
// Tests: StatusBarViewTests, PreferencesChangesLinkTests.
public static class TextLinkButton
{
    /// <summary>Makes <paramref name="button"/> a link: no fill at rest, the hover shade while the pointer is over it.</summary>
    public static void Apply(Button button)
    {
        button.Background = AvaloniaPalette.TransparentBrush;
        HoverShade.Attach(button, hovered => button.Background = hovered ? HoverShade.Brush(button) : AvaloniaPalette.TransparentBrush);
        button.CornerRadius = new CornerRadius(LinkCornerRadius);
    }

    private const double LinkCornerRadius = 4;
}
