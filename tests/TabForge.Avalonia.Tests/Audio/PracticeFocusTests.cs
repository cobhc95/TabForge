using System;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Focus Track's behaviour: off changes nothing; the selected track is raised by decibels, percent or to a level; every other track is lowered like the
//   boost by default, or left alone, lowered or raised by its group's choice; the selected track is never lowered; the ready-made templates; the engine levels
//   follow it; the fader and saved song are untouched.
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

    private static FocusTrackSettings WithOthers(FocusBoostMode mode, double amount, FocusOther other, SongProject project)
    {
        var rules = Rules(mode, amount);
        rules.Others[MixerGroups.GroupOf(project, project.Tracks[0])] = other;
        return rules;
    }

    [Fact]
    public void OffChangesNothing()
    {
        var project = Song();
        Assert.False(_focus.Set(false, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6)));
        Assert.False(_focus.IsOn);
        Assert.Equal(90, _focus.Scale(project.Tracks[1], 90));
    }

    [Fact]
    public void ByDefaultEveryOtherTrackIsLoweredByTheAmountTheSelectedOneIsRaised()
    {
        var project = Song(first: 50, second: 100);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6));
        Assert.InRange(_focus.Scale(project.Tracks[0], 50), 99, 101);    // +6 dB is about twice the level
        Assert.InRange(_focus.Scale(project.Tracks[1], 100), 49, 51);    // the others go down 6 dB
    }

    [Fact]
    public void PercentLowersTheOthersByTheSamePercentage()
    {
        var project = Song(first: 60, second: 100);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Percent, 20));
        Assert.Equal(72, _focus.Scale(project.Tracks[0], 60));    // 20% louder
        Assert.Equal(80, _focus.Scale(project.Tracks[1], 100));   // 20% quieter
    }

    [Fact]
    public void ToLevelPlaysAtThatLevelAndTheOthersFallByTheSameChangeInLoudness()
    {
        var project = Song(first: 50, second: 100);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.ToLevel, 100));   // twice its level
        Assert.Equal(100, _focus.Scale(project.Tracks[0], 50));
        Assert.InRange(_focus.Scale(project.Tracks[1], 100), 49, 51);
    }

    [Fact]
    public void PastTheLoudestLevelTheSelectedTrackStopsAt127()
    {
        var project = Song(first: 100, second: 100);
        _focus.Set(true, project, project.Tracks[0], Rules(FocusBoostMode.Decibels, 6));
        Assert.Equal(127, _focus.Scale(project.Tracks[0], 100));
        Assert.InRange(_focus.Scale(project.Tracks[1], 100), 49, 51);
    }

    [Fact]
    public void AGroupLeftAsItIsKeepsItsLevelAndTheSelectedTrackKeepsItsBoost()
    {
        var project = Song(first: 60, second: 100);   // both in the same group
        var rules = WithOthers(FocusBoostMode.Percent, 20, new FocusOther(FocusOtherMode.LeaveAlone), project);
        _focus.Set(true, project, project.Tracks[0], rules);
        Assert.Equal(72, _focus.Scale(project.Tracks[0], 60));
        Assert.Equal(100, _focus.Scale(project.Tracks[1], 100));
    }

    [Fact]
    public void AGroupCanBeLoweredOrRaisedByAnAmountOfItsOwn()
    {
        var project = Song(first: 60, second: 50);
        _focus.Set(true, project, project.Tracks[0], WithOthers(FocusBoostMode.Percent, 20, new FocusOther(FocusOtherMode.Lower, new FocusBoost(FocusBoostMode.Decibels, 6)), project));
        Assert.InRange(_focus.Scale(project.Tracks[1], 50), 24, 26);
        _focus.Set(true, project, project.Tracks[0], WithOthers(FocusBoostMode.Percent, 20, new FocusOther(FocusOtherMode.Raise, new FocusBoost(FocusBoostMode.Percent, 50)), project));
        Assert.Equal(75, _focus.Scale(project.Tracks[1], 50));
        Assert.Equal(72, _focus.Scale(project.Tracks[0], 60));   // the selected track is the boosted one either way
    }

    [Fact]
    public void TheSelectedTrackIsNeverLoweredWhateverItsGroupSays()
    {
        var project = Song(first: 60, second: 60);
        var rules = WithOthers(FocusBoostMode.Decibels, 6, new FocusOther(FocusOtherMode.Lower, new FocusBoost(FocusBoostMode.Percent, 100)), project);   // silence the group
        _focus.Set(true, project, project.Tracks[0], rules);
        Assert.True(_focus.Scale(project.Tracks[0], 60) >= 60);
        Assert.Equal(0, _focus.Scale(project.Tracks[1], 60));
    }

    [Fact]
    public void TheReadyMadeTemplatesSayWhatTheyDo()
    {
        Assert.All(FocusTrackSettings.Groups, g => Assert.Equal(FocusOtherMode.SameAsBoost, FocusTrackSettings.ForPreset(FocusTrackSettings.Spotlight)[g].Mode));
        var keep = FocusTrackSettings.ForPreset(FocusTrackSettings.KeepRhythm);
        Assert.Equal(FocusOtherMode.LeaveAlone, keep[FocusTrackSettings.Drums].Mode);
        Assert.Equal(FocusOtherMode.LeaveAlone, keep[FocusTrackSettings.Basses].Mode);
        Assert.Equal(FocusOtherMode.SameAsBoost, keep[FocusTrackSettings.Guitars].Mode);
        Assert.All(FocusTrackSettings.Groups, g => Assert.Equal(FocusOtherMode.LeaveAlone, FocusTrackSettings.ForPreset(FocusTrackSettings.OnlyBoost)[g].Mode));
        Assert.Equal(FocusOtherMode.Raise, FocusTrackSettings.ForPreset(FocusTrackSettings.RhythmUp)[FocusTrackSettings.Drums].Mode);
    }

    [Fact]
    public void AGroupOfTheUsersOwnFollowsOtherInstruments()
    {
        var rules = new FocusTrackSettings();
        rules.Others[FocusTrackSettings.OtherInstruments] = new FocusOther(FocusOtherMode.LeaveAlone);
        Assert.Equal(FocusOtherMode.LeaveAlone, rules.OtherFor("My strings").Mode);
        Assert.Equal(FocusOtherMode.SameAsBoost, new FocusTrackSettings().OtherFor("My strings").Mode);
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
        Assert.Equal(60, new FocusBoost(FocusBoostMode.ToLevel, 80).ApplyLower(60));    // a cap never raises
        Assert.Equal(80, new FocusBoost(FocusBoostMode.ToLevel, 80).ApplyLower(110));
        Assert.Equal(0, new FocusBoost(FocusBoostMode.Percent, 500).ApplyLower(100));
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
