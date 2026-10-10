namespace TabForge.Audio;

/// <summary>A MIDI input a listener can open and close: the device set itself (<see cref="MidiInputCapture"/>) or a client of a <see cref="MidiInputHub"/>.</summary>
public interface IMidiInputSource : IDisposable
{
    /// <summary>(status, data1, data2, stopwatch timestamp) on the MIDI driver thread.</summary>
    event Action<int, int, int, long>? Message;
    /// <summary>True while this source is open (a client: while it holds the devices).</summary>
    bool IsOpen { get; }
    /// <summary>Opens (idempotent); returns how many devices are open, 0 when none could be opened.</summary>
    int Open();
    void Close();
}

/// <summary>A hub client that can listen to one named device or to all of them.</summary>
public interface IMidiInputClient : IMidiInputSource
{
    /// <summary>The device name this client hears; null or empty = every open device (any). Settable at any time on the UI thread.</summary>
    string? Device { get; set; }
}

// Owns: sharing one set of MIDI input handles between several listeners (recording, Keyboard mode): the devices open while at least one client holds them
//   and close with the last one; every message is passed to the clients that hold them and listen to its device; rescanning for devices connected or removed.
// Does not own: the devices (the <see cref="IMidiInputSource"/> it wraps), what a client does with a message, or the recording state.
// Tests: TestKeyboardModeMidiListener, TestMidiInputDeviceFilter.
/// <summary>One shared MIDI input. A client opens and closes like the device set does, so a listener changes nothing for the others.</summary>
public sealed class MidiInputHub
{
    private readonly IMidiInputSource _device;
    private Client[] _held = Array.Empty<Client>();   // copy on write: UI thread writes, the driver thread reads

    private readonly IMidiDeviceSet? _set;

    public MidiInputHub(IMidiInputSource device)
    {
        _device = device;
        _set = device as IMidiDeviceSet;
        if (_set != null) _set.DeviceMessage += Fan;
        else device.Message += (s, a, b, t) => Fan("", s, a, b, t);   // a plain source is one unnamed device
    }

    /// <summary>How many clients hold the devices now.</summary>
    public int HeldCount => Volatile.Read(ref _held).Length;

    public IMidiInputClient CreateClient() => new Client(this);

    /// <summary>Names of the devices present now (UI thread; never throws). A plain source has none to list.</summary>
    public IReadOnlyList<string> DeviceNames()
    {
        try { return _set?.ListNames() ?? Array.Empty<string>(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>Names of the devices open now (empty when closed).</summary>
    public IReadOnlyList<string> OpenDeviceNames => _set?.OpenNames ?? Array.Empty<string>();

    /// <summary>While open: opens devices connected since and drops ones gone. True when the open set changed.</summary>
    public bool Rescan() => _set != null && _set.IsOpen && _set.Rescan();

    // Lock-free and allocation-free: a client hears a message when it has no device set or its device is this one (names compare ordinally).
    private void Fan(string device, int status, int data1, int data2, long stamp)
    {
        foreach (var c in Volatile.Read(ref _held)) c.Raise(device, status, data1, data2, stamp);
    }

    private int Acquire(Client client)
    {
        var count = _device.Open();   // idempotent: the count of the devices already open when another client holds them
        if (count == 0) return 0;
        Volatile.Write(ref _held, Volatile.Read(ref _held).Append(client).ToArray());
        return count;
    }

    private void Release(Client client)
    {
        var next = Volatile.Read(ref _held).Where(c => !ReferenceEquals(c, client)).ToArray();
        Volatile.Write(ref _held, next);
        if (next.Length == 0 && _device.IsOpen) _device.Close();
    }

    private sealed class Client : IMidiInputClient
    {
        private readonly MidiInputHub _hub;
        public Client(MidiInputHub hub) => _hub = hub;
        public event Action<int, int, int, long>? Message;
        public bool IsOpen { get; private set; }
        private volatile string? _device;
        public string? Device { get => _device; set => _device = value; }
        public int Open()
        {
            if (IsOpen) return _hub._device.Open();
            var count = _hub.Acquire(this);
            IsOpen = count > 0;
            return count;
        }
        public void Close() { if (!IsOpen) return; IsOpen = false; _hub.Release(this); }
        public void Dispose() => Close();
        internal void Raise(string device, int s, int a, int b, long t)
        {
            var want = _device;
            if (string.IsNullOrEmpty(want) || string.Equals(want, device, StringComparison.Ordinal)) Message?.Invoke(s, a, b, t);
        }
    }
}
