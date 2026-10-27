using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a song shorter than the pane: the timeline starts next to the track list and is at least as wide as the viewer, so its empty grid fills the pane.
/// Does not own: the bars' geometry (TimelineView), the scroll bar (ArrangementSidewaysScrollBarTests).
/// </summary>
public class ArrangementShortSongTests
{
    [AvaloniaFact]
    public void FourBarSongStartsNextToTheTrackListAndFillsTheViewport()
    {
        var project = DemoSongFactory.Create();
        foreach (var track in project.Tracks)
            while (track.Measures.Count > 4) track.Measures.RemoveAt(track.Measures.Count - 1);
        var song = new SongDocument(() => null) { Project = project };
        var view = new ArrangementView(project, song);
        var window = new Window { Width = 1000, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();

        var sideways = view.Timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        var viewport = sideways.Viewport.Width;
        var sidewaysLeft = sideways.TranslatePoint(new Point(0, 0), view)!.Value.X;
        var headerRight = view.Header.TranslatePoint(new Point(view.Header.Bounds.Width, 0), view)!.Value.X;
        var timelineLeft = view.Timeline.TranslatePoint(new Point(0, 0), sideways)!.Value.X;

        Assert.InRange(sidewaysLeft - headerRight, 0, 20);
        Assert.Equal(0, timelineLeft, 1);
        Assert.True(view.Timeline.Bounds.Width >= viewport - 0.5, $"timeline width {view.Timeline.Bounds.Width} is narrower than the viewport {viewport}");
    }
}
