using System.Diagnostics;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.LiveInput;

// Owns: the MIDI keyboard of live input: one client of the shared MIDI input while live input is on with a MIDI choice, each
//   channel message sent to the stand-in's engine slot (the live chain's instrument plug-in), the reset that lets held notes go,
//   and noticing a device that appears or disappears (a poll every two seconds while listening or waiting).
// Does not own: the devices (MidiInputHub), which input is chosen and when live input is on (LiveInputController), the
//   stand-in and its slot (LiveInputArm, AudioEngineClient). No song channel is involved: the message names the slot.
// Tests: LiveInputStripTests.
internal sealed class LiveInputMidi
{
    private const int PollMs = 2000;
    // Sustain off first (under it All Notes Off keeps notes sounding), then All Notes Off, Reset All Controllers, bend centre and pressure 0.
    private static readonly IReadOnlyList<(byte Status, byte Data1, byte Data2)> ResetMessages = new (byte Status, byte Data1, byte Data2)[]
    {
        (0xB0, 64, 0), (0xB0, 123, 0), (0xB0, 121, 0), (0xE0, 0x00, 0x40), (0xD0, 0, 0)
    };

    private readonly AudioEngineClient _engine;
    private readonly TrackModel _track;
    private readonly MidiInputHub _hub;
    private readonly IMidiInputClient _client;
    private readonly IUiThread _dispatcher;
    private IUiTimer? _poll;
    private volatile bool _on;
    private int _channels;   // bit per MIDI channel played since the latest reset (the reset goes to those)
    private int _epoch;   // changes at each start with another device and at each stop: a message queued before it is dropped

    /// <summary>The UI thread the poll timer and the status lines run on (the caller's <c>Ui.Current</c>).</summary>
    public LiveInputMidi(AudioEngineClient engine, TrackModel track, MidiInputHub hub, IUiThread dispatcher)
    {
        _engine = engine; _track = track; _hub = hub; _dispatcher = dispatcher;
        _client = hub.CreateClient();
        if (_client is IMidiDeviceInputClient named) named.DeviceMessage += OnMessage;
        else _client.Message += (s, a, b, t) => OnMessage("", s, a, b, t);
    }

    /// <summary>A line for the status bar: a device appeared or disappeared while live input listens.</summary>
    public event Action<string>? Notice;

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/> (also while it waits for a device).</summary>
    public bool IsOn => _on;
    /// <summary>True while the device (or any device) is open and heard.</summary>
    public bool IsListening => _client.IsOpen;
    /// <summary>The device listened to; null: any.</summary>
    public string? Device => _client.Device;

    /// <summary>The devices connected now (asked each time, never kept).</summary>
    public IReadOnlyList<string> Devices() => _hub.DeviceNames();

    /// <summary>UI thread: listens to <paramref name="device"/> (null or empty: any). False: it is not connected, and this waits for it.</summary>
    public bool Start(string? device)
    {
        device = string.IsNullOrEmpty(device) ? null : device;
        if (_on && _client.Device != device) { Reset(); Interlocked.Increment(ref _epoch); }
        _client.Device = device;
        _on = true;
        if (_poll == null)
        {
            _poll = _dispatcher.CreateTimer(TimeSpan.FromMilliseconds(PollMs), UiPriority.Background);
            _poll.Tick += (_, _) => Poll();
        }
        _poll.Start();
        if (Present()) _client.Open(); else _client.Close();
        return _client.IsOpen;
    }

    /// <summary>UI thread: lets held notes go and closes this listener's hold on the devices (idempotent).</summary>
    public void Stop()
    {
        if (!_on) return;
        Reset();
        Interlocked.Increment(ref _epoch);
        _on = false;
        _poll?.Stop();
        _client.Close();
    }

    private bool Present()
    {
        var names = _hub.DeviceNames();
        return _client.Device is { } device ? names.Contains(device) : names.Count > 0;
    }

    private string DeviceLabel => _client.Device ?? "MIDI keyboard";

    /// <summary>UI thread, every two seconds while on: opens the device when it appears, lets notes go and closes when it is gone.</summary>
    internal void Poll()
    {
        if (!_on) return;
        var present = Present();
        if (_client.IsOpen && !present)
        {
            Reset();
            _client.Close();
            Notice?.Invoke($"Live input: {DeviceLabel} disconnected; waiting for it");
        }
        else if (!_client.IsOpen && present)
        {
            if (_client.Open() > 0) { _hub.Rescan(); Notice?.Invoke($"Live input: {DeviceLabel} connected"); }
        }
        else if (present && _hub.Rescan()) Reset();   // "any": a second device plugged in is opened too; one unplugged leaves no note held
    }

    /// <summary>MIDI driver thread: nothing that waits; the message is played on the UI thread, which knows the stand-in's slot.</summary>
    private void OnMessage(string device, int status, int data1, int data2, long stamp)
    {
        if (!_on || status < 0x80 || status >= 0xF0) return;
        if (_engine.MidiMonitor?.ArmedClaims(device) == true) return;   // an armed track plays this input through its own instrument: one place sounds a note
        var epoch = Volatile.Read(ref _epoch);
        _dispatcher.Post(() => Play(status, data1, data2, epoch), UiPriority.Send);
    }

    /// <summary>The value a queued message carries; another one by the time it plays drops it.</summary>
    internal int Epoch => Volatile.Read(ref _epoch);

    internal void Play(int status, int data1, int data2) => Play(status, data1, data2, Epoch);

    /// <summary>UI thread: sends one channel message to the stand-in's slot (nothing while the stand-in is not in a running engine).</summary>
    internal void Play(int status, int data1, int data2, int epoch)
    {
        if (!_on || epoch != Epoch) return;
        var slot = _engine.SlotOf(_track);
        if (slot < 0) return;
        _channels |= 1 << (status & 0x0F);
        Write(slot, (byte)status, (byte)data1, (byte)data2);
    }

    /// <summary>The slot's ordered all notes off, then the reset messages on every channel played, to the slot the stand-in has now.</summary>
    private void Reset()
    {
        var channels = _channels;
        _channels = 0;
        var slot = _engine.SlotOf(_track);
        if (channels == 0 || slot < 0) return;
        _engine.Write(new TimedMidi { Timestamp = Stopwatch.GetTimestamp() - _engine.LatencyTicks, Slot = slot, Status = 0xB0, Data1 = 123, Flags = TimedMidi.PanicFlag });
        for (var channel = 0; channel < 16; channel++)
            if ((channels & (1 << channel)) != 0)
                foreach (var (status, data1, data2) in ResetMessages) Write(slot, (byte)(status | channel), data1, data2);
    }

    // Stamped "already due", so the engine plays it in its next block.
    private void Write(int slot, byte status, byte data1, byte data2) =>
        _engine.Write(new TimedMidi { Timestamp = Stopwatch.GetTimestamp() - _engine.LatencyTicks, Slot = slot, Status = status, Data1 = data1, Data2 = data2 });
}
