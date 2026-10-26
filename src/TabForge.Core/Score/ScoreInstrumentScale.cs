using TabForge.Presenters;

namespace TabForge.Views.Score;

// Owns: the score's text scale as the instrument pane reads it: the score's zoom times its spacing (the score sets the pane's ContentScale to
//   that on every zoom and page layout), and the drawing scale the pane takes from it: the fitted scale times the text scale, kept within the
//   fretboard's scale range. At 100% zoom with the default spacing the drawing is the fitted scale, so the pane's own size rules are unchanged.
// Does not own: the pane's drawing and hit tests (Avalonia InstrumentPanelView), the zoom (ScorePreviewView), the fit rule (FretboardScaleRules).
// Tests: TabForge.Avalonia.Tests.ScoreInstrumentScaleTests.

public static class ScoreInstrumentScale
{
    /// <summary>The pane's text scale: the score's spacing only. The score's zoom is deliberately not part of it: the fretboard keeps its size when the score is zoomed.</summary>
    public static double ContentScaleFor(double scoreSpacing) => scoreSpacing;

    /// <summary>The pane's drawing scale: the scale the pane fits to its size, times the score's text scale, within the fretboard's scale range.</summary>
    public static double DrawingScale(double fittedScale, double contentScale) =>
        Math.Clamp(fittedScale * contentScale, FretboardScaleRules.MinScale, FretboardScaleRules.MaxScale);
}
