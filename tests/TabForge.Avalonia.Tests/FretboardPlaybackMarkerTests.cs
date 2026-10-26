using System;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Visualization;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the playback marker radii of each role, the fit of a marker into the drawn string gap at every spacing
/// setting (so neighbouring bubbles never overlap), and the movement line's rule: only the changed positions, from the held shape (or the
/// released chord at a rest) to the next shape.
/// Does not own: drawing the markers and the line (FretboardPlaybackMarkers draws through IDrawContext), the text metrics of a bubble's label
/// (InstrumentFretNumberTests).
/// Tests: FretboardPlaybackMarkers, FretboardMovementRules.
/// </summary>
public class FretboardPlaybackMarkerTests
{
    /// <summary>The pane width the marker fit is checked at, and the panel's own widest string gap.</summary>
    private const double PanelWidth = 900;
    private const double RoomiestStringGap = 44;

    [Xunit.Fact]
    public void MarkerRadiiFollowTheRoleSizesOfTheWpfPanel()
    {
        const double fretWidth = 40;
        // A gap with room for the largest marker keeps the role's own size.
        double Radius(PlaybackMarkerRole role) => FretboardPlaybackMarkers.GeometryOf(role, fretWidth, 1, 1, RoomiestStringGap).Radius;
        Assert.Equal(16, Radius(PlaybackMarkerRole.Now), 1e-9);            // min(16, 40 * 0.44)
        Assert.Equal(16, Radius(PlaybackMarkerRole.ReleasedNow), 1e-9);    // a released sounding note keeps the sounding size
        Assert.Equal(13, Radius(PlaybackMarkerRole.Next), 1e-9);           // min(13, 40 * 0.36)
        Assert.Equal(12, Radius(PlaybackMarkerRole.Upcoming), 1e-9);       // min(12, 40 * 0.32)
        Assert.Equal(12, Radius(PlaybackMarkerRole.Recent), 1e-9);
        // A narrow fret scales the sounding marker down from its cap.
        Assert.Equal(20 * 0.44, FretboardPlaybackMarkers.GeometryOf(PlaybackMarkerRole.Now, 20, 1, 1, RoomiestStringGap).Radius, 1e-9);
    }

    [Xunit.Fact]
    public void MarkerDiameterFitsTheStringGapAtEverySpacing()
    {
        foreach (var strings in new[] { 6, 7 })
            foreach (var spacing in FretStringSpacings.All)
            {
                var layout = PanelLayout(strings, FretStringSpacings.Factor(spacing));
                var marker = FretboardPlaybackMarkers.GeometryOf(PlaybackMarkerRole.Now, layout.FretWidth, 1, 1, layout.StringGap);
                Assert.True(marker.Radius * 2 <= layout.StringGap + 1e-9,
                    $"{strings} strings, {spacing} spacing: marker diameter {marker.Radius * 2:F2} over a string gap of {layout.StringGap:F2}");
                // The last fret's bubble still sits inside the board's right edge.
                var last = FretboardGeometryRules.PositionOf(layout, 0, layout.LastFret);
                Assert.True(last.X + marker.Radius <= layout.Board.Right + 1e-9,
                    $"{strings} strings, {spacing} spacing: marker reaches {last.X + marker.Radius:F2}, board ends at {layout.Board.Right:F2}");
            }
    }

    [Xunit.Fact]
    public void NaturalSpacingKeepsTheBaseMarkerWhereTheGapHasRoom()
    {
        // Natural spacing adds no factor of its own: a gap with room draws the base radius, and only a tight gap shrinks it.
        Assert.Equal(16, FretboardPlaybackMarkers.GeometryOf(PlaybackMarkerRole.Now, 40, 1, 1, RoomiestStringGap).Radius, 1e-9);
        Assert.True(FretboardPlaybackMarkers.GeometryOf(PlaybackMarkerRole.Now, 40, 1, 1, 26).Radius < 16);
    }

    /// <summary>The board the panel lays out at <see cref="PanelWidth"/>: the same virtual pane, scales and limits the panel draws at.</summary>
    private static FretboardGeometryRules.Layout PanelLayout(int strings, double spacing)
    {
        var input = new FretboardGeometryRules.Input(strings, 24, 24, spacing, false, 0);
        var natural = FretboardScaleRules.NaturalHeight(strings, spacing);
        var scale = FretboardScaleRules.Scale(PanelWidth, InstrumentPanelView.PanelHeight, natural);
        var pane = new UiRect(0, 0, (PanelWidth - InstrumentPanelView.CloseButtonReserve) / scale, InstrumentPanelView.PanelHeight / scale);
        var area = FretboardGeometryRules.ContentArea(input, pane, FretboardHorizontalPosition.Centre, InstrumentPanelView.BoardLimits);
        return FretboardGeometryRules.Compute(input, area, FretboardHorizontalPosition.Centre, limits: InstrumentPanelView.BoardLimits);
    }
}
