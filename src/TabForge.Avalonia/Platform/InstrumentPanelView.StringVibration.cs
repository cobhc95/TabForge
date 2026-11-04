using System;
using System.Diagnostics;
using Avalonia.Controls;
using TabForge.Playback;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the fretboard's sounding-string vibration: a monitor-refresh frame loop (TopLevel.RequestAnimationFrame)
// while a fretboard shows sounding notes, driving the strings' sideways offsets for the notes' duration.
// The looks come from Core FretboardStringRing: a soft blur over the note's length and a very slight movement;
// frames invalidate only (no per-frame allocation, no text changes: labels stay put).
// Does not own: the curves (Core FretboardStringRing), the playing lists (InstrumentPanelView.Playback.cs) or the drawing.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
// Threads: UI thread only.
public sealed partial class InstrumentPanelView
{
    /// <summary>Milliseconds since the vibration epoch (the last list change) at the latest frame; negative when no frame ran yet.</summary>
    private double _vibratePhaseMs = -1;
    private TimeSpan _vibrateEpoch;
    private bool _vibrateEpochSet;
    private bool _frameQueued;
    private Action<TimeSpan>? _onFrame;
    private readonly (long Started, int Fret)[] _previewRings = new (long, int)[12];

    internal void StartPreviewVibration(InstrumentNotePick pick)
    {
        if (pick.StringIndex is not { } s || pick.Fret is not { } fret || s < 0 || s >= _previewRings.Length) return;
        _previewRings[s] = (Stopwatch.GetTimestamp(), fret);
        KickVibration();
        InvalidateVisual();
    }

    private (double Offset, int Fret, double Blur)? PreviewVibration(int stringIndex)
    {
        if (stringIndex < 0 || stringIndex >= _previewRings.Length) return null;
        var ring = _previewRings[stringIndex];
        if (ring.Started == 0) return null;
        var elapsed = Stopwatch.GetElapsedTime(ring.Started).TotalMilliseconds * _look.VibrationRate;
        if (elapsed >= StringVibration.RingMs) { _previewRings[stringIndex] = default; return null; }
        return (FretboardStringRing.Motion(elapsed, _look.VibrationPeak), ring.Fret, FretboardStringRing.Blur(elapsed, StringVibration.RingMs, _look.VibrationPeak));
    }

    private bool HasPreviewVibration()
    {
        for (var i = 0; i < _previewRings.Length; i++)
            if (PreviewVibration(i) is not null) return true;
        return false;
    }

    private bool HasVibration => IsEffectivelyVisible && _kind == InstrumentPanelKind.Fretboard && _look.VibrationPeak > 0
        && ((_playbackShown && _playing.Now.Count > 0) || HasPreviewVibration());

    /// <summary>Restarts the vibration epoch after the playing lists changed and asks for frames while sounding notes show.</summary>
    private void NoteListsChanged()
    {
        _vibrateEpochSet = false;
        _vibratePhaseMs = -1;
        KickVibration();
    }

    /// <summary>Asks for the next display frame while a fretboard shows sounding notes; at most one request is ever queued.</summary>
    private void KickVibration()
    {
        if (_frameQueued || !HasVibration) return;
        if (TopLevel.GetTopLevel(this) is null) return;
        _frameQueued = true;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_onFrame ??= OnVibrateFrame);
    }

    private void OnVibrateFrame(TimeSpan time)
    {
        _frameQueued = false;
        if (!HasVibration)
        {
            _vibrateEpochSet = false;
            _vibratePhaseMs = -1;
            return;
        }
        if (!_vibrateEpochSet) { _vibrateEpoch = time; _vibrateEpochSet = true; }
        _vibratePhaseMs = (time - _vibrateEpoch).TotalMilliseconds;
        InvalidateVisual();
        if (TopLevel.GetTopLevel(this) is { } top && !_frameQueued)
        {
            _frameQueued = true;
            top.RequestAnimationFrame(_onFrame ??= OnVibrateFrame);
        }
    }

    /// <summary>The string's very slight sideways offset for a sounding note, over the note's
    /// duration only (zero before its onset and after it ends), driven by the latest frame's phase.</summary>
    private double VibrationOffset(NoteEvent note)
    {
        if (_vibratePhaseMs < 0) return 0;
        var elapsed = (_playbackNowMs - note.OnsetMs) + _vibratePhaseMs;
        if (elapsed < 0 || elapsed > note.DurationMs || _look.VibrationPeak <= 0) return 0;
        var curveMs = elapsed * _look.VibrationRate;
        return FretboardStringRing.Motion(curveMs, _look.VibrationPeak);
    }

    /// <summary>The string's blur for a sounding note, from the note's own length and whether it is palm-muted.</summary>
    private double RingBlur(NoteEvent note)
    {
        if (_vibratePhaseMs < 0) return 0;
        return FretboardStringRing.Blur((_playbackNowMs - note.OnsetMs) + _vibratePhaseMs, note.DurationMs, _look.VibrationPeak, _look.VibrationRate, note.Technique == TechniqueTag.PalmMute);
    }

    /// <summary>Sets the frame phase directly; tests drive it without a display frame.</summary>
    internal void TestSetVibrationPhase(double phaseMs) => _vibratePhaseMs = phaseMs;
}
