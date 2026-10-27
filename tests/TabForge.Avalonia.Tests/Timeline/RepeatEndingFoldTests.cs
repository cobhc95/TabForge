using System.Collections.Generic;
using System.Linq;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the fold of a clip over a repeat with alternate endings: a bar played on one pass only stays on that pass lane, the other lane is empty.
// Does not own: the plain repeat fold (RepeatViewTests) or the drawing.
// Tests: this class.
public sealed class RepeatEndingFoldTests
{
    // voltas: written bar -> its ending mask (bit n = pass n+1), as PlaybackOrder reads it from the score.
    private static PerformedBar[] Performed(params int[] order) => Performed(new Dictionary<int, int>(), order);

    private static PerformedBar[] Performed(Dictionary<int, int> voltas, params int[] order) =>
        order.Select((bar, index) => new PerformedBar(bar, index * 1000.0, (index + 1) * 1000.0, voltas.GetValueOrDefault(bar))).ToArray();

    [Fact]
    public void EndingsOnPass1AndPass2_StayOnTheirOwnPassLane()
    {
        // Bars 19-21 repeat; bar 22 is [1.] and bar 23 is [2.]: performed 18, 19-22, 19-21, 23, 24.
        var slices = RepeatFold.FoldedSlices(0, 10, Performed(new Dictionary<int, int> { [22] = 1, [23] = 2 }, 18, 19, 20, 21, 22, 19, 20, 21, 23, 24));

        Assert.Equal(5, slices.Count);
        Assert.Equal((1d, 5d, 0, 2), (slices[1].StartSec, slices[1].EndSec, slices[1].PartIndex, slices[1].PartCount));   // pass 1: bars 19-22
        Assert.Equal((5d, 8d, 1, 2), (slices[2].StartSec, slices[2].EndSec, slices[2].PartIndex, slices[2].PartCount));   // pass 2: bars 19-21
        Assert.Equal((8d, 9d, 1, 2), (slices[3].StartSec, slices[3].EndSec, slices[3].PartIndex, slices[3].PartCount));   // bar 23: pass 2 lane only
        Assert.Equal(8d, slices[3].WrittenStartSec, 6);
        Assert.False(slices[4].IsPart);
    }

    [Fact]
    public void RepeatWithoutEndings_IsUnchanged()
    {
        var slices = RepeatFold.FoldedSlices(0, 7, Performed(1, 2, 3, 2, 3, 4, 5));

        Assert.Equal(new[] { false, true, true, false }, slices.Select(s => s.IsPart).ToArray());
        Assert.Empty(RepeatEndings.Of(Performed(1, 2, 3, 2, 3, 4, 5)));
    }

    [Fact]
    public void ADalSegnoGapWithoutAVoltaIsNotAnEnding()
    {
        // 1 2 3 4 (D.S. at 4, segno on 2) 2 3 (to coda) 5: bar 4 is played once between two passes of 2-3 but carries no volta.
        Assert.Empty(RepeatEndings.Of(Performed(1, 2, 3, 4, 2, 3, 5)));
    }

    [Fact]
    public void NestedRepeatsStaySeparateRegionsAndKeepTheirOwnPassCount()
    {
        // Outer 1-4 twice around an inner 2-3 played twice per outer pass.
        var regions = RepeatRegions.Of(Performed(1, 2, 3, 2, 3, 4, 1, 2, 3, 2, 3, 4));

        Assert.Equal(new[] { new RepeatRegion(1, 1, 2), new RepeatRegion(2, 3, 4), new RepeatRegion(4, 4, 2) }, regions);
    }

    [Fact]
    public void ThreeEndingsEachGetTheirOwnPassAndTheRegionPassCount()
    {
        var endings = RepeatEndings.Of(Performed(new Dictionary<int, int> { [3] = 1, [4] = 2, [5] = 4 }, 1, 2, 3, 1, 2, 4, 1, 2, 5, 6));

        Assert.Equal(new RepeatEnding(1, 3, 1), endings[3]);
        Assert.Equal(new RepeatEnding(2, 3, 1), endings[4]);
        Assert.Equal(new RepeatEnding(3, 3, 1), endings[5]);
        Assert.False(endings.ContainsKey(6));
    }

    [Fact]
    public void AnEndingLongerThanOneBarKeepsAllItsBarsAndNothingAfterThem()
    {
        // [1.] spans bars 3-4, [2.] spans bars 5-6 (longer than the first ending is irrelevant: the masks decide), bar 7 is the music that goes on.
        var voltas = new Dictionary<int, int> { [3] = 1, [4] = 1, [5] = 2, [6] = 2, [7] = 0 };
        var endings = RepeatEndings.Of(Performed(voltas, 1, 2, 3, 4, 1, 2, 5, 6, 7));

        Assert.Equal(new[] { 3, 4, 5, 6 }, endings.Keys.OrderBy(b => b).ToArray());
        Assert.Equal(2, endings[6].Pass);
    }
}
