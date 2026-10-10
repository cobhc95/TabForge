namespace TabForge.Views.Band;

// Owns: giving every Band lane the widest of each bar and the smallest wanted zoom, so one bar has the same place and size in all lanes
//   (the shared floor of bar widths is kept between passes). Re-entrant calls are ignored.
// Does not own: the rows, when to align (BandViewController) or the lanes' own engraving (BandLane).
// Tests: TestBandLanesInSync, TestBandVerticalLanes.
internal sealed class BandLaneAligner
{
    private bool _aligning;
    private double[]? _floor;

    /// <summary>Aligns <paramref name="rows"/>; false when a pass was already running and nothing was done.</summary>
    public bool Align(IReadOnlyList<BandRow> rows)
    {
        if (_aligning) return false;
        _aligning = true;
        try
        {
            // A lane that refits at the new cap or width may want another zoom (its system height is not exactly linear): settle in a few passes.
            for (var pass = 0; pass < 4; pass++)
            {
                double[]? widest = null;
                var cap = 4.0;
                var narrow = double.MaxValue;
                for (var i = 0; i < rows.Count; i++)
                {
                    var lane = rows[i].Lane;
                    var natural = lane.NaturalBarWidths();
                    if (natural is null) continue;
                    cap = Math.Min(cap, lane.WantedZoom);
                    if (lane.ActualWidth > 1) narrow = Math.Min(narrow, lane.ActualWidth);
                    if (widest is null) widest = (double[])natural.Clone();
                    else
                    {
                        if (natural.Length > widest.Length) Array.Resize(ref widest, natural.Length);
                        for (var b = 0; b < natural.Length; b++) widest[b] = Math.Max(widest[b], natural[b]);
                    }
                }
                if (widest is not null && (_floor is null || !_floor.AsSpan().SequenceEqual(widest))) _floor = widest;
                else if (widest is null) _floor = null;
                var changed = false;
                for (var i = 0; i < rows.Count; i++)
                {
                    var lane = rows[i].Lane;
                    var before = (lane.WantedZoom, lane.Editor.Zoom);
                    lane.ShareBarWidths(_floor); lane.ZoomCap = cap; lane.SharedWidth = narrow < double.MaxValue ? narrow : 0;
                    if (before != (lane.WantedZoom, lane.Editor.Zoom)) changed = true;
                }
                if (!changed) break;
            }
            return true;
        }
        finally { _aligning = false; }
    }
}
