using TabForge.Audio.Contracts;
using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: placing the recorded MIDI clips of a track's clip lanes into the timeline, in song time, with the timeline's start offset.
// Does not own: the score note emission (ScoreToMidiCompiler.cs, ScoreToMidiCompiler.Techniques.cs).
// Tests: TestClipEdgesAndLoops.

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
        var clipTracks = _players.Where(t => (!t.IsAudio || MixerGroups.InstrumentPlays(t)) && t.AudioClips.Any(c => c.IsMidi && !c.Muted)).ToList();   // Q1: an audio track's MIDI clips sound only through its instrument plug-in
        if (clipTracks.Count == 0) return;
        var full = new ScoreToMidiCompiler(_project, new PlaybackOptions { RespectMuteSolo = false, SkipClips = true, Speed = _opt.Speed }).Build();
        var first = _timeline.Bars[0];
        var songStart = full.Bars.FirstOrDefault(b => b.Bar == first.Bar);
        var offsetMs = songStart.StartMs - first.StartMs;   // song ms -> this timeline's ms
        var fromMs = _timeline.PlayFromMs;
        foreach (var track in clipTracks)
        {
            var trackIndex = _project.Tracks.IndexOf(track);
            var channel = trackIndex < _channels.Length ? _channels[trackIndex] : 0;
            if (channel < 0) continue;   // an audio track with no free channel
            foreach (var clip in track.AudioClips)
            {
                if (!clip.IsMidi || clip.Muted) continue;
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
                    var pitch = Math.Clamp(note.Pitch + (int)Math.Round(clip.Pitch) + track.Transpose, 0, 127);
                    var velocity = Math.Clamp((int)Math.Round(note.Velocity * Gain.FromDb(clip.GainDb)), 1, 127);
                    _timeline.Events.Add(new ScoreEvent { TimeMs = onMs, DeviceId = track.MidiOutputDeviceId, Channel = channel, Status = 0x90 | (channel & 0x0F), Data1 = pitch, Data2 = velocity, TrackIndex = trackIndex });
                    _timeline.Events.Add(new ScoreEvent { TimeMs = Math.Min(offMs, _timeline.TotalMs), DeviceId = track.MidiOutputDeviceId, Channel = channel, Status = 0x80 | (channel & 0x0F), Data1 = pitch, Data2 = 0, TrackIndex = trackIndex });
                }
            }
        }
    }
}
