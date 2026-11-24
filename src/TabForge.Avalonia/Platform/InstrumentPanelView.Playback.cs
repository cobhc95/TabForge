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
    // A released note fades with the song time. Its repaint waits this much song time after the last one, so the fade repaints 30 times a second.
    private const double FadeRepaintIntervalMs = 1000.0 / 30;
    private double _lastFadeRepaintMs = double.NegativeInfinity;

    /// <summary>Whether the fade repaints at <paramref name="nowMs"/>: at least <see cref="FadeRepaintIntervalMs"/> of song time from the last repaint, either way (a seek moves it back).</summary>
    internal static bool FadeRepaintDue(double lastRepaintMs, double nowMs) => Math.Abs(nowMs - lastRepaintMs) >= FadeRepaintIntervalMs;

    // Membership cache: the note lists change only at an onset, a sounding end, or a recent expiry. Reused while moving forward before that boundary; vibration and fade still follow the clock. Reset on seek back, loop wrap, track, look or stream change (a new array reference).
    private NoteEvent[]? _cachedNotes;
    private double _cachedFillMs = double.NaN;
    private double _nextChangeMs = double.PositiveInfinity;
    private int _cachedHorizon;
    private bool _cachedIncludeRecent;
    private int _membershipFillCount;

    private void InvalidateMembershipCache()
    {
        _cachedNotes = null;
        _cachedFillMs = double.NaN;
        _nextChangeMs = double.PositiveInfinity;
    }

    public void ShowPlayback(NoteEvent[]? notes, double nowMs, bool animateStrings = false)
    {
        if (notes is null)
        {
            InvalidateMembershipCache();
            if (!_playbackShown) return;
            _playbackShown = false;
            _playing.Clear();
            _movement = new();
            _vibratePhaseMs = -1;
            InvalidateVisual();
            return;
        }
        var horizon = _kind == InstrumentPanelKind.Drums ? -1 : _look.PreviewHorizon;
        var includeRecent = _look.PreviewPrevious;
        var cacheHit = _playbackShown && ReferenceEquals(notes, _cachedNotes) && horizon == _cachedHorizon && includeRecent == _cachedIncludeRecent
            && nowMs >= _cachedFillMs && nowMs + InstrumentPlaybackLists.OnsetToleranceMs < _nextChangeMs;
        if (cacheHit)
        {
            // Membership is unchanged: only the clock-driven vibration and recent fade move.
            var reuseAnimate = animateStrings && IsEffectivelyVisible && _kind == InstrumentPanelKind.Fretboard
                && _look.VibrationPeak > 0 && _playing.Now.Count > 0;
            var reuseVibration = reuseAnimate || _vibratePhaseMs >= 0;
            _vibratePhaseMs = reuseAnimate ? 0 : -1;
            _playbackNowMs = nowMs;
            if (_playing.Recent.Count > 0 && FadeRepaintDue(_lastFadeRepaintMs, nowMs))
            {
                _lastFadeRepaintMs = nowMs;
                reuseVibration = true;
            }
            if (reuseVibration) InvalidateVisual();
            return;
        }
        // Sounding notes only when the look has no look-ahead (the drum map always, melodic views when preview notes are off):
        // a negative horizon skips the Next/Upcoming lists (and their work) entirely.
        InstrumentPlaybackLists.Fill(_scratch, notes, nowMs, horizon,
            includeRecent: includeRecent);
        _membershipFillCount++;
        var animate = animateStrings && IsEffectivelyVisible && _kind == InstrumentPanelKind.Fretboard
            && _look.VibrationPeak > 0 && _scratch.Now.Count > 0;
        var vibrationChanged = animate || _vibratePhaseMs >= 0;
        _vibratePhaseMs = animate ? 0 : -1;
        _playbackNowMs = nowMs;
        if (_playbackShown && _scratch.SameNotesAs(_playing))
        {
            _cachedNotes = notes;
            _cachedFillMs = nowMs;
            _cachedHorizon = horizon;
            _cachedIncludeRecent = includeRecent;
            _nextChangeMs = InstrumentPlaybackLists.NextChangeMs(_playing, notes, nowMs);
            // The recent notes fade with the playback time: they repaint while any is shown, at most 30 times a second.
            if (_playing.Recent.Count > 0 && FadeRepaintDue(_lastFadeRepaintMs, nowMs))
            {
                _lastFadeRepaintMs = nowMs;
                vibrationChanged = true;
            }
            if (vibrationChanged) InvalidateVisual();
            return;
        }
        (_playing, _scratch) = (_scratch, _playing);
        _movement = MovementOf(_playing);
        _playbackShown = true;
        _lastFadeRepaintMs = nowMs;
        _cachedNotes = notes;
        _cachedFillMs = nowMs;
        _cachedHorizon = horizon;
        _cachedIncludeRecent = includeRecent;
        _nextChangeMs = InstrumentPlaybackLists.NextChangeMs(_playing, notes, nowMs);
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
