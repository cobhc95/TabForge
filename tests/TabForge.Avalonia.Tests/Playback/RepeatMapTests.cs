using System.Linq;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the S1 repeat-view core: the (written bar, fraction, pass) <-> performed mapping (RepeatMap), the derived
//   regions (RepeatRegions) and the shown-pass view state (RepeatViewState), over the spec song (bars 1-12, 5-8 x2).
// Does not own: the pass mapping (RepeatPassesTests) or the fold (RepeatViewTests).
// Tests: this class.
public sealed class RepeatMapTests
{
    /// <summary>Bars 1-12, 2 s each, repeat 5-8 x <paramref name="passes"/>: performed 1-4, (5-8) x passes, 9-12.</summary>
    private static PerformedBar[] SpecSong(double barSec = 2.0, int passes = 2)
    {
        var order = new[] { 1, 2, 3, 4 }
            .Concat(Enumerable.Repeat(new[] { 5, 6, 7, 8 }, passes).SelectMany(part => part))
            .Concat(new[] { 9, 10, 11, 12 }).ToArray();
        var ms = barSec * 1000.0;
        return order.Select((bar, index) => new PerformedBar(bar, index * ms, (index + 1) * ms)).ToArray();
    }

    [Fact]
    public void ToPerformed_Bar6Middle_Pass2_Is19s()
    {
        Assert.Equal(19.0, RepeatMap.ToPerformed(SpecSong(), new RepeatPosition(6, 0.5, 2)), 6);
    }

    [Fact]
    public void ToPerformed_Bar6Middle_Pass1_Is11s()
    {
        Assert.Equal(11.0, RepeatMap.ToPerformed(SpecSong(), new RepeatPosition(6, 0.5, 1)), 6);
    }

    [Fact]
    public void FromPerformed_19s_IsBar6Middle_Pass2()
    {
        var pos = RepeatMap.FromPerformed(SpecSong(), 19.0);

        Assert.Equal(6, pos.WrittenBar);
        Assert.Equal(0.5, pos.Fraction, 6);
        Assert.Equal(2, pos.Pass);
    }

    [Fact]
    public void FromPerformed_3s_IsBar2_Pass1()
    {
        var pos = RepeatMap.FromPerformed(SpecSong(), 3.0);

        Assert.Equal(2, pos.WrittenBar);
        Assert.Equal(1, pos.Pass);
    }

    [Fact]
    public void ToPerformed_Bar6Middle_Pass5Of7_Is43s()
    {
        // Pill 5 then bar 6 middle: pass 5 starts at 8 + 4 * 8, bar 6 is one bar in, plus half a bar.
        Assert.Equal(43.0, RepeatMap.ToPerformed(SpecSong(passes: 7), new RepeatPosition(6, 0.5, 5)), 6);
    }

    [Fact]
    public void Regions_OfX2Song_IsOneRegion5To8_N2()
    {
        var region = Assert.Single(RepeatRegions.Of(SpecSong()));

        Assert.Equal(new RepeatRegion(5, 8, 2), region);
    }

    [Fact]
    public void BarsOutsideRepeats_AlwaysPass1()
    {
        var performed = SpecSong();

        Assert.Equal(1, RepeatMap.PassCount(performed, 2));
        Assert.Equal(1, RepeatMap.FromPerformed(performed, 3.0).Pass);
        Assert.True(double.IsNaN(RepeatMap.ToPerformed(performed, new RepeatPosition(2, 0.5, 2))));
    }

    [Fact]
    public void SetPass_9_OnN7_ClampsTo7()
    {
        var state = new RepeatViewState();
        var region = new RepeatRegion(5, 8, 7);

        state.SetPass(region, 9);

        Assert.Equal(7, state.ShownPass(region));
        state.SetPass(region, 0);
        Assert.Equal(1, state.ShownPass(region));
    }

    [Fact]
    public void Volta_FirstAndSecondEndingsDiffer_PerPlayKeepsItsOwnLength()
    {
        // Volta: 5-6 with ending 7 first, ending 9 second; the second play of bar 6 is longer (3 s vs 2 s).
        var performed = new[]
        {
            new PerformedBar(5, 0, 2000), new PerformedBar(6, 2000, 4000), new PerformedBar(7, 4000, 6000),
            new PerformedBar(5, 6000, 8000), new PerformedBar(6, 8000, 11000), new PerformedBar(9, 11000, 13000),
        };

        Assert.Equal(3.0, RepeatMap.ToPerformed(performed, new RepeatPosition(6, 0.5, 1)), 6);
        Assert.Equal(9.5, RepeatMap.ToPerformed(performed, new RepeatPosition(6, 0.5, 2)), 6);
        Assert.Equal(new RepeatPosition(6, 0.5, 2), RepeatMap.FromPerformed(performed, 9.5));
        // Endings play once and stay out of the region; the shared bars form one region with N = max.
        Assert.Equal(new[] { new RepeatRegion(5, 6, 2) }, RepeatRegions.Of(performed));
    }

    [Fact]
    public void ViewState_ShownDefaults1_LastPlayedKeptAfterLeaving_ResetClears()
    {
        var state = new RepeatViewState();
        var region = new RepeatRegion(5, 8, 2);
        var other = new RepeatRegion(20, 21, 3);

        Assert.Equal(1, state.ShownPass(region));
        Assert.Equal(1, state.LastPlayed(region));

        state.FollowPlayback(region, 2);
        state.FollowPlayback(other, 3);   // playback leaves the first region

        Assert.Equal(2, state.ShownPass(region));
        Assert.Equal(2, state.LastPlayed(region));

        state.Reset();

        Assert.Equal(1, state.ShownPass(region));
        Assert.Equal(1, state.LastPlayed(region));
    }
}
