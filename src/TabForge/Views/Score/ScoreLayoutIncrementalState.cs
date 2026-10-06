namespace TabForge.Views.Score;

// Owns: data reused by a stable-range score relayout and the current bar's extent scan.
// Does not own: width calculation, page composition or fallback decisions.
// Tests: TestScoreLayoutPartial.
internal sealed class ScoreLayoutIncrementalState
{
    internal double[]? NaturalMeasureWidths;
    internal bool[]? ForceLineBreaks;
    internal bool[]? PreventLineBreaks;
    internal bool[]? LayoutRisk;
    internal MeasureMarkExtents[]? MeasureExtents;
    internal (int First, int Last)? PendingMeasureRange;
    internal (int First, int Last)? CapturedMeasureRange;
    internal bool PartialFactsAreLocal;
    internal int LastNaturalMeasureCount;
    internal bool LastRelayoutWasPartial;
    internal string LastRelayoutReason = "full invalidation";
    internal double MeasureScanTop, MeasureScanBottom, MeasureScanTabBelow;
    internal int MeasureScanRows;
    internal bool MeasureScanVolta;
}

internal readonly record struct MeasureMarkExtents(double Top, double Bottom, int Rows, bool Volta, double TabBelow);
