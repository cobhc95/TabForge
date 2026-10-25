using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Capture;

namespace TabForge.Avalonia.Capture;

// Owns: the pointer verb: a left-button drag on the timeline made of real routed pointer events (press, move, release) so the timeline's own controller handles it as it handles a mouse.
// Does not own: the gestures (TimelineInputController and its helpers), the shots taken between the events (ShotSteps, FrameSteps).
// Tests: TabForge.Avalonia.Tests.CaptureFixtureTests.
internal sealed class PointerSteps(CaptureSession session)
{
    private readonly Pointer _pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
    private Point _pressed;
    private bool _down;

    public void Register(Dictionary<string, Func<CaptureStep, Task>> verbs) => verbs["pointer"] = RunAsync;

    /// <summary>
    /// <c>{"pointer":"press","clip":[track,clip]}</c> presses 10 DIPs inside that clip, <c>{"pointer":"press","bar":5.5,"zone":"section"|"ruler"|"lanes","row":2}</c> presses at a bar
    /// (1-based, a fraction is the part of the bar) in the section strip, the ruler or the lanes of a row; <c>{"pointer":"move","by-bars":4.5}</c> moves the pointer sideways from the press point (the count is from the press, not cumulative) by that many bars,
    /// <c>{"pointer":"move","bar":9,"zone":..}</c> moves it to a bar; <c>{"pointer":"release"}</c> lets go where it is.
    /// </summary>
    private async Task RunAsync(CaptureStep step)
    {
        var timeline = session.Find<ArrangementView>().Timeline;
        switch (step.Text)
        {
            case "press": _pressed = PointOf(timeline, step); Raise(timeline, PointerPressedKind, _pressed); _down = true; break;
            case "move": Raise(timeline, PointerMovedKind, step.Has("by-bars") ? new Point(_pressed.X + step.Option("by-bars", 0.0) * timeline.BarsWidth / timeline.BarCount, _pressed.Y) : PointOf(timeline, step)); break;
            case "release": Raise(timeline, PointerReleasedKind, _last); _down = false; break;
            default: throw new InvalidOperationException($"Unknown pointer action \"{step.Text}\".");
        }
        await session.SettleAsync(80);
    }

    private const int PointerPressedKind = 0, PointerMovedKind = 1, PointerReleasedKind = 2;
    private Point _last;

    private static Point PointOf(TimelineView timeline, CaptureStep step)
    {
        if (step.NumbersOption("clip") is { Length: 2 } at)
        {
            var track = timeline.Song.Tracks[(int)at[0]];
            var box = TimelineAudioClips.ClipBounds(timeline, track, track.AudioClips[(int)at[1]]);
            return new Point(box.X + 10, (box.Top + box.Bottom) / 2);
        }
        var bar = step.Option("bar", 1.0);
        var index = Math.Clamp((int)Math.Floor(bar) - 1, 0, timeline.BarCount - 1);
        var x = timeline.BarX(index) + (bar - Math.Floor(bar)) * (timeline.BarX(index + 1) - timeline.BarX(index));
        var row = step.Option("row", 0);
        var y = step.Option("zone", "lanes") switch
        {
            "section" => TimelineView.RulerHeight + TimelineView.SectionHeight / 2,
            "ruler" => TimelineView.RulerHeight / 2,
            _ => timeline.RowTop(row) + timeline.BaseBandHeightOf(row) / 2,
        };
        return new Point(x, y);
    }

    private void Raise(TimelineView timeline, int kind, Point at)
    {
        _last = at;
        var root = TopLevel.GetTopLevel(timeline) ?? throw new InvalidOperationException("The timeline is not in a window.");
        var rootPoint = timeline.TranslatePoint(at, root) ?? at;
        var stamp = (ulong)Environment.TickCount64;
        var pressedProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        RoutedEventArgs e = kind switch
        {
            PointerPressedKind => new PointerPressedEventArgs(timeline, _pointer, root, rootPoint, stamp, pressedProps, KeyModifiers.None) { RoutedEvent = InputElement.PointerPressedEvent },
            PointerMovedKind => new PointerEventArgs(InputElement.PointerMovedEvent, timeline, _pointer, root, rootPoint, stamp,
                new PointerPointProperties(_down ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None),
            _ => new PointerReleasedEventArgs(timeline, _pointer, root, rootPoint, stamp,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left) { RoutedEvent = InputElement.PointerReleasedEvent },
        };
        timeline.RaiseEvent(e);
    }
}
