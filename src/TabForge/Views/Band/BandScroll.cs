namespace TabForge.Views.Band;

// Owns: the one scroll position every Band lane stands at (pixels sideways in the horizontal layout, the top system in the vertical one),
//   worked out once per frame from the first lane that can place the bar and handed to all lanes, so they always show the same bar.
// Does not own: the engraving or the slides themselves (BandLane) or the follow rule (BandFollow).
// Tests: TestBandLanesInSync, TestBandVerticalLanes.
internal sealed class BandScroll
{
    private double _pos;
    private int _top;

    public bool Vertical { get; set; }

    /// <summary>The shared position of the last frame.</summary>
    public double Pos => _pos;

    public void Reset() { _pos = 0; _top = 0; }

    /// <summary>Puts every lane's playhead on a bar and a fraction of it, at one shared scroll position.</summary>
    public void Show(IReadOnlyList<BandRow> rows, int bar, double fraction, bool glide, BandFollow follow)
    {
        BandLane? lead = null;
        for (var i = 0; i < rows.Count && lead is null; i++) if (rows[i].Lane.HasGeometry(bar)) lead = rows[i].Lane;
        if (lead is not null)
        {
            if (Vertical)
            {
                var visible = lead.VisibleSystems;
                _top = follow.NextTop(_top, lead.SystemOf(bar), visible, lead.SystemCount);
                var ease = glide && (follow.Glide || follow.Mode == BandFollowMode.Continuous) && Math.Abs(_top - _pos) <= visible + 1;
                _pos = ease && Math.Abs(_top - _pos) > 0.02 ? _pos + (_top - _pos) * 0.25 : _top;
            }
            else _pos = lead.NextOffset(bar, fraction, glide, _pos) ?? _pos;
        }
        // Vertical: lanes of different widths wrap into different systems, so each lane puts the playing bar's own system
        // at the same row of the lane (the lead's position of it) instead of reusing the lead's system number.
        double? rel = Vertical && lead is not null ? lead.SystemOf(bar) - _pos : null;
        // The first lane with notes in the bar sets where in the bar the line stands for lanes that have none there.
        double? spaced = null;
        for (var i = 0; i < rows.Count && spaced is null; i++) if (rows[i].Lane.BarHasNotes(bar)) spaced = rows[i].Lane.SpacedFraction(bar, fraction);
        for (var i = 0; i < rows.Count; i++) rows[i].Lane.Place(bar, fraction, _pos, rel, spaced);
    }
}
