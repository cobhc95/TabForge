using TabForge.Sf2Synth.Effects;
using TabForge.Sf2Synth.Sf2;

namespace TabForge.Sf2Synth;

// Owns: the public synth: MIDI in (MeltySynth-shaped: ProcessMidiMessage, Render, NoteOffAll, Reset), zone matching, voice
//   allocation (free, else quietest released, else oldest), exclusive class, sustain, the optional ILayer, reverb/chorus, master volume.
// Does not own: a voice's DSP (Voice), file parsing (Sf2), the layer's sound (e.g. Guitar/GuitarLayer).
// Tests: Sf2SynthTests, GuitarLayerTests.
public sealed class Synthesizer
{
    public const int Chunk = 64;
    private readonly SoundFont _font;
    private readonly Channel[] _ch = Enumerable.Range(0, 16).Select(i => new Channel(i == 9)).ToArray();
    private readonly Voice[] _voices;
    private readonly float[] _l = new float[Chunk], _r = new float[Chunk], _rev = new float[Chunk], _cho = new float[Chunk];
    private readonly Reverb? _reverb;
    private readonly Chorus? _chorus;
    private long _count;

    public int SampleRate { get; }
    public int MaximumPolyphony => _voices.Length;
    public float MasterVolume { get; set; } = 0.5f;
    /// <summary>Plays the channels it owns instead of the SoundFont; null = SoundFont only.</summary>
    public ILayer? Layer { get; set; }
    public int ActiveVoices => _voices.Count(v => v.Active);

    public Synthesizer(SoundFont font, int sampleRate, int maxVoices = 64, bool effects = true)
    {
        (_font, SampleRate) = (font, sampleRate);
        _voices = Enumerable.Range(0, maxVoices).Select(_ => new Voice(sampleRate)).ToArray();
        if (effects) (_reverb, _chorus) = (new Reverb(sampleRate), new Chorus(sampleRate));
        Reset();
    }

    public void Reset()
    {
        foreach (var v in _voices) v.Stop();
        foreach (var c in _ch) c.Reset();
        _reverb?.Reset();
        _chorus?.Reset();
        Layer?.Reset();
    }

    public void NoteOffAll(bool immediate)
    {
        foreach (var v in _voices) if (v.Active) { if (immediate) v.Stop(); else v.Release(); }
    }

    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        channel &= 15;
        var c = _ch[channel];
        if (command == 0x90 && data2 > 0 && Layer?.Owns(channel) == true) { Layer.ProcessMidi(channel, command, data1, data2); return; }
        Layer?.ProcessMidi(channel, command, data1, data2);
        switch (command)
        {
            case 0x90 when data2 > 0: NoteOn(c, channel, data1, data2); break;
            case 0x90 or 0x80: NoteOff(c, channel, data1); break;
            case 0xC0: c.Program = data1; break;
            case 0xD0: c.Pressure = data1; c.Version++; break;
            case 0xE0: c.Bend = (data1 & 0x7F) | ((data2 & 0x7F) << 7); c.Version++; break;
            case 0xB0: Controller(c, channel, data1, data2); break;
        }
    }

    private void Controller(Channel c, int channel, int cc, int v)
    {
        c.Controller(cc, v);
        if (cc == 64 && v < 64) foreach (var x in _voices) if (x.Active && x.Channel == channel && x.Held) x.Release();
        if (cc == 120) foreach (var x in _voices) if (x.Active && x.Channel == channel) x.Stop();
        if (cc == 123) foreach (var x in _voices) if (x.Active && x.Channel == channel && !x.Released) x.Release();
    }

    private void NoteOff(Channel c, int channel, int key)
    {
        foreach (var v in _voices)
        {
            if (!v.Active || v.Released || v.Channel != channel || v.Key != key) continue;
            if (c.Sustain) v.Held = true;
            else v.Release();
        }
    }

    private void NoteOn(Channel c, int channel, int key, int vel)
    {
        var preset = _font.Find(c.Bank, c.Program, c.Drum);
        foreach (var pz in preset.Zones)
        {
            if (!pz.Covers(key, vel) || preset.Global is { } pg && !pg.Covers(key, vel) || pz.Link >= _font.Instruments.Length) continue;
            var inst = _font.Instruments[pz.Link];
            foreach (var iz in inst.Zones)
            {
                if (!iz.Covers(key, vel) || inst.Global is { } ig && !ig.Covers(key, vel) || iz.Link >= _font.Samples.Length) continue;
                var exclusive = iz.Has(57) ? iz.Gen[57] : inst.Global?.Gen[57] ?? 0;
                if (exclusive != 0) foreach (var x in _voices) if (x.Active && x.Channel == channel && x.ExclusiveClass == exclusive) x.Kill();
                Free().Start(_font, c, channel, key, vel, preset.Global, pz, inst.Global, iz, ++_count);
            }
        }
    }

    private Voice Free()
    {
        Voice? quiet = null, old = null;
        foreach (var v in _voices)
        {
            if (!v.Active) return v;
            if (v.Released && (quiet is null || v.Level < quiet.Level)) quiet = v;
            if (old is null || v.Started < old.Started) old = v;
        }
        return quiet ?? old!;
    }

    /// <summary>Overwrites both spans with the next frames (any length). No allocation.</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        for (var at = 0; at < left.Length; at += Chunk)
        {
            var n = Math.Min(Chunk, left.Length - at);
            RenderChunk(left.Slice(at, n), right.Slice(at, n));
        }
    }

    private void RenderChunk(Span<float> left, Span<float> right)
    {
        var n = left.Length;
        Span<float> l = _l.AsSpan(0, n), r = _r.AsSpan(0, n), rev = _rev.AsSpan(0, n), cho = _cho.AsSpan(0, n);
        l.Clear(); r.Clear(); rev.Clear(); cho.Clear();
        foreach (var v in _voices) if (v.Active) v.Render(l, r, rev, cho);
        _reverb?.Process(rev, l, r);
        _chorus?.Process(cho, l, r);
        for (var i = 0; i < n; i++) { left[i] = l[i] * MasterVolume; right[i] = r[i] * MasterVolume; }
        Layer?.Render(left, right);
    }
}
