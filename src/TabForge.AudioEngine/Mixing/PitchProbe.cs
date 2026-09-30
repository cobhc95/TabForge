using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

/// <summary>
/// One pitch measurement of an instrument (see EngineHost.MeasurePitch). Built on the engine thread with every buffer preallocated;
/// the audio thread feeds the plug-in its test notes (on its own MIDI input only) and captures its output here instead of the mix.
/// </summary>
public sealed class PitchProbe
{
    public readonly int Index;
    public readonly int[] Notes;
    public readonly int NoteSamples, GapSamples, Channel;
    /// <summary>Mono capture, <see cref="NoteSamples"/> per note.</summary>
    public readonly float[] Capture;
    private readonly BlockMidi[] _events = new BlockMidi[8];
    private long _pos;
    private volatile bool _done;
    public bool Done => _done;
    /// <summary>Set by the engine to abort (playback started): the next block ends the notes and finishes.</summary>
    public volatile bool Stop;
    private long Total => (long)Notes.Length * (NoteSamples + GapSamples);

    public PitchProbe(int index, int[] notes, int channel, int sampleRate)
    {
        Index = index; Notes = notes; Channel = channel & 0x0F;
        NoteSamples = sampleRate / 2; GapSamples = sampleRate / 5;
        Capture = new float[notes.Length * NoteSamples];
    }

    /// <summary>Audio thread: this block's test note-ons / note-offs.</summary>
    public ReadOnlySpan<BlockMidi> Events(int frames)
    {
        var n = 0; var period = NoteSamples + GapSamples;
        if (Stop)
        {
            // Aborted: end the test note on the plug-in (all notes off on its channel) and finish.
            _events[0] = new BlockMidi { Frame = 0, Status = (byte)(0xB0 | Channel), Data1 = 123 };
            _done = true;
            return new ReadOnlySpan<BlockMidi>(_events, 0, 1);
        }
        for (var i = 0; i < Notes.Length && n < _events.Length - 1; i++)
        {
            long on = (long)i * period, off = on + NoteSamples;
            if (on >= _pos && on < _pos + frames) _events[n++] = new BlockMidi { Frame = (int)(on - _pos), Status = (byte)(0x90 | Channel), Data1 = (byte)Notes[i], Data2 = 100 };
            if (off >= _pos && off < _pos + frames) _events[n++] = new BlockMidi { Frame = (int)(off - _pos), Status = (byte)(0x80 | Channel), Data1 = (byte)Notes[i], Data2 = 0 };
        }
        return new ReadOnlySpan<BlockMidi>(_events, 0, n);
    }

    /// <summary>Audio thread: stores the plug-in's output of this block and advances.</summary>
    public void Take(float[][] output, int frames)
    {
        if (Stop) return;
        var period = NoteSamples + GapSamples;
        for (var s = 0; s < frames; s++)
        {
            var t = _pos + s;
            if (t >= Total) break;
            var i = (int)(t / period); var local = (int)(t % period);
            if (local < NoteSamples) Capture[i * NoteSamples + local] = (output[0][s] + output[1][s]) * 0.5f;
        }
        _pos += frames;
        if (_pos >= Total) _done = true;
    }
}
