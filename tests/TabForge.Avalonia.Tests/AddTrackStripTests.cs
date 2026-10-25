using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the "Add track" strip as the arrangement's own row under the track list and the timeline: its height, its visibility from the View setting
/// and compact mode, and the track it adds.
/// Does not own: the strip's drawing (AddTrackStrip), the full-width layout and the rows' alignment (TrackListOwnerLayoutTests) or the Add track question (PromptWindow).
/// Tests: TabForge.Avalonia.Tests.AddTrackStripTests.
/// </summary>
public class AddTrackStripTests
{
    [AvaloniaFact]
    public void TheStripShowsItsHeightUnderTheRowsAndTheViewSettingHidesIt()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        document.MarkClean();
        var view = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        Assert.True(view.AddTrackBar.IsVisible);
        Assert.Equal(AddTrackLane.Height, view.AddTrackBarHeight);
        Assert.Equal(AddTrackLane.Height, view.AddTrackBar.Bounds.Height, 0.5);

        view.AddTrackLaneShown = false;
        window.UpdateLayout();
        Assert.False(view.AddTrackBar.IsVisible);
        Assert.Equal(0, view.AddTrackBarHeight);
        view.AddTrackLaneShown = true;
    }

    [AvaloniaFact]
    public void PressingTheTimelineSideOfTheStripAsksForATrackLikeTheListSide()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        document.MarkClean();
        var view = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        var strip = view.AddTrackBar;
        var presses = 0;
        strip.Clicked += () => presses++;

        var listSide = strip.TranslatePoint(new Point(10, strip.Bounds.Height / 2), window)!.Value;
        var timelineSide = strip.TranslatePoint(new Point(strip.Bounds.Width - 20, strip.Bounds.Height / 2), window)!.Value;
        window.MouseDown(listSide, MouseButton.Left);
        window.MouseUp(listSide, MouseButton.Left);
        window.MouseDown(timelineSide, MouseButton.Left);
        window.MouseUp(timelineSide, MouseButton.Left);

        Assert.Equal(2, presses);
        window.Close();
    }

    [AvaloniaFact]
    public void AddingATrackRelaysOutTheTimelineAndItsHeader()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        document.MarkClean();
        var view = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        var tracksBefore = document.Project.Tracks.Count;

        MainWindowTrackMenu.AddTrack(document, TrackKind.Bass);
        window.UpdateLayout();

        // The timeline gains the track's row, and its header always shows the same height as the timeline.
        Assert.Equal(tracksBefore + 1, document.Project.Tracks.Count);
        Assert.Equal(tracksBefore + 1, view.Timeline.RowCount);
        Assert.Equal(view.Timeline.Height, view.Header.Bounds.Height, 1);
    }
}
