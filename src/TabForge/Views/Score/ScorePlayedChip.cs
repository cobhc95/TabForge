using System.Windows;
using System.Windows.Media;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views.Score;

// Owns: the glow around the fret number of the note sounding now (a pill, plus a ring for a note just struck).
// Does not own: choosing which notes sound (PlaybackOverlay) or where they sit (ScoreRenderer, BandLane).
// Tests: TestBandNoteGlow.
internal static class ScorePlayedChip
{
    /// <summary>Draws the glow behind a fret number whose chip is <paramref name="chipWidth"/> by <paramref name="chipHeight"/>, centred on (<paramref name="centerX"/>, <paramref name="y"/>).</summary>
    public static void Draw(DrawingContext dc, Color playColor, bool struck, double centerX, double y, double chipWidth, double chipHeight)
    {
        var glow = ScoreText.Brush(Color.FromArgb(struck ? (byte)90 : (byte)46, playColor.R, playColor.G, playColor.B));
        dc.DrawRoundedRectangle(glow, RenderDraw.Pen(playColor, struck ? 1.8 : 1.0),
            new Rect(centerX - chipWidth / 2 - 2, y - chipHeight / 2 - 2, chipWidth + 4, chipHeight + 4), 4, 4);
        if (struck)
            dc.DrawRoundedRectangle(null, RenderDraw.Pen(playColor, 1.0),
                new Rect(centerX - chipWidth / 2 - 5, y - chipHeight / 2 - 3, chipWidth + 10, chipHeight + 6), 5, 5);
    }
}
