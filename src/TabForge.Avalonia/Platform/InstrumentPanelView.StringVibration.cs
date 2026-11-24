using System;
using System.Diagnostics;
using Avalonia.Controls;
using TabForge.Playback;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the finite frame loop for auditioned notes and the vibration drawn at the current playback time.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
// Threads: UI thread only.
public sealed partial class InstrumentPanelView
{
    // Negative while the fretboard is showing a stationary preview.
    private double _vibratePhaseMs = -1;
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

    private void KickVibration()
    {
        if (_frameQueued || !IsEffectivelyVisible || _kind != InstrumentPanelKind.Fretboard || _look.VibrationPeak <= 0
            || !HasPreviewVibration() || TopLevel.GetTopLevel(this) is not { } top) return;
        _frameQueued = true;
        top.RequestAnimationFrame(_onFrame ??= OnVibrateFrame);
    }

    private void OnVibrateFrame(TimeSpan _)
    {
        _frameQueued = false;
        if (IsEffectivelyVisible) InvalidateVisual();   // clear the last blur even when the preview has just ended
        KickVibration();
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
