using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Shaded scale style draws each scale note's cell as a tinted band on the board (more filled rectangles than the circles style, which
/// draws dots over the strings instead), so the default highlight reads as shaded frets.
/// Does not own: the scale rule (ScaleHighlightStyles), the look's settings (FretboardSettingsBinding) or the board's own drawing.
/// Tests: TabForge.Avalonia.Tests.FretboardShadedScaleTests.
/// </summary>
public sealed class FretboardShadedScaleTests
{
    private const string Scale = "C Major";
    private static readonly Size PaneSize = new(1000, 150);
    /// <summary>The filled rectangles drawn whatever the scale style: the panel background and the board.</summary>
    private const int BackgroundAndBoard = 2;

    [AvaloniaFact]
    public void ShadedStyleDrawsEachScaleCellBehindTheBoard()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard };
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(PaneSize);
        view.Arrange(new Rect(0, 0, PaneSize.Width, PaneSize.Height));

        var settings = new AppSettings { Editing = { ScaleHighlight = Scale, ScaleHighlightStyle = ScaleHighlightStyles.Shaded } };
        view.Look = FretboardSettingsBinding.Look.From(settings);
        var shaded = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(shaded, light: false);
        var filledCells = shaded.Rects.Count(rect => rect.Fill is not null) - BackgroundAndBoard;

        settings.Editing.ScaleHighlightStyle = ScaleHighlightStyles.Circles;
        view.Look = FretboardSettingsBinding.Look.From(settings);
        var circles = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(circles, light: false);
        var filledCirclesCells = circles.Rects.Count(rect => rect.Fill is not null) - BackgroundAndBoard;

        Assert.True(filledCells > 0, "the shaded style drew no scale cells");
        Assert.Equal(0, filledCirclesCells);
    }
}
