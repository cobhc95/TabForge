using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TabForge.Audio.Contracts;
using TabForge.Platform.Contracts;

namespace TabForge.Platform.Portable;

// Owns: the portable MIDI adapters over RtMidi (CoreMIDI on macOS, the ALSA sequencer on Linux; winmm on Windows when the dev switch asks): the input
//   backend (list, open, raw messages) and the output devices behind IMidiDevices (list, open, short message, reset, close).
// Does not own: the native library (TfPortable in Contracts), which backend is used (MidiBackends), or the handle bookkeeping (WinMmMidiOutput) and the
//   sharing of the open inputs (MidiInputHub).
// Threads: input messages arrive on the RtMidi driver thread; nothing here locks or allocates per message.
// Tests: PortableBackendTests (the lists, and a clean no-op when the library is missing).

/// <summary>Lists and opens MIDI inputs through the tfportable library. With no library it lists nothing and cannot open.</summary>
public sealed class RtMidiInputBackend : IMidiInputBackend
{
    public string Id => "rtmidi";
    public bool IsAvailable => TfPortable.Current is not null;

    public IReadOnlyList<string> InputNames() => RtMidiNames.List(input: true);

    public IMidiInputPort Open(int index) =>
        TfPortable.Current is { } lib ? new RtMidiInputPort(lib, index) : throw new MidiPortException("The MIDI library (tfportable) is not installed next to TabForge.");
}

/// <summary>One RtMidi input. The port is opened by <see cref="Start"/> (so no message is lost between subscribing and starting) and closed by <see cref="Stop"/>.</summary>
public sealed unsafe class RtMidiInputPort : IMidiInputPort
{
    private readonly TfPortable _lib;
    private readonly int _index;
    private GCHandle _self;
    private nint _port;
    private readonly object _gate = new();

    internal RtMidiInputPort(TfPortable lib, int index) { _lib = lib; _index = index; }

    public event Action<int>? RawMessage;

    public void Start()
    {
        lock (_gate)
        {
            if (_port != 0) return;
            _self = GCHandle.Alloc(this);
            _port = _lib.MidiInOpen(_index, &OnMessage, GCHandle.ToIntPtr(_self));
            if (_port != 0) return;
            _self.Free();
            throw new MidiPortException("The MIDI input could not be opened (it may be in use by another program).");
        }
    }

    public void Stop()
    {
        nint port;
        lock (_gate) { port = _port; _port = 0; }
        if (port == 0) return;
        _lib.MidiInClose(port);   // returns once no callback runs
        _self.Free();
    }

    public void Dispose() => Stop();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnMessage(nint user, byte* message, int length, double deltaSeconds)
    {
        if (length < 1 || GCHandle.FromIntPtr(user).Target is not RtMidiInputPort self) return;
        var raw = message[0] | (length > 1 ? message[1] << 8 : 0) | (length > 2 ? message[2] << 16 : 0);
        self.RawMessage?.Invoke(raw);
    }
}

/// <summary>The MIDI output devices through the tfportable library, in the shape the playback layer uses (<see cref="IMidiDevices"/>): ids are RtMidi port indexes, handles are the open ports.</summary>
public sealed class RtMidiOutputDevices : IMidiDevices
{
    public bool IsSupported => TfPortable.Current is not null;
    public string DefaultDeviceName => "Default MIDI output";

    public uint OutputDeviceCount() => (uint)RtMidiNames.List(input: false).Count;

    public bool TryGetOutputProductName(uint deviceId, out string productName)
    {
        var names = RtMidiNames.List(input: false);
        productName = deviceId < names.Count ? names[(int)deviceId] : "";
        return deviceId < names.Count;
    }

    /// <summary>Opens port <paramref name="deviceId"/>; the "mapper" id (0xFFFFFFFF) is the first port. Zero when it cannot be opened.</summary>
    public IntPtr OpenOutput(uint deviceId) => TfPortable.Current?.MidiOutOpen(deviceId == uint.MaxValue ? 0 : (int)Math.Min(deviceId, int.MaxValue)) ?? IntPtr.Zero;

    public void ShortMessage(IntPtr handle, uint message)
    {
        var status = (byte)(message & 0xFF);
        var length = (status & 0xF0) is 0xC0 or 0xD0 ? 2 : 3;
        TfPortable.Current?.MidiOutSend(handle, status, (byte)((message >> 8) & 0x7F), (byte)((message >> 16) & 0x7F), length);
    }

    /// <summary>All sound off and all notes off on the 16 channels (what winmm's reset does).</summary>
    public void ResetOutput(IntPtr handle)
    {
        for (byte channel = 0; channel < 16; channel++)
        {
            TfPortable.Current?.MidiOutSend(handle, (byte)(0xB0 | channel), 120, 0, 3);
            TfPortable.Current?.MidiOutSend(handle, (byte)(0xB0 | channel), 123, 0, 3);
        }
    }

    public void CloseOutput(IntPtr handle) => TfPortable.Current?.MidiOutClose(handle);
}

internal static class RtMidiNames
{
    /// <summary>Port names of one direction; empty without the library or when RtMidi fails. Never throws.</summary>
    public static IReadOnlyList<string> List(bool input)
    {
        if (TfPortable.Current is not { } lib) return Array.Empty<string>();
        try
        {
            var names = new List<string>();
            var count = lib.MidiCount(input);
            for (var i = 0; i < count; i++) names.Add(lib.MidiName(input, i) ?? $"MIDI {(input ? "in" : "out")} {i}");
            return names;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return Array.Empty<string>(); }   // swallow: a library of another version lists nothing
    }
}
