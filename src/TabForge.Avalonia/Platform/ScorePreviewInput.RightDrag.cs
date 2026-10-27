using System;
using Avalonia;
using Avalonia.Input;

namespace TabForge.Avalonia.Platform;

// Owns: the score's right-button drag: a deliberate horizontal drag selects the notes under it exactly as a left drag does but makes no loop (the
//   release raises neither SelectionReleased nor PlainClicked, and places no playback), Escape puts the earlier selection back, and the release
//   of a drag opens no context menu. A press with no deliberate drag changes nothing here: its release opens the context menu as ever.
// Does not own: the left press, drag and release (ScorePreviewInput), the selection rules (Core ScorePreviewEditor), the menu rows (ScorePreviewView.ContextMenu).
// Tests: TabForge.Avalonia.Tests.ScoreRightDragSelectTests.
// Threads: UI thread only.
internal sealed partial class ScorePreviewInput
{
    /// <summary>How far (DIP) the right button must travel sideways before a drag starts; less is a click with a jittery hand and opens the menu.</summary>
    internal const double RightDragMinDx = 12;

    /// <summary>The sideways travel must beat the vertical travel by this factor, so a diagonal or vertical wobble stays a click.</summary>
    internal const double RightDragDominance = 1.5;

    private IPointer? _rightPointer;
    private Point? _rightPressAt;        // where the right button went down; null while it is up
    private bool _rightDragging;         // the drag started and has not been cancelled or released
    private bool _rightSwallowRelease;   // the drag ended (Escape, lost capture) but the button is still down: its release does nothing
    private ((int Measure, int Cell, int String)? Cursor, (int, int, int, int)? Range, bool WholeBars) _rightBefore;

    /// <summary>True from the drag's start until the button comes up: the release opens no context menu.</summary>
    internal bool RightHoldSwallowsMenu => _rightDragging || _rightSwallowRelease;

    /// <summary>A right press: remembered only; nothing changes until the pointer travels far enough sideways.</summary>
    internal void PressRight(PointerPressedEventArgs e)
    {
        CancelRightHold();
        _rightPressAt = e.GetPosition(_view);
    }

    // True when a right drag owns this move (so the left/hover path is skipped).
    private bool MoveRight(PointerEventArgs e, Point page)
    {
        if (_rightSwallowRelease) return true;
        if (_rightPressAt is not { } from) return false;
        var at = e.GetPosition(_view);
        if (!_rightDragging)
        {
            var dx = Math.Abs(at.X - from.X);
            if (dx < RightDragMinDx || dx <= RightDragDominance * Math.Abs(at.Y - from.Y)) return false;
            StartRightDrag(e, _view.PagePoint(from));
        }
        _view.Editor.DragTo(page.X, page.Y);
        _view.InvalidateVisual();
        _view.RaiseCursorChanged();
        return true;
    }

    private void StartRightDrag(PointerEventArgs e, Point pressPage)
    {
        _rightDragging = true;
        _rightBefore = _view.Editor.SnapshotSelection();
        _view.Focus();
        _view.RaiseFocused();
        _view.Editor.Click(pressPage.X, pressPage.Y);   // the drag's fixed end, as the left press makes it; no playback seek
        _rightPointer = e.Pointer;
        e.Pointer.Capture(_view);
    }

    // The button came up: true when a drag (or a cancelled one) owned it, so the caller opens no menu and the loop link hears nothing.
    private bool EndRightHold(PointerReleasedEventArgs e)
    {
        var owned = RightHoldSwallowsMenu;
        _rightPressAt = null;
        _rightDragging = _rightSwallowRelease = false;
        _rightPointer = null;
        if (!owned) return false;
        _view.Editor.Release();
        if (e.Pointer.Captured == _view) e.Pointer.Capture(null);
        _view.InvalidateVisual();
        _view.RaiseCursorChanged();
        return true;
    }

    // Escape mid-drag: the selection before the drag comes back and the rest of the hold does nothing. False when no drag is running.
    private bool CancelRightDrag()
    {
        if (!_rightDragging) return false;
        _rightDragging = false;
        _rightSwallowRelease = true;
        _view.Editor.Release();
        _view.Editor.RestoreSelection(_rightBefore);
        _rightPointer?.Capture(null);   // after the flags, so the capture-lost call finds nothing running
        _view.InvalidateVisual();
        _view.RaiseCursorChanged();
        return true;
    }

    // Capture lost mid-drag: the range stays as dragged, the later release does nothing.
    private void LoseRightCapture()
    {
        if (!_rightDragging) return;
        _rightDragging = false;
        _rightSwallowRelease = true;
        _view.Editor.Release();
    }

    private void CancelRightHold() { _rightPressAt = null; _rightDragging = _rightSwallowRelease = false; _rightPointer = null; }
}
