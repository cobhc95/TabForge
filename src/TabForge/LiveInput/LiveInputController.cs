using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.LiveInput;

/// <summary>What live input needs from its window.</summary>
internal interface ILiveInputHost
{
    /// <summary>The shown song's tracks (the wiring window lists them).</summary>
    IReadOnlyList<TrackModel> Tracks { get; }
    PluginSettings Settings { get; }
    AudioEngineClient Engine { get; }
    IntPtr OwnerWindow { get; }
    void SyncAudioEngine();
    void SaveSettings();
    void SetStatus(string text);
    void OpenAudioSettings();
    /// <summary>Shows the live FX chain window (or brings it forward); <paramref name="closed"/> runs when that window closes.</summary>
    void ShowFxChain(TrackModel track, IFxChainHost fx, Action closed);
    /// <summary>Asks "Restore your previous live FX chain?"; Remember is the "Remember my answer" box.</summary>
    (bool Restore, bool Remember) AskRestoreChain();
}

// Owns: one window's side of live input: turning the program's live input on and off, the restore question, saving the live FX
//   chain, the choices its menu changes, opening the chain window, and being that window's host (chain edits go to the app
//   settings, never to a song).
// Does not own: the program-wide arm and the stand-in track (LiveInputArm), the engine sync (AudioEngineClient), the button
//   (LiveInputButton), the menu (LiveInputMenu), the command and Preferences row (LiveInputFeatureModule).
// Tests: TestLiveInput.
internal sealed class LiveInputController : IFxChainHost, IDisposable
{
    internal const string FxTitle = "FX: Live input";
    private const int StateTimeoutMs = 3000;
    private readonly ILiveInputHost _host;
    private readonly LiveInputArm _arm;
    private readonly LiveInputMidi _midi;
    private bool _disposed;

    public LiveInputController(ILiveInputHost host)
    {
        _host = host;
        _arm = host.Engine.LiveInput;
        _arm.Changed += OnArmChanged;
        _midi = _arm.Midi ??= new LiveInputMidi(host.Engine, _arm.Track, host.Engine.MidiInput);
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
    private LiveInputSettings Saved => _host.Settings.LiveInput;

    /// <summary>The audio inputs the menu offers: the device's inputs shown for live input, and the saved one (also when hidden or missing).</summary>
    public IReadOnlyList<string> Inputs => AudioInputs.For(AudioInputUse.LiveInput, Saved.Input.Length > 0 ? Saved.Input : null);

    public string Input => Saved.Input;

    internal const string MidiPrefix = "MIDI: ", MidiAny = "Any available device";
    /// <summary>True with a MIDI choice ticked (beside the audio input or alone).</summary>
    public bool IsMidi => Saved.MidiDevice != null;
    /// <summary>The chosen MIDI device; empty: any available device; null: none.</summary>
    public string? MidiDevice => Saved.MidiDevice;
    private string MidiName => string.IsNullOrEmpty(Saved.MidiDevice) ? "MIDI keyboard" : Saved.MidiDevice;
    private bool HasAudio => Saved.Input.Length > 0;
    /// <summary>The chosen audio input when the running device lacks it (it stays chosen and is silent); null otherwise.</summary>
    private string? MissingAudio => HasAudio && !AudioInputs.IsAvailable(Saved.Input) ? Saved.Input : null;
    private string MissingText => $"{MissingAudio} is not available on {(AudioInputs.Device.Length > 0 ? AudioInputs.Device : "this audio device")}";

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

    private string WaitingText => $"Live input: {MidiName} is not connected; waiting for it";

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

    /// <summary>Ticks an audio row (one at a time).</summary>
    public void SetInput(string input)
    {
        if (!Inputs.Contains(input)) return;
        var off = Saved.Input == input;   // the ticked row again: no audio input (the MIDI choice stays)
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
        _host.SetStatus("Live input off: no input chosen");
        return true;
    }

    public void OpenAudioSettings() => _host.OpenAudioSettings();

    /// <summary>The live input's own volume (0-127), pan (0-127, 64 centre) and mute, as the mixer's live input row shows them.</summary>
    public int Volume => _arm.Track.Volume;
    public int Pan => _arm.Track.Pan;
    public bool Muted => _arm.Track.Mute;
    /// <summary>The number of plug-ins in the live FX chain.</summary>
    public int PluginCount => _arm.Track.Rig.Plugins.Count;

    /// <summary>Sets the live input's volume and pan: saved program-wide, sent to the engine at once while on.</summary>
    public void SetLevel(int volume, int pan)
    {
        Saved.Volume = Math.Clamp(volume, 0, 127);
        Saved.Pan = Math.Clamp(pan, 0, 127);
        _arm.SetLevel(Saved.Volume, Saved.Pan);
        _host.SaveSettings();
        if (IsOn) _host.SyncAudioEngine();
    }

    /// <summary>Mutes or unmutes the live input without turning it off (its volume is kept).</summary>
    public void SetMuted(bool muted)
    {
        _arm.SetMute(muted);
        if (IsOn) _host.SyncAudioEngine();
    }

    /// <summary>The saved volume and pan go to the stand-in: at the start, and after Reset all settings (the engine follows while on).</summary>
    public void ApplySavedLevel()
    {
        if (_arm.Track.Volume == Saved.Volume && _arm.Track.Pan == Saved.Pan) return;
        _arm.SetLevel(Saved.Volume, Saved.Pan);
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

    /// <summary>An empty session chain and a saved one with plug-ins: Always restores, Never does not, Ask asks once per run (again after the choice was changed).</summary>
    private void RestoreChain()
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
        var track = _arm.Track;
        _midi.Stop();   // held notes are let go while the stand-in still has its slot
        _arm.Disarm();
        _host.SyncAudioEngine();
        _host.SetStatus("Live input off");
        await SaveStatesAsync();
        if (IsOn) return;   // turned on again meanwhile
        _arm.Release();   // the next sync, from any window, takes the stand-in out
        if (!_disposed) _host.SyncAudioEngine();
    }

    /// <summary>While the chain is in the engine: reads its plug-ins' states into it and saves it (bounded; never throws).</summary>
    private async Task SaveStatesAsync()
    {
        var track = _arm.Track;
        try
        {
            if (track.Rig.Plugins.Count == 0 || !_host.Engine.IsRunning || _host.Engine.SlotOf(track) < 0) return;
            await _host.Engine.CollectStatesAsync(new[] { track }, StateTimeoutMs);
            if (_disposed) return;   // the window closed meanwhile: its closing saved, and the states are in the chain
            SaveChain();
        }
        catch (Exception ex) { Trace.Error(Trace.Engine, "live input: plug-in states: " + ex.Message); }
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
        catch (Exception ex) { Trace.Error(Trace.Engine, "live input: plug-in states on close: " + ex.Message); }
    }

    /// <summary>Opens the live FX chain window without changing the armed state.</summary>
    public void OpenFx()
    {
        RestoreChain();   // before the chain can be edited: an edit saves it over the previous one
        _host.ShowFxChain(_arm.Track, this, () => _ = SaveStatesAsync());
        if (!IsOn) _host.SetStatus("Live input is off: turn it on to hear the chain");
    }

    private void SaveChain()
    {
        Saved.Chain = _arm.Track.Rig;   // from the first change on, the session's chain is the saved one
        _host.SaveSettings();
    }

    private void OnArmChanged() => Changed?.Invoke();

    private void OnLevelChanged() => LevelChanged?.Invoke();

    /// <summary>The window closed. Live input belongs to the program, so it stays as it is.</summary>
    public void Dispose() { _disposed = true; _arm.Changed -= OnArmChanged; _arm.LevelChanged -= OnLevelChanged; _midi.Notice -= _host.SetStatus; }

    // ---------- IFxChainHost: the live chain's window ----------

    IReadOnlyList<TrackModel> IFxChainHost.Tracks => _host.Tracks;
    PluginSettings IFxChainHost.PluginSettings => _host.Settings;
    void IFxChainHost.BeginChainEdit() { }   // not a song edit: no undo step
    void IFxChainHost.ChainChanged(TrackModel track) { SaveChain(); _host.SyncAudioEngine(); }
    void IFxChainHost.PluginSwitched(TrackModel track) { SaveChain(); _host.SyncAudioEngine(); }
    void IFxChainHost.SaveSettings() => _host.SaveSettings();
    bool IFxChainHost.MonitorUseGlobal => true;
    void IFxChainHost.SetMonitorUseGlobal(bool useGlobal) { }
    void IFxChainHost.OpenAudioSettings() => _host.OpenAudioSettings();
    IntPtr IFxChainHost.OwnerWindow => _host.OwnerWindow;
    bool IFxChainHost.DarkTheme => !Visualization.VisualTheme.IsLight;
    AudioEngineClient IFxChainHost.Engine => _host.Engine;
    string? IFxChainHost.AppWideChainTitle => FxTitle;
    System.Windows.UIElement? IFxChainHost.AppWideChainNote => LiveInputHint.Build(this);
}
