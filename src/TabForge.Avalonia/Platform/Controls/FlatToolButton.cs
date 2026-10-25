using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the flat tool button of a header strip: transparent at rest with no focus rectangle, the soft grey hover shade under the pointer and the
//   accent outline while the pointer is over it.
// Does not own: its content, its size, padding and margin (the caller sets them), what its click does.
// Tests: TabForge.Avalonia.Tests.CompactTrackStripTests.
public static class FlatToolButton
{
    /// <summary>The outline width; the outline is transparent at rest and accent under the pointer, so the layout does not move.</summary>
    private const double OutlineWidth = 1;

    /// <summary>A flat button around <paramref name="content"/>.</summary>
    public static Button Create(Control content)
    {
        var button = new Button
        {
            Content = content, Focusable = false, BorderThickness = new Thickness(OutlineWidth),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = global::Avalonia.Media.Brushes.Transparent, BorderBrush = global::Avalonia.Media.Brushes.Transparent,
        };
        HoverShade.Attach(button, hovered =>
        {
            button.Background = hovered ? HoverShade.Brush(button) : global::Avalonia.Media.Brushes.Transparent;
            button.BorderBrush = hovered ? BandBrush.Of(button, "Accent") : global::Avalonia.Media.Brushes.Transparent;
        });
        return button;
    }
}
