using System;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Focus Track's rules: off changes nothing; the selected track is raised by decibels, percent or to a level, for any track or a mixer group; the others
//   drop only when the boost would pass the loudest level; the engine levels follow it; the fader and saved song are untouched.
public sealed class PracticeFocusTests
{
    private readonly PracticeFocus _focus = new();

    private static SongProject Song(int first = 60, int second = 100)
    {
        var project = new SongProject();
        project.Tracks.Add(new TrackModel { Volume = first });
        project.Tracks.Add(new TrackModel { Volume = second });
        return project;
    }

    private static FocusTrackSettings Rules(FocusBoostMode mode, double amount) => new() { Boost = new FocusBoost(mode, amount) };

    [Fact]
    public void OffChangesNothing()
    {
        var project = Song();
        Assert.False(_focus.Set(false, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6)));
        Assert.False(_focus.IsOn);
        Assert.Equal(90, _focus.Scale(project.Tracks[1], 90));
    }

    [Fact]
    public void DecibelsRaiseTheSelectedTrackAndLeaveTheOthersWhenThereIsRoom()
    {
        var project = Song(first: 50);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6));
        Assert.InRange(_focus.Scale(project.Tracks[0], 50), 99, 101);   // +6 dB is about twice the level
        Assert.Equal(100, _focus.Scale(project.Tracks[1], 100));
    }

    [Fact]
    public void PercentRaisesByThatShareOfTheTracksOwnLevel()
    {
        var project = Song(first: 60);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Percent, 50));
        Assert.Equal(90, _focus.Scale(project.Tracks[0], 60));
    }

    [Fact]
    public void ToLevelPlaysAtThatLevelWhateverTheFaderSays()
    {
        var project = Song(first: 40);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.ToLevel, 110));
        Assert.Equal(110, _focus.Scale(project.Tracks[0], 40));
    }

    [Fact]
    public void PastTheLoudestLevelTheOthersMakeUpTheDifference()
    {
        var project = Song(first: 100);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6));   // wants about 199, can reach 127
        Assert.Equal(127, _focus.Scale(project.Tracks[0], 100));
        Assert.True(_focus.Scale(project.Tracks[1], 100) < 100);
    }

    [Fact]
    public void AGroupRuleWinsOverTheDefaultAndOtherGroupsKeepTheDefault()
    {
        var project = Song(first: 50);
        var rules = Rules(FocusBoostMode.Percent, 20);
        rules.Groups[MixerGroups.GroupOf(project, project.Tracks[0])] = new FocusBoost(FocusBoostMode.Percent, 100);
        _focus.Set(true, project, project.Tracks[0], rules);
        Assert.Equal(100, _focus.Scale(project.Tracks[0], 50));   // the group's +100% (twice), not the default +20%
        Assert.Equal(20, rules.Boost.Amount);
    }

    [Fact]
    public void FocusMovesWithTheSelectionAndAnotherSongIsUntouched()
    {
        var project = Song(first: 100, second: 100);
        var other = Song();
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6));
        Assert.True(_focus.Set(true, project, project.Tracks[1], Rules(FocusBoostMode.Decibels, 6)));
        Assert.True(_focus.Scale(project.Tracks[0], 100) < 100);
        Assert.Equal(60, _focus.Scale(other.Tracks[0], 60));   // a track of a song that is not shown
    }

    [Fact]
    public void RefreshFollowsAFaderChange()
    {
        var project = Song(first: 50);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.ToLevel, 100));
        Assert.Equal(100, _focus.Scale(project.Tracks[0], 50));
        project.Tracks[0].Volume = 25;
        Assert.True(_focus.Refresh(project));
        Assert.Equal(100, _focus.Scale(project.Tracks[0], 25));   // still played at level 100: the ratio was worked out again
    }

    [Fact]
    public void NoSelectedTrackChangesNothing()
    {
        var project = Song();
        _focus.Set(true, project, null, Rules(FocusBoostMode.Decibels, 6));
        Assert.False(_focus.IsOn);
    }

    [Fact]
    public void AmountsStayInsideTheirLimits()
    {
        Assert.Equal(FocusBoost.MaxDecibels, new FocusBoost(FocusBoostMode.Decibels, 99).ClampedAmount);
        Assert.Equal(0, new FocusBoost(FocusBoostMode.Percent, -5).ClampedAmount);
        Assert.Equal(FocusBoost.MaxLevel, new FocusBoost(FocusBoostMode.ToLevel, 500).ClampedAmount);
    }

    [Fact]
    public void EngineLevelsFollowTheFocusButTheFaderDoesNot()
    {
        var project = Song(first: 100, second: 100);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6));
        var levels = EngineMixLevels.Levels(project, 100, false, _focus);
        Assert.Equal(127, levels(project.Tracks[0]).Volume);
        Assert.True(levels(project.Tracks[1]).Volume < MixerGroups.Volume(project, project.Tracks[1]));
        Assert.Equal(100, project.Tracks[1].Volume);   // the fader and the saved song are untouched
        Assert.Equal(100, EngineMixLevels.Levels(project, 100)(project.Tracks[1]).Volume);   // without Focus Track the level is the plain one
    }
}
