using TabForge.Audio.Contracts;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Playback;

// Owns: placing the recorded MIDI clips of a track's clip lanes into the timeline, in song time, with the timeline's start offset: their notes
//   (a muted note skipped, a note's own channel kept) and their controller points (control changes, pitch bend, program change, channel aftertouch).
// Does not own: the score note emission (ScoreToMidiCompiler.cs, ScoreToMidiCompiler.Techniques.cs).
// Tests: TabForge.Avalonia.Tests.PianoRollControllerTests.

// ScoreToMidiCompiler: recorded MIDI clips on a track's clip lanes.
internal sealed partial class ScoreToMidiCompiler
{
    // ---------- MIDI clips (recorded MIDI on a track's clip lanes) ----------

    /// <summary>True once a lane point before the start was folded into the chase (the setup then carries lane state too).</summary>
    private bool _clipChased;

    /// <summary>A clip piece's controller points (CC, pitch bend, program change, channel aftertouch) inside its trimmed part, on the track's channel.
    /// A lane program change overrides the track's own program from its position (the mixer program plays where no lane point holds); points before
    /// the start are chased into the channel setup, so a seek into the middle restores the last lane value.</summary>
    private void EmitClipControllers(TrackModel track, int trackIndex, int channel, AudioClip clip, double startSec, double offsetSec, double lengthSec, double speed, double offsetMs, double fromMs)
    {
        if (clip.Controllers is not { Count: > 0 } points) return;
        foreach (var point in points)
        {
            if (point.StartSec < offsetSec - 1e-6 || point.StartSec >= offsetSec + lengthSec) continue;
            var atMs = (startSec + (point.StartSec - offsetSec) / speed) * 1000 * _speedScale - offsetMs;
            if (!ClipMessage(point, channel, out var statusBase, out var data1, out var data2)) continue;   // e.g. a program point on the percussion channel
            if (atMs < fromMs - 0.5)
            {
                RecordChase(channel, statusBase, data1, data2, trackIndex);
                _clipChased = true;
                continue;
            }
            if (atMs >= _timeline.TotalMs) continue;
            _timeline.Events.Add(new ScoreEvent
            {
                TimeMs = atMs, DeviceId = OutputDeviceOf(track), Channel = channel, TrackIndex = trackIndex,
                Status = statusBase | (channel & 0x0F), Data1 = data1, Data2 = data2,
            });
        }
    }

    /// <summary>The MIDI message for a lane point: CC (0xB0), pitch bend (0xE0, 14-bit), program change (0xC0, one data byte) or
    /// channel aftertouch (0xD0, one data byte). False when the point sends nothing (program on the percussion channel).</summary>
    private static bool ClipMessage(ClipController point, int channel, out int statusBase, out int data1, out int data2)
    {
        if (point.Controller == ClipController.ProgramChange)
        {
            statusBase = 0xC0; data1 = Math.Clamp(point.Value, 0, MidiLimits.Max); data2 = 0;
            return channel != ChannelAllocator.PercussionChannel;
        }
        if (point.Controller == ClipController.ChannelAftertouch)
        {
            statusBase = 0xD0; data1 = Math.Clamp(point.Value, 0, MidiLimits.Max); data2 = 0;
            return true;
        }
        if (point.Controller == ClipController.PitchBend)
        {
            var bend = Math.Clamp(point.Value + 8192, 0, 16383);
            statusBase = 0xE0; data1 = bend & 0x7F; data2 = bend >> 7;
            return true;
        }
        statusBase = 0xB0; data1 = Math.Clamp(point.Controller, 0, MidiLimits.Max); data2 = Math.Clamp(point.Value, 0, MidiLimits.Max);
        return true;
    }

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
                    if (note.Muted) continue;   // a note muted in the piano roll stays in the clip but never plays
                    // Only the part of the note inside the (trimmed) clip plays.
                    var srcOn = Math.Max(note.StartSec, piece.OffsetSec);
                    var srcOff = Math.Min(note.StartSec + note.LengthSec, piece.OffsetSec + piece.SourceLengthSec);
                    if (srcOff <= srcOn || note.StartSec < piece.OffsetSec - 1e-6) continue;
                    var onMs = (piece.StartSec + (srcOn - piece.OffsetSec) / speed) * 1000 * _speedScale - offsetMs;
                    var offMs = (piece.StartSec + (srcOff - piece.OffsetSec) / speed) * 1000 * _speedScale - offsetMs;
                    if (onMs < fromMs - 0.5 || onMs >= _timeline.TotalMs) continue;
                    var pitch = Math.Clamp(note.Pitch + (int)Math.Round(clip.Pitch) + track.Transpose, 0, MidiLimits.Max);
                    var velocity = Math.Clamp((int)Math.Round(note.Velocity * Gain.FromDb(clip.GainDb)), 1, MidiLimits.Max);
                    var noteChannel = note.Channel is int own and >= 0 and <= 15 ? own : channel;   // a note's own channel (set in the piano roll)
                    _timeline.Events.Add(new ScoreEvent { TimeMs = onMs, DeviceId = OutputDeviceOf(track), Channel = noteChannel, Status = 0x90 | (noteChannel & 0x0F), Data1 = pitch, Data2 = velocity, TrackIndex = trackIndex });
                    _timeline.Events.Add(new ScoreEvent { TimeMs = Math.Min(offMs, _timeline.TotalMs), DeviceId = OutputDeviceOf(track), Channel = noteChannel, Status = 0x80 | (noteChannel & 0x0F), Data1 = pitch, Data2 = 0, TrackIndex = trackIndex });
                }
                foreach (var piece in TabForge.Services.ClipLoop.Pieces(clip))
                    EmitClipControllers(track, trackIndex, channel, clip, piece.StartSec, piece.OffsetSec, piece.SourceLengthSec, speed, offsetMs, fromMs);
            }
        }
    }
}
