using System.Windows.Media;
using TabForge.Views.Score;

namespace TabForge.Views.Band;

// Owns: copying the main score's look (paper, ink, lines, spacing, labels) onto a lane's editor, and the key that says the look changed.
// Does not own: the settings that produce the look (the main editor's applier writes them) or the lane's drawing.
// Tests: TestBandViewRows.
internal static class BandAppearance
{
    /// <summary>What a lane reads from the look; two equal keys draw the same.</summary>
    internal readonly record struct Key(
        bool Dark, Color Paper, Color Ink, Color Lines, Color Accent, Color Play, double Spacing, double VerticalSpacing,
        double HorizontalSpacing, bool Dynamics, bool BarNumbers, int BarNumberEvery, bool Headings, LedgerLineMode Ledger);

    public static Key KeyOf(ScoreAppearance a) => new(
        a.DarkPaper, a.DarkPaper ? a.DarkPaperColor : a.LightPaperColor, a.DarkPaper ? a.DarkInkColor : a.LightInkColor,
        a.DarkPaper ? a.DarkStaffLineColor : a.LightStaffLineColor, a.AccentColor, a.PlaybackColor, a.ScoreSpacing,
        a.SystemVerticalSpacing, a.MeasureHorizontalSpacing, a.ShowDynamics, a.ShowBarNumbers, a.BarNumberFrequency,
        a.ShowSectionHeadings, a.LedgerLines);

    /// <summary>The paper colour of an appearance (the lane's background).</summary>
    public static Color PaperOf(ScoreAppearance a) => a.DarkPaper ? a.DarkPaperColor : a.LightPaperColor;

    /// <summary>Copies the look; the lane never plays a bar band or a glow of its own.</summary>
    public static void Copy(ScoreAppearance from, ScoreAppearance to)
    {
        to.DarkPaper = from.DarkPaper;
        to.DarkPaperColor = from.DarkPaperColor;
        to.LightPaperColor = from.LightPaperColor;
        to.DarkInkColor = from.DarkInkColor;
        to.LightInkColor = from.LightInkColor;
        to.DarkStaffLineColor = from.DarkStaffLineColor;
        to.LightStaffLineColor = from.LightStaffLineColor;
        to.LedgerLines = from.LedgerLines;
        to.AccentColor = from.AccentColor;
        to.SelectionColor = from.SelectionColor;
        to.HoverColor = from.HoverColor;
        to.CursorColor = from.CursorColor;
        to.PlaybackColor = from.PlaybackColor;
        to.ShowSectionHeadings = from.ShowSectionHeadings;
        to.ShowBarNumbers = from.ShowBarNumbers;
        to.BarNumberFrequency = from.BarNumberFrequency;
        to.ShowDynamics = from.ShowDynamics;
        to.ScoreSpacing = from.ScoreSpacing;
        to.SystemVerticalSpacing = from.SystemVerticalSpacing;
        to.MeasureHorizontalSpacing = from.MeasureHorizontalSpacing;
        to.CenterSystems = false;
        to.PlayingBarEnabled = false;
    }
}
