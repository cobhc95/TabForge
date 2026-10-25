using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the bottom of the main window in the Band view. Alone, it is the normal bottom (the track-list column, the full transport, the timeline
/// fitted to its rows with no empty band under it), on the way in and on the way out. With compact mode on too, the Arrangement shows its compact
/// content (no track-list column, the transport two rows at the left of the timeline), and it keeps that when the other mode leaves and comes back.
/// Does not own: the compact content itself (ArrangementView.SetCompact), the header strip (BandCompactModesTests) or the Band cards (BandViewTests).
/// Tests: TabForge.Avalonia.Tests.BandBottomLayoutTests.
/// </summary>
public sealed class BandBottomLayoutTests
{
    private static void Pick(MainWindow main, string commandId)
    {
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == commandId).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
    }

    /// <summary>The bottom is the compact content: the track-list column hidden, and the timeline starting right after the transport.</summary>
    private static void AssertCompactBottom(MainWindow main, string step)
    {
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        Assert.True(arrangement.IsEffectivelyVisible, step);
        Assert.False(arrangement.Header.IsVisible, $"{step}: the track-list column is shown");
        var transportRight = arrangement.Transport.TranslatePoint(new Point(arrangement.Transport.Bounds.Width, 0), main)!.Value.X;
        var timelineLeft = arrangement.Timeline.TranslatePoint(default, main)!.Value.X;
        Assert.True(timelineLeft - transportRight >= 0 && timelineLeft - transportRight <= 12, $"{step}: the timeline starts {timelineLeft - transportRight} past the transport");
    }

    /// <summary>The normal bottom: the track-list column shown, the full transport above the timeline, and no empty band under the timeline.</summary>
    private static void AssertNormalBottom(MainWindow main, string step)
    {
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        Assert.True(arrangement.IsEffectivelyVisible, step);
        Assert.True(arrangement.Header.IsVisible, $"{step}: the track-list column is hidden");
        var workspace = main.GetVisualDescendants().OfType<DockWorkspaceView>().Single();
        var workspaceBottom = workspace.TranslatePoint(new Point(0, workspace.Bounds.Height), main)!.Value.Y;
        var arrangementBottom = arrangement.TranslatePoint(new Point(0, arrangement.Bounds.Height), main)!.Value.Y;
        Assert.True(workspaceBottom - arrangementBottom <= GapTolerance, $"{step}: {workspaceBottom - arrangementBottom} px of empty band under the timeline");
    }

    /// <summary>The largest empty band allowed under the timeline, in DIP.</summary>
    private const double GapTolerance = 4;

    /// <summary>The owner's window size, in DIP: with ten tracks a split left unfitted shows at once.</summary>
    private const double OwnerWidth = 2000;
    private const double OwnerHeight = 1100;

    /// <summary>The owner's session has ten tracks (the demo's five grown to ten).</summary>
    private const int OwnerTrackCount = 10;

    /// <summary>Pixels the pane may fall short of its natural height (the splitter rounds to whole pixels).</summary>
    private const double FitSlack = 2;

    /// <summary>Layout passes that let the deferred fit run and the split settle.</summary>
    private const int SettlePasses = 3;

    /// <summary>How far the splitter is dragged to cramp the track list: past its minimum, so the pane is clearly not the fit.</summary>
    private const double SplitShrinkDistance = 3000;

    /// <summary>Runs the UI thread's queued jobs and lays out until the window settles.</summary>
    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < SettlePasses; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    /// <summary>Drags the vertical splitter down by <paramref name="distance"/>: the timeline is its lower half, so it shrinks.</summary>
    private static void Drag(MainWindow window, DockSplitter splitter, double distance)
    {
        var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, distance / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(start + new Vector(0, distance), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(0, distance), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static SongDocument TenTrackSong()
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count < OwnerTrackCount) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        return new SongDocument(() => null) { Project = project };
    }

    /// <summary>The track list shows every row: its pane is as tall as its rows, not the share of the split it inherited.</summary>
    private static void AssertTrackListFitted(MainWindow main, string step)
    {
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        Assert.True(arrangement.Bounds.Height >= arrangement.NaturalHeight - FitSlack,
            $"{step}: the track list pane shows {arrangement.Bounds.Height} of its natural {arrangement.NaturalHeight}");
        Assert.Equal(0, arrangement.Timeline.MaxRowScrollOffset, 3);
    }

    /// <summary>
    /// The Band view alone keeps the normal bottom on both swaps: entering the Band layout (a vertical split of its own) and leaving it for the
    /// layout it replaced (its own row, or its pane closed by hand) both fit the timeline pane to its track rows again, as a song or a layout shown does.
    /// </summary>
    [AvaloniaFact]
    public void EnteringAndLeavingTheBandViewFitsTheTrackListToItsRows()
    {
        var main = new MainWindow(first: TenTrackSong()) { Width = OwnerWidth, Height = OwnerHeight };
        main.Show();
        Settle(main);
        AssertTrackListFitted(main, "at launch");

        // A splitter the user dragged is not the fit: the next layout shown (the Band view in, then out) fits the rows again.
        DragTrackList(main, "before the Band view");
        Pick(main, "View.BandView");
        Settle(main);
        AssertTrackListFitted(main, "in the Band view");

        Pick(main, "View.BandView");
        Settle(main);
        AssertTrackListFitted(main, "after the Band view left");

        // The Band pane closed by hand leaves the Band view too, and the layout it replaced comes back fitted.
        DragTrackList(main, "before the Band pane closed by hand");
        Pick(main, "View.BandView");
        Settle(main);
        main.DockWorkspace.SetPanelVisible("band", false);
        main.DockWorkspace.FlushLayoutChanged();
        Settle(main);
        AssertTrackListFitted(main, "after the Band pane was closed by hand");
        main.Close();
    }

    /// <summary>Drags the splitter over the track list down to its minimum, so the pane is clearly not the fit.</summary>
    private static void DragTrackList(MainWindow main, string step)
    {
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        var split = arrangement.GetVisualAncestors().OfType<DockSplitPanel>().First();
        Drag(main, split.Splitter, SplitShrinkDistance);
        Assert.True(arrangement.Bounds.Height < arrangement.NaturalHeight - FitSlack,
            $"{step}: the dragged splitter left the track list fitted ({arrangement.Bounds.Height} of its natural {arrangement.NaturalHeight})");
    }

    [AvaloniaFact]
    public void BandViewAloneKeepsTheNormalBottomAndCompactModeAddsTheCompactBottom()
    {
        var main = new MainWindow { Width = 2000, Height = 1100 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();

        Pick(main, "View.BandView");
        AssertNormalBottom(main, "band view alone");

        Pick(main, "View.CompactMode");
        AssertCompactBottom(main, "band view with compact mode");

        Pick(main, "View.CompactMode");
        AssertNormalBottom(main, "band view after compact mode leaves");

        Pick(main, "View.CompactMode");
        Pick(main, "View.BandView");
        AssertCompactBottom(main, "compact mode after band view left");
        main.Close();
    }
}
