// Owns: the shared stroke entry point: the seeded base widths, the density and UI-scale multiplier, the score spacing
//   gate, and the outline building.
// Does not own: the outline shape (UiStroke) or the scale constants (UiDensity).
// Tests: UiStrokesTests.
using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class UiStrokesTests
{
    [Fact]
    public void SeededWidthsAreOneDipAtComfortableScaleOne()
    {
        var appearance = new AppearanceSettings { Density = "Comfortable" };
        Assert.Equal(1.0, UiStrokes.Width("hairline", appearance));
        Assert.Equal(1.0, UiStrokes.Width("border", appearance));
    }

    [Fact]
    public void WidthFollowsTheClampedUiScale()
    {
        Assert.Equal(0.8, UiStrokes.Width("hairline", new AppearanceSettings { Density = "Comfortable", UiScale = 0.8 }), 9);
        Assert.Equal(1.0, UiStrokes.Width("hairline", new AppearanceSettings { Density = "Comfortable", UiScale = 1.0 }), 9);
        Assert.Equal(1.5, UiStrokes.Width("hairline", new AppearanceSettings { Density = "Comfortable", UiScale = 1.5 }), 9);
        Assert.Equal(1.5, UiStrokes.Width("hairline", new AppearanceSettings { Density = "Comfortable", UiScale = 9.9 }), 9);
        Assert.Equal(0.8, UiStrokes.Width("hairline", new AppearanceSettings { Density = "Comfortable", UiScale = 0.1 }), 9);
    }

    [Fact]
    public void ScoreSpacingAppliesOnlyToScoreStrokes()
    {
        var appearance = new AppearanceSettings { Density = "Comfortable", ScoreSpacing = 2.0 };
        Assert.Equal(1.0, UiStrokes.Width("hairline", appearance), 9);
        Assert.Equal(2.0, UiStrokes.Width("hairline", appearance, score: true), 9);
    }

    [Fact]
    public void CompactDensityScalesTheWidth()
    {
        var appearance = new AppearanceSettings { Density = "Compact" };
        Assert.Equal(0.9, UiStrokes.Width("hairline", appearance), 9);
        var spacious = new AppearanceSettings { Density = "Spacious" };
        Assert.Equal(1.0, UiStrokes.Width("hairline", spacious), 9);
    }

    [Fact]
    public void GetBuildsTheOutlineFromTheWidth()
    {
        var appearance = new AppearanceSettings { Density = "Comfortable" };
        var colour = new UiColor(255, 10, 20, 30);
        var stroke = UiStrokes.Get("hairline", colour, appearance);
        Assert.Equal(colour, stroke.Colour);
        Assert.Equal(1.0, stroke.Width, 9);
        Assert.False(stroke.RoundCaps);
        var wide = UiStrokes.Get("border", colour, new AppearanceSettings { Density = "Comfortable", UiScale = 1.5 }, roundCaps: true, roundJoins: true);
        Assert.Equal(1.5, wide.Width, 9);
        Assert.True(wide.RoundCaps);
        Assert.True(wide.RoundJoins);
        Assert.Equal(0, wide.Dash);
        Assert.Equal(0, wide.Gap);
    }

    [Fact]
    public void UnknownTokenThrows()
    {
        Assert.Throws<KeyNotFoundException>(() => UiStrokes.Width("no.such.stroke", new AppearanceSettings { Density = "Comfortable" }));
    }
}
