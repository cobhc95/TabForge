using System.Diagnostics;
using NAudio.Midi;

namespace TabForge.Audio;

/// <summary>A MIDI input that knows its devices by name: each message carries the device it came from, and devices can be added or dropped while open.</summary>
public interface IMidiDeviceSet : IMidiInputSource
{
    /// <summary>(device name, status, data1, data2, stopwatch timestamp) on the MIDI driver thread.</summary>
    event Action<string, int, int, int, long>? DeviceMessage;
    /// <summary>Names of the devices open now (empty when closed). A copy: safe to keep.</summary>
    IReadOnlyList<string> OpenNames { get; }
    /// <summary>Names of the devices present now; never throws.</summary>
    IReadOnlyList<string> ListNames();
    /// <summary>While open: opens devices connected since and drops ones gone, leaving the others running. True when the open set changed.</summary>
    bool Rescan();
}

// Owns: the NAudio MIDI input handles: opening, closing and rescanning them, and stamping each message with its device name.
// Does not own: sharing the handles between listeners (<see cref="MidiInputHub"/>) or what a message means.
// Tests: TestMidiInputDeviceFilter (through a fake device set), TestKeyboardModeMidiListener.
/// <summary>
/// MIDI input for tracks armed with the MIDI input: every MIDI input device, open only while such a track is
/// armed (closed otherwise, so nothing runs for users without MIDI gear). Messages arrive on the driver's thread
/// with a <see cref="Stopwatch"/> timestamp taken on arrival; the listener does the rest.
/// </summary>
public sealed class MidiInputCapture : IMidiDeviceSet
{
    private sealed class OpenInput
    {
        public MidiIn Input = null!;
        public string Name = "";
        public int Index;   // the device id it was opened with
        public EventHandler<MidiInMessageEventArgs> Handler = null!;
    }

    private readonly List<OpenInput> _inputs = new();
    private string[] _lastNames = Array.Empty<string>();   // the devices present at the last open or rescan: a rescan with the same list does nothing

    /// <summary>(status, data1, data2, stopwatch timestamp) on the MIDI driver thread, from any device.</summary>
    public event Action<int, int, int, long>? Message;
    public event Action<string, int, int, int, long>? DeviceMessage;

    public bool IsOpen => _inputs.Count > 0;

    /// <summary>Names of the MIDI input devices (for the status line).</summary>
    public static IReadOnlyList<string> DeviceNames()
    {
        var names = new List<string>();
        try { for (var i = 0; i < MidiIn.NumberOfDevices; i++) names.Add(MidiIn.DeviceInfo(i).ProductName); }
        catch (NAudio.MmException ex) { Services.Trace.Error(Services.Trace.Playback, "MIDI: list input devices: " + ex.Message); }
        return names;
    }

    public IReadOnlyList<string> ListNames() => DeviceNames();

    public IReadOnlyList<string> OpenNames => _inputs.Select(o => o.Name).ToArray();

    /// <summary>Opens every input (idempotent). Returns how many opened; devices in use elsewhere are skipped.</summary>
    public int Open()
    {
        if (IsOpen) return _inputs.Count;
        var names = DeviceNames();
        _lastNames = names.ToArray();
        for (var i = 0; i < names.Count; i++) TryOpen(i, names[i]);
        return _inputs.Count;
    }

    public bool Rescan()
    {
        if (!IsOpen) return false;
        var names = DeviceNames();
        if (names.SequenceEqual(_lastNames)) return false;   // nothing connected or removed
        _lastNames = names.ToArray();
        var changed = false;
        // Drop the open inputs whose name no longer appears as often as it is open (a removed device).
        foreach (var group in _inputs.GroupBy(o => o.Name).ToList())
        {
            var extra = group.Count() - names.Count(n => n == group.Key);
            // The handles whose device id no longer holds that name are the ones gone; the rest keep running.
            var order = group.OrderByDescending(o => !(o.Index < names.Count && names[o.Index] == o.Name)).ThenByDescending(o => o.Index);
            foreach (var gone in order.Take(Math.Max(0, extra))) { CloseOne(gone); _inputs.Remove(gone); changed = true; }
        }
        // Open the names present more often than open (a new device); a handle already open fails and the next index of that name is tried.
        foreach (var name in names.Distinct())
        {
            var missing = names.Count(n => n == name) - _inputs.Count(o => o.Name == name);
            for (var i = 0; i < names.Count && missing > 0; i++)
                if (names[i] == name && TryOpen(i, name)) { missing--; changed = true; }
        }
        return changed;
    }

    private bool TryOpen(int index, string name)
    {
        try
        {
            var input = new MidiIn(index);
            var entry = new OpenInput { Input = input, Name = name, Index = index };
            entry.Handler = (_, e) => OnMessage(name, e);
            input.MessageReceived += entry.Handler;
            input.Start();
            _inputs.Add(entry);
            return true;
        }
        catch (NAudio.MmException ex) { Services.Trace.Error(Services.Trace.Playback, "MIDI: open input: " + ex.Message); return false; }   // the device is open in another program
    }

    private static void CloseOne(OpenInput o)
    {
        o.Input.MessageReceived -= o.Handler;
        try { o.Input.Stop(); } catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException) { Services.Trace.Error(Services.Trace.Playback, "MIDI: stop input: " + ex.Message); }
        try { o.Input.Dispose(); } catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException) { Services.Trace.Error(Services.Trace.Playback, "MIDI: close input: " + ex.Message); }
    }

    public void Close()
    {
        foreach (var o in _inputs) CloseOne(o);
        _inputs.Clear();
    }

    private void OnMessage(string name, MidiInMessageEventArgs e)
    {
        var stamp = Stopwatch.GetTimestamp();
        var raw = e.RawMessage;
        var status = raw & 0xFF;
        if (status < 0x80 || status >= 0xF0) return;   // running status / system messages are not used
        var d1 = (raw >> 8) & 0x7F; var d2 = (raw >> 16) & 0x7F;
        DeviceMessage?.Invoke(name, status, d1, d2, stamp);
        Message?.Invoke(status, d1, d2, stamp);
    }

    public void Dispose() => Close();
}
