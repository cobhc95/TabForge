using TabForge.Models;

namespace TabForge.Playback;

// Owns: the explicit releases a panic sends before the device reset: a note-off for every sounding note and sustain off (CC64 = 0) on every channel.
// Does not own: the device reset itself (RoutedMidiOutput.ResetAll) or the output queue (PlaybackEngine.MidiOut.cs).
// Tests: no dedicated test class.
public sealed partial class PlaybackEngine
{
    /// <summary>Runs under _outputGate, before the all-notes-off reset: synths that ignore CC123 still get each note-off, and held pedals are let go.</summary>
    private void ReleaseHeldNotes(ScoreTimeline? timeline)
    {
        ReleaseSoundingNotes(null);
        var devices = new HashSet<int>();
        if (timeline is not null) foreach (var e in timeline.ChannelSetup) devices.Add(e.DeviceId);
        if (devices.Count == 0) foreach (var d in _output.Devices) devices.Add(d.DeviceId);
        foreach (var device in devices)
            for (var channel = 0; channel < 16; channel++)
                _output.Send(device, 0xB0 | channel, 64, 0);
    }
}
