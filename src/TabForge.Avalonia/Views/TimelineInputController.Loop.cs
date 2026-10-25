using Avalonia;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the loop region gestures on the bar-number ruler: a drag on empty ruler makes a loop (Shift extends the existing one), a drag on an edge resizes
//   it, a drag inside moves it; the pointer turns to the resize arrow only over the end handles and the section edge knobs (whole bars; a loop made from notes keeps its cells). The drag only draws a preview (TimelineView.LoopPreview); the release
//   writes the model once as one undo step (DocumentEdits.EditLoop), so the engine hears one change; a lost capture drops the preview. A click without a drag only moves the playhead (TimelineInputController.Released).
// Does not own: the loop state and its rules (SelectionModel, LoopRulerRules), its drawing (TimelineLoopMarks) or the transport (SelectionLoopLink).
// Tests: TabForge.Avalonia.Tests.LoopModelTests.
public sealed partial class TimelineInputController
{
    private LoopGrip _loopGrip;
    private LoopRegionSnapshot _loopBefore;

    private Cursor? _resizeCursor;

    // A press on the ruler: which part of the loop region (if any) it grabbed.
    private void PressRuler() => _loopGrip = GripAt(_pressPoint.X);

    private LoopGrip GripAt(double x)
    {
        var loop = _selection.LoopBars;
        return loop is { } bars && bars.Start < _view.BarCount
            ? LoopRulerRules.GripAt(x, _view.BarX(bars.Start), _view.BarX(bars.End + 1))
            : LoopGrip.None;
    }

    /// <summary>True while the pointer is over one of the loop bar's end handles on the ruler: only there does the pointer become the resize arrow.</summary>
    internal bool OverLoopHandle(Point p) => p.Y < TimelineView.RulerHeight && GripAt(p.X) is LoopGrip.StartEdge or LoopGrip.EndEdge;

    // The hover sets the resize arrow over a handle and clears it elsewhere; the bar middle and the rest keep the normal pointer.
    private void ApplyLoopCursor(Point p)
    {
        var cursor = OverLoopHandle(p) || _view.SectionKnobAt(p.X, p.Y) is not null ? _resizeCursor ??= new Cursor(StandardCursorType.SizeWestEast) : null;
        if (!ReferenceEquals(_view.Cursor, cursor)) _view.Cursor = cursor;
    }

    private void BeginLoopDrag(Point pointer)
    {
        _gesture = Gesture.Loop;
        _loopBefore = _selection.LoopSnapshot();
        UpdateLoopDrag(pointer);
    }

    // Each move asks the pure rule for the bars and only redraws the preview; the model (and the engine) take the loop once, on release.
    private void UpdateLoopDrag(Point pointer)
    {
        if (_view.BarCount == 0) return;
        var before = _loopBefore;
        (int, int)? loop = before.Has ? (before.StartBar, before.EndBar) : null;
        var bars = LoopRulerRules.Drag(_loopGrip, _pressBar, _view.BarAtX(pointer.X), loop, _view.BarCount, _pressShift);
        if (_view.LoopPreview == bars) return;
        _view.LoopPreview = bars;
        _view.InvalidateVisual();
    }

    // The release writes the dragged bars as one undo step. A move keeps a note-made loop on its beats; a resized edge snaps to the bar line; a new loop is whole bars.
    private void EndLoopDrag()
    {
        if (_view.LoopPreview is not { } bars) return;
        _view.LoopPreview = null;
        var before = _loopBefore;
        var (start, end) = bars;
        var keepsCells = before.FromScore && _loopGrip == LoopGrip.Inside;
        var startCell = keepsCells || before.FromScore && _loopGrip == LoopGrip.EndEdge && start == before.StartBar ? before.StartCell : 0;
        var endCell = keepsCells || before.FromScore && _loopGrip == LoopGrip.StartEdge && end == before.EndBar ? before.EndCell : -1;
        DocumentEdits.EditLoop(_document, model => model.SetLoop(start, end, startCell, endCell, before.FromScore && _loopGrip != LoopGrip.None, SelectionOrigin.Timeline),
            _loopGrip == LoopGrip.None ? "Set loop" : "Adjust loop");
        _view.InvalidateVisual();
    }

    private void CancelLoopDrag()
    {
        _view.LoopPreview = null;
        _view.InvalidateVisual();
    }
}
