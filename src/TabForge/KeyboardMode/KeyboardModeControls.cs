namespace TabForge.KeyboardMode;

/// <summary>The window's transport as Keyboard mode's control bar uses it (the same handlers as the main play bar and its hotkeys).</summary>
internal sealed record KeyboardModeTransport(Action PlayPause, Action Stop, Func<bool> IsPlaying, Func<double> Speed, Action<double> SetSpeed, Func<bool> LoopOn, Action ToggleLoop);

// Owns: what Keyboard mode's control bars read and change, in one place for the pane's bar and the pop-out's: play / pause, stop, speed and loop (the window's transport), wait, hands filter,
//   look-ahead, note names, finger numbers and the MIDI input (the saved settings, applied by the frame controller), and the Changed signal: a cheap check each frame of the controller (and on
//   every change made here) so a bar follows hotkeys, the main play bar and connected devices.
// Does not own: the controls themselves (KeyboardModeControlBar), the transport (the window's handlers), the MIDI devices (MidiInputHub) or the frame step (KeyboardModeFrameController).
// Tests: TestKeyboardModeControlBar.
internal sealed class KeyboardModeControls
{
    /// <summary>The speed presets of the bar (the transport's speed, 1 = the song's tempo).</summary>
    public static readonly double[] SpeedPresets = { 0.5, 0.6, 0.7, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25 };

    private readonly KeyboardModeFrameController _learn;
    private readonly KeyboardModeTransport _transport;
    private (bool, double, bool, bool, string, bool, int, string, int, (bool, bool)) _seen;

    public KeyboardModeControls(KeyboardModeFrameController learn, KeyboardModeTransport transport)
    {
        _learn = learn; _transport = transport;
        learn.Ticked += Poll;
    }

    /// <summary>Something a bar shows changed (raised on the UI thread).</summary>
    public event Action? Changed;

    private KeyboardModeSettings Settings => _learn.Settings;

    public bool IsPlaying => _transport.IsPlaying();
    public void PlayPause() { _transport.PlayPause(); Poll(); }
    public void Stop() { _transport.Stop(); Poll(); }

    public double Speed => _transport.Speed();
    public void SetSpeed(double speed) { _transport.SetSpeed(speed); Poll(); }

    public bool LoopOn => _transport.LoopOn();
    public void ToggleLoop() { _transport.ToggleLoop(); Poll(); }

    public bool WaitOn => Settings.WaitForNotes;
    public void ToggleWait() { _learn.ToggleWait(); Poll(); }

    /// <summary>Gives up the chord the song waits for (nothing when it does not wait).</summary>
    public void Skip() { _learn.SkipWait(); Poll(); }

    public KeyboardHandsFilter Hands => KeyboardHands.Parse(Settings.Hands);
    public void SetHands(KeyboardHandsFilter hands) => Change(s => s.Hands = KeyboardHands.NameOf(hands), "Keyboard mode: " + KeyboardHands.NameOf(hands).ToLowerInvariant());

    public int LookAheadSeconds => _learn.LookAheadSeconds;
    public void StepLookAhead(int step) { _learn.StepLookAhead(step); Poll(); }

    public bool ShowNames => Settings.ShowNoteNames;
    public void SetShowNames(bool on) => Change(s => s.ShowNoteNames = on, "Keyboard mode: note names " + (on ? "on" : "off"));
    public bool ShowFingers => Settings.ShowFingers;
    public void SetShowFingers(bool on) => Change(s => s.ShowFingers = on, "Keyboard mode: finger numbers " + (on ? "on" : "off"));
    /// <summary>The shown track has fingering to show.</summary>
    public bool HasFingers => _learn.HasFingers;

    /// <summary>The saved MIDI input: <see cref="KeyboardModeSettings.MidiOff"/>, <see cref="KeyboardModeSettings.MidiAny"/> or a device name.</summary>
    public string MidiInput => KeyboardModeSettings.NormalizeMidiInput(Settings.MidiInput);
    public void SetMidiInput(string input) => Change(s => s.MidiInput = KeyboardModeSettings.NormalizeMidiInput(input),
        input == KeyboardModeSettings.MidiOff ? "Keyboard mode: MIDI input off" : "Keyboard mode: MIDI input " + (input == KeyboardModeSettings.MidiAny ? "any available device" : input));
    /// <summary>The MIDI input devices present now (enumerated on each call: for the menu as it opens).</summary>
    public IReadOnlyList<string> MidiDevices() => _learn.MidiDevices();
    /// <summary>The devices Keyboard mode hears now (empty: nothing is connected or the input is off).</summary>
    public IReadOnlyList<string> HeardDevices => _learn.HeardDevices;

    private void Change(Action<KeyboardModeSettings> change, string status)
    {
        change(Settings);
        _learn.SettingsChanged(status);
        Poll();
    }

    /// <summary>Raises <see cref="Changed"/> when anything a bar shows differs from the last check (allocation-free when nothing changed).</summary>
    public void Poll()
    {
        var heard = _learn.HeardDevices;
        var now = (IsPlaying, Speed, LoopOn, WaitOn, Settings.Hands ?? "", ShowNames, LookAheadSeconds, MidiInput, heard.Count == 0 ? -1 : heard[0].GetHashCode() ^ heard.Count, (ShowFingers, HasFingers));
        if (now.Equals(_seen)) return;
        _seen = now;
        Changed?.Invoke();
    }
}
