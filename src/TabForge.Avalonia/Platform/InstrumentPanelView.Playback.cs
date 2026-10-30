using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the playing notes of the panel (ShowPlayback: now, released, next, upcoming and recent notes from InstrumentPlaybackLists), the hand's
//       movement between the playing shapes, and the brightness of the recent and upcoming notes.
// Does not own: the drawing of the lit notes (InstrumentPanelView.cs, DrawPlayingNotes), the playback clock or audio.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests, TabForge.Avalonia.Tests.InstrumentPlaybackListsTests.
public sealed partial class InstrumentPanelView
{
    /// <summary>Shows the notes playback lights at a song time, replacing the score cursor's notes while it shows them. Null clears them.</summary>
    /// <remarks>Repaints only when the lists change (a note starts, ends or leaves the hold), not on every frame.</remarks>
    // A released note fades with the song time. Its repaint waits this much song time after the last one, so the fade repaints 30 times a second.
    private const double FadeRepaintIntervalMs = 1000.0 / 30;
    private double _lastFadeRepaintMs = double.NegativeInfinity;

    /// <summary>Whether the fade repaints at <paramref name="nowMs"/>: at least <see cref="FadeRepaintIntervalMs"/> of song time from the last repaint, either way (a seek moves it back).</summary>
    internal static bool FadeRepaintDue(double lastRepaintMs, double nowMs) => Math.Abs(nowMs - lastRepaintMs) >= FadeRepaintIntervalMs;

    public void ShowPlayback(NoteEvent[]? notes, double nowMs)
    {
        if (notes is null)
        {
            if (!_playbackShown) return;
            _playbackShown = false;
            _playing.Clear();
            _movement = new();
            InvalidateVisual();
            return;
        }
        // Sounding notes only when the look has no look-ahead (the drum map always, melodic views when preview notes are off):
        // a negative horizon skips the Next/Upcoming lists (and their work) entirely.
        InstrumentPlaybackLists.Fill(_scratch, notes, nowMs, _kind == InstrumentPanelKind.Drums ? -1 : _look.PreviewHorizon,
            includeRecent: _look.PreviewPrevious);
        _playbackNowMs = nowMs;
        if (_playbackShown && _scratch.SameNotesAs(_playing))
        {
            // The recent notes fade with the playback time: they repaint while any is shown, at most 30 times a second.
            if (_playing.Recent.Count > 0 && FadeRepaintDue(_lastFadeRepaintMs, nowMs))
            {
                _lastFadeRepaintMs = nowMs;
                InvalidateVisual();
            }
            return;
        }
        (_playing, _scratch) = (_scratch, _playing);
        _movement = MovementOf(_playing);
        _playbackShown = true;
        _lastFadeRepaintMs = nowMs;
        InvalidateVisual();
    }

    // The hand's movement between the playing shapes. It depends only on the playing notes, so it is computed when they change, not on each frame.
    private List<FretboardMovementRules.Move> _movement = new();

    private static List<FretboardMovementRules.Move> MovementOf(InstrumentPlaybackSnapshot playing) =>
        FretboardMovementRules.Between(playing.Now, playing.Released, playing.Next);

    internal IEnumerable<int> LitMidis()
    {
        if (!_playbackShown)
        {
            foreach (var n in _lit) if (!n.Dead) yield return n.Midi;
            yield break;
        }
        foreach (var n in PlayingNotes()) yield return n.Midi;
    }

    private IEnumerable<NoteEvent> PlayingNotes()
    {
        foreach (var n in _playing.Now) yield return n;
        foreach (var n in _playing.Released) yield return n;
        // Sounding notes only without the look-ahead (the drum map always, melodic views while preview notes are off).
        if (_kind == InstrumentPanelKind.Drums || _look.PreviewHorizon < 0) { foreach (var n in _playing.Recent) yield return n; yield break; }
        foreach (var n in _playing.Next) yield return n;
        foreach (var n in _playing.Upcoming) yield return n;
        foreach (var n in _playing.Recent) yield return n;
    }

    /// <summary>How bright a recently released note is: quick at first, then a faint trace while it is still held.</summary>
    private double RecentBrightness(NoteEvent note)
    {
        var age = _playbackNowMs - note.EndMs;
        var fast = Math.Exp(-age / ReleaseFadeMs);
        var slow = Math.Max(0, 1.0 - age / InstrumentPlaybackLists.DefaultHoldMs);
        return Math.Clamp(QuickReleaseWeight * fast + TraceWeight * slow, RecentFloor, RecentCeiling);
    }

    /// <summary>How bright an upcoming movement is: its depth past the next movement fades it.</summary>
    private static double UpcomingBrightness(int depth) => Math.Max(UpcomingFloor, UpcomingStart - depth * UpcomingStep);
}
