using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the arrangement pane's maximum height: the transport bar plus the timeline at base row height, so the dock's splitter stops the pane
///     at its content, and it grows when a track is added.
/// Does not own: the splitter clamp (DockSplitPanel, Fenn's area) or the rows (TimelineView).
/// </summary>
public class ArrangementNaturalHeightTests
{
    [AvaloniaFact]
    public void ArrangementMaxHeightIsItsContentHeightAndGrowsWhenATrackIsAdded()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 1000, Height = 1300, Content = view };
        window.Show();
        window.UpdateLayout();
        // The natural height includes the Add-track strip under the track rows (its height is AddTrackLane.Height).
        var stripHeight = TabForge.Services.AddTrackLane.Height;
        var before = view.MaxHeight;
        Assert.Equal(view.TopBandHeight + view.Timeline.BaseContentHeight + stripHeight, before, 3);

        song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio));
        song.Project.MarkTimelineChanged();
        window.UpdateLayout();

        Assert.True(view.MaxHeight > before);
        Assert.Equal(view.TopBandHeight + view.Timeline.BaseContentHeight + stripHeight, view.MaxHeight, 3);
    }
}
