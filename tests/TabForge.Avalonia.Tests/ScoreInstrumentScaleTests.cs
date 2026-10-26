using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the instrument pane's text scale follows the score's zoom (SC-09): zooming the score sets the pane's ContentScale to the score's zoom times
// its spacing, and the pane's drawing never grows past its cap at that scale.
// Does not own: the pane's drawing (InstrumentPanelView), the zoom (ScorePreviewView), the cap rule's constants (Core ScoreInstrumentScale).
// Tests: ScoreInstrumentScaleTests (this file).
public class ScoreInstrumentScaleTests
{
    [AvaloniaFact]
    public void ZoomingTheScoreDoesNotChangeTheInstrumentPaneTextScale()
    {
        var score = new ScorePreviewView();
        var strip = new InstrumentPanelStrip(score);

        var before = strip.Panel.ContentScale;
        score.SetZoom(1.5);

        Assert.Equal(before, strip.Panel.ContentScale, 6);
        Assert.Equal(ScoreInstrumentScale.ContentScaleFor(score.Probe.Appearance.ScoreSpacing), strip.Panel.ContentScale, 6);
    }

    [Fact]
    public void DrawingScaleIsTheFittedScaleTimesTheTextScaleWithinTheFretboardRange()
    {
        Assert.Equal(0.8 * 1.5, ScoreInstrumentScale.DrawingScale(0.8, 1.5), 6);
        Assert.Equal(0.8, ScoreInstrumentScale.DrawingScale(0.8, 1), 6);
        Assert.Equal(TabForge.Presenters.FretboardScaleRules.MinScale, ScoreInstrumentScale.DrawingScale(0.8, 0.1), 6);
        Assert.Equal(TabForge.Presenters.FretboardScaleRules.MaxScale, ScoreInstrumentScale.DrawingScale(1.5, 10), 6);
    }
}
