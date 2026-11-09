using TabForge.Sf2Synth.Humanize;
using TabForge.Sf2Synth.Sf2;

namespace TabForge.Sf2Synth;

// Owns: starting a voice from a preset zone + instrument zone (§9.4: instrument generators absolute, preset generators added;
//   §9.5: instrument modulators override defaults, preset modulators add), sample addressing and the modulator sums.
// Does not own: rendering (Voice), choosing zones and voices (Synthesizer).
// Tests: Sf2SynthTests.
internal sealed partial class Voice
{
    private const int MaxMods = 64;
    // EMU hardware (and FluidSynth/MeltySynth after it) applies 0.4 × initialAttenuation; real banks are voiced for that.
    private const float EmuAttenuationScale = 0.4f;
    private readonly float[] _gen = new float[Gens.Count];
    private readonly float[] _mod = new float[Gens.Count];
    private readonly Mod[] _mods = new Mod[MaxMods];
    private int _modN, _version;

    public void Start(SoundFont font, Channel ch, int channel, int key, int vel, Zone? pGlobal, Zone pZone, Zone? iGlobal, Zone iZone, long started, in Nuance nu)
    {
        for (var g = 0; g < Gens.Count; g++)
        {
            float v = iZone.Has(g) ? iZone.Gen[g] : iGlobal?.Gen[g] ?? Gens.Defaults[g];
            if (!Gens.PresetIgnores(g)) v += pZone.Has(g) ? pZone.Gen[g] : pGlobal is { } pg && pg.Has(g) ? pg.Gen[g] : 0;
            _gen[g] = v;
        }
        _gen[(int)G.Attenuation] *= EmuAttenuationScale;
        _modN = 0;
        foreach (var m in Mod.Defaults) Put(m, false);
        if (iGlobal is not null) foreach (var m in iGlobal.Mods) Put(m, false);
        foreach (var m in iZone.Mods) Put(m, false);
        if (pGlobal is not null) foreach (var m in pGlobal.Mods) if (!Contains(pZone.Mods, m)) Put(m, true);
        foreach (var m in pZone.Mods) Put(m, true);

        var s = font.Samples[iZone.Link];
        _data = font.Data;
        int I(G g) => (int)_gen[(int)g];
        _start = s.Start + I(G.StartOffset) + 32768 * I(G.StartCoarse);
        _end = Math.Min(_data.Length - 3, s.End + I(G.EndOffset) + 32768 * I(G.EndCoarse));
        _loopStart = s.LoopStart + I(G.LoopStartOffset) + 32768 * I(G.LoopStartCoarse);
        _loopEnd = Math.Min(_end, s.LoopEnd + I(G.LoopEndOffset) + 32768 * I(G.LoopEndCoarse));
        _loopMode = _loopEnd - _loopStart > 1 ? I(G.SampleModes) & 3 : 0;
        _rootKey = I(G.RootKey) >= 0 ? I(G.RootKey) : s.Root;
        _correction = s.Correction;
        _rateRatio = (double)s.Rate / _rate;
        _pos = Math.Max(1, _start);

        (Channel, Key, Velocity, Started, Active, Released, Held) = (channel, key, vel, started, true, false, false);
        _ch = ch;
        _modKey = I(G.Keynum) >= 0 ? I(G.Keynum) : key;
        _modVel = I(G.Velocity) >= 0 ? I(G.Velocity) : vel;
        ExclusiveClass = I(G.ExclusiveClass);
        Evaluate();
        StartEnvelopes();
        _filter = default;
        _z1 = _z2 = _gl = _gr = 0;
        _filterOn = false;
        _first = true;
        (_nu, _age) = (nu, 0);
    }

    private static bool Contains(Mod[] list, in Mod m)
    {
        foreach (var x in list) if (x.SameAs(m)) return true;
        return false;
    }

    private void Put(in Mod m, bool add)
    {
        for (var i = 0; i < _modN; i++)
        {
            if (!_mods[i].SameAs(m)) continue;
            _mods[i] = add ? m with { Amount = (short)Math.Clamp(_mods[i].Amount + m.Amount, short.MinValue, short.MaxValue) } : m;
            return;
        }
        if (_modN < MaxMods) _mods[_modN++] = m;
    }

    // Sums every modulator into _mod (cB, cents, 0.1 % as the destination generator); runs when the channel's inputs changed.
    private void Evaluate()
    {
        _version = _ch.Version;
        Array.Clear(_mod);
        for (var i = 0; i < _modN; i++)
        {
            ref readonly var m = ref _mods[i];
            if ((m.Src & 0xFF) == 0 || m.Dest >= Gens.Count) continue;
            var v = Mod.Map(m.Src, _ch.Source(m.Src & 0x7F, (m.Src & 0x80) != 0, _modKey, _modVel))
                  * Mod.Map(m.AmtSrc, _ch.Source(m.AmtSrc & 0x7F, (m.AmtSrc & 0x80) != 0, _modKey, _modVel)) * m.Amount;
            _mod[m.Dest] += m.Transform == 2 ? MathF.Abs(v) : v;
        }
    }

    private float P(G g) => _gen[(int)g] + _mod[(int)g];

    private static double Seconds(float timecents) => Math.Pow(2, Math.Max(-12000, timecents) / 1200.0);

    private void StartEnvelopes()
    {
        var k = 60 - _modKey;
        _volEnv.Start(true, Seconds(P(G.VolEnvDelay)), Seconds(P(G.VolEnvAttack)), Seconds(P(G.VolEnvHold) + k * P(G.KeyToVolEnvHold)),
            Seconds(P(G.VolEnvDecay) + k * P(G.KeyToVolEnvDecay)), Math.Clamp(P(G.VolEnvSustain), 0, 1440) / 10.0, Seconds(P(G.VolEnvRelease)));
        _modEnv.Start(false, Seconds(P(G.ModEnvDelay)), Seconds(P(G.ModEnvAttack)), Seconds(P(G.ModEnvHold) + k * P(G.KeyToModEnvHold)),
            Seconds(P(G.ModEnvDecay) + k * P(G.KeyToModEnvDecay)), 1 - Math.Clamp(P(G.ModEnvSustain), 0, 1000) / 1000.0, Seconds(P(G.ModEnvRelease)));
        _modLfo.Start(Seconds(P(G.ModLfoDelay)), 8.176 * Math.Pow(2, P(G.ModLfoFreq) / 1200.0));
        _vibLfo.Start(Seconds(P(G.VibLfoDelay)), 8.176 * Math.Pow(2, P(G.VibLfoFreq) / 1200.0));
    }
}
