using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the armed strip pill: its two halves join into one rounded pill of the density height, a click on the speaker half toggles the monitor and a
/// click on the input half opens the input menu (and not the other way round), the open menu marks both halves, and both halves carry the hand cursor.
/// Does not own: the strip position (TrackHeaderTopAlignTests), the monitor and input edits (ArmedRowTests), the menu rows (TrackInputPickerTests).
/// Tests: this class.
/// </summary>
public class ArmedInputPillTests
{
    private const int ArmedRow = 2;

    private static (Window Window, ArrangementView View, SongProject Project) Open()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < 4) song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio));
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        DocumentEdits.Run(view.Document, project => { project.Tracks[ArmedRow].RecordArm = true; return true; });
        window.UpdateLayout();
        return (window, view, song.Project);
    }

    private static void Click(Window window, Control half)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);   // the hit test reads the frame, which the layout above reaches only after a render tick
        var at = half.TranslatePoint(new Point(half.Bounds.Width / 2, half.Bounds.Height / 2), window) ?? default;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(28.9, 20)]
    [InlineData(34, 24)]
    public void TheTwoHalvesJoinIntoOnePillWhoseHeightFollowsTheDensity(double rowHeight, double pill)
    {
        var (window, view, _) = Open();
        view.Timeline.BaseRowHeight = rowHeight;
        view.ArmedRows.Sync();
        window.UpdateLayout();
        var monitor = view.ArmedRows.Children.OfType<ToggleButton>().Single();
        var picker = view.ArmedRows.Children.OfType<TrackInputPicker>().Single();

        Assert.Equal(pill, monitor.Height);
        Assert.Equal(pill, picker.Height);
        Assert.Equal(pill / 2, monitor.CornerRadius.TopLeft);
        Assert.Equal(pill / 2, picker.CornerRadius.BottomRight);
        Assert.Equal(0, monitor.CornerRadius.TopRight);
        Assert.Equal(Canvas.GetLeft(monitor) + monitor.Width - 1, Canvas.GetLeft(picker), 6);   // one shared divider
        Assert.Equal(Canvas.GetTop(monitor), Canvas.GetTop(picker), 6);
        Assert.Null(monitor.Cursor);   // only resize cursors are used; the hover fill shows the halves are pressable
        Assert.Null(picker.Cursor);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingTheSpeakerHalfTogglesTheMonitorAndDoesNotOpenTheMenu()
    {
        var (window, view, project) = Open();
        var monitor = view.ArmedRows.Children.OfType<ToggleButton>().Single();
        var picker = view.ArmedRows.Children.OfType<TrackInputPicker>().Single();
        var before = project.Tracks[ArmedRow].MonitorInput;

        Click(window, monitor);

        Assert.Equal(!before, monitor.IsChecked == true);
        Assert.Equal(!before, view.Document.Project.Tracks[ArmedRow].MonitorInput);
        Assert.False(picker.Flyout!.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingTheInputHalfOpensTheMenuAndLeavesTheMonitorAlone()
    {
        var (window, view, project) = Open();
        var monitor = view.ArmedRows.Children.OfType<ToggleButton>().Single();
        var picker = view.ArmedRows.Children.OfType<TrackInputPicker>().Single();
        var before = project.Tracks[ArmedRow].MonitorInput;

        Click(window, picker);

        Assert.True(picker.Flyout!.IsOpen);
        Assert.Equal(before, view.Document.Project.Tracks[ArmedRow].MonitorInput);
        Assert.True(picker.Classes.Contains("ArmedPillOpen") && monitor.Classes.Contains("ArmedPillOpen"));   // the whole pill takes the accent outline
        picker.Flyout.Hide();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(picker.Classes.Contains("ArmedPillOpen") || monitor.Classes.Contains("ArmedPillOpen"));
        window.Close();
    }
}
