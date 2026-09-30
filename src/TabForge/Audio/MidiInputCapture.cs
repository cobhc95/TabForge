using System.Diagnostics;
using NAudio.Midi;

namespace TabForge.Audio;

/// <summary>
/// MIDI input for tracks armed with the MIDI input: every MIDI input device, open only while such a track is
/// armed (closed otherwise, so nothing runs for users without MIDI gear). Messages arrive on the driver's thread
/// with a <see cref="Stopwatch"/> timestamp taken on arrival; the listener does the rest.
/// </summary>
public sealed class MidiInputCapture : IDisposable
{
    private readonly List<MidiIn> _inputs = new();

    /// <summary>(status, data1, data2, stopwatch timestamp) on the MIDI driver thread.</summary>
    public event Action<int, int, int, long>? Message;

    public bool IsOpen => _inputs.Count > 0;

    /// <summary>Names of the MIDI input devices (for the status line).</summary>
    public static IReadOnlyList<string> DeviceNames()
    {
        var names = new List<string>();
        try { for (var i = 0; i < MidiIn.NumberOfDevices; i++) names.Add(MidiIn.DeviceInfo(i).ProductName); }
        catch (NAudio.MmException) { }
        return names;
    }

    /// <summary>Opens every input (idempotent). Returns how many opened; devices in use elsewhere are skipped.</summary>
    public int Open()
    {
        if (IsOpen) return _inputs.Count;
        int count;
        try { count = MidiIn.NumberOfDevices; }
        catch (NAudio.MmException) { return 0; }
        for (var i = 0; i < count; i++)
        {
            try
            {
                var input = new MidiIn(i);
                input.MessageReceived += OnMessage;
                input.Start();
                _inputs.Add(input);
            }
            catch (NAudio.MmException) { }   // the device is open in another program
        }
        return _inputs.Count;
    }

    public void Close()
    {
        foreach (var input in _inputs)
        {
            input.MessageReceived -= OnMessage;
            try { input.Stop(); } catch (NAudio.MmException) { }
            input.Dispose();
        }
        _inputs.Clear();
    }

    private void OnMessage(object? sender, MidiInMessageEventArgs e)
    {
        var stamp = Stopwatch.GetTimestamp();
        var raw = e.RawMessage;
        var status = raw & 0xFF;
        if (status < 0x80 || status >= 0xF0) return;   // running status / system messages are not used
        Message?.Invoke(status, (raw >> 8) & 0x7F, (raw >> 16) & 0x7F, stamp);
    }

    public void Dispose() => Close();
}
