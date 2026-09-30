using System.Runtime.InteropServices;

namespace TabForge.Playback;

public sealed class MidiOutputDeviceInfo
{
    public int DeviceId { get; init; }
    public string Name { get; init; } = "";
    public override string ToString() => Name;
}

/// <summary>
/// MIDI byte sink. One narrow abstraction so the scheduling engine can be tested without a device
/// and so alternative outputs (SoundFont/VST later) can be added behind the same boundary.
/// </summary>
public interface IMidiOutput : IDisposable
{
    IReadOnlyList<MidiOutputDeviceInfo> Devices { get; }
    void Send(int deviceId, int status, int data1, int data2);
    /// <summary>A note played live (MIDI input monitoring): as soon as possible, never held back for sync.</summary>
    void SendLive(int deviceId, int status, int data1, int data2) => Send(deviceId, status, data1, data2);
    /// <summary>Turns all notes off on every open device (panic). Must be safe to call repeatedly.</summary>
    void ResetAll();
    void Close();
}

/// <summary>
/// One tab's view of the process-wide MIDI device. WinMM lets only one handle own a device, so every
/// playback engine shares <see cref="Shared"/>; this wrapper remembers which channels its engine used
/// so stopping one tab silences only those channels instead of resetting the whole synth.
/// </summary>
public sealed class SharedMidiOutput : IMidiOutput
{
    private static readonly Lazy<WinMmMidiOutput> SharedDevice = new(() => new WinMmMidiOutput());
    public static WinMmMidiOutput Shared => SharedDevice.Value;

    // Bit per (device slot, channel); device ids are few, so a small set is cheap and allocation-free on Send.
    private readonly HashSet<(int device, int channel)> _used = new();
    private readonly object _gate = new();

    public IReadOnlyList<MidiOutputDeviceInfo> Devices => Shared.Devices;

    public void Send(int deviceId, int status, int data1, int data2)
    {
        if ((status & 0xF0) is >= 0x80 and <= 0xE0)
            lock (_gate) _used.Add((deviceId, status & 0x0F));
        Shared.Send(deviceId, status, data1, data2);
    }

    /// <summary>All notes off + all sound off on this engine's channels only.</summary>
    public void ResetAll()
    {
        (int device, int channel)[] used;
        lock (_gate) used = _used.ToArray();
        foreach (var (device, channel) in used)
        {
            Shared.Send(device, 0xB0 | channel, 123, 0);
            Shared.Send(device, 0xB0 | channel, 120, 0);
        }
    }

    /// <summary>The device stays open for the other tabs; this engine just goes quiet.</summary>
    public void Close() => ResetAll();

    public void Dispose() => Close();
}

/// <summary>WinMM (winmm.dll) MIDI output: one device handle per device id, opened on first use.</summary>
public sealed class WinMmMidiOutput : IMidiOutput
{
    private readonly Dictionary<int, IntPtr> _handles = new();
    private readonly object _gate = new();

    public IReadOnlyList<MidiOutputDeviceInfo> Devices
    {
        get
        {
            var list = new List<MidiOutputDeviceInfo> { new() { DeviceId = -1, Name = "Microsoft MIDI Mapper (default)" } };
            if (!OperatingSystem.IsWindows()) return list;
            var count = midiOutGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                if (midiOutGetDevCaps(i, out var caps, (uint)Marshal.SizeOf<MIDIOUTCAPS>()) == 0)
                    list.Add(new MidiOutputDeviceInfo
                    {
                        DeviceId = (int)i,
                        Name = string.IsNullOrWhiteSpace(caps.szPname) ? $"MIDI out {i}" : caps.szPname
                    });
            }
            return list;
        }
    }

    public void Send(int deviceId, int status, int data1, int data2)
    {
        var handle = GetHandle(deviceId);
        if (handle == IntPtr.Zero) return;
        // Short messages are one API call; no allocation, no locks held across the call.
        var msg = (uint)((status & 0xFF) | ((data1 & 0x7F) << 8) | ((data2 & 0x7F) << 16));
        // winmm reports failures as MMRESULT codes (a lost device just drops the message); it does not throw.
        midiOutShortMsg(handle, msg);
    }

    private IntPtr GetHandle(int deviceId)
    {
        lock (_gate)
        {
            if (_handles.TryGetValue(deviceId, out var existing)) return existing;
        }
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var raw = deviceId < 0 ? 0xFFFFFFFFu : (uint)deviceId;
        if (midiOutOpen(out var handle, raw, IntPtr.Zero, IntPtr.Zero, 0) != 0) return IntPtr.Zero;
        lock (_gate)
        {
            if (_handles.TryGetValue(deviceId, out var raced)) { midiOutClose(handle); return raced; }
            _handles[deviceId] = handle;
        }
        return handle;
    }

    public void ResetAll()
    {
        List<IntPtr> handles;
        lock (_gate) handles = _handles.Values.ToList();
        foreach (var h in handles) midiOutReset(h);
    }

    public void Close()
    {
        List<IntPtr> handles;
        lock (_gate)
        {
            handles = _handles.Values.ToList();
            _handles.Clear();
        }
        foreach (var h in handles)
        {
            if (h == IntPtr.Zero) continue;
            midiOutReset(h);
            midiOutClose(h);
        }
    }

    public void Dispose() => Close();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MIDIOUTCAPS
    {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public ushort wTechnology, wVoices, wNotes, wChannelMask;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Auto)] private static extern int midiOutGetDevCaps(uint uDeviceID, out MIDIOUTCAPS caps, uint cbmoc);
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr handle);
}

/// <summary>Output that drops every message. Used by headless timing tests and when no device is wanted.</summary>
public sealed class NullMidiOutput : IMidiOutput
{
    public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = new[]
    {
        new MidiOutputDeviceInfo { DeviceId = -1, Name = "No output (silent)" }
    };

    public void Send(int deviceId, int status, int data1, int data2) { }
    public void ResetAll() { }
    public void Close() { }
    public void Dispose() { }
}
