using TabForge.Services;

namespace TabForge.Views.Band;

// Owns: how a Band lane follows the playhead: the mode (hold, page jump, glided jump, continuous) resolved from the score's follow
//   settings or the Band's own override, and the pure offset rule the lane applies each frame.
// Does not own: the settings values (FollowSettings, BandSettings) or moving the strip (BandLane).
// Tests: TestBandFollow.
internal readonly record struct BandFollow(BandFollowMode Mode, bool Glide, double LookAheadBars, bool StopAtEnd)
{
    /// <summary>The default: continuous, as before any preference.</summary>
    public static BandFollow Continuous { get; } = new(BandFollowMode.Continuous, false, 0.75, true);

    /// <summary>The score's follow settings (horizontal only), or the Band's own choice when it does not follow the score.</summary>
    public static BandFollow Resolve(FollowSettings score, BandSettings band)
    {
        if (!band.FollowsScore) return band.SmoothFollow ? Continuous : new BandFollow(BandFollowMode.Jump, false, 0.75, true);
        if (score.Mode == FollowModes.Off || !score.HorizontalFollow) return new BandFollow(BandFollowMode.Hold, false, 0, true);
        var look = Math.Clamp(score.AnticipationBars, 0, 4) * 0.75;
        // The score turns half a page sideways in both Jump and Smooth (Smooth only eases its vertical moves); "glide" eases the turn.
        return new BandFollow(BandFollowMode.Jump, score.ContinuousScroll, look, score.StopAtEnd);
    }

    /// <summary>Vertical layout: the top system for a playhead in <paramref name="system"/> when <paramref name="visible"/> systems fit the lane.</summary>
    public int NextTop(int top, int system, int visible, int systems)
    {
        var max = StopAtEnd ? Math.Max(0, systems - visible) : Math.Max(0, systems - 1);
        switch (Mode)
        {
            case BandFollowMode.Hold: return top;
            case BandFollowMode.Continuous: return Math.Min(system, max);
            default: return system >= top && system < top + visible ? top : Math.Min(system, max);
        }
    }

    /// <summary>The strip offset for a playhead at <paramref name="x"/> in a strip <paramref name="stripWidth"/> wide.</summary>
    public double NextOffset(double offset, double laneWidth, double x, double barWidth, double stripWidth, double playheadAt)
    {
        var max = Math.Max(0, stripWidth - laneWidth);
        if (laneWidth <= 1) return offset;
        switch (Mode)
        {
            case BandFollowMode.Hold: return offset;
            case BandFollowMode.Continuous:
                var follow = Math.Max(0, x - laneWidth * playheadAt);
                return StopAtEnd ? Math.Min(follow, max) : follow;
            default:
                if (x < offset - 0.5 || x > offset + laneWidth) return Math.Clamp(x - laneWidth * 0.25, 0, max);
                return ScoreHorizontalFollow.NextOffset(offset, laneWidth, x, barWidth, StopAtEnd ? stripWidth : double.MaxValue / 4, LookAheadBars) ?? offset;
        }
    }
}

internal enum BandFollowMode { Hold, Jump, Continuous }
