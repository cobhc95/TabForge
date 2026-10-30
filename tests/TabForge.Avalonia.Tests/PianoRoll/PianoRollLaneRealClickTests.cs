using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the lane picker and the lane's show/hide control driven by real pointer presses in the real window, on a tab track and a MIDI track: a press on
//   the picker opens its menu and never collapses the lane (the label stays), the menu of a tab track disables the controller rows under a note line,
//   a press on the header outside the picker still collapses and expands the lane, and the chevron at the header's right end does the same.
// Does not own: the picker's rows and look (PianoRollLaneButtonTests) or the piano roll gestures (PianoRollRealPathTests).
// Tests: MainWindow + PianoRollView.LaneButton + PianoRollView.LaneToggle + the header of PianoRollSurface.
public sealed class PianoRollLaneRealClickTests
{

    private static (MainWindow Window, PianoRollView Roll) Open(SongDocument song)
    {
        var window = new MainWindow(first: song) { Width = 1600, Height = 1000 };
        window.Show();
        window.UpdateLayout();
        var roll = window.GetLogicalDescendants().OfType<PianoRollView>().Single();
        Assert.True(roll.IsEffectivelyVisible);
        return (window, roll);
    }

    private static Button Named(MainWindow window, string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static Point Centre(MainWindow window, Visual control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(MainWindow window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Label(MainWindow window) =>
        Named(window, "PianoRollLaneButton").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text != "▾").Text!;

    /// <summary>A point on the lane header strip, between the picker at the left and the chevron at the right.</summary>
    private static Point HeaderMiddle(MainWindow window, PianoRollSurface surface) =>
        surface.TranslatePoint(new Point(surface.Bounds.Width / 2, surface.GridBottom + PianoRollSurface.LaneHeaderSize / 2), window)!.Value;

    private static MainWindow TabWindow(out PianoRollView roll)
    {
        var song = PianoRollSongs.Tab();
        song.Project.Tracks[0].EditorView = TrackEditorView.PianoRoll;
        var (window, view) = Open(song);
        roll = view;
        Assert.True(roll.Presenter.IsTab);
        return window;
    }

    [AvaloniaFact]
    public void APressOnTheTabTrackPickerOpensTheMenuAndKeepsTheLane()
    {
        var window = TabWindow(out var roll);
        var shown = roll.Surface.LaneShown;
        Click(window, Centre(window, Named(window, "PianoRollLaneButton")));
        Assert.NotNull(roll.LaneMenu);
        Assert.True(roll.LaneMenu!.IsOpen);
        Assert.Equal(shown, roll.Surface.LaneShown);   // the press did not collapse the lane
        Assert.Equal("LANE: VELOCITY", Label(window));
        roll.LaneMenu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void APressOnTheMidiTrackPickerOpensTheMenuAndKeepsTheLane()
    {
        var (window, roll) = Open(PianoRollSongs.Midi());
        Assert.False(roll.Presenter.IsTab);
        Click(window, Centre(window, Named(window, "PianoRollLaneButton")));
        Assert.NotNull(roll.LaneMenu);
        Assert.True(roll.LaneMenu!.IsOpen);
        Assert.True(roll.Surface.LaneShown);
        Assert.Equal("LANE: VELOCITY", Label(window));
        roll.LaneMenu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void OnATabTrackEveryControllerRowIsSelectableAndNoNoteLineShows()
    {
        var window = TabWindow(out var roll);
        var rows = roll.LaneRows();
        Assert.DoesNotContain(rows, r => r.IsLabel);
        Assert.All(rows.Where(r => r.Id == "PianoRoll.Lane"), r => Assert.True(r.Enabled));
        roll.ShowLane(7);
        Assert.Equal(7, roll.ShownLane);
        window.Close();
    }

    [AvaloniaFact]
    public void APressOnTheHeaderOutsideThePickerStillCollapsesAndExpandsTheLane()
    {
        var (window, roll) = Open(PianoRollSongs.Midi());
        var surface = roll.Surface;
        Assert.True(surface.LaneShown);
        Click(window, HeaderMiddle(window, surface));
        Assert.False(surface.LaneShown);
        Assert.Null(roll.LaneMenu);
        Click(window, HeaderMiddle(window, surface));
        Assert.True(surface.LaneShown);
        window.Close();
    }

    [AvaloniaFact]
    public void TheChevronAtTheHeadersRightEndCollapsesAndExpandsTheLane()
    {
        var (window, roll) = Open(PianoRollSongs.Midi());
        var toggle = Named(window, "PianoRollLaneToggle");
        Assert.Equal("Show or hide the lane", ToolTip.GetTip(toggle));
        Click(window, Centre(window, toggle));
        Assert.False(roll.Surface.LaneShown);
        Assert.Null(roll.LaneMenu);
        Click(window, Centre(window, Named(window, "PianoRollLaneToggle")));
        Assert.True(roll.Surface.LaneShown);
        window.Close();
    }
}
