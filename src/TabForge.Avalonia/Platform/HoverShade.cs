using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TabForge.Avalonia.Platform;

// Owns: the soft grey hover shade of interactive controls: the pointer enter and exit wiring, the HoverSurface colour as a brush, and the
// hovered bar cell's fill (white on the dark theme, black on the light one).
// Does not own: what a control paints with the shade (each control decides), the theme values (the palette keys).
// Tests: TransportBarViewTests (the hover fill of a transport button), TimelineHoverTests (the bar cell and the track row).
// Threads: UI thread only.
public static class HoverShade
{
    /// <summary>The palette key of the soft grey hover shade (the theme's HoverSurface).</summary>
    public const string PaletteKey = "HoverSurface";

    /// <summary>The palette key of the pressed shade (the theme's PressSurface).</summary>
    public const string PressPaletteKey = "PressSurface";

    /// <summary>How strongly the shade covers a surface that has its own colour (a track-list row), so the colour underneath still shows through.</summary>
    public const double OverlayOpacity = 0.50;

    /// <summary>The hovered bar cell's strength on the dark theme (white) and the light theme (black), and its corner radius in DIPs.</summary>
    public const double CellDarkOpacity = 0.12, CellLightOpacity = 0.08, CellRadius = 2;

    /// <summary>The hovered bar cell's fill for the light or dark theme: white over the dark theme, black over the light one.</summary>
    public static TabForge.Platform.UiColor CellFill(bool light)
    {
        const string key = "OverlayInk";
        var opacity = light ? CellLightOpacity : CellDarkOpacity;
        return AvaloniaPalette.Get(key, light).WithOpacity(opacity);
    }

    /// <summary>The shade as a see-through overlay colour for the light or dark theme.</summary>
    public static TabForge.Platform.UiColor Overlay(bool light)
    {
        var c = AvaloniaPalette.Get(PaletteKey, light);
        return c.WithOpacity(OverlayOpacity);
    }

    /// <summary>Calls <paramref name="hoverChanged"/> with true when the pointer enters the element and false when it leaves.</summary>
    public static void Attach(InputElement element, Action<bool> hoverChanged)
    {
        element.PointerEntered += (_, _) => hoverChanged(true);
        element.PointerExited += (_, _) => hoverChanged(false);
    }

    /// <summary>The hover shade as a brush for the light or dark theme.</summary>
    public static IBrush Brush(Control control) => Brush(control.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light);

    public static IBrush Brush(bool light) => AvaloniaPalette.PaletteBrush(PaletteKey, light);

    /// <summary>The palette key of the stronger hover of the toolbar and transport icon buttons (the theme's IconHoverSurface).</summary>
    public const string IconPaletteKey = "IconHoverSurface";

    /// <summary>The icon-button hover shade as a brush, in the theme the control is shown in.</summary>
    public static IBrush IconBrush(Control control) =>
        AvaloniaPalette.PaletteBrush(IconPaletteKey, control.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light);

    /// <summary>Keeps a control's fill in step with the pointer: <paramref name="rest"/> at rest, the hover shade while over it, the press shade while pressed.</summary>
    /// <returns>A setter for the keyboard hold (Enter or Space down or up), so a key press shows the same shade through the same state as the pointer.</returns>
    public static Action<bool> Wire(Control control, Action<IBrush> setFill, Func<IBrush> rest)
    {
        bool over = false, pointerHeld = false, keyHeld = false;
        void Paint() => setFill(pointerHeld || keyHeld ? AvaloniaPalette.PaletteBrush(PressPaletteKey, control.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light) : over ? Brush(control) : rest());
        control.PointerEntered += (_, _) => { over = true; Paint(); };
        control.PointerExited += (_, _) => { over = false; pointerHeld = false; Paint(); };
        control.PointerPressed += (_, _) => { pointerHeld = true; Paint(); };
        control.PointerReleased += (_, _) => { pointerHeld = false; Paint(); };
        return isHeld => { keyHeld = isHeld; Paint(); };
    }
}
