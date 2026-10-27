using System;
using System.Linq;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the repeat-pass math of a recording through score repeats (RepeatPasses): ordinary spans, one segment per play of
//   each repeated range, and the part k -> performed occurrence k mapping that landing, the engine sync and the MIDI compile share.
// Does not own: landing the segments as clips (RepeatTakeLandingTests) or any drawing.
// Tests: this class.
public sealed class RepeatPassesTests
{
    private static PerformedBar[] Bars(params int[] bars)
    {
        var list = new PerformedBar[bars.Length];
        for (var i = 0; i < bars.Length; i++) list[i] = new PerformedBar(bars[i], i * 1000.0, (i + 1) * 1000.0);
        return list;
    }

    [Fact]
    public void Repeat48To50TwiceRecorded47To51GivesPreTwoPartsAndPost()
    {
        // The diagnosis example: bars 47,48,49,50,48,49,50,51 as performed; the file 0..8 s covers it all.
        var segments = RepeatPasses.Segments(Bars(47, 48, 49, 50, 48, 49, 50, 51), 0, 8).ToList();

        Assert.Equal(4, segments.Count);
        Assert.Equal((RepeatSegmentKind.Ordinary, 0, 0.0, 0.0, 1.0), (segments[0].Kind, segments[0].Pass, segments[0].SongStartSec, segments[0].FileOffsetSec, segments[0].LengthSec));
        Assert.Equal((RepeatSegmentKind.RepeatPart, 1, 1.0, 1.0, 3.0), (segments[1].Kind, segments[1].Pass, segments[1].SongStartSec, segments[1].FileOffsetSec, segments[1].LengthSec));
        Assert.Equal(48, segments[1].SourceBar);
        Assert.Equal((RepeatSegmentKind.RepeatPart, 2, 4.0, 4.0, 3.0), (segments[2].Kind, segments[2].Pass, segments[2].SongStartSec, segments[2].FileOffsetSec, segments[2].LengthSec));
        Assert.Equal((RepeatSegmentKind.Ordinary, 0, 7.0, 7.0, 1.0), (segments[3].Kind, segments[3].Pass, segments[3].SongStartSec, segments[3].FileOffsetSec, segments[3].LengthSec));
    }

    [Fact]
    public void VoltaEndingsBecomeOrdinarySpansBetweenTheParts()
    {
        // Volta 48-49 with end 1 = 50 and end 2 = 51: the parts cover 48-49, the endings land as ordinary spans.
        var segments = RepeatPasses.Segments(Bars(48, 49, 50, 48, 49, 51), 0, 6).ToList();

        Assert.Equal(4, segments.Count);
        Assert.Equal((RepeatSegmentKind.RepeatPart, 1, 0.0, 2.0), (segments[0].Kind, segments[0].Pass, segments[0].SongStartSec, segments[0].LengthSec));
        Assert.Equal((RepeatSegmentKind.Ordinary, 0, 2.0, 1.0), (segments[1].Kind, segments[1].Pass, segments[1].SongStartSec, segments[1].LengthSec));
        Assert.Equal((RepeatSegmentKind.RepeatPart, 2, 3.0, 2.0), (segments[2].Kind, segments[2].Pass, segments[2].SongStartSec, segments[2].LengthSec));
        Assert.Equal((RepeatSegmentKind.Ordinary, 0, 5.0, 1.0), (segments[3].Kind, segments[3].Pass, segments[3].SongStartSec, segments[3].LengthSec));
    }

    [Fact]
    public void StartingInsideTheRepeatOpensWithAPartialFirstPart()
    {
        // Record from the middle of bar 48's first play: part 1 = 48.5-50, part 2 the whole second play.
        var segments = RepeatPasses.Segments(Bars(48, 49, 50, 48, 49, 50), 0.5, 5.0).ToList();

        Assert.Equal(2, segments.Count);
        Assert.Equal((RepeatSegmentKind.RepeatPart, 1, 0.5, 2.5), (segments[0].Kind, segments[0].Pass, segments[0].SongStartSec, segments[0].LengthSec));
        Assert.Equal((RepeatSegmentKind.RepeatPart, 2, 3.0, 2.5), (segments[1].Kind, segments[1].Pass, segments[1].SongStartSec, segments[1].LengthSec));
    }

    [Fact]
    public void EveryFurtherPlayOfABarRangeIsTheNextPartNumber()
    {
        // D.S./nested repeats: three plays of 7-8 become parts 1, 2 and 3.
        var segments = RepeatPasses.Segments(Bars(7, 8, 7, 8, 7, 8), 0, 6).ToList();

        Assert.Equal(new[] { 1, 2, 3 }, segments.Select(segment => segment.Pass).ToArray());
        Assert.All(segments, segment => Assert.Equal(RepeatSegmentKind.RepeatPart, segment.Kind));
        Assert.All(segments, segment => Assert.Equal(2.0, segment.LengthSec));
    }

    [Fact]
    public void NoRepeatGivesOneOrdinarySpan()
    {
        var performed = Bars(1, 2, 3);

        Assert.False(RepeatPasses.HasRepeatSpan(performed, 0, 3));
        var single = Assert.Single(RepeatPasses.Segments(performed, 0, 3));
        Assert.Equal(RepeatSegmentKind.Ordinary, single.Kind);
        Assert.Equal((0.0, 0.0, 3.0), (single.SongStartSec, single.FileOffsetSec, single.LengthSec));
    }

    [Fact]
    public void HasRepeatSpanIgnoresRangesOutsideTheRecording()
    {
        var performed = Bars(47, 48, 49, 50, 48, 49, 50, 51);

        Assert.False(RepeatPasses.HasRepeatSpan(performed, 7, 1));
        Assert.True(RepeatPasses.HasRepeatSpan(performed, 0, 8));
        Assert.True(RepeatPasses.HasRepeatSpan(performed, 2, 1));
    }

    [Fact]
    public void PerformedStartForPassMapsPartKToOccurrenceK()
    {
        var performed = Bars(47, 48, 49, 50, 48, 49, 50, 51);

        Assert.Equal(1.0, RepeatPasses.PerformedStartForPass(performed, 48, 1));
        Assert.Equal(4.0, RepeatPasses.PerformedStartForPass(performed, 48, 2));
        Assert.True(double.IsNaN(RepeatPasses.PerformedStartForPass(performed, 48, 3)));
        Assert.Equal(7.0, RepeatPasses.PerformedStartForPass(performed, 51, 1));
        Assert.True(double.IsNaN(RepeatPasses.PerformedStartForPass(performed, 51, 2)));
    }

    [Fact]
    public void PerformedRangeForPassKeepsEachPlaysOwnLength()
    {
        // Volta: pass 1 plays 48-49-50, pass 2 plays 48-49-51.
        var performed = Bars(48, 49, 50, 48, 49, 51);

        Assert.Equal((0.0, 3.0), RepeatPasses.PerformedRangeForPass(performed, 48, 3, 1));
        Assert.Equal((3.0, 3.0), RepeatPasses.PerformedRangeForPass(performed, 48, 3, 2));
        Assert.Equal((double.NaN, 0.0), RepeatPasses.PerformedRangeForPass(performed, 48, 3, 3));
    }
}
