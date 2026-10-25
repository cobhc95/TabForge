using TabForge.Audio;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.LiveInput;

/// <summary>What one window gives live input: its settings, the shared engine, the sync, the status line and its two prompts.</summary>
public interface ILiveInputSessionHost
{
    PluginSettings Settings { get; }
    AudioEngineClient Engine { get; }
    IUiThread UiThread { get; }
    void SyncAudioEngine();
    void SaveSettings();
    /// <summary>Saves like <see cref="SaveSettings"/> without the SettingsChanged broadcast (a live knob drag); a host without a quiet path saves in full.</summary>
    void SaveLevelQuietly() => SaveSettings();
    void SetStatus(string text);
    /// <summary>Asks "Restore your previous live FX chain?"; Remember is the "Remember my answer" box.</summary>
    (bool Restore, bool Remember) AskRestoreChain();
    /// <summary>Shows the live FX chain window (or brings it forward); <paramref name="closed"/> runs when that window closes.</summary>
    void ShowFxChain(Action closed);
}

// Owns: one window's live input logic without UI types: turning the program's live input on and off, the restore question's outcome,
//   the audio and MIDI input choices the menu changes, the live level, and saving the live FX chain with its plug-in states.
// Does not own: the program-wide arm and stand-in track (LiveInputArm), the engine sync (AudioEngineClient), the button, the menu and the
//   chain window (WPF LiveInput folder, Avalonia MainWindowLiveInput), the saved values (LiveInputSettings).
// Tests: LiveInputStripTests (WPF), TabForge.Avalonia.Tests.LiveInputSessionTests.
public sealed class LiveInputSession : IDisposable
{
    private const int StateTimeoutMs = 3000;
    public const string MidiPrefix = "MIDI: ", MidiAny = "Any available device";
    public const string OffText = "Live input off", NoInputText = "Live input off: no input chosen";
    private readonly ILiveInputSessionHost _host;
    private readonly LiveInputArm _arm;
    private readonly LiveInputMidi _midi;
    private bool _disposed, _syncQueued;

    public LiveInputSession(ILiveInputSessionHost host)
    {
        _host = host;
        _arm = host.Engine.LiveInput;
        _arm.Changed += OnArmChanged;
        _midi = _arm.Midi ??= new LiveInputMidi(host.Engine, _arm.Track, host.Engine.MidiInput, host.UiThread);
        _midi.Notice += _host.SetStatus;
        _arm.LevelChanged += OnLevelChanged;
        ApplySavedLevel();
    }

    /// <summary>Raised when live input turns on or off (from any window).</summary>
    public event Action? Changed;
    /// <summary>Raised when the live input's volume, pan or mute changes (from any window).</summary>
    public event Action? LevelChanged;
    /// <summary>Raised when a press could not turn live input on and the input menu should open instead.</summary>
    public event Action? MenuRequested;

    public bool IsOn => _arm.IsOn;
    /// <summary>The program's stand-in track (its rig is the live FX chain).</summary>
    public TrackModel Track => _arm.Track;
    private LiveInputSettings Saved => _host.Settings.LiveInput;

    /// <summary>The audio inputs the menu offers: the device's inputs shown for live input, and the saved one (also when hidden or missing).</summary>
    public IReadOnlyList<string> Inputs => AudioInputs.For(AudioInputUse.LiveInput, Saved.Input.Length > 0 ? Saved.Input : null);
    public string Input => Saved.Input;
    /// <summary>True with a MIDI choice ticked (beside the audio input or alone).</summary>
    public bool IsMidi => Saved.MidiDevice != null;
    /// <summary>The chosen MIDI device; empty: any available device; null: none.</summary>
    public string? MidiDevice => Saved.MidiDevice;
    private string MidiName => string.IsNullOrEmpty(Saved.MidiDevice) ? "MIDI keyboard" : Saved.MidiDevice;
    private bool HasAudio => Saved.Input.Length > 0;
    /// <summary>The chosen audio input when the running device lacks it (it stays chosen and is silent); null otherwise.</summary>
    private string? MissingAudio => HasAudio && !AudioInputs.IsAvailable(Saved.Input) ? Saved.Input : null;
    private string MissingText => $"{MissingAudio} is not available on {(AudioInputs.Device.Length > 0 ? AudioInputs.Device : "this audio device")}";
    private string WaitingText => $"Live input: {MidiName} is not connected; waiting for it";

    /// <summary>The menu's MIDI rows, from the devices connected now: any, each device, and the chosen one when it is not connected.</summary>
    public IReadOnlyList<(string Label, string Device)> MidiChoices()
    {
        var names = _midi.Devices().Distinct().ToList();
        var rows = new List<(string, string)> { (MidiPrefix + MidiAny, "") };
        rows.AddRange(names.Select(n => (MidiPrefix + n, n)));
        if (Saved.MidiDevice is { Length: > 0 } saved && !names.Contains(saved)) rows.Add((MidiPrefix + saved + " (not connected)", saved));
        return rows;
    }

    /// <summary>Ticks a MIDI row (an empty <paramref name="device"/> is any available device); the ticked row again unticks it. The audio choice stays.</summary>
    public void SetMidiInput(string device)
    {
        var off = Saved.MidiDevice == device;
        Saved.MidiDevice = off ? null : device;
        _host.SaveSettings();
        var heard = ApplyMidi();
        if (OffWithoutInput()) return;
        _host.SetStatus(off ? "Live input: MIDI off" : heard ? $"Live input: {MidiPrefix}{(device.Length == 0 ? MidiAny : device)}" : WaitingText);
    }

    /// <summary>Listens to the MIDI keyboard while live input is on with a MIDI choice, else closes the listener. False: on, MIDI, and the device is not connected.</summary>
    private bool ApplyMidi()
    {
        if (IsOn && IsMidi) return _midi.Start(Saved.MidiDevice);
        _midi.Stop();
        return true;
    }

    /// <summary>False when nothing chosen can be heard: no MIDI choice, and no audio input or ASIO with its inputs switched off.</summary>
    public bool HasUsableInput => IsMidi || HasAudio && MissingAudio is null && !(_host.Settings.Driver == AudioDrivers.Asio && !_host.Settings.AsioInputsEnabled);

    public bool OpenFxOnTurnOn
    {
        get => Saved.OpenFxOnTurnOn;
        set { Saved.OpenFxOnTurnOn = value; _host.SaveSettings(); }
    }

    /// <summary>Ticks an audio row (one at a time); the ticked row again leaves no audio input (the MIDI choice stays).</summary>
    public void SetInput(string input)
    {
        if (!Inputs.Contains(input)) return;
        var off = Saved.Input == input;
        Saved.Input = off ? "" : input;
        _host.SaveSettings();
        if (OffWithoutInput()) return;
        if (IsOn) _host.SyncAudioEngine();
        _host.SetStatus(off ? "Live input: audio input off" : $"Live input: {input}");
    }

    /// <summary>On with neither an audio input nor a MIDI device ticked: turns live input off and says why. True when it did.</summary>
    private bool OffWithoutInput()
    {
        if (!IsOn || IsMidi || HasAudio) return false;
        TurnOff();
        _host.SetStatus(NoInputText);
        return true;
    }

    /// <summary>The live input's own volume (0-127), pan (0-127, 64 centre) and mute, as the mixer's live input row shows them.</summary>
    public int Volume => _arm.Track.Volume;
    public int Pan => _arm.Track.Pan;
    public bool Muted => _arm.Track.Mute;
    /// <summary>True while the live input is soloed (session only): the song's tracks play only if soloed too.</summary>
    public bool Soloed => _arm.Solo;
    public int PluginCount => _arm.Track.Rig.Plugins.Count;

    /// <summary>Sets the live input's volume and pan: saved program-wide, sent to the engine at once while on.</summary>
    public void SetLevel(int volume, int pan)
    {
        Saved.Volume = Math.Clamp(volume, 0, MidiLimits.Max);
        Saved.Pan = Math.Clamp(pan, 0, MidiLimits.Max);
        _arm.SetLevel(Saved.Volume, Saved.Pan);
        _host.SaveLevelQuietly();
        SyncLevelSoon();
    }

    /// <summary>A knob drag moves many times a frame: the engine sync (the whole song's tracks) runs once per frame, not per move.</summary>
    private void SyncLevelSoon()
    {
        if (!IsOn || _syncQueued) return;
        _syncQueued = true;
        _host.UiThread.Post(() => { _syncQueued = false; if (!_disposed && IsOn) _host.SyncAudioEngine(); }, UiPriority.Render);
    }

    /// <summary>Mutes or unmutes the live input without turning it off (its volume is kept); saved program-wide.</summary>
    public void SetMuted(bool muted)
    {
        Saved.Muted = muted;
        _arm.SetMute(muted);
        _host.SaveLevelQuietly();
        SyncLevelSoon();
    }

    /// <summary>Solos or unsolos the live input (not saved); the engine follows at the next frame's sync.</summary>
    public void SetSolo(bool solo)
    {
        _arm.SetSolo(solo);
        SyncLevelSoon();
    }

    /// <summary>The gain in dB into the live FX chain (the strip's Gain knob); the input meter and the tuner stay on the raw input.</summary>
    public int GainDb => _arm.GainDb;

    /// <summary>Sets the gain: saved program-wide, sent to the engine at once while on.</summary>
    public void SetGain(int db)
    {
        Saved.GainDb = LiveStripRules.ClampGainDb(db);
        _arm.SetGain(Saved.GainDb);
        _host.SaveLevelQuietly();
        SyncLevelSoon();
    }

    /// <summary>The tuner's A4 reference (Hz) and analysis window (ms), as Preferences and the strip's menu hold them.</summary>
    public int TunerReferenceHz => Saved.TunerReferenceHz;
    public int TunerWindowMs => Saved.TunerWindowMs;

    /// <summary>Whether the strip's Tuner switch is on (the default); the choice is saved.</summary>
    public bool TunerOn => Saved.TunerOn;

    public void SetTunerOn(bool on)
    {
        if (on == Saved.TunerOn) return;
        Saved.TunerOn = on;
        _host.SaveLevelQuietly();
    }

    /// <summary>Sets the tuner's analysis window (one of <see cref="LiveStripRules.WindowChoicesMs"/>); saved, and sent to the engine while it runs.</summary>
    public void SetTunerWindow(int ms)
    {
        if (!LiveStripRules.WindowChoicesMs.Contains(ms) || ms == Saved.TunerWindowMs) return;
        Saved.TunerWindowMs = ms;
        _host.SaveLevelQuietly();
        if (_host.Engine.IsRunning) _host.SyncAudioEngine();
    }

    /// <summary>The saved volume, pan, gain and mute go to the stand-in: at the start, and after Reset all settings (the engine follows while on).</summary>
    public void ApplySavedLevel()
    {
        if (_arm.Track.Volume == Saved.Volume && _arm.Track.Pan == Saved.Pan && _arm.GainDb == Saved.GainDb && _arm.Track.Mute == Saved.Muted) return;
        _arm.SetLevel(Saved.Volume, Saved.Pan);
        _arm.SetGain(Saved.GainDb);
        _arm.SetMute(Saved.Muted);
        if (IsOn) _host.SyncAudioEngine();
    }

    /// <summary>True hides the live input row under the mixer's Master row (saved with the app settings).</summary>
    public bool MixerCollapsed
    {
        get => Saved.MixerCollapsed;
        set { Saved.MixerCollapsed = value; _host.SaveSettings(); }
    }

    public void Toggle()
    {
        if (IsOn) TurnOff();
        else if (HasUsableInput) TurnOn();
        else
        {
            _host.SetStatus(MissingAudio is not null ? $"Live input: no usable input ({MissingText}). Choose another audio input or a MIDI device from the menu."
                : "Live input: no usable input (the driver's inputs are off, or no input is chosen). Choose an audio input or a MIDI device from the menu, or open the audio device settings.");
            MenuRequested?.Invoke();
        }
    }

    public void TurnOn()
    {
        if (IsOn) return;
        RestoreChain();
        _arm.Arm();
        _host.SyncAudioEngine();
        var heard = ApplyMidi();
        if (Saved.OpenFxOnTurnOn) OpenFx();
        var what = !IsMidi ? Saved.Input : HasAudio ? $"{Saved.Input} + MIDI" : "MIDI";   // short: the status bar cell is narrow
        _host.SetStatus(!heard ? WaitingText
            : MissingAudio is not null ? $"Live input on: {MissingText}: the audio input is silent"
            : IsMidi && !MixerGroups.InstrumentPlays(_arm.Track) ? "Live input: add an instrument plug-in to hear the MIDI keyboard"
            : $"Live input on: {what}");
    }

    /// <summary>True when turning on or opening the chain would ask the restore question now (an app with only async prompts asks first).</summary>
    public bool RestoreQuestionPending => _arm.RestoreDecided != Saved.Restore && _arm.Track.Rig.Plugins.Count == 0 && Saved.Chain.Plugins.Count > 0
        && Saved.Restore == LiveInputSettings.RestoreAsk;

    /// <summary>An empty session chain and a saved one with plug-ins: Always restores, Never does not, Ask asks once per run (again after the choice was changed).</summary>
    public void RestoreChain()
    {
        var track = _arm.Track;
        if (_arm.RestoreDecided == Saved.Restore || track.Rig.Plugins.Count > 0 || Saved.Chain.Plugins.Count == 0) return;
        var restore = Saved.Restore == LiveInputSettings.RestoreAlways;
        if (Saved.Restore == LiveInputSettings.RestoreAsk)
        {
            (restore, var remember) = _host.AskRestoreChain();
            if (remember) { Saved.Restore = restore ? LiveInputSettings.RestoreAlways : LiveInputSettings.RestoreNever; _host.SaveSettings(); }
        }
        _arm.RestoreDecided = Saved.Restore;
        if (restore) track.Rig = Saved.Chain;
    }

    /// <summary>Disarms at once; the chain keeps its plug-ins. Their states are read before the engine lets the chain go.</summary>
    public async void TurnOff()
    {
        if (!IsOn) return;
        _midi.Stop();   // held notes are let go while the stand-in still has its slot
        _arm.Disarm();
        _arm.SetSolo(false);   // a solo belongs to the live strip: off, the song's tracks must not stay gated by it
        _host.SyncAudioEngine();
        _host.SetStatus(OffText);
        await SaveStatesAsync();
        if (IsOn) return;   // turned on again meanwhile
        _arm.Release();   // the next sync, from any window, takes the stand-in out
        if (!_disposed) _host.SyncAudioEngine();
    }

    /// <summary>While the chain is in the engine: reads its plug-ins' states into it and saves it (bounded; never throws).</summary>
    public async Task SaveStatesAsync()
    {
        var track = _arm.Track;
        try
        {
            if (track.Rig.Plugins.Count == 0 || !_host.Engine.IsRunning || _host.Engine.SlotOf(track) < 0) return;
            await _host.Engine.CollectStatesAsync(new[] { track }, StateTimeoutMs);
            if (_disposed) return;   // the window closed meanwhile: its closing saved, and the states are in the chain
            SaveChain();
        }
        catch (Exception ex) { Trace.Error(Trace.Engine, "live input: plug-in states: " + ex.Message); }   // logged: a state read failure must not end the turn-off
    }

    /// <summary>
    /// The window has closed with live input on. The last window reads the chain's plug-in states (waiting at most the limit) and saves
    /// it; with another window open live input stays on and that window's close does it.
    /// </summary>
    public void SaveStatesOnClose(bool lastWindow)
    {
        var track = _arm.Track;
        try
        {
            if (lastWindow) _midi.Stop();
            if (!lastWindow || !IsOn || track.Rig.Plugins.Count == 0 || !_host.Engine.IsRunning) return;
            _host.Engine.CollectStates(new[] { track }, StateTimeoutMs);
            SaveChain();
        }
        catch (Exception ex) { Trace.Error(Trace.Engine, "live input: plug-in states on close: " + ex.Message); }   // logged: closing must go on
    }

    /// <summary>Opens the live FX chain window without changing the armed state.</summary>
    public void OpenFx()
    {
        RestoreChain();   // before the chain can be edited: an edit saves it over the previous one
        _host.ShowFxChain(() => _ = SaveStatesAsync());
        if (!IsOn) _host.SetStatus("Live input is off: turn it on to hear the chain");
    }

    /// <summary>From the first change on, the session's chain is the saved one.</summary>
    public void SaveChain()
    {
        Saved.Chain = _arm.Track.Rig;
        _host.SaveSettings();
    }

    private void OnArmChanged() => Changed?.Invoke();

    private void OnLevelChanged() => LevelChanged?.Invoke();

    /// <summary>The window closed. Live input belongs to the program, so it stays as it is, except its solo (the strip is gone) and a sync still queued, which is flushed first.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            var soloed = _arm.Solo;
            if (soloed) _arm.SetSolo(false);
            if (soloed || (_syncQueued && IsOn)) _host.SyncAudioEngine();
        }
        _disposed = true; _arm.Changed -= OnArmChanged; _arm.LevelChanged -= OnLevelChanged; _midi.Notice -= _host.SetStatus;
    }
}
