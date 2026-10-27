using TabForge.Audio;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.LiveInput;

/// <summary>The choices of the "Monitor MIDI input" setting.</summary>
public static class MidiMonitorModes
{
    public const string ArmedAndSelected = "Armed tracks + selected track", ArmedOnly = "Armed tracks only", Off = "Off";
    public static string[] All => new[] { ArmedAndSelected, ArmedOnly, Off };

    /// <summary>The saved text as one of the choices (anything else is the default).</summary>
    public static string Normalize(string? mode) => mode is ArmedAndSelected or ArmedOnly or Off ? mode : ArmedAndSelected;
}

/// <summary>What the monitor needs from its window: the shown song's tracks, the selection, the setting, and the engine's way to sound a message.</summary>
public interface IMidiMonitorHost
{
    IReadOnlyList<TrackModel> Tracks { get; }
    TrackModel? Selected { get; }
    string Mode { get; }
    /// <summary>True while live input's stand-in listens to the keyboard (the selected track then stays silent: one place sounds a note).</summary>
    bool StandInListening { get; }
    /// <summary>The engine slot of the track, or -1 (not in the engine).</summary>
    int SlotOf(TrackModel track);
    /// <summary>Any thread, no waiting: one channel message to the slot, due at once; <paramref name="panic"/>: the ordered all notes off for the slot.</summary>
    void Write(int slot, byte status, byte data1, byte data2, bool panic);
    /// <summary>Any thread: one channel message for a track that has no engine slot (Windows MIDI, on the track's channel). Nothing by default.</summary>
    void WriteFallback(TrackModel track, byte status, byte data1, byte data2) { }
}

// Owns: hearing a MIDI keyboard through a track's own instrument (like a DAW's input monitoring): the tracks that monitor (armed with a MIDI input; the
//   selected track in the default mode), one hub client that is open only while a track monitors, each message sent from the driver thread straight to
//   the track's engine slot on the track's channel (no UI hop), the track's program sent when it starts monitoring, held notes let go when it stops.
// Does not own: the devices (MidiInputHub), recording (MidiTakeCapture holds its own client), the key lights (PianoRollView), the stand-in of live input
//   (LiveInputMidi asks ArmedClaims and yields to an armed track), the slots (AudioEngineClient).
// Tests: MidiInputMonitorTests.
// Threads: Refresh and Poll on the UI thread; messages on the MIDI driver thread (lock-free: they read an immutable snapshot of the targets).
public sealed class MidiInputMonitor : IDisposable
{
    private const int PollMs = 2000;
    private sealed record Target(TrackModel Track, int Slot, int Channel, int Program, string[] Devices, bool Armed, MidiNoteSlot Notes, bool Silent = false)
    {
        public bool Hears(string device) => Devices.Length == 0 || Array.IndexOf(Devices, device) >= 0;
    }

    private readonly IMidiMonitorHost _host;
    private readonly MidiInputHub _hub;
    private readonly IMidiInputClient _client;
    private readonly IUiThread _ui;
    private IUiTimer? _poll;
    private volatile Target[] _targets = Array.Empty<Target>();
    // The latest note-on each track heard, for the armed row's velocity meter. Key: the track (weak: it goes with the track); written on the driver thread, read on the UI thread.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TrackModel, MidiNoteSlot> _notes = new();
    private bool _disposed;

    public MidiInputMonitor(IMidiMonitorHost host, MidiInputHub hub, IUiThread ui)
    {
        _host = host; _hub = hub; _ui = ui;
        _client = hub.CreateClient();
        if (_client is IMidiDeviceInputClient named) named.DeviceMessage += OnMessage;
        else _client.Message += (s, a, b, t) => OnMessage("", s, a, b, t);
    }

    /// <summary>True while a track monitors (the hub client is held when a device is there).</summary>
    public bool IsActive => _targets.Length > 0;
    /// <summary>The tracks that monitor now.</summary>
    public IReadOnlyList<TrackModel> MonitoredTracks => _targets.Select(t => t.Track).ToList();
    /// <summary>True while the client holds the MIDI devices.</summary>
    public bool IsOpen => _client.IsOpen;
    /// <summary>True when this monitor is sounding keys for the track now (muted, not monitoring or only metering: false).</summary>
    public bool Sounds(TrackModel track) => _targets.Any(t => !t.Silent && ReferenceEquals(t.Track, track));

    /// <summary>Raised on the MIDI driver thread (nothing that waits) when an armed track hears a note-on; the armed row wakes its meter frames with it.</summary>
    public event Action? NoteHeard;

    /// <summary>A muted monitor stays registered but sounds nothing and holds no input (only the active window's monitor sounds). UI thread.</summary>
    public bool Muted
    {
        get => _muted;
        set { if (_muted == value) return; _muted = value; Refresh(); }
    }
    private bool _muted;

    /// <summary>Any thread: the latest note-on an armed track heard from its chosen devices, packed for <see cref="MidiVelocityMeter.Update"/>; 0 when none.</summary>
    public long LastNoteOf(TrackModel track) => _notes.TryGetValue(track, out var slot) ? slot.Packed : 0;

    /// <summary>MIDI driver thread: true when an armed track monitors a message from <paramref name="device"/> (live input's stand-in then stays silent for it).</summary>
    public bool ArmedClaims(string device)
    {
        foreach (var t in _targets) if (t.Armed && !t.Silent && t.Hears(device)) return true;
        return false;
    }

    /// <summary>UI thread: re-reads which tracks monitor (after an arm, a selection, an edit, a setting or an engine sync); opens or closes the MIDI inputs to match.</summary>
    public void Refresh()
    {
        if (_disposed) return;
        var old = _targets;
        var next = Build();
        _targets = next;
        foreach (var o in old) if (!next.Any(n => Same(n, o))) Silence(o);
        foreach (var n in next) if (!old.Any(o => Same(o, n) && o.Program == n.Program)) SendProgram(n);
        if (next.Length > 0)
        {
            if (!_client.IsOpen) _client.Open();
            StartPoll();
        }
        else
        {
            _client.Close();
            _poll?.Stop();
        }
    }

    private static bool Same(Target a, Target b) => ReferenceEquals(a.Track, b.Track) && a.Slot == b.Slot && a.Channel == b.Channel && a.Armed == b.Armed && a.Silent == b.Silent;

    private Target[] Build()
    {
        var mode = MidiMonitorModes.Normalize(_host.Mode);
        if (_muted) return Array.Empty<Target>();
        // Off sounds nothing, but an armed MIDI track still listens (silently) so its input meter shows the notes.
        var off = mode == MidiMonitorModes.Off;
        var list = new List<Target>();
        foreach (var t in _host.Tracks)
            if (!t.IsBus && t.RecordArm && AudioInputs.TakesMidi(t)) list.Add(Make(t, true, off));
        if (off) return list.ToArray();
        if (mode == MidiMonitorModes.ArmedAndSelected && _host.Selected is { IsBus: false } s && !list.Any(x => ReferenceEquals(x.Track, s))
            && (!s.IsAudio || MixerGroups.InstrumentPlays(s)))
            list.Add(Make(s, false));
        return list.ToArray();
    }

    private Target Make(TrackModel track, bool armed, bool silent = false) =>
        new(track, _host.SlotOf(track), track.MidiChannel & 0x0F, track.MidiProgram, armed ? AudioInputs.MidiDevicesOf(track) : Array.Empty<string>(), armed, _notes.GetValue(track, _ => new MidiNoteSlot()), silent);

    private void StartPoll()
    {
        if (_poll == null)
        {
            _poll = _ui.CreateTimer(TimeSpan.FromMilliseconds(PollMs), UiPriority.Background);
            _poll.Tick += (_, _) => Poll();
        }
        _poll.Start();
    }

    /// <summary>UI thread, every two seconds while a track monitors: a keyboard plugged in is opened, the tracks and slots are read again.</summary>
    internal void Poll()
    {
        if (_disposed) return;
        if (_client.IsOpen) _hub.Rescan();
        Refresh();
    }

    /// <summary>MIDI driver thread: nothing that waits. Armed tracks that take the device sound it; otherwise the selected track does.</summary>
    private void OnMessage(string device, int status, int data1, int data2, long stamp)
    {
        if (status < 0x80 || status >= 0xF0) return;
        var targets = _targets;
        if (targets.Length == 0) return;
        var claimed = false;
        foreach (var t in targets)
            if (t.Armed && t.Hears(device))
            {
                if ((status & 0xF0) == 0x90 && data2 > 0) { t.Notes.Record(data2, Environment.TickCount64); NoteHeard?.Invoke(); }
                if (t.Silent) continue;
                claimed = true;
                Send(t, status, data1, data2);
            }
        if (claimed || _host.StandInListening) return;
        foreach (var t in targets) if (!t.Armed) Send(t, status, data1, data2);
    }

    private void Send(Target t, int status, int data1, int data2)
    {
        var s = (byte)((status & 0xF0) | t.Channel);
        if (t.Slot >= 0) _host.Write(t.Slot, s, (byte)data1, (byte)data2, false);
        else _host.WriteFallback(t.Track, s, (byte)data1, (byte)data2);
    }

    private void SendProgram(Target t) { if (!t.Silent) Send(t, 0xC0, Math.Clamp(t.Program, 0, 127), 0); }

    // Sustain off first (under it All Notes Off keeps notes sounding), then the ordered all notes off, then the bend back to centre.
    private void Silence(Target t)
    {
        if (t.Silent) return;   // it sounded nothing
        Send(t, 0xB0, 64, 0);
        if (t.Slot >= 0) _host.Write(t.Slot, (byte)(0xB0 | t.Channel), 123, 0, true);
        else Send(t, 0xB0, 123, 0);
        Send(t, 0xE0, 0, 0x40);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var t in _targets) Silence(t);
        _targets = Array.Empty<Target>();
        _disposed = true;
        _poll?.Stop();
        _client.Dispose();
    }
}
