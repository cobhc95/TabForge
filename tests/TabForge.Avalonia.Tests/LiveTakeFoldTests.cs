using System.Linq;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Features.Recording.Model;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the live (while recording) take's folded geometry and its lane footprint: one box before the first wrap, one band per lap
// folded onto the loop range after it, the current lap growing from the loop start and never past the playhead.
// Does not own: the drawing (TimelineLiveTakeLayer) or the landed takes (RecordedTakes).
// Tests: this class.
public class LiveTakeFoldTests
{
    private static void AssertBand(LiveTakeBand band, double start, double end, int row, int pass)
    {
        Assert.Equal(start, band.StartSec, 9);
        Assert.Equal(end, band.EndSec, 9);
        Assert.Equal(row, band.Row);
        Assert.Equal(pass, band.Pass);
    }

    [Fact]
    public void BeforeTheFirstWrapTheLiveTakeIsOneBandFromTheRecordStartToTheFront()
    {
        var band = Assert.Single(LiveTakeFold.Bands(startSec: 1, nowSec: 3, loop: (2, 4)));

        AssertBand(band, 1, 3, 0, 1);
    }

    [Fact]
    public void OneAndAHalfLapsFoldTheFirstLapAndGrowThePartialSecondFromTheLoopStart()
    {
        var bands = LiveTakeFold.Bands(startSec: 1, nowSec: 5, loop: (2, 4));

        Assert.Collection(bands,
            band => AssertBand(band, 1, 4, 0, 1),
            band => AssertBand(band, 2, 3, 1, 2));
    }

    [Fact]
    public void ThreeAndAFifthLapsStackOneLiveLanePerLapInsideTheLoopRange()
    {
        var bands = LiveTakeFold.Bands(startSec: 1, nowSec: 8.4, loop: (2, 4));

        Assert.Equal(4, bands.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, bands.Select(band => band.Row).ToArray());
        AssertBand(bands[0], 1, 4, 0, 1);
        AssertBand(bands[1], 2, 4, 1, 2);
        AssertBand(bands[2], 2, 4, 2, 3);
        AssertBand(bands[^1], 2, 2.4, 3, 4);
    }

    [Fact]
    public void NoBandEverEndsPastThePlayhead()
    {
        foreach (var now in new[] { 3.0, 5.0, 8.4, 12.0 })
        {
            var bands = LiveTakeFold.Bands(startSec: 1, nowSec: now, loop: (2, 4));
            Assert.All(bands, band => Assert.True(band.EndSec <= now + 1e-9, $"{band.EndSec} > {now}"));
            Assert.All(bands, band => Assert.True(band.EndSec > band.StartSec, $"{band.StartSec}..{band.EndSec} is empty"));
        }
    }

    [Fact]
    public void RecordingFrom47WithLoop48To51IsOneBandBeforeTheWrapAndThreeFoldedBandsAfterTwoWraps()
    {
        var before = Assert.Single(LiveTakeFold.Bands(startSec: 47, nowSec: 50, loop: (48, 51)));
        AssertBand(before, 47, 50, 0, 1);

        var after = LiveTakeFold.Bands(startSec: 47, nowSec: 57, loop: (48, 51));
        Assert.Collection(after,
            band => AssertBand(band, 47, 51, 0, 1),   // lap 1 includes the 47..48 pre-roll
            band => AssertBand(band, 48, 51, 1, 2),
            band => AssertBand(band, 48, 51, 2, 3));
    }

    [Fact]
    public void APartialSecondLapGrowsOnItsOwnLiveLaneFromTheLoopStart()
    {
        var bands = LiveTakeFold.Bands(startSec: 47, nowSec: 52, loop: (48, 51));

        Assert.Collection(bands,
            band => AssertBand(band, 47, 51, 0, 1),
            band => AssertBand(band, 48, 49, 1, 2));
    }

    [Fact]
    public void TheLiveLaneFootprintIsTheRecordStartToTheLoopEndNotTheGrowingFront()
    {
        var track = new TrackModel { Name = "Audio", Kind = TrackKind.Audio };
        track.AudioClips.Add(new AudioClip { File = "right.wav", Name = "right", StartSec = 5, SourceLengthSec = 5, FileLengthSec = 5, Lane = 0 });
        var takes = new LiveTakes { Loop = (2d, 4d) };

        Assert.True(takes.Frame(track, 1, 0.5f, midi: false));   // the take starts at 1 s, then its front grows past the loop end
        Assert.True(takes.Frame(track, 6, 0.5f, midi: false));
        var take = Assert.Single(takes.All);
        Assert.Equal(6, take.EndSec, 6);
        Assert.Equal(4, take.FootprintEndSec, 6);   // start..loop end, not the 1..6 front
        Assert.Equal(0, take.Lane);                 // the clip right of the loop end does not push the take down
    }
}
