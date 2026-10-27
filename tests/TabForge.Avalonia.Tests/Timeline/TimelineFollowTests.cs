using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline following the playhead sideways: a seek to a bar outside the view scrolls the view so the playhead is inside it, and the
///     view-advance thresholds of ArrangementFollowRules.
/// Does not own: the seek sources (score, Sections panel, skip buttons) or the playhead drawing (TimelineView, TimelinePlayheadLayer).
/// </summary>
public class TimelineFollowTests
{
    [AvaloniaFact]
    public void SeekingToAFarBarScrollsTheTimelineSoThePlayheadIsInView()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        var timeline = view.Timeline;
        timeline.ZoomFactor = 8;
        window.UpdateLayout();
        var sideways = timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.Equal(0, sideways.Offset.X, 3);

        timeline.SetPlayhead(timeline.BarCount - 2, 0.5);
        window.UpdateLayout();
        TimelineFollowScroll.FollowPlayhead(sideways, timeline);
        window.UpdateLayout();

        var x = timeline.PlayheadX();
        Assert.True(sideways.Offset.X > 0);
        Assert.InRange(x, sideways.Offset.X, sideways.Offset.X + sideways.Viewport.Width);
    }

    [AvaloniaFact]
    public void ClickingAnEdgeBarAtTheRightPagesHalfTheVisibleWidth()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        var timeline = view.Timeline;
        timeline.ZoomFactor = 8;
        window.UpdateLayout();
        var sideways = timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        var viewport = sideways.Viewport.Width;
        var clickedBar = timeline.BarAtX(viewport - 1);
        var before = sideways.Offset.X;

        TimelineFollowScroll.PageForEdgeBar(sideways, timeline, clickedBar);
        window.UpdateLayout();

        Assert.Equal(before + viewport * ArrangementFollowRules.HalfPageShare, sideways.Offset.X, 1);
    }
}
