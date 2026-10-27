using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: performed bar order for edge-case repeat/ending layouts (gapped endings, nested-adjacent repeats, pickup bars, lone second-ending closes).
/// Does not own: the repeat/jump rules themselves (SelfTestPlaybackOrderSpec) or the start-bar rules (PlaybackStartOrderTests).
/// Tests: this class.
/// </summary>
public class PlaybackOrderEdgeTests
{
    private static SongProject LinearSong(int bars)
    {
        var song = new SongProject { Tempo = 120 };
        song.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(bars) });
        return song;
    }

    private static int[] Performed(SongProject song) =>
        PlaybackOrder.Build(song, new PlaybackOptions { StartBar = 0 }).Select(bar => bar + 1).ToArray();

    private static SongProject Repeat(int bars, int open, int close, int count, params (int Bar, int Mask)[] endings)
    {
        var song = LinearSong(bars);
        var ms = song.Tracks[0].Measures;
        ms[open].RepeatStart = true;
        ms[close].RepeatEnd = true;
        ms[close].RepeatCount = count;
        foreach (var (bar, mask) in endings) ms[bar].AlternateEndingMask = mask;
        return song;
    }

    [Fact]
    public void AGappedEndingPairWithACloseCountOfThreeFollowsTheEndingsOnEachPass()
    {
        // T1: bars 1-4, open 1, bar 3 [1.3.], bar 4 [2.] and close x3 on bar 4, nothing after.
        var song = Repeat(4, 0, 3, 3, (2, 5), (3, 2));
        Assert.Equal(new[] { 1, 2, 3, 1, 2, 4, 1, 2, 3 }, Performed(song));
    }

    [Fact]
    public void ANestedAdjacentRepeatAfterACloseKeepsEachEndingOnItsOwnPass()
    {
        // T2: bars 1-6; repeat 1-2 x2, then repeat from bar 3 closing on bar 4 x2 with [1.] on 4 and [2.] on 5; bar 6 plain.
        var song = LinearSong(6);
        var ms = song.Tracks[0].Measures;
        ms[0].RepeatStart = true;
        ms[1].RepeatEnd = true;
        ms[1].RepeatCount = 2;
        ms[2].RepeatStart = true;
        ms[3].RepeatEnd = true;
        ms[3].RepeatCount = 2;
        ms[3].AlternateEndingMask = 1;
        ms[4].AlternateEndingMask = 2;
        Assert.Equal(new[] { 1, 2, 1, 2, 3, 4, 3, 5, 6 }, Performed(song));
    }

    [Fact]
    public void APickupBarBeforeARepeatIsPlayedOnceAndTheRepeatRunsFromBarTwo()
    {
        // T3: bar 1 is a pickup, repeat opens at bar 2, closes on bar 3 x2, bar 4 plain.
        var song = Repeat(4, 1, 2, 2);
        Assert.Equal(new[] { 1, 2, 3, 2, 3, 4 }, Performed(song));
    }

    [Fact]
    public void ALoneSecondEndingCloseGetsOneMorePassThatSkipsIt()
    {
        // T4: open 1, bar 2 plain, bar 3 [2.] with close x2, bar 4 plain. TuxGuitar (phanton_of_the_opera.gp3, one_2.gp3,
        // suicide_and_redemption_5.gp4): pass 1 skips the [2.] bar but still goes back, pass 2 plays it and goes back, pass 3 skips it.
        var song = Repeat(4, 0, 2, 2, (2, 2));
        Assert.Equal(new[] { 1, 2, 1, 2, 3, 1, 2, 4 }, Performed(song));
    }
}
