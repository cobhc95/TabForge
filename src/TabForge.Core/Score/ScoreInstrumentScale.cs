using TabForge.Presenters;

namespace TabForge.Views.Score;

// Owns: the score's text scale as the instrument pane reads it: the score's zoom times its spacing (the WPF score sets the pane's ContentScale to
//   that on every zoom and page layout), and the drawing scale the pane takes from it: the fitted scale times the text scale, kept within the
//   fretboard's scale range. At 100% zoom with the default spacing the drawing is the fitted scale, so the pane's own size rules are unchanged.
// Does not own: the pane's drawing and hit tests (Avalonia InstrumentPanelView), the zoom (ScorePreviewView), the fit rule (FretboardScaleRules).
// Tests: TabForge.Avalonia.Tests.ScoreInstrumentScaleTests.

public static class ScoreInstrumentScale
{
    /// <summary>The text scale of the score at <paramref name="zoom"/> and <paramref name="scoreSpacing"/>; 1 is the score at 100% with its default spacing.</summary>
    public static double ContentScaleFor(double zoom, double scoreSpacing) => zoom * scoreSpacing;

    /// <summary>The pane's drawing scale: the scale the pane fits to its size, times the score's text scale, within the fretboard's scale range.</summary>
    public static double DrawingScale(double fittedScale, double contentScale) =>
        Math.Clamp(fittedScale * contentScale, FretboardScaleRules.MinScale, FretboardScaleRules.MaxScale);
}
