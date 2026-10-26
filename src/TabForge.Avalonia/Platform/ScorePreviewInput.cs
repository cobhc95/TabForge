using System;
using Avalonia;
using Avalonia.Input;
using TabForge.Models;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: the score view's pointer and key input on the editor surface: a press places the cursor (playback follows it on press), a drag selects
//   (also when it starts on a note: notes are never moved by the mouse) and shows the marquee from the press to the pointer, a release places the cursor
//   at the start of a dragged range, hides the marquee and reports the finished selection (the loop link loops it), a pointer without a button sets the hover, and the editor's keys and Escape.
// Does not own: the drawing, the zoom and the wheel (ScorePreviewView), the edit rules (ScorePreviewEditor), the key mapping (AvaloniaEditorKeys),
//   the section heading hit test (ScoreSectionLinks).
// Tests: TabForge.Avalonia.Tests.ScorePreviewEditorTests, TabForge.Avalonia.Tests.ScoreCursorNoteboxTests.
// Threads: UI thread only.

/// <summary>The score view's pointer and key input, forwarded to the Core editor; the view's overrides call these.</summary>
internal sealed class ScorePreviewInput
{
    private readonly ScorePreviewView _view;

    // True from a press that places the cursor (no Shift) until its release.
    private bool _pressPlacesCursor;

    // The bar and cell the last cursor placement reported, or null after a press; a release reports again only when a drag moved the range start.
    private (int Bar, int Cell)? _lastPlacedCursor;

    // Where the left press was, in view coordinates (the marquee's fixed corner); null while no press is held.
    private Point? _pressAt;

    // True once the held press moved the selection (a drag), so the release is not a plain click.
    private bool _dragged;

    /// <summary>How far (px) a held press may wobble and still be a plain click.</summary>
    private const double ClickSlop = 3;

    internal ScorePreviewInput(ScorePreviewView view) => _view = view;

    /// <summary>A left press: a section heading, or the cursor at the press point (playback follows it on press).</summary>
    internal void Press(PointerPressedEventArgs e)
    {
        var p = _view.PagePoint(e.GetPosition(_view));
        if (ScoreSectionLinks.HeadingAt(_view.Probe, p.X, p.Y) is { } section)
        {
            _view.RaiseSectionHeadingClicked(section, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
            return;
        }
        _pressAt = e.GetPosition(_view);
        _dragged = false;
        var extendsSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _view.Editor.Click(p.X, p.Y, extendsSelection);
        _pressPlacesCursor = !extendsSelection;
        if (_pressPlacesCursor) _view.ResumeFollow();   // the reader placed the cursor: the follow resumes after a hand scroll
        e.Pointer.Capture(_view);
        _view.InvalidateVisual();
        _view.RaiseCursorChanged();
        if (_pressPlacesCursor)
        {
            // Playback moves on the press, not the release, so a click does not wait for the button to come up.
            _lastPlacedCursor = null;
            PlaceCursorForPlayback();
        }
        e.Handled = true;
    }

    /// <summary>The release ends a drag; a drag that moved the range start places the cursor there, a plain click leaves it where the press put it.</summary>
    internal void Release(PointerReleasedEventArgs e)
    {
        _view.Editor.Release();
        _pressAt = null;
        if (_view.Marquee is not null) { _view.Marquee = null; _view.InvalidateVisual(); }
        if (_pressPlacesCursor && !_dragged) _view.RaisePlainClicked();
        else _view.RaiseSelectionReleased();
        if (_pressPlacesCursor)
        {
            _pressPlacesCursor = false;
            PlaceCursorForPlayback();
        }
        if (e.Pointer.Captured == _view) e.Pointer.Capture(null);
    }

    /// <summary>A pointer move: a drag extends the range, a pointer without a button sets the hover.</summary>
    internal void Move(PointerEventArgs e)
    {
        var p = _view.PagePoint(e.GetPosition(_view));
        if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed)
        {
            if (_view.Editor.HoverAt(p.X, p.Y)) _view.InvalidateVisual();
            return;
        }
        if (_pressAt is { } from)
        {
            var at = e.GetPosition(_view);   // a struct: the marquee costs no allocation per move
            _dragged |= Math.Abs(at.X - from.X) > ClickSlop || Math.Abs(at.Y - from.Y) > ClickSlop;
            // disabled 25 Oct: the score uses the old note-selection drag, no marquee box
            // _view.Marquee = new Rect(Math.Min(from.X, at.X), Math.Min(from.Y, at.Y), Math.Abs(at.X - from.X), Math.Abs(at.Y - from.Y));
        }
        _view.Editor.DragTo(p.X, p.Y);
        _view.InvalidateVisual();
    }

    /// <summary>The press ended without a release (capture lost): no marquee.</summary>
    internal void CaptureLost()
    {
        _pressAt = null;
        if (_view.Marquee is null) return;
        _view.Marquee = null;
        _view.InvalidateVisual();
    }

    /// <summary>The pointer left the score: no hover outline.</summary>
    internal void Exited()
    {
        if (_view.Editor.HoverEnd()) _view.InvalidateVisual();
    }

    /// <summary>A key on the score: Space is left to the window, Escape clears a range first, and the editor takes the rest.</summary>
    internal void KeyDown(KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        // Space is Transport.PlayPause and Escape clears the score selection first; Space is left to bubble to the window's key table.
        if (e.Key == Key.Space) return;
        if (e.Key == Key.Escape && _view.Editor.HasSelection)
        {
            _view.Editor.ClearSelection();
            _view.InvalidateVisual();
            _view.RaiseCursorChanged();
            e.Handled = true;
            return;
        }
        // Avalonia's key names are the hotkey catalogue's (R, OemPeriod, D1...).
        if (!_view.Editor.Key(AvaloniaEditorKeys.ToEditorKey(e.Key), e.Key.ToString(), mods.HasFlag(KeyModifiers.Control), mods.HasFlag(KeyModifiers.Shift), mods.HasFlag(KeyModifiers.Alt))) return;
        _view.ResumeFollow();
        _view.InvalidateVisual();
        _view.RaiseCursorChanged();
        e.Handled = true;
    }

    private void PlaceCursorForPlayback()
    {
        var (bar, cell, _, _) = _view.Editor.SelectionRange();
        if (_lastPlacedCursor == (bar, cell)) return;
        _lastPlacedCursor = (bar, cell);
        _view.RaiseCursorPlaced(bar, cell);
    }
}
