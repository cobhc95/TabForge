using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// ScoreToMidiCompiler: channel setup, count-in and the metronome click.
internal sealed partial class ScoreToMidiCompiler
{
    // ---------- setup / metronome ----------

    private void EmitChannelSetup(double atMs)
    {
        foreach (var track in _players)
        {
            var trackIndex = _project.Tracks.IndexOf(track);
            var mainChannel = _channels.Length > trackIndex ? _channels[trackIndex] : track.MidiChannel;
            // The effect channel (bent notes) gets exactly the main channel's program, volume, pan, sends and pitch-bend range.
            for (var pass = 0; pass < 2; pass++)
            {
            var channel = pass == 0 ? mainChannel : EffectChannelOf(trackIndex);
            if (channel < 0) break;
            Setup(track, trackIndex, channel, 0xC0, track.MidiProgram, atMs);
            Setup(track, trackIndex, channel, 0xB0, 7, atMs, MixerGroups.Volume(_project, track));
            Setup(track, trackIndex, channel, 0xB0, 10, atMs, MixerGroups.Pan(_project, track));
            Setup(track, trackIndex, channel, 0xB0, 91, atMs, Math.Clamp(track.Reverb, 0, 127));
            Setup(track, trackIndex, channel, 0xB0, 93, atMs, Math.Clamp(track.Chorus, 0, 127));
            if (channel != ChannelAllocator.PercussionChannel)
            {
                // Define the wheel scale instead of inheriting a synth/device default. Slides and
                // bends below are encoded for exactly +/-2 semitones.
                Setup(track, trackIndex, channel, 0xB0, 101, atMs, 0); // RPN MSB 0
                Setup(track, trackIndex, channel, 0xB0, 100, atMs, 0); // RPN LSB 0: pitch-bend sensitivity
                Setup(track, trackIndex, channel, 0xB0, 6, atMs, PitchBendRangeSemitones);
                Setup(track, trackIndex, channel, 0xB0, 38, atMs, 0);
                Setup(track, trackIndex, channel, 0xB0, 101, atMs, 127); // deselect RPN
                Setup(track, trackIndex, channel, 0xB0, 100, atMs, 127);
                Setup(track, trackIndex, channel, 0xB0, 1, atMs, 0); // neutral modulation wheel on every arm
                // Expression back to full: fade-in/out ramps use CC11, so a start or seek inside one must not leave the channel quiet.
                Setup(track, trackIndex, channel, 0xB0, 11, atMs, 127);
            }
            // Pitch wheel to centre: a bend left behind on stop/pause/loop must not transpose the next passage.
            Setup(track, trackIndex, channel, 0xE0, 0x00, atMs, 0x40);
            }
        }
    }

    private void Setup(TrackModel track, int trackIndex, int channel, int statusBase, int data1, double atMs, int data2 = 0)
    {
        var e = new ScoreEvent
        {
            TimeMs = atMs, DeviceId = track.MidiOutputDeviceId, Channel = channel,
            Status = statusBase | (channel & 0x0F), Data1 = data1, Data2 = data2,
            TrackIndex = trackIndex, IsSetup = true
        };
        _timeline.Events.Add(e);
        _timeline.ChannelSetup.Add(e);
    }

    private double CountInMs(int firstBar)
    {
        var barMs = MusicTime.BarMs(_project, firstBar, _speedScale);
        var num = MusicTime.BarOf(_project, firstBar)?.TimeSigNum ?? _project.TimeSignatureNumerator;
        var beatMs = barMs / Math.Max(1, num);
        for (var b = 0; b < num; b++)
            for (var tick = 0; tick < MetronomeTicksPerBeat; tick++)
                if (ShouldCompileMetronomeTick(tick))
                    Click(b * beatMs + beatMs * tick / MetronomeTicksPerBeat, b == 0 && tick == 0, tick, isCountIn: true);
        return barMs;
    }

    private const int MetronomeTicksPerBeat = 12;

    private void EmitMetronome(double barStart, double barMs, int numerator)
    {
        var beats = Math.Max(1, numerator);
        var beatMs = barMs / beats;
        for (var b = 0; b < beats; b++)
            for (var tick = 0; tick < MetronomeTicksPerBeat; tick++)
                if (ShouldCompileMetronomeTick(tick))
                    Click(barStart + b * beatMs + beatMs * tick / MetronomeTicksPerBeat,
                        b == 0 && tick == 0, tick, isCountIn: false);
    }

    private bool ShouldCompileMetronomeTick(int tick)
    {
        if (_opt.LiveMetronomeEvents) return true;
        var subdivisions = Math.Clamp(_opt.MetronomeSubdivision, 1, 4);
        return tick % (MetronomeTicksPerBeat / subdivisions) == 0;
    }

    private void Click(double timeMs, bool accent, int tick, bool isCountIn)
    {
        // General MIDI's dedicated metronome sounds (34 bell / 33 click), matching TuxGuitar.
        var note = accent ? _opt.MetronomeAccentNote : _opt.MetronomeClickNote;
        var level = accent ? _opt.MetronomeAccentVolume : _opt.MetronomeClickVolume;
        var vel = Math.Clamp((int)Math.Round(110 * Math.Clamp(_opt.MetronomeVolume, 0, 100) / 100.0 * Math.Clamp(level, 0, 100) / 100.0), 0, 127);
        var pairId = ++_metronomePairId;
        _timeline.Events.Add(new ScoreEvent
        {
            TimeMs = timeMs, DeviceId = _metronomeDevice, Channel = ChannelAllocator.PercussionChannel,
            Status = 0x99, Data1 = note, Data2 = vel, TrackIndex = -1,
            IsMetronome = true, IsCountInClick = isCountIn, IsMetronomeAccent = accent,
            MetronomeTick = tick, MetronomePairId = pairId
        });
        _timeline.Events.Add(new ScoreEvent
        {
            TimeMs = timeMs + 35, DeviceId = _metronomeDevice, Channel = ChannelAllocator.PercussionChannel,
            Status = 0x89, Data1 = note, Data2 = 0, TrackIndex = -1,
            IsMetronome = true, IsCountInClick = isCountIn, IsMetronomeAccent = accent,
            MetronomeTick = tick, MetronomePairId = pairId
        });
    }
}
