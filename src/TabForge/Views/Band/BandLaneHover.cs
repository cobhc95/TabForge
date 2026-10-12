using System.Windows;
using System.Windows.Input;

namespace TabForge.Views.Band;

// Owns: the note length outline under the mouse in a Band lane. The lane's score takes no mouse input of its own, so a mouse move over
//   the lane is handed to the score's own hover handling (the same hit test and drawing as the main score), also while playing, and
//   leaving the lane takes the outline away. The score repaints only when the hovered beat changes, and only in that lane.
// Does not own: the hit test, the outline's drawing or its setting (EditorInputController, ScoreRenderer, ScoreAppearance.ShowNoteOutline),
//   or what a click in a lane does (BandLane).
// Tests: TestBandVerticalLanes.
internal static class BandLaneHover
{
    /// <summary>Hooks a lane: moves reach its score, leaving the lane takes the outline away.</summary>
    public static void Attach(BandLane lane)
    {
        // The score's own move event bubbles back through the lane: that one is not handed on again.
        lane.MouseMove += (_, e) => { if (!ReferenceEquals(e.Source, lane.Editor)) Send(lane, e.MouseDevice, Mouse.MouseMoveEvent); };
        lane.MouseLeave += (_, e) => Send(lane, e.MouseDevice, Mouse.MouseLeaveEvent);
    }

    private static void Send(BandLane lane, MouseDevice mouse, RoutedEvent what) =>
        lane.Editor.RaiseEvent(new MouseEventArgs(mouse, Environment.TickCount) { RoutedEvent = what });
}
