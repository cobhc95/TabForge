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
    /// <summary>A metronome or count-in click: never routed to a song channel's engine slot (clicks share channel 9 with the drums).</summary>
    void SendMetronome(int deviceId, int status, int data1, int data2) => Send(deviceId, status, data1, data2);
    /// <summary>Turns all notes off on every open device (panic). Must be safe to call repeatedly.</summary>
    void ResetAll();
    void Close();
}

/// <summary>
/// One tab's view of the process-wide MIDI device. WinMM lets only one handle own a device, so every
/// playback engine shares the one device it is given (the app composes it once, <c>Ui.MidiOutputDevice</c>); this wrapper
/// remembers which channels its engine used so stopping one tab silences only those channels instead of resetting the whole synth.
/// Owns: this engine's channel bookkeeping. Does not own: the device or the Windows calls.
/// </summary>
public sealed class SharedMidiOutput : IMidiOutput
{
    private readonly IMidiOutput _device;

    // Bit per (device slot, channel); device ids are few, so a small set is cheap and allocation-free on Send.
    private readonly HashSet<(int device, int channel)> _used = new();
    private readonly object _gate = new();

    public SharedMidiOutput(IMidiOutput device) => _device = device;

    public IReadOnlyList<MidiOutputDeviceInfo> Devices => _device.Devices;

    public void Send(int deviceId, int status, int data1, int data2)
    {
        if ((status & 0xF0) is >= 0x80 and <= 0xE0)
            lock (_gate) _used.Add((deviceId, status & 0x0F));
        _device.Send(deviceId, status, data1, data2);
    }

    /// <summary>All notes off + all sound off on this engine's channels only.</summary>
    public void ResetAll()
    {
        (int device, int channel)[] used;
        lock (_gate) used = _used.ToArray();
        foreach (var (device, channel) in used)
        {
            _device.Send(device, 0xB0 | channel, 123, 0);
            _device.Send(device, 0xB0 | channel, 120, 0);
        }
    }

    /// <summary>The device stays open for the other tabs; this engine just goes quiet.</summary>
    public void Close() => ResetAll();

    public void Dispose() => Close();
}

/// <summary>
/// WinMM MIDI output over <see cref="IMidiDevices"/>: one device handle per device id, opened on first use.
/// Owns: the handle table, the device-list policy (the Microsoft MIDI Mapper first, unreadable devices left out) and the
/// "no device, no message" rule. Does not own: the Windows calls (the adapter is Core/Platform/Windows/WindowsMidiDevices.cs).
/// Tests: no dedicated test class.
/// </summary>
public sealed class WinMmMidiOutput : IMidiOutput
{
    /// <summary>The name of the Microsoft MIDI Mapper entry (device id -1), the first entry of every device list.</summary>
    public const string MapperName = "Microsoft MIDI Mapper (default)";

    private readonly IMidiDevices _devices;
    private readonly Dictionary<int, IntPtr> _handles = new();
    private readonly object _gate = new();

    public WinMmMidiOutput(IMidiDevices devices) => _devices = devices;

    public IReadOnlyList<MidiOutputDeviceInfo> Devices
    {
        get
        {
            var list = new List<MidiOutputDeviceInfo> { new() { DeviceId = -1, Name = MapperName } };
            if (!OperatingSystem.IsWindows()) return list;
            var count = _devices.OutputDeviceCount();
            for (uint i = 0; i < count; i++)
            {
                if (_devices.TryGetOutputProductName(i, out var productName))
                    list.Add(new MidiOutputDeviceInfo
                    {
                        DeviceId = (int)i,
                        Name = string.IsNullOrWhiteSpace(productName) ? $"MIDI out {i}" : productName
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
        _devices.ShortMessage(handle, msg);
    }

    private IntPtr GetHandle(int deviceId)
    {
        lock (_gate)
        {
            if (_handles.TryGetValue(deviceId, out var existing)) return existing;
        }
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var raw = deviceId < 0 ? 0xFFFFFFFFu : (uint)deviceId;
        var handle = _devices.OpenOutput(raw);
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        lock (_gate)
        {
            if (_handles.TryGetValue(deviceId, out var raced)) { _devices.CloseOutput(handle); return raced; }
            _handles[deviceId] = handle;
        }
        return handle;
    }

    public void ResetAll()
    {
        List<IntPtr> handles;
        lock (_gate) handles = _handles.Values.ToList();
        foreach (var h in handles) _devices.ResetOutput(h);
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
            _devices.ResetOutput(h);
            _devices.CloseOutput(h);
        }
    }

    public void Dispose() => Close();
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
