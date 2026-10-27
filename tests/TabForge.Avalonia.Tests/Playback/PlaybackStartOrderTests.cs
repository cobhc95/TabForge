using System;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: playback starts follow the full performed order (repeats, voltas and D.S./D.C. jumps after the start included).
/// Does not own: the repeat/jump rules themselves (SelfTestPlaybackOrderSpec).
/// Tests: this class.
/// </summary>
public class PlaybackStartOrderTests
{
    private static readonly SongProject Demo = FullDemoSongFactory.Create();

    private static int[] Bars(int first, int last) => Enumerable.Range(first, last - first + 1).ToArray();

    private static int[] Join(params int[][] parts) => parts.SelectMany(part => part).ToArray();

    private static int[] Start(SongProject song, int startBar, int? occurrence = null) =>
        PlaybackOrder.Build(song, new PlaybackOptions { StartBar = startBar, StartOccurrence = occurrence }).ToArray();

    private static int[] DemoStart(int oneBasedStart) =>
        Start(Demo, oneBasedStart - 1).Select(bar => bar + 1).ToArray();

    private static SongProject LinearSong(int bars)
    {
        var song = new SongProject { Tempo = 120 };
        song.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(bars) });
        return song;
    }

    [Fact]
    public void StartingFromTheFirstBarKeepsTheWholeDemoOrder()
    {
        Assert.Equal(FullDemoSongFactory.ExpectedPerformedBars.ToArray(), DemoStart(1));
    }

    [Fact]
    public void StartingFromBar20FollowsTheRepeatIntoTheNextPass()
    {
        var expected = Join(
            new[] { 20, 21, 22, 19, 20, 21, 23 },
            Bars(24, 47),
            new[] { 48, 49, 48, 49, 48, 50 },
            Bars(51, 117),
            Bars(36, 39),
            Bars(118, 144));
        Assert.Equal(expected, DemoStart(20));
    }

    [Fact]
    public void StartingFromBar37FollowsTheDsAndTheRepeatsAfterIt()
    {
        var expected = Join(
            Bars(37, 47),
            new[] { 48, 49, 48, 49, 48, 50 },
            Bars(51, 117),
            Bars(36, 39),
            Bars(118, 144));
        Assert.Equal(expected, DemoStart(37));
    }

    [Fact]
    public void StartingFromBar50StartsAtThePerformanceAfterTheRepeat()
    {
        var expected = Join(new[] { 50 }, Bars(51, 117), Bars(36, 39), Bars(118, 144));
        Assert.Equal(expected, DemoStart(50));
    }

    [Fact]
    public void StartingFromBar117FollowsTheDsAlCodaBackToTheSegno()
    {
        var expected = Join(new[] { 117 }, Bars(36, 39), Bars(118, 144));
        Assert.Equal(expected, DemoStart(117));
    }

    [Fact]
    public void StartingFromBar140PlaysToTheEndWithNoJumpLeft()
    {
        Assert.Equal(Bars(140, 144), DemoStart(140));
    }

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
    public void ACloseBarWithEndings123PlaysAsManyPassesAsTheLaterEndingNames()   // black_widow_2.gp3 bars 27-31: TuxGuitar 26..29 x3, 26..28, 30
    {
        var song = Repeat(8, 2, 5, 2, (5, 7), (6, 8));
        Assert.Equal(Join(new[] { 0, 1 }, Bars(2, 5), Bars(2, 5), Bars(2, 5), Bars(2, 4), new[] { 6, 7 }), Start(song, 0));
    }

    [Fact]
    public void ALastEndingBracketOver1And2ThatEndsAtTheCloseGivesAThirdPassWithoutIt()   // aces_high_2.gp3 bars 125-133: TuxGuitar 125..132 x2, 125..130, 133
    {
        var song = Repeat(8, 1, 4, 2, (3, 3));
        Assert.Equal(Join(new[] { 0 }, Bars(1, 4), Bars(1, 4), Bars(1, 2), new[] { 5, 6, 7 }), Start(song, 0));
    }

    [Fact]
    public void ACloseOnItsOwnEnding123BarWithNoLaterEndingGivesAFourthPassWithoutIt()   // genghis_khan.gp3 bars 12-14: TuxGuitar 12, 13 x3, 12, 14
    {
        var song = Repeat(6, 2, 3, 2, (3, 7));
        Assert.Equal(new[] { 0, 1, 2, 3, 2, 3, 2, 3, 2, 4, 5 }, Start(song, 0));
    }

    [Fact]
    public void AnEndingBeforeAPlainCloseStillRunsTheRepeatUntilTheLastEnding()   // running_free.gp3 bars 63-67
    {
        var song = Repeat(7, 1, 4, 2, (3, 7), (5, 8));
        Assert.Equal(Join(new[] { 0 }, Bars(1, 4), Bars(1, 4), Bars(1, 4), new[] { 1, 2, 5, 6 }), Start(song, 0));
    }

    [Fact]
    public void ACloseBarWhoseEndingSkipsThePassDoesNotJumpBack()   // como_estais_amigos.gp3: x4 close with endings 1.2. and 3.
    {
        var song = Repeat(6, 0, 3, 4, (3, 3), (4, 4));
        Assert.Equal(Join(Bars(0, 3), Bars(0, 3), new[] { 0, 1, 2, 4, 5 }), Start(song, 0));
    }

    [Fact]
    public void AStartBarSkippedByAnEndingBeginsAtTheNextPerformedBar()
    {
        var song = LinearSong(4);
        song.Tracks[0].Measures[1].AlternateEndingMask = 1 << 1;   // written bar 2 plays only on pass 2
        Assert.Equal(new[] { 0, 2, 3 }, Start(song, 0));
        Assert.Equal(new[] { 2, 3 }, Start(song, 1));
    }

    [Fact]
    public void AStartBarSkippedByAnEndingStillFollowsTheJumpBack()
    {
        var song = LinearSong(6);
        song.Tracks[0].Measures[1].AlternateEndingMask = 1 << 1;   // written bar 2 is never performed
        song.Tracks[0].Measures[0].Directions = "Fine";
        song.Tracks[0].Measures[4].Directions = "DaCapo";
        Assert.Equal(new[] { 0, 2, 3, 4, 0 }, Start(song, 0));
        Assert.Equal(new[] { 2, 3, 4, 0 }, Start(song, 1));
    }

    [Fact]
    public void AStartBarNoJumpReachesPlaysAloneWhenNothingLaterIsPerformed()
    {
        var song = LinearSong(6);
        song.Tracks[0].Measures[0].Directions = "Fine";
        song.Tracks[0].Measures[2].Directions = "DaCapo";
        Assert.Equal(new[] { 0, 1, 2, 0 }, Start(song, 0));
        Assert.Equal(new[] { 4 }, Start(song, 4));
    }

    [Fact]
    public void ALinearSongIsUnchangedForEveryStart()
    {
        var song = LinearSong(6);
        for (var start = 0; start < 6; start++)
            Assert.Equal(Enumerable.Range(start, 6 - start).ToArray(), Start(song, start));
        Assert.Empty(Start(song, 6));
    }

    [Fact]
    public void ASecondStartOccurrenceKeepsThePassAwareSuffix()
    {
        var song = LinearSong(3);
        song.Tracks[0].Measures[0].RepeatStart = true;
        song.Tracks[0].Measures[1].RepeatEnd = true;
        Assert.Equal(new[] { 0, 1, 0, 1, 2 }, Start(song, 0));
        Assert.Equal(new[] { 0, 1, 2 }, Start(song, 0, 2));
    }

    [Fact]
    public void StartIndexPicksTheRequestedPassOrFallsBackToTheWrittenStart()
    {
        var order = new[] { 0, 1, 0, 1, 2 };
        Assert.Equal(2, PlaybackOrder.StartIndex(order, 0, 2));
        Assert.Equal(0, PlaybackOrder.StartIndex(order, 0, null));
        Assert.Equal(1, PlaybackOrder.StartIndex(order, 1, 1));
        Assert.Equal(0, PlaybackOrder.StartIndex(order, 0, 5));   // no fifth pass: the written-order start
        Assert.Equal(order.Length, PlaybackOrder.StartIndex(order, 9, null));
    }

    [Fact]
    public void TimelineStartIndexMatchesTheRunNearestThePreferredIndex()
    {
        var order = new[] { 0, 1, 2, 0, 1, 2, 3 };
        var timeline = new[] { 0, 1, 2 };
        Assert.Equal(0, PlaybackOrder.TimelineStartIndex(order, timeline, 0));
        Assert.Equal(3, PlaybackOrder.TimelineStartIndex(order, timeline, 3));
        Assert.Equal(3, PlaybackOrder.TimelineStartIndex(order, timeline, 5));
        Assert.Equal(-1, PlaybackOrder.TimelineStartIndex(order, new[] { 9, 9 }, 0));
        Assert.Equal(-1, PlaybackOrder.TimelineStartIndex(order, Array.Empty<int>(), 0));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(117)]
    public void TheDemoScheduleIsTheFreshStartOrderAndReuseRefusesAnEarlierBar(int oneBasedStart)
    {
        var options = new PlaybackOptions { StartBar = oneBasedStart - 1 };
        var timeline = MidiTimelineBuilder.Build(Demo, options);
        var reuse = PlaybackScheduleReuse.Capture(Demo, options, timeline);

        Assert.Equal(PlaybackOrder.Build(Demo, options).ToArray(), timeline.Bars.Select(bar => bar.Bar).ToArray());
        for (var bar = 0; bar < options.StartBar; bar++)
            Assert.False(reuse.TryResolve(Demo, options, timeline, bar, 0, out _),
                $"a seek to bar {bar + 1} must compile its own start order");
    }

    [Fact]
    public void AReusedLinearScheduleAgreesWithAFreshCompile()
    {
        var song = LinearSong(150);
        var options = new PlaybackOptions();
        var timeline = MidiTimelineBuilder.Build(song, options);
        var reuse = PlaybackScheduleReuse.Capture(song, options, timeline);

        foreach (var start in new[] { 19, 116 })   // 1-based bars 20 and 117
        {
            Assert.True(reuse.TryResolve(song, options, timeline, start, 0, out var targetMs));
            var fresh = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = start });
            Assert.Equal(fresh.Bars.Select(bar => bar.Bar).ToArray(), timeline.Bars.Skip(start).Select(bar => bar.Bar).ToArray());
            Assert.Equal(timeline.Bars[start].StartMs, targetMs, 6);
            Assert.Equal(start, fresh.Bars[0].Bar);
        }
    }

    [Fact]
    public void ACloseOnTheLastEndingPassDoesNotJumpBack()
    {
        var song = LinearSong(4);
        var ms = song.Tracks[0].Measures;
        ms[0].RepeatStart = true;
        ms[1].AlternateEndingMask = 1;
        ms[2].AlternateEndingMask = 2;
        ms[3].RepeatEnd = true;
        ms[3].RepeatCount = 2;

        Assert.Equal(new[] { 0, 1, 0, 2, 3 }, Start(song, 0));
    }

    [Fact]
    public void AMidSongStartEntersTheNormalPerformedOrderOfADalSegno()
    {
        var song = LinearSong(6);
        var ms = song.Tracks[0].Measures;
        ms[1].Directions = "Segno";
        ms[3].Directions = "Fine";
        ms[4].Directions = "DalSegno";

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 1, 2, 3 }, Start(song, 0));
        Assert.Equal(new[] { 2, 3, 4, 1, 2, 3 }, Start(song, 2));
    }
}
