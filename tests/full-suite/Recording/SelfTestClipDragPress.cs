using System.Windows;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Pressing a clip while a song plays (part of <see cref="SelfTest"/>): the press starts the drag without seeking or switching track
/// (those wait for a click), a missed release or lost capture ends the drag, and a click still seeks.
/// </summary>
public static partial class SelfTest
{
    private static void TestClipDragPress()
    {
        var song = DropSong();
        var clip = MoveClip("Loop", 4, 4, 0);
        song.Tracks[0].AudioClips.Add(clip);
        ClipLanes.Ensure(song.Tracks[0], 1);
        var timeline = new TrackTimeline
        {
            Project = song, MeasureWidth = 30,
            BarStartSec = b => b * 2.0, BarOfSec = s => ((int)Math.Floor(s / 2), s / 2 - Math.Floor(s / 2)),
        };
        int seeks = 0, tracks = 0, takes = 0;
        timeline.BarClicked += (_, _) => seeks++;
        timeline.TrackClicked += (_, _) => tracks++;
        timeline.ClipLaneSelected += (_, _, _, _) => takes++;
        var x = timeline.XOfBar(2) + 20;
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        Point? hit = null;
        for (var y = gridTop; y < gridTop + 400 && hit is null; y += 4)
        {
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
            if (timeline.ClipMouseDown(down, new Point(x, y)) && timeline.ClipDragActive) hit = new Point(x, y);
        }
        Check("clip press: the press starts the drag", hit is not null);
        Check("clip press: no seek, track switch or take change before the drag", seeks == 0 && tracks == 0 && takes == 0);
        Check("clip press: a mouse move with the button released ends the drag",
            timeline.ClipMouseMove(new MouseEventArgs(Mouse.PrimaryDevice, 0), hit ?? default) && !timeline.ClipDragActive);
        if (hit is { } p)
        {
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
            timeline.ClipMouseDown(down, p);
            Check("clip press: a lost capture ends the drag", timeline.ClipCaptureLost() && !timeline.ClipDragActive && seeks == 0);
            timeline.ClipMouseDown(down, p);
            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
            timeline.RaiseEvent(up);
            Check("clip press: a click (release without drag) seeks and selects", seeks == 1 && tracks == 1 && takes == 1);
        }
    }
}
