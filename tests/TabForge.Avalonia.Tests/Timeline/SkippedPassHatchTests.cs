using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the demo song's first repeat (bars 19-21, then [1.] bar 22 and [2.] bar 23, 1-based) and the hatch over the empty pass lane of a clip folded on it.
// Does not own: the fold itself (RepeatEndingFoldTests) or the colours of the hatch.
// Tests: this class.
public sealed class SkippedPassHatchTests
{
    private const int FirstRepeatBar = 18, FirstEndingBar = 21, SecondEndingBar = 22;   // 0-based

    private static (TimelineView View, System.Collections.Generic.IReadOnlyList<RepeatSlice> Slices) FoldedDemoClip()
    {
        var project = FullDemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(track);
        var performed = SongExtent.Timing(project).Performed;
        var start = performed.First(b => b.Bar == FirstRepeatBar).StartMs / 1000.0;
        var end = performed.Last(b => b.Bar == SecondEndingBar).EndMs / 1000.0;
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = start, SourceLengthSec = end - start, FileLengthSec = end - start };
        track.AudioClips.Add(clip);
        var view = new TimelineView(project);
        return (view, TimelineAudioClips.FoldOf(view, clip)!);
    }

    [Fact]
    public void TheDemoSongsFirstRepeatIsBars19To21WithEnding1OnBar22AndEnding2OnBar23()
    {
        var project = FullDemoSongFactory.Create();
        var performed = SongExtent.Timing(project).Performed;

        Assert.Equal(new[] { new RepeatRegion(FirstRepeatBar, FirstEndingBar - 1, 2) }, RepeatRegions.Of(performed).Where(r => r.StartBar == FirstRepeatBar));
        var endings = RepeatEndings.Of(performed);
        Assert.Equal(new RepeatEnding(1, 2, FirstRepeatBar), endings[FirstEndingBar]);
        Assert.Equal(new RepeatEnding(2, 2, FirstRepeatBar), endings[SecondEndingBar]);
        var order = performed.SkipWhile(b => b.Bar != FirstRepeatBar).Take(8).Select(b => b.Bar + 1).ToArray();
        Assert.Equal(new[] { 19, 20, 21, 22, 19, 20, 21, 23 }, order);
    }

    [AvaloniaFact]
    public void ThePassThatSkipsAnEndingBarGetsTheHatchOnItsEmptyRowOnly()
    {
        var (view, slices) = FoldedDemoClip();
        var hatched = TimelineAudioClips.SkippedPassRects(view, slices, 0);

        Assert.Equal(2, hatched.Count);
        var inner = ClipLanes.AudioLaneHeight - 4;
        var firstEnding = hatched.Single(r => r.X >= view.BarX(FirstEndingBar) - 0.5 && r.Right <= view.BarX(FirstEndingBar + 1) + 0.5);   // pass 2 skips [1.]
        var secondEnding = hatched.Single(r => r.X >= view.BarX(SecondEndingBar) - 0.5 && r.Right <= view.BarX(SecondEndingBar + 1) + 0.5);   // pass 1 skips [2.]
        Assert.True(firstEnding.Y > secondEnding.Y);
        Assert.Equal(inner / 2, firstEnding.Height, 3);
        Assert.Equal(view.BarX(FirstEndingBar + 1) - view.BarX(FirstEndingBar), firstEnding.Width, 3);
    }

    [AvaloniaFact]
    public void AClipOnPlainBarsHasNoHatch()
    {
        var project = FullDemoSongFactory.Create();
        var view = new TimelineView(project);
        var slices = new[] { new RepeatSlice(0, 1, 0, 1, 0, 1) };

        Assert.Empty(TimelineAudioClips.SkippedPassRects(view, slices, 0));
    }
}
