namespace TabForge.Platform.Contracts;

// Owns: the port for the system's MIDI input devices: list them, open one, and receive its short messages. The output side is IMidiDevices (Playback).
// Does not own: the adapters (Platform/Windows/WinMmMidiInput.cs on Windows, Platform/Portable/RtMidiBackend.cs elsewhere), sharing the open devices between listeners
//   (MidiInputHub) or naming and rescanning them (MidiInputCapture).
// Tests: PortableBackendTests (the choice per OS, the RtMidi adapter's list), TestKeyboardModeMidiListener (through a fake device set).

/// <summary>The MIDI input devices of a MIDI stack (winmm on Windows, RtMidi: CoreMIDI / ALSA elsewhere).</summary>
public interface IMidiInputBackend
{
    /// <summary>Short name for logs and tests ("winmm", "rtmidi").</summary>
    string Id { get; }

    /// <summary>False when this backend cannot run here (the native library is missing).</summary>
    bool IsAvailable { get; }

    /// <summary>The names of the input devices present now, in device-id order; empty when there are none. Never throws.</summary>
    IReadOnlyList<string> InputNames();

    /// <summary>Opens input device <paramref name="index"/> (an index of <see cref="InputNames"/>), not yet receiving. Throws <see cref="MidiPortException"/> when it cannot be opened (for example it is in use by another program).</summary>
    IMidiInputPort Open(int index);
}

/// <summary>One open MIDI input device.</summary>
public interface IMidiInputPort : IDisposable
{
    /// <summary>One short message on the driver's thread, packed as status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16 (the winmm raw form). System-exclusive and timing messages are not delivered.</summary>
    event Action<int>? RawMessage;

    /// <summary>Starts delivering messages (idempotent).</summary>
    void Start();

    /// <summary>Stops delivering messages (idempotent).</summary>
    void Stop();
}

/// <summary>A MIDI device could not be opened or stopped (busy, unplugged, driver error).</summary>
public sealed class MidiPortException : Exception
{
    public MidiPortException(string message) : base(message) { }
    public MidiPortException(string message, Exception inner) : base(message, inner) { }
}
