using TabForge.Platform.Contracts;
using NAudio.Midi;

namespace TabForge.Platform.Windows;

// Owns: the winmm (NAudio.Midi) MIDI input calls: device count and names, open, start, stop, close, and the raw message event.
// Does not own: which devices a listener opens or how a message is read (Core/Audio/MidiInputCapture.cs).
// Tests: none dedicated (the same NAudio calls MidiInputCapture made before the port existed).
/// <summary>The Windows adapter of <see cref="IMidiInputBackend"/>: NAudio's winmm MidiIn, one call per member.</summary>
public sealed class WinMmMidiInputBackend : IMidiInputBackend
{
    public string Id => "winmm";
    public bool IsAvailable => true;

    public IReadOnlyList<string> InputNames()
    {
        var names = new List<string>();
        try { for (var i = 0; i < MidiIn.NumberOfDevices; i++) names.Add(MidiIn.DeviceInfo(i).ProductName); }
        catch (NAudio.MmException ex) { Services.Trace.Error(Services.Trace.Playback, "MIDI: list input devices: " + ex.Message); }
        return names;
    }

    public IMidiInputPort Open(int index)
    {
        try { return new Port(new MidiIn(index)); }
        catch (NAudio.MmException ex) { throw new MidiPortException(ex.Message, ex); }
    }

    private sealed class Port : IMidiInputPort
    {
        private readonly MidiIn _input;
        private readonly EventHandler<MidiInMessageEventArgs> _handler;

        public Port(MidiIn input)
        {
            _input = input;
            _handler = (_, e) => RawMessage?.Invoke(e.RawMessage);
            input.MessageReceived += _handler;
        }

        public event Action<int>? RawMessage;

        public void Start()
        {
            try { _input.Start(); }
            catch (NAudio.MmException ex) { throw new MidiPortException(ex.Message, ex); }
        }

        public void Stop()
        {
            try { _input.Stop(); } catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException) { Services.Trace.Error(Services.Trace.Playback, "MIDI: stop input: " + ex.Message); }
        }

        public void Dispose()
        {
            _input.MessageReceived -= _handler;
            try { _input.Dispose(); } catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException) { Services.Trace.Error(Services.Trace.Playback, "MIDI: close input: " + ex.Message); }
        }
    }
}
