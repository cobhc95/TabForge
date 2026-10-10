using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views;

// Owns: the score pane's own scrolling: the wheel step (Shift scrolls sideways), the scrollbar / key / touch gestures that tell the follow
//   to stand down, and bringing the cursor bar into view.
// Does not own: following the playhead (ScoreFollowCoordinator), the zoom (ScoreZoomController, which the window asks for Ctrl + wheel),
//   the scroll viewer itself.
// Tests: TestFollowResumesAfterSeek, TestFollowIgnoresClampedScroll, TestWindowLifetime.
internal sealed class ScoreScrollController
{
    private readonly IScoreZoomHost _host;

    public ScoreScrollController(IScoreZoomHost host) => _host = host;

    private ScrollViewer Scroll => _host.ScoreScroll;

    /// <summary>Keeps the playhead / cursor visible by scrolling the score pane.</summary>
    public void ScrollToCursor()
    {
        var track = _host.SelectedTrack;
        if (track is null || track.Measures.Count == 0) return;
        var editor = _host.Editor;
        var follow = _host.Follow;
        var bar = Math.Clamp(editor.SelectedMeasure, 0, track.Measures.Count - 1);
        follow.JumpTo(Scroll.ViewportHeight > 1 && editor.SystemHeightNow > Scroll.ViewportHeight ? follow.FocusTop(editor.SystemTopForMeasure(bar)) : editor.ScrollOffsetForMeasure(bar));
        // One-line mode: bring the cursor bar into view unless it already is.
        if (editor.HorizontalScroll)
        {
            var x = editor.HorizontalOffsetForMeasure(editor.SelectedMeasure);
            if (x < Scroll.HorizontalOffset || x > Scroll.HorizontalOffset + Scroll.ViewportWidth - 120)
                Scroll.ScrollToHorizontalOffset(x);
        }
    }

    /// <summary>Use a tunable, smaller pixel step instead of WPF's coarse default score-wheel jump.</summary>
    public void OnMouseWheel(MouseWheelEventArgs e, int wheelScrollPixels)
    {
        _host.Follow.NoteUserScrollGesture();
        var distance = wheelScrollPixels * (e.Delta / 120.0);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            Scroll.ScrollToHorizontalOffset(Scroll.HorizontalOffset - distance);
        else
            Scroll.ScrollToVerticalOffset(Scroll.VerticalOffset - distance);
        e.Handled = true;
    }

    /// <summary>Scroll gestures that are not the wheel: scrollbar press/drag, scroll keys and touch pan.</summary>
    public void WireGestures()
    {
        var follow = _host.Follow;
        Scroll.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(d) is not null)
                follow.SetScrollBarDrag(true);
        }), true);
        Scroll.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => follow.SetScrollBarDrag(false)), true);
        Scroll.AddHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler((_, _) => follow.SetScrollBarDrag(false)), true);
        Scroll.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down or Key.Left or Key.Right)
                follow.NoteUserScrollGesture();
        }), true);
        Scroll.AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>((_, _) => follow.NoteUserScrollGesture()), true);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
