using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Audio;

namespace TabForge.Avalonia.Platform;

// Owns: sounding one previewed note through the track's plug-in instrument: note-on to the channel's engine slot, a delayed note-off.
// Does not own: the choice of sound (NotePreviewSound), the routes (RoutedMidiOutput; a channel has one only when its track has an instrument plug-in).
// Tests: TabForge.Avalonia.Tests.FretboardNoteAuditionTests.
// Threads: UI thread only; the delayed off resumes on the UI thread, so engine writes stay in order.
internal sealed class TrackInstrumentPreview
{
    private const int Velocity = 100;
    private readonly Dictionary<(int Channel, int Note), long> _tokens = new();   // a newer preview of the same key makes the older off a no-op
    private long _serial;

    /// <summary>Sounds the note on the routed output and schedules its off; false (nothing sent) when the channel has no plug-in instrument, so the caller uses the built-in synth.</summary>
    public bool TryPlay(RoutedMidiOutput? routing, int channel, int note, int lengthMs)
    {
        if (routing is null) return false;
        var ch = channel & 0x0F;
        var key = (ch, note);
        if (_tokens.ContainsKey(key)) routing.SendLiveEngineOnly(0x80 | ch, note, 0);
        if (!routing.SendLiveEngineOnly(0x90 | ch, note, Velocity)) return false;
        var token = ++_serial;
        _tokens[key] = token;
        _ = ReleaseAsync(routing, key, token, lengthMs);
        return true;
    }

    private async Task ReleaseAsync(RoutedMidiOutput routing, (int Channel, int Note) key, long token, int lengthMs)
    {
        try { await Task.Delay(Math.Clamp(lengthMs, 60, 8000)); }
        finally
        {
            if (_tokens.TryGetValue(key, out var current) && current == token)
            {
                _tokens.Remove(key);
                routing.SendLiveEngineOnly(0x80 | key.Channel, key.Note, 0);
            }
        }
    }
}
