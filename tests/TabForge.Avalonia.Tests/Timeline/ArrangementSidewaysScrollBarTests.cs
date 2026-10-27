using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline's footer scroll bar, hosted in the status bar: it is shown while the pane is open, it moves the timeline's offset, and the
///     timeline's offset moves it.
/// Does not own: the wheel and drag scrolling (ArrangementView), the zoom (TimelineView), the status bar's other readouts (StatusBarView).
/// </summary>
public class ArrangementSidewaysScrollBarTests
{
    [AvaloniaFact]
    public void StatusBarScrollBarMovesTheTimelineOffsetAndFollowsIt()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song);
        var status = new StatusBarView();
        status.SetTimelineScrollBar(view.FooterScrollBar);
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(view);
        var window = new Window { Width = 800, Height = 500, Content = root };
        window.Show();
        view.Timeline.ZoomFactor = 8;
        window.UpdateLayout();

        var sideways = view.Timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        var bar = view.FooterScrollBar;

        Assert.True(bar.IsVisible);
        Assert.Equal(ArrangementView.SidewaysBarHeight, bar.Bounds.Height, 3);
        Assert.True(bar.Maximum > 0);
        Assert.Equal(ScrollBarVisibility.Hidden, sideways.HorizontalScrollBarVisibility);

        bar.Value = 50;
        window.UpdateLayout();
        Assert.Equal(50, sideways.Offset.X, 3);

        sideways.Offset = new Vector(80, 0);
        window.UpdateLayout();
        Assert.Equal(80, bar.Value, 3);
    }
}
