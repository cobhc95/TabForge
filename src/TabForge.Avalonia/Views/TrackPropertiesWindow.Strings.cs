using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The string rows of the tuning card and what a string does when it is clicked: the note edits, one ring with a decaying vibrate, the speaker repeat.
/// Owns: the list of rows, the ring loop timer and the vibrate frame. Does not own: the tuning (TrackTuningDraft), the row's controls (TrackStringRow),
/// the sound (IStringRinger, given by the host; none means silent) or the ring rules (StringRingLoop, StringVibration).
/// Threads: UI thread only; a ring never waits on the engine, and the vibrate moves on display frames (one request per frame, no allocation).
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests.
/// </summary>
public sealed partial class TrackPropertiesWindow
{
    private readonly StackPanel _stringRows = CreateStringRows();

    private static StackPanel CreateStringRows()
    {
        var panel = new StackPanel { Name = "StringRows" };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(panel, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringRowsGap);
        return panel;
    }
    private readonly List<TrackStringRow> _rows = new();
    private readonly StringRingLoop _loop = new();
    private readonly IStringRinger? _ringer;
    private DispatcherTimer? _loopTimer;
    private Action<TimeSpan>? _onFrame;
    private int _vibratingRow = -1;
    private long _vibrateStart;
    private bool _frameQueued;

    private bool Light => ActualThemeVariant == ThemeVariant.Light;

    // Redraws every string row and the preset label for the working strings (a structure edit, a preset or a reloaded instrument).
    private void RebuildStrings()
    {
        RebuildStringRows();
        SyncTuningHeader();
        SyncCapoCard();
    }

    private void RebuildStringRows()
    {
        StopVibrating();
        _stringRows.Children.Clear();
        _rows.Clear();
        if (_loop.Looping is { } looping && looping >= _draft.Count) StopRinging();
        for (var i = 0; i < _draft.Count; i++)
        {
            var row = new TrackStringRow(i, StepString, TypeNote, RingString, ToggleLoop);
            row.Show(_draft.Tunings[i], Light);
            row.SetLooping(_loop.Looping == i);
            _rows.Add(row);
            _stringRows.Children.Add(row);
        }
    }

    // Only the notes and lines of the rows, after a retune that kept the strings (All -1 / All +1).
    private void ShowAllRows()
    {
        for (var i = 0; i < _rows.Count; i++) _rows[i].Show(_draft.Tunings[i], Light);
        SyncTuningHeader();
    }

    private void StepString(int index, int delta)
    {
        _draft.Step(index, delta);
        _rows[index].Show(_draft.Tunings[index], Light);
        SyncTuningHeader();
    }

    private bool TypeNote(int index, string text)
    {
        if (!TrackPropertiesPresenter.TryParseNote(text, out var midi)) return false;
        _draft.Set(index, midi);
        _rows[index].Show(_draft.Tunings[index], Light);
        SyncTuningHeader();
        return true;
    }

    /// <summary>The sounding pitch of an open string: its tuning plus the working effective capo (the full capo or a higher partial covering it).</summary>
    private int SoundingPitch(int index) => System.Math.Clamp(_draft.Tunings[index] + WorkingEffectiveCapo(index), 0, MidiLimits.Max);

    // One ring: the sound goes out at once and the string starts to vibrate.
    internal void RingString(int index)
    {
        _ringer?.Ring(SoundingPitch(index));
        StartVibrating(index);
    }

    // The speaker toggle: the string repeats its ring until toggled off; turning one on turns the previous one off.
    private void ToggleLoop(int index)
    {
        var on = _loop.Toggle(index);
        foreach (var row in _rows) row.SetLooping(row.Index == _loop.Looping);
        if (!on) { _loopTimer?.Stop(); _ringer?.Silence(); return; }
        RingString(index);
        if (_loopTimer is null)
        {
            _loopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StringRingLoop.RepeatMs) };
            _loopTimer.Tick += (_, _) => { if (_loop.Looping is { } i && i < _rows.Count) RingString(i); };
        }
        _loopTimer.Start();
    }

    private void StopRinging()
    {
        _loop.Stop();
        foreach (var row in _rows) row.SetLooping(false);
        _loopTimer?.Stop();
        _ringer?.Silence();
        StopVibrating();
    }

    private void StartVibrating(int index)
    {
        StopVibrating();
        _vibratingRow = index;
        _rows[index].Glow(1);
        _vibrateStart = Stopwatch.GetTimestamp();
        if (_frameQueued) return;
        _frameQueued = true;
        RequestAnimationFrame(_onFrame ??= OnVibrateFrame);
    }

    private void OnVibrateFrame(TimeSpan time)
    {
        _frameQueued = false;
        if (_vibratingRow < 0 || _vibratingRow >= _rows.Count) return;
        var elapsed = Stopwatch.GetElapsedTime(_vibrateStart).TotalMilliseconds;
        if (elapsed >= StringVibration.RingMs) { StopVibrating(); return; }
        _rows[_vibratingRow].Vibrate(StringVibration.Offset(elapsed));
        _rows[_vibratingRow].Glow(1 - elapsed / StringVibration.RingMs);   // the strike light fades as the string rings out
        _frameQueued = true;
        RequestAnimationFrame(_onFrame ??= OnVibrateFrame);
    }

    private void StopVibrating()
    {
        if (_vibratingRow >= 0 && _vibratingRow < _rows.Count) { _rows[_vibratingRow].Vibrate(0); _rows[_vibratingRow].Glow(0); }
        _vibratingRow = -1;
    }

    /// <summary>Closing the window stops the repeat, the sound and the vibrate.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        StopRinging();
        base.OnClosed(e);
    }
}
