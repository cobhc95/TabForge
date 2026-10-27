using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: where a new tab opens (the new-tab position setting) and the notation and track grouping a new song takes from the settings.</summary>
public sealed class MainWindowNewTabTests
{
    private static MainWindowTabs Tabs(AppSettings settings, out SongDocument first, out SongDocument second)
    {
        first = MainWindowTabs.NewSong();
        second = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(first, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false), settings: settings);
        tabs.Open(second);
        tabs.Switch(0);
        return tabs;
    }

    [Fact]
    public void NewTabOpensAfterTheShownTabByDefault()
    {
        var tabs = Tabs(new AppSettings(), out var first, out var second);
        tabs.New();
        Assert.Equal(3, tabs.Songs.Count);
        Assert.Equal(1, tabs.ActiveIndex);
        Assert.Same(first, tabs.Songs[0]);
        Assert.Same(second, tabs.Songs[2]);
    }

    [Fact]
    public void NewTabOpensAtTheEndWhenTheSettingSaysSo()
    {
        var settings = new AppSettings();
        settings.Tabs.NewTabPosition = NewTabPositions.AtEnd;
        var tabs = Tabs(settings, out var first, out var second);
        tabs.New();
        Assert.Equal(3, tabs.Songs.Count);
        Assert.Equal(2, tabs.ActiveIndex);
        Assert.Same(first, tabs.Songs[0]);
        Assert.Same(second, tabs.Songs[1]);
    }

    [Fact]
    public void NewSongTakesThePreferredNotation()
    {
        var settings = new AppSettings { NotationPreferenceSet = true, Notation = nameof(NotationMode.StaffOnly) };
        var tabs = Tabs(settings, out _, out _);
        tabs.New();
        Assert.Equal(NotationMode.StaffOnly, tabs.Active.Notation);
    }

    [Fact]
    public void NewSongShowsGroupsInItsTrackListWhenTheSettingSaysSo()
    {
        var settings = new AppSettings();
        settings.Timeline.ShowGroupsInNewSongs = true;
        var tabs = Tabs(settings, out _, out _);
        tabs.New();
        Assert.True(tabs.Active.Project.Mixer.ShowGroupsInTrackList);
    }
}
