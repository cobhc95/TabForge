using TabForge.Platform.Contracts;
using TabForge.Platform.Portable;
using TabForge.Playback;

namespace TabForge.Platform;

// Owns: choosing, once, the MIDI stack: winmm (NAudio.Midi) on Windows, RtMidi everywhere else; on Windows RtMidi only when the dev switch asks.
// Does not own: the adapters (WinMmMidiInputBackend, WindowsMidiDevices, RtMidiInputBackend, RtMidiOutputDevices) or what listens to them.
// Tests: PortableBackendTests (Windows chooses the Windows adapters; the simulated other OS chooses RtMidi; the dev switch).

/// <summary>The MIDI input and output adapters of this OS.</summary>
public static class MidiBackends
{
    /// <summary>Dev switch for Windows: set to "portable" to use RtMidi instead of winmm (needs tfportable.dll next to the program). Default off.</summary>
    public const string DevVariable = "TABFORGE_MIDI_BACKEND";

    /// <summary>Whether the dev switch is set.</summary>
    public static bool PortableOnWindows => string.Equals(Environment.GetEnvironmentVariable(DevVariable), "portable", StringComparison.OrdinalIgnoreCase);

    /// <summary>The input adapter the app uses.</summary>
    public static IMidiInputBackend Input { get; } = CreateInput(OperatingSystem.IsWindows(), PortableOnWindows);

    /// <summary>The output adapter the app uses.</summary>
    public static IMidiDevices Output { get; } = CreateOutput(OperatingSystem.IsWindows(), PortableOnWindows);

    /// <summary>The input adapter for a host: the Windows one on a Windows host unless <paramref name="portableRequested"/>; RtMidi otherwise.</summary>
    public static IMidiInputBackend CreateInput(bool windowsHost, bool portableRequested)
    {
#if WINDOWS
        if (windowsHost && !portableRequested) return new Windows.WinMmMidiInputBackend();
#endif
        return new RtMidiInputBackend();
    }

    /// <summary>The output adapter for a host, chosen as <see cref="CreateInput"/>.</summary>
    public static IMidiDevices CreateOutput(bool windowsHost, bool portableRequested)
    {
#if WINDOWS
        if (windowsHost && !portableRequested) return new Windows.WindowsMidiDevices();
#endif
        return new RtMidiOutputDevices();
    }
}
