using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the slim strip in the menu row of a track's FX window: the track name with its meters, the Options dropdown (given by the window), the tuner (LiveStripTuner, as in the live input strip), the Vol and Pan sliders and the M and S buttons
//   (the track's own values: the same model fields the track list shows), and the frame loop (one callback per screen refresh) that moves the meters and the tuner while the window shows.
//   The engine runs one tuner on the armed audio input; every window that wants it counts for itself, and all read that one reading. A track that is not armed on an audio input has none: the block shows "--" and the switch is off.
// Does not own: the options and what they do (FxChainWindow.Options.cs), the parts (LiveStripMeters, LiveStripKnobs), the frame (StripFrame) or the edit path (the host).
// Tests: TabForge.Avalonia.Tests.TrackFxStripTests.
// Thread: UI thread only. A frame allocates nothing; a slider drag changes the model live and syncs the engine at the preview interval, and makes one undo step on release.
/// <summary>The strip of a track's FX window. M and S act on this track only (not on the tracks selected in the track list). A song track has no input gain, so Gain is hidden.</summary>
public sealed class TrackFxStrip : StripFrame
{
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly AnimationFrameSource _frames;
    private readonly SliderChangeCoalescer _sync;
    private UndoSnapshot? _since;
    private bool _dragged;
    private readonly TextBlock _name;
    private readonly LiveTunerReadout _readout = new();
    private bool _hadInput = true;

    public TrackFxStrip(IFxChainHost host, TrackModel track, Control options) : base("TrackFxStrip")
    {
        _host = host;
        _track = track;
        Meters = new LiveStripMeters();
        Knobs = new LiveStripKnobs("Track");
        Knobs.HideGain();
        var name = _name = Caption(track.Name);
        name.MaxWidth = ChromeMetrics.LiveStripNameMaxWidth;
        name.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(name, track.Name);
        ToolTip.SetTip(Knobs.Mute, "Mute this track");
        ToolTip.SetTip(Knobs.Solo, "Solo this track");
        Tuner = new LiveStripTuner();
        Compose("TrackNameGroup", name, Meters, options, Tuner, Knobs);
        Tuner.Switch.Display(!_host.PluginSettings.TunerOffTracks.Contains(_track.Id));
        Tuner.TunerSwitched += SwitchTuner;
        Tuner.WindowChosen += ChooseWindow;
        _sync = new SliderChangeCoalescer(() => _host.SyncEngine(_track));
        Knobs.VolumeChanged += volume => Level(Knobs.Volume, () => _track.Volume = volume);
        Knobs.PanChanged += pan => Level(Knobs.Pan, () => _track.Pan = pan);
        Knobs.Volume.Released += EndDrag;
        Knobs.Pan.Released += EndDrag;
        Knobs.MuteChanged += on => _host.Edit(_track, () => _track.Mute = on);
        Knobs.SoloChanged += on => _host.Edit(_track, () => _track.Solo = on);
        Show();
        ToolTip.SetShowOnDisabled(Tuner.Switch, true);
        _frames = new AnimationFrameSource(AvaloniaUiThread.ForApplication(), () => TopLevel.GetTopLevel(this), () => 0);
        _frames.Frame += () => Tick(Environment.TickCount64, DateTime.UtcNow);
        AttachedToVisualTree += (_, _) => { Show(); WantTuner(); _frames.Start(); };
        DetachedFromVisualTree += (_, _) =>
        {
            _frames.Stop();
            _host.Engine?.SetTunerWanted(this, false);   // the window is gone: it no longer wants the tuner
            _sync.Cancel();   // a drag cut short by the window closing leaves no pending engine sync
            _dragged = false;
            _since = null;
        };
    }

    public LiveStripMeters Meters { get; }
    public LiveStripKnobs Knobs { get; }
    public LiveStripTuner Tuner { get; }

    /// <summary>The tuner listens to the armed audio input of the track.</summary>
    public bool HasInput => _track.RecordArm && !AudioInputs.IsMidi(_track.AudioInput);

    /// <summary>Moves the sliders and buttons to the track's values (it changed elsewhere: the track list, an undo); a slider being dragged is left alone.</summary>
    public void Show()
    {
        if (_name.Text != _track.Name) { _name.Text = _track.Name; ToolTip.SetTip(_name, _track.Name); }
        if (Knobs.Volume.IsDragging || Knobs.Pan.IsDragging) return;
        Knobs.Show(0, _track.Volume, _track.Pan, _track.Mute, _track.Solo);
    }

    /// <summary>One frame: the meters from the engine peaks and, while the tuner is on and the track has an input, the tuner reading. Public for the tests.</summary>
    public void Tick(double nowMs, DateTime utcNow)
    {
        var engine = _host.Engine;
        Meters.Update(HasInput ? engine?.InputPeakOf(_track) ?? 0 : 0, engine?.PeakOf(_track) ?? 0, nowMs);
        var live = _host.PluginSettings.LiveInput;
        _readout.ReferenceHz = live.TunerReferenceHz;
        Tuner.ShowWindow(live.TunerWindowMs);
        if (HasInput != _hadInput) WantTuner();
        if (!_hadInput || !Tuner.IsOn || engine is not { IsRunning: true }) return;
        var (hz, clarity, seq) = engine.TunerReading;
        if (_readout.Update(hz, clarity, seq, utcNow)) Tuner.Show(_readout, nowMs);
        else Tuner.Flush(nowMs);
    }

    // The engine hears the tuner wanted only while the switch is on and the track has an audio input; without one the block reads "--" and the switch cannot be used.
    private void WantTuner()
    {
        _hadInput = HasInput;
        Tuner.Switch.IsEnabled = _hadInput;
        Tuner.Switch.Opacity = _hadInput ? 1 : 0.4;
        ToolTip.SetTip(Tuner.Switch, _hadInput ? "Tune by this track's audio input, before the FX chain" : "No audio input on this track");
        _host.Engine?.SetTunerWanted(this, _hadInput && Tuner.IsOn);
        if (!_hadInput || !Tuner.IsOn) Tuner.ShowOff();
    }

    private void SwitchTuner(bool on)
    {
        var off = _host.PluginSettings.TunerOffTracks;
        if (on) off.Remove(_track.Id); else if (!off.Contains(_track.Id)) off.Add(_track.Id);
        _host.SaveSettingsQuietly();
        WantTuner();
    }

    // The analysis window is the app's one tuner setting (Preferences and the live input strip show the same value).
    private void ChooseWindow(int ms)
    {
        var live = _host.PluginSettings.LiveInput;
        if (ms == live.TunerWindowMs) return;
        live.TunerWindowMs = ms;
        _host.SaveSettingsQuietly();
        _host.SyncEngine(_track);
    }

    // A drag changes the model live and syncs the engine at the preview interval; the undo step is made once, on release. Any other change (a click, a reset) is its own step.
    private void Level(MiniSlider slider, Action set)
    {
        if (!slider.IsDragging) { _host.Edit(_track, set); return; }
        if (!_dragged) { _dragged = true; _since = _host.Checkpoint(); }
        set();
        _sync.SliderChanged();
    }

    private void EndDrag()
    {
        if (!_dragged) return;
        _dragged = false;
        var since = _since;
        _since = null;
        _host.Edit(_track, () => { }, since);
    }
}
