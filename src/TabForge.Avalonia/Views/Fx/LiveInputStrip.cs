using System;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.LiveInput;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the slim line under the menu row of the live input FX window: the caption with the meters, the tuner and the levels (Gain, Vol, Pan, M, S) as whole groups in a
//   wrapping panel (one rounded row of one control height when wide; groups drop to the next row, never overlapping, when narrow); it wires them to the live input session and the engine, and runs the frame loop (one callback per screen refresh) that moves the
//   meters and the tuner while the window shows.
// Does not own: the parts (LiveStripMeters, LiveStripTuner, LiveStripKnobs), the rules (LiveStripRules, LiveTunerReadout, PeakHold), the stored values
//   (LiveInputSession) or the engine side (AudioEngineClient).
// Tests: TabForge.Avalonia.Tests.LiveInputStripTests, LiveStripSliderTests.
// Thread: UI thread only. A frame allocates nothing while the readings stay the same, and no part changes text or size per frame (drawn bars, a throttled fixed-width text).
/// <summary>The strip of the "FX: Live input" window: Input and Output meters, the tuner, and the Gain, Vol, Pan, Mute and Solo controls.</summary>
public sealed class LiveInputStrip : StripFrame
{
    public const string CaptionText = "Live input";
    private readonly LiveInputSession _session;
    private readonly AudioEngineClient? _engine;
    private readonly LiveTunerReadout _readout = new();
    private readonly AnimationFrameSource _frames;
    private readonly Action _onLevel;

    public LiveInputStrip(LiveInputSession session, AudioEngineClient? engine) : base("LiveInputStrip")
    {
        _session = session;
        _engine = engine;
        Meters = new LiveStripMeters();
        Tuner = new LiveStripTuner();
        Knobs = new LiveStripKnobs();
        Compose("LiveMetersGroup", Caption(CaptionText), Meters, null, Tuner, Knobs);
        _onLevel = ShowKnobs;
        Knobs.GainChanged += _session.SetGain;
        Knobs.VolumeChanged += volume => _session.SetLevel(volume, _session.Pan);
        Knobs.PanChanged += pan => _session.SetLevel(_session.Volume, pan);
        Knobs.MuteChanged += _session.SetMuted;
        Knobs.SoloChanged += _session.SetSolo;
        Tuner.WindowChosen += _session.SetTunerWindow;
        Tuner.Switch.Display(_session.TunerOn);
        Tuner.TunerSwitched += SwitchTuner;
        ShowKnobs();
        ShowPreferences();
        _frames = new AnimationFrameSource(AvaloniaUiThread.ForApplication(), () => TopLevel.GetTopLevel(this), () => 0);
        _frames.Frame += () => Tick(Environment.TickCount64, DateTime.UtcNow);
        AttachedToVisualTree += (_, _) =>
        {
            _session.LevelChanged -= _onLevel; _session.LevelChanged += _onLevel;
            ShowKnobs();
            if (Tuner.IsOn) _engine?.SetTunerWanted(this, true);
            _frames.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _frames.Stop();
            _session.LevelChanged -= _onLevel;
            _engine?.SetTunerWanted(this, false);   // the strip is gone: it no longer wants the tuner
        };
    }

    /// <summary>Capture mode only: the input and output peaks a frame reads instead of the engine peaks (null: the engine peaks).</summary>
    internal Func<(float In, float Out)>? PeakSource { get; set; }

    /// <summary>Capture mode only: the tuner reading a frame reads instead of the engine reading (null: the engine reading).</summary>
    internal Func<(float Hz, float Clarity, int Seq)>? TunerSource { get; set; }

    public LiveStripMeters Meters { get; }
    public LiveStripTuner Tuner { get; }
    public LiveStripKnobs Knobs { get; }

    /// <summary>One frame: the meters from the engine peaks, Preferences changes to the tuner, and the tuner reading while it is on. Public for the tests.</summary>
    public void Tick(double nowMs, DateTime utcNow)
    {
        var (input, output) = PeakSource?.Invoke() ?? (_engine?.LiveInputPeak ?? 0, _engine?.LiveOutputPeak ?? 0);
        Meters.Update(input, output, nowMs);
        ShowPreferences();
        if (!Tuner.IsOn || (TunerSource is null && _engine is not { IsRunning: true })) return;
        var (hz, clarity, seq) = TunerSource?.Invoke() ?? _engine!.TunerReading;
        if (_readout.Update(hz, clarity, seq, utcNow)) Tuner.Show(_readout, nowMs);
        else Tuner.Flush(nowMs);
    }

    private void ShowKnobs() => Knobs.Show(_session.GainDb, _session.Volume, _session.Pan, _session.Muted, _session.Soloed);

    /// <summary>The tuner follows Preferences: the A4 reference and the window (the dropdown shows the same value).</summary>
    private void ShowPreferences()
    {
        _readout.ReferenceHz = _session.TunerReferenceHz;
        Tuner.ShowWindow(_session.TunerWindowMs);
    }

    private void SwitchTuner(bool on)
    {
        _session.SetTunerOn(on);
        _engine?.SetTunerWanted(this, on);
        if (!on) Tuner.ShowOff();
    }
}
