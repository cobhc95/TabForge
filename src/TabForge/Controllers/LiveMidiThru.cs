using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Controllers;

// Owns: the transpose, the channel stamp and the audio/MIDI split of one live MIDI message to a track's sound.
// Does not own: which tracks listen (RecordingController.MonitorsLive), channel assignment, recording. UI thread only.
// Tests: TestPracticeSilence, TestAudioTrackRouting (full-suite).
internal static class LiveMidiThru
{
    /// <summary>Plays a message on the track's channel; the low nibble of <paramref name="status"/> is replaced and note on/off are transposed.</summary>
    internal static void Send(DocumentSession source, TrackModel track, int channel, int status, int data1, int data2)
    {
        var type = status & 0xF0;
        var pitch = Math.Clamp(data1 + (type is 0x80 or 0x90 ? track.Transpose : 0), 0, 127);
        if (track.IsAudio) source.Playback.Routing?.SendLiveEngineOnly(type | (channel & 0x0F), pitch, data2);   // its instrument plug-in or nothing
        else source.Playback.Engine.SendLive(track.MidiOutputDeviceId, type | (channel & 0x0F), pitch, data2);
    }

    /// <summary>Sets the track's program on its channel; an audio track keeps its instrument plug-in's own sound.</summary>
    internal static void SetProgram(DocumentSession source, TrackModel track, int channel)
    {
        if (track.IsAudio) return;
        source.Playback.Engine.SendLive(track.MidiOutputDeviceId, 0xC0 | (channel & 0x0F), track.MidiProgram, 0);
    }
}
