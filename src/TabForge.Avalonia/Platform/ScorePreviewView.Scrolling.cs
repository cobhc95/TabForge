using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the score's hand scrolling: the wheel (a notch moves the viewer by WheelScrollPixels, Shift+wheel sideways, Ctrl+wheel zooms), and the
//   hand gestures that pause the playhead follow while FollowSettings.StopOnManualScroll is on (a wheel notch, a press or drag on the viewer's
//   scroll bar). The pause ends ResumeAfterSeconds after the last gesture (FollowHold), when playback starts again, or when the reader places the cursor.
// Does not own: the follow maths and the scroll it writes (ScorePlayheadFollower, FollowScrollPolicy), the zoom (ScorePreviewView.Zoom), the
//   cursor (ScorePreviewInput).
// Tests: TabForge.Avalonia.Tests.ScoreScrollGestureTests.
// Threads: UI thread only.
public sealed partial class ScorePreviewView
{
    // The hand-scroll pause of this view's follow: the playhead follow stands still while it holds.
    internal FollowHold FollowHold { get; } = new();

    private bool _scrollBarHeld;   // the pointer is down on the viewer's scroll bar: a drag keeps the pause fresh

    /// <summary>The follow resumes at once: playback started, or the reader placed the cursor on the score.</summary>
    internal void ResumeFollow() => FollowHold.Release();

    /// <summary>A hand scroll: pauses the playhead follow when the Follow setting says a hand scroll stops it.</summary>
    private void NoteHandScroll()
    {
        if (Follow.StopOnManualScroll) FollowHold.Pause();
    }

    /// <summary>A press on the enclosing viewer's scroll bar is a hand scroll (the bar's thumb and track are its descendants).</summary>
    private void ViewerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source || source.FindAncestorOfType<ScrollBar>(includeSelf: true) is null) return;
        _scrollBarHeld = true;
        NoteHandScroll();
    }

    private void ViewerReleased(object? sender, PointerReleasedEventArgs e) => _scrollBarHeld = false;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (this.FindAncestorOfType<ScrollViewer>() is not { } viewer) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl + wheel zooms one step per notch, about the pointer; the viewer does not also scroll.
            if (e.Delta.Y != 0) ZoomStep(Math.Sign(e.Delta.Y), e.GetPosition(viewer));
            e.Handled = true;
            return;
        }
        NoteHandScroll();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Shift + wheel moves sideways; a platform that already reports the wheel as a sideways delta is read the same way.
            var notches = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            EnsurePageOrigin(viewer, new Vector(viewer.Offset.X - notches * WheelScrollPixels, viewer.Offset.Y));
        }
        else EnsurePageOrigin(viewer, new Vector(viewer.Offset.X, Math.Max(0, viewer.Offset.Y - e.Delta.Y * WheelScrollPixels)));
        e.Handled = true;
    }
}
