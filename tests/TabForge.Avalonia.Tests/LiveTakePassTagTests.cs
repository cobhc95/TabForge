using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the live recording box of a take through a repeat: the "pass k" tag of a take that plays one pass of a repeat, the fold it keeps as the
//   take grows into the next pass, and that the live boxes sit exactly where the landed clip draws.
// Does not own: the fold (RepeatViewTests, RepeatFold) or the landing (RepeatTakeLandingTests).
// Tests: this class.
public sealed class LiveTakePassTagTests
{
    private static (SongDocument Song, TrackModel Track, PerformedBar[] Performed) Setup(int repeatCount)
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        if (repeatCount > 1)
        {
            song.Project.Tracks[0].Measures[1].RepeatStart = true;
            song.Project.Tracks[0].Measures[2].RepeatEnd = true;
            song.Project.Tracks[0].Measures[2].RepeatCount = repeatCount;
        }
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        return (song, track, SongExtent.Timing(song.Project).Performed);
    }

    private static (TimelineView View, LiveTakes Takes) Live(SongDocument song, PerformedBar[] performed)
    {
        var view = new TimelineView(song.Project);
        var takes = new LiveTakes { RepeatPerformed = performed };
        view.UseLiveTakes(takes);
        takes.LaneCreated = _ => view.Reload(song.Project);
        return (view, takes);
    }

    private static RepeatTakeLandingTests.TextRecorder DrawLive(TimelineView view)
    {
        var drawn = new RepeatTakeLandingTests.TextRecorder();
        view.LiveTakeLayer.Draw(drawn, light: false, view.BarsWidth + 100);
        return drawn;
    }

    [AvaloniaFact]
    public void ATakeStartedInPassTwoOfAThreePassRepeatShowsPassTwo()
    {
        var (song, track, performed) = Setup(3);
        var (view, takes) = Live(song, performed);
        var start = RepeatPasses.PerformedStartForPass(performed, 1, 2);
        Assert.True(takes.Frame(track, start, 0.2f, midi: false));
        takes.Frame(track, start + 0.4, 0.2f, midi: false);

        var drawn = DrawLive(view);
        Assert.Contains("pass 2", drawn.Texts);
        Assert.DoesNotContain("pass 3", drawn.Texts);
    }

    [AvaloniaFact]
    public void CrossingIntoPassThreeContinuesAsTheNextSliceAndMatchesTheLandedClip()
    {
        var (song, track, performed) = Setup(3);
        var (view, takes) = Live(song, performed);
        var start = RepeatPasses.PerformedStartForPass(performed, 1, 2);
        var end = RepeatPasses.PerformedStartForPass(performed, 1, 3) + 0.5;
        takes.Frame(track, start, 0.2f, midi: false);
        takes.Frame(track, start + 0.4, 0.2f, midi: false);
        Assert.Contains("pass 2", DrawLive(view).Texts);
        takes.Frame(track, end, 0.2f, midi: false);

        var drawn = DrawLive(view);
        var live = drawn.Rects.Where(r => r.Fill).Select(r => (Left: Math.Round(r.X, 2), Right: Math.Round(r.X + r.Width, 2))).OrderBy(r => r.Left).ThenBy(r => r.Right).ToList();
        Assert.DoesNotContain("pass 2", drawn.Texts);   // pass 2 and 3 are now both covered: stacked parts, numbered 2 and 3
        Assert.Contains("2", drawn.Texts);
        Assert.Contains("3", drawn.Texts);

        MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", start, end - start);
        var clip = Assert.Single(track.AudioClips);
        var slices = TimelineAudioClips.FoldOf(view, clip)!;
        var landed = slices.Select(s => TimelineAudioClips.SliceRect(view, s, 0)).Select(r => (Left: Math.Round(r.X, 2), Right: Math.Round(r.Right, 2))).OrderBy(r => r.Left).ThenBy(r => r.Right).ToList();
        Assert.Equal(landed, live);
    }

    [AvaloniaFact]
    public void ALiveBoxMatchesTheLandedClipForAOnePassTake()
    {
        var (song, track, performed) = Setup(3);
        var (view, takes) = Live(song, performed);
        var start = RepeatPasses.PerformedStartForPass(performed, 1, 2);
        var end = start + 0.6;
        takes.Frame(track, start, 0.2f, midi: false);
        takes.Frame(track, end, 0.2f, midi: false);
        var live = DrawLive(view).Rects.Where(r => r.Fill).Select(r => (Left: Math.Round(r.X, 2), Right: Math.Round(r.X + r.Width, 2))).ToList();

        MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", start, end - start);
        var slices = TimelineAudioClips.FoldOf(view, Assert.Single(track.AudioClips))!;
        var landed = slices.Select(s => TimelineAudioClips.SliceRect(view, s, 0)).Select(r => (Left: Math.Round(r.X, 2), Right: Math.Round(r.Right, 2))).ToList();
        Assert.Equal(landed, live);
    }

    [AvaloniaFact]
    public void ARegionPlayedOnceHasNoPassTag()
    {
        var (song, track, performed) = Setup(1);
        var (view, takes) = Live(song, performed);
        takes.Frame(track, 0.5, 0.2f, midi: false);
        takes.Frame(track, 1.5, 0.2f, midi: false);

        var drawn = DrawLive(view);
        Assert.DoesNotContain(drawn.Texts, text => text.StartsWith("pass ", StringComparison.Ordinal));
    }
}
