using System;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia.Transport;

// Owns: one track-properties window's string sound over its song's playback: each Ring fires one preview note
//   through the track's live device, channel and program and remembers it as the one owned note; Silence cuts
//   only that owned note with one note-off. At most one note is owned: ringing another pitch silences the old one first.
// Does not own: the tuning math (the window already sounds SoundingPitch = draft + capo), the vibrate or the repeat
//   timer (TrackPropertiesWindow.Strings.cs), the engine or its creation (AvaloniaPlaybackSessions).
// Tests: TabForge.Avalonia.Tests.EngineStringRingerTests.
// Threads: UI thread only. Ring never waits on the engine: one fire-and-forget preview whose auto-off the engine owns.
public sealed class EngineStringRinger : IStringRinger
{
    private readonly AvaloniaPlaybackSessions _playback;
    private readonly SongDocument _song;
    private readonly TrackModel _track;
    private (int Device, int Channel, int Pitch)? _owned;   // the preview note still sounding, if any; UI thread only

    private EngineStringRinger(AvaloniaPlaybackSessions playback, SongDocument song, TrackModel track)
    {
        _playback = playback;
        _song = song;
        _track = track;
    }

    /// <summary>The ringer for <paramref name="track"/> through its song's playback; null when there is no playback
    /// or the document is not a song (the window stays silent, as in captures and parity shots).</summary>
    public static IStringRinger? For(AvaloniaPlaybackSessions? playback, IEditableDocument? document, TrackModel track)
    {
        if (playback is null || document is not SongDocument song) return null;
        return new EngineStringRinger(playback, song, track);
    }

    /// <summary>Starts one ring of this sounding pitch; the engine fades it out by itself after <see cref="StringVibration.RingMs"/>.</summary>
    public void Ring(int midi)
    {
        var pitch = Math.Clamp(midi, 0, MidiLimits.Max);
        var channel = _track.MidiChannel & 0x0F;
        var device = _track.MidiOutputDeviceId;
        var program = Math.Clamp(_track.MidiProgram, 0, MidiLimits.Max);
        // A new pitch takes the one owned slot: the old note is cut first (same note re-rings: the engine retriggers it).
        if (_owned is { } owned && (owned.Device != device || owned.Channel != channel || owned.Pitch != pitch)) Silence();
        _owned = (device, channel, pitch);
        _ = _playback.PlayTestNoteAsync(device, channel, program, pitch, (int)StringVibration.RingMs);
    }

    /// <summary>Cuts the owned ring, if any; anything else playing (the song, other previews) is untouched.</summary>
    public void Silence()
    {
        if (_owned is not { } owned) return;
        _owned = null;
        _playback.SilencePreview(_song, owned.Device, owned.Channel, owned.Pitch);
    }
}
