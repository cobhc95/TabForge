using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

// Owns: a track chain's live plug-in settings (auto pitch, bypass, synth on, gain, wiring, id lookup), split from TrackChain.cs.
// Does not own: the audio render or the MIDI input side (TrackChain, TrackChain.Midi).
public sealed partial class TrackChain : IDisposable
{
    /// <summary>Any thread: the automatic pitch-match transpose of the instrument at a chain index.</summary>
    public void SetAutoPitch(int index, int semitones)
    {
        foreach (var e in _effects) if (e.Index == index && e.IsInstrument) e.AutoPitch.Shift = semitones;
    }

    /// <summary>Audio thread: the stream through an instrument's automatic transpose, into a scratch list (the stream passed on stays untransposed).</summary>
    private ReadOnlySpan<BlockMidi> AutoPitched(Effect e, ReadOnlySpan<BlockMidi> input)
    {
        var n = 0;
        foreach (var m in input)
        {
            if (n >= _xpose.Length) break;
            var status = m.Status; var d1 = m.Data1;
            if (e.AutoPitch.Map(ref status, ref d1, m.Data2)) _xpose[n++] = new BlockMidi { Frame = m.Frame, Status = status, Data1 = d1, Data2 = m.Data2 };
        }
        return new ReadOnlySpan<BlockMidi>(_xpose, 0, n);
    }

    /// <summary>Every plug-in stage in chain order (engine thread sets processors and links on them).</summary>
    public IReadOnlyList<Effect> Effects => _effects;

    /// <summary>Bus or master chain: audio comes from the summed input, fader at unity.</summary>
    private bool IsBus => _slot >= MixEngine.BusBase;

    /// <summary>Post-fader output of the last block (sidechain source, stems).</summary>
    private float[] PostL { get; }
    private float[] PostR { get; }
    private readonly float[][] _side4 = new float[4][];
    private bool _unsorted;

    /// <summary>Live bypass (any thread): the plug-in stays loaded, only its processing stops.</summary>
    public void SetPluginBypass(int index, bool bypassed)
    {
        foreach (var e in _effects)
            if (e.Index == index)
            {
                e.Bypass = bypassed;
                if (!bypassed) { e.Misbehaved = false; e.MisbehaveReported = false; }   // switched on again: it gets another chance
                if (!bypassed && e.IsInstrument) _panic = true;
            }
    }

    private volatile bool _synthOn = true, _synthSilence;
    private readonly BlockMidi[] _synthStop = new BlockMidi[32];
    /// <summary>Set when the General MIDI synth is switched back on: the mixer replays the slot's channel state (program, controllers) into it.</summary>
    public volatile bool Reprime;

    /// <summary>
    /// Live (any thread): the General MIDI synth sounds or not. Off costs nothing (it is skipped); its notes are released first.
    /// No rebuild, so GM taking over from a bypassed instrument (or the chain switched off) keeps the timing and every plug-in editor.
    /// </summary>
    public void SetSynthOn(bool on)
    {
        if (_synthOn == on) return;
        _synthOn = on;
        if (on) Reprime = true; else _synthSilence = true;
    }

    /// <summary>Changes a plug-in's output volume live; the audio thread ramps to it per block (no zipper noise).</summary>
    public void SetPluginGain(int index, float gain)
    {
        foreach (var e in _effects) if (e.Index == index) e.Target = gain;
    }

    /// <summary>Engine thread: the chain id (PluginSpec.Id) of the plug-in at a chain index, or null.</summary>
    public string? IdAt(int index)
    {
        foreach (var e in _effects)
            if (e.Index == index) foreach (var (id, p) in ById) if (ReferenceEquals(p, e.Plugin)) return id;
        return null;
    }

    /// <summary>Main thread, diagnostics: blocks the chain's stage for this instance has processed so far; -1 when the chain has no such stage.</summary>
    public long ProcessCallsOf(IPluginInstance plugin)
    {
        foreach (var e in _effects) if (ReferenceEquals(e.Plugin, plugin)) return Volatile.Read(ref e.ProcessCalls);
        return -1;
    }

    /// <summary>
    /// Live serial-chain options of one plug-in (any thread): bit 0 pass incoming MIDI on, bit 1 send its MIDI output on, bit 2 instrument audio replaces,
    /// bit 3 MIDI input Source is None, bits 16..31 the channels it does not take.
    /// </summary>
    public void SetPluginWiring(int index, int flags)
    {
        foreach (var e in _effects)
            if (e.Index == index) ApplyWiring(e, flags);
    }

    /// <summary>Sets one stage from wiring flags (see <see cref="SetPluginWiring"/>).</summary>
    internal static void ApplyWiring(Effect e, int flags)
    {
        e.PassMidi = (flags & 1) != 0; e.MidiOutToNext = (flags & 2) != 0; e.Replace = (flags & 4) != 0;
        e.MidiNone = (flags & 8) != 0; e.MidiBlock = (int)((uint)flags >> 16);
    }

    private readonly BlockMidi[] _heard = new BlockMidi[2048];

    /// <summary>Audio thread, no allocation: the part of the arriving stream this plug-in's MIDI input takes (none for Source None, minus blocked channels).</summary>
    private ReadOnlySpan<BlockMidi> HeardBy(Effect e, ReadOnlySpan<BlockMidi> stream)
    {
        if (e.MidiNone) return default;
        var block = e.MidiBlock;
        if (block == 0 || stream.Length == 0) return stream;
        var n = 0;
        foreach (var m in stream)
            if ((m.Status >= 0xF0 || (block & (1 << (m.Status & 0x0F))) == 0) && n < _heard.Length) _heard[n++] = m;
        return new ReadOnlySpan<BlockMidi>(_heard, 0, n);
    }
}
