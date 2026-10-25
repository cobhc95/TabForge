using System;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline hover and playhead shades as WPF draws them: the track-list row shade at half strength, the bar cell shade (white on dark,
///     black on light) and its radius.
/// Does not own: the hover wiring (HoverShade.Attach, TimelineView.HoverBar) or the drawing itself.
/// </summary>
public class TimelineHoverTests
{
    [Fact]
    public void BarCellShadeIsWhiteOnDarkAndBlackOnLightAtWpfStrength()
    {
        var dark = HoverShade.CellFill(light: false);
        var light = HoverShade.CellFill(light: true);

        Assert.Equal(255, dark.R);
        Assert.Equal(255, dark.G);
        Assert.Equal(255, dark.B);
        Assert.Equal((byte)Math.Round(255 * HoverShade.CellDarkOpacity), dark.A);
        Assert.Equal(0, light.R);
        Assert.Equal((byte)Math.Round(255 * HoverShade.CellLightOpacity), light.A);
        Assert.Equal(2, HoverShade.CellRadius);
    }

    [Fact]
    public void TrackRowShadeIsHalfStrengthOfTheHoverSurface()
    {
        var surface = AvaloniaPalette.Get(HoverShade.PaletteKey, light: false);

        Assert.Equal(0.5, HoverShade.OverlayOpacity);
        Assert.Equal((byte)Math.Round(surface.A * HoverShade.OverlayOpacity), HoverShade.Overlay(light: false).A);
    }
}
