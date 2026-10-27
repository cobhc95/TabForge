using System;
using Avalonia.Threading;
using TabForge.Audio;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's MIDI keyboard input: while the piano roll shows it holds a client of the shared MIDI input (the inputs the app has open;
//       it never changes which), hearing only the shown track's chosen MIDI device when it has one. Every note-on and note-off lights or clears the key on the piano roll's own keys (PianoRollKeyState, repainted once
//       per UI pass however many messages arrive), and with step input on each note-on adds a step (keys pressed within a moment of each other make
//       one chord). The client goes when the pane hides.
// Does not own: the MIDI devices (MidiInputHub), the step itself (PianoRollSurface.StepAdd).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollView
{
    private const long ChordWindowMs = 60;

    private IMidiInputClient? _midi;
    private long _lastStepMs = long.MinValue / 2;

    private int _keysQueued;   // 1 while a key repaint is posted (set on the driver thread, cleared on the UI thread)

    /// <summary>Opens (the piano roll shows) or closes (it hides) the MIDI client the live keys and step input listen to.</summary>
    private void ListenForKeys(bool on)
    {
        if (!on || _host.MidiInput() is not { } hub)
        {
            if (_midi is null) return;
            _midi.Message -= OnMidiMessage;
            _midi.Dispose();
            _midi = null;
            if (Surface.Keys.Clear()) Surface.Playhead.InvalidateVisual();
            return;
        }
        if (_midi is not null) { _midi.Device = KeyDevice(); return; }
        _midi = hub.CreateClient();
        _midi.Device = KeyDevice();
        _midi.Message += OnMidiMessage;
        if (_midi.Open() != 0) return;
        _midi.Message -= OnMidiMessage;
        _midi.Dispose();
        _midi = null;
    }

    /// <summary>The device the live keys hear: the one MIDI device chosen for the shown track; null (every open device) for All, None or no track.</summary>
    private string? KeyDevice() => Presenter.Track is { } track && track.MidiInputs.Count == 1 && track.MidiInputs[0] != TabForge.Models.AudioInputs.AllMidi ? track.MidiInputs[0] : null;

    // The driver thread: the key state changes here (cheap, locked); the repaint is posted once until it runs; a note-on in step input posts a step.
    private void OnMidiMessage(int status, int data1, int data2, long timestamp)
    {
        if (Surface.Keys.Message(status, data1, data2) && System.Threading.Interlocked.Exchange(ref _keysQueued, 1) == 0)
            Dispatcher.UIThread.Post(RepaintKeys, DispatcherPriority.Render);
        if ((status & 0xF0) == 0x90 && data2 > 0 && _stepOn) Dispatcher.UIThread.Post(() => StepNote(data1, data2));
    }

    private volatile bool _stepOn;   // step input is on (read on the driver thread)

    private void RepaintKeys()
    {
        System.Threading.Interlocked.Exchange(ref _keysQueued, 0);
        Surface.Playhead.InvalidateVisual();
    }

    /// <summary>A message from the MIDI keyboard as the driver delivers it (tests feed it directly).</summary>
    internal void MidiMessage(int status, int data1, int data2) => OnMidiMessage(status, data1, data2, 0);

    /// <summary>The pane went back to the score: step input stops listening.</summary>
    public void Hidden()
    {
        Surface.StepInput = false;
        _stepOn = false;
        _step.IsChecked = false;
        ListenForKeys(false);
    }

    /// <summary>A key played for step input (a MIDI keyboard or a test): a new step, or the last step's chord when it follows within a moment.</summary>
    internal void StepNote(int pitch, int velocity)
    {
        if (!Surface.StepInput || !IsEffectivelyVisible) return;
        var now = Environment.TickCount64;
        Surface.StepAdd(pitch, velocity, sameStep: now - _lastStepMs < ChordWindowMs);
        _lastStepMs = now;
        if (Presenter.Track is { } track && _host.MonitorSounds(track)) return;   // monitoring already sounds this key: no second sound
        Surface.Audition?.Invoke(pitch);
    }
}
