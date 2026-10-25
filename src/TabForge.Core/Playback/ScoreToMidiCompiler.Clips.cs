using TabForge.Audio.Contracts;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Playback;

// Owns: placing the recorded MIDI clips of a track's clip lanes into the timeline, in song time, with the timeline's start offset.
// Does not own: the score note emission (ScoreToMidiCompiler.cs, ScoreToMidiCompiler.Techniques.cs).
// Tests: no dedicated test class.

// ScoreToMidiCompiler: recorded MIDI clips on a track's clip lanes.
internal sealed partial class ScoreToMidiCompiler
{
    // ---------- MIDI clips (recorded MIDI on a track's clip lanes) ----------

    /// <summary>
    /// MIDI clips are placed in song time (the whole song as performed, repeats included), like audio clips.
    /// This timeline may start mid-song, so its first bar is matched to that bar's first performance.
    /// </summary>
    private void EmitClipNotes()
    {
        if (_opt.SkipClips || _timeline.Bars.Count == 0) return;
        var clipTracks = _players.Where(t => (!t.IsAudio || MixerGroups.InstrumentPlays(t)) && t.AudioClips.Any(c => c.IsMidi && ClipLanes.Audible(t, c))).ToList();   // Q1: an audio track's MIDI clips sound only through its instrument plug-in
        if (clipTracks.Count == 0) return;
        var full = new ScoreToMidiCompiler(_project, new PlaybackOptions { RespectMuteSolo = false, SkipClips = true, Speed = _opt.Speed }).Build();
        var first = _timeline.Bars[0];
        var occurrence = _opt.StartOccurrence is { } started && started > 1 ? started : 1;
        var starts = full.Bars.Where(b => b.Bar == first.Bar).ToList();
        var songStart = starts.Count >= occurrence ? starts[occurrence - 1] : starts.FirstOrDefault();
        var offsetMs = songStart.StartMs - first.StartMs;   // song ms -> this timeline's ms (the started pass when it is the second play)
        var fromMs = _timeline.PlayFromMs;
        foreach (var track in clipTracks)
        {
            var trackIndex = _project.Tracks.IndexOf(track);
            var channel = trackIndex < _channels.Length ? _channels[trackIndex] : 0;
            if (channel < 0) continue;   // an audio track with no free channel
            foreach (var clip in track.AudioClips)
            {
                // A clip plays straight through in performed time, even over a repeat (the repeat view only folds its drawing).
                if (!clip.IsMidi || !ClipLanes.Audible(track, clip)) continue;
                var speed = Math.Clamp(clip.Speed, 0.25, 4);
                foreach (var piece in TabForge.Services.ClipLoop.Pieces(clip))   // a clip longer than its media loops: one pass per piece
                foreach (var note in clip.Notes!)
                {
                    // Only the part of the note inside the (trimmed) clip plays.
                    var srcOn = Math.Max(note.StartSec, piece.OffsetSec);
                    var srcOff = Math.Min(note.StartSec + note.LengthSec, piece.OffsetSec + piece.SourceLengthSec);
                    if (srcOff <= srcOn || note.StartSec < piece.OffsetSec - 1e-6) continue;
                    var onMs = (piece.StartSec + (srcOn - piece.OffsetSec) / speed) * 1000 * _speedScale - offsetMs;
                    var offMs = (piece.StartSec + (srcOff - piece.OffsetSec) / speed) * 1000 * _speedScale - offsetMs;
                    if (onMs < fromMs - 0.5 || onMs >= _timeline.TotalMs) continue;
                    var pitch = Math.Clamp(note.Pitch + (int)Math.Round(clip.Pitch) + track.Transpose, 0, MidiLimits.Max);
                    var velocity = Math.Clamp((int)Math.Round(note.Velocity * Gain.FromDb(clip.GainDb)), 1, MidiLimits.Max);
                    _timeline.Events.Add(new ScoreEvent { TimeMs = onMs, DeviceId = track.MidiOutputDeviceId, Channel = channel, Status = 0x90 | (channel & 0x0F), Data1 = pitch, Data2 = velocity, TrackIndex = trackIndex });
                    _timeline.Events.Add(new ScoreEvent { TimeMs = Math.Min(offMs, _timeline.TotalMs), DeviceId = track.MidiOutputDeviceId, Channel = channel, Status = 0x80 | (channel & 0x0F), Data1 = pitch, Data2 = 0, TrackIndex = trackIndex });
                }
            }
        }
    }
}
