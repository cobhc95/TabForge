using System.Collections.Frozen;
using System.Collections.Immutable;
using TabForge.Audio.Contracts;
using MeltySynth;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Synth;

// Owns: the per-track General MIDI sound rendered in the engine (MeltySynth over gm.dls, sample-accurate, effects-ready).

/// <summary>
/// The General MIDI sound of a track, rendered in the engine (MeltySynth playing Windows' own gm.dls, the bank
/// behind the Microsoft GS Wavetable Synth) so effects can process it. Created ONLY for a track whose chain has
/// effects and no VST instrument, with MIDI sound on; every other track stays on Windows MIDI.
/// Renders sample-accurately between MIDI messages; no allocation per block.
/// </summary>
public sealed class GmSynth : IPluginInstance
{
    private Synthesizer? _synth;
    private int _bankVersion;
    private readonly GmBank _bank;

    /// <param name="bank">The sound bank choice of the engine session; null = a bank of its own (Windows GM).</param>
    public GmSynth(int sampleRate, int maxBlock, GmBank? bank = null)
    {
        _bank = bank ?? new GmBank();
        _synth = _bank.Current.Silent ? null : NewSynth(sampleRate, _bank.Current, out _bankVersion);
        SampleRate = sampleRate;
        _ = maxBlock;
    }

    /// <summary>
    /// Voices per track synth. One GM synth serves one track (a guitar's chords with let-ring, or a kit's hits and cymbal tails), so
    /// 32 is ample; MeltySynth steals the oldest voice beyond it, which bounds the worst-case CPU per block (was 64).
    /// </summary>
    public const int MaxVoices = 32;

    /// <summary>The live synthesizer's voice limit (self-test); 0 while the bank is silence.</summary>
    public int Polyphony => _synth?.MaximumPolyphony ?? 0;

    private static Synthesizer NewSynth(int sampleRate, GmBank.Choice bank, out int version)
    {
        version = bank.Version;
        var settings = new SynthesizerSettings(sampleRate)
        {
            BlockSize = 64,
            MaximumPolyphony = MaxVoices,
            EnableReverbAndChorus = GmSynthTuning.ReverbAndChorus,
        };
        return new Synthesizer(bank.Bank, settings) { MasterVolume = bank.MasterVolume };
    }

    /// <summary>The rate the synthesizer renders at.</summary>
    public int SampleRate { get; private set; }

    /// <summary>
    /// Device change (main thread, audio stopped): a new synthesizer at the new rate (MeltySynth fixes it at creation; the block
    /// size does not matter, it renders any length). The channel state the song already sent (programs, controllers, bend) is
    /// replayed, as after an offline render, so the tracks keep their instruments.
    /// </summary>
    public void Reconfigure(double sampleRate, int maxBlock)
    {
        var rate = (int)Math.Round(sampleRate);
        if (_bank.Current.Silent)
        {
            // Sound engine None: no synthesizer at all (the next non-silent bank rebuilds it below).
            var dropped = _synth;
            _synth = null;
            SampleRate = rate;
            try { dropped?.NoteOffAll(true); } catch (Exception) { } // swallow: best-effort note-off on a replaced synth
            return;
        }
        if (rate == SampleRate && _synth is not null) return;
        // MeltySynth's Synthesizer holds no unmanaged resources of its own (the SoundFont bank is shared via
        // DlsToSoundFont's cache); dropping the reference is the dispose. Silence the old instance first so no
        // voice survives the swap (Reconfigure runs with audio stopped, but a render may have left voices ringing).
        var old = _synth;
        try { old?.NoteOffAll(true); } catch (Exception) { } // swallow: best-effort note-off on a replaced synth
        _synth = NewSynth(rate, _bank.Current, out _bankVersion);
        SampleRate = rate;
        ReplayChannelState();
    }

    public string Path => "General MIDI synth";
    public bool IsInstrument => true;
    public bool HasEditor => false;
    public int LatencySamples => 0;

    /// <summary>Offline render start: a clean synth (no voices, default channels, block phase 0), so a render never depends on what played before and repeats bit for bit. The song's own events set programs and controllers again.</summary>
    public void SetOfflineMode(bool offline)
    {
        if (offline) { _sProgram = (int[])_program.Clone(); _sCc = (int[,])_cc.Clone(); _sBend = (int[])_bend.Clone(); _synth?.Reset(); return; }
        if (_sProgram is not null) { Array.Copy(_sProgram, _program, 16); Array.Copy(_sCc!, _cc, _cc.Length); Array.Copy(_sBend!, _bend, 16); _sProgram = null; }
        // Back to realtime: the reset wiped the programs / controllers the live song had already sent (they are not
        // resent mid-song), so replay the remembered channel state or every track falls back to piano.
        ReplayChannelState();
    }

    private void ReplayChannelState()
    {
        if (_synth is null) return;
        for (var ch = 0; ch < 16; ch++)
        {
            // Bank select (CC0, CC32) first: a program change picks its sound from the bank that is selected when it arrives.
            foreach (var c in new[] { 0, 32 }) if (_cc[ch, c] >= 0) _synth.ProcessMidiMessage(ch, 0xB0, c, _cc[ch, c]);
            if (_program[ch] >= 0) _synth.ProcessMidiMessage(ch, 0xC0, _program[ch], 0);   // never set: the synth's own default (the drum kit on channel 10) stays
            for (var c = 1; c < 128; c++) if (c != 32 && _cc[ch, c] >= 0) _synth.ProcessMidiMessage(ch, 0xB0, c, _cc[ch, c]);
            if (_bend[ch] >= 0) _synth.ProcessMidiMessage(ch, 0xE0, _bend[ch] & 0x7F, _bend[ch] >> 7);
        }
    }

    private int[]? _sProgram, _sBend; private int[,]? _sCc;
    private readonly int[] _program = Enumerable.Repeat(-1, 16).ToArray();   // -1 = no program change heard
    private readonly int[,] _cc = NewCc();
    private readonly int[] _bend = Enumerable.Repeat(-1, 16).ToArray();
    private static int[,] NewCc() { var a = new int[16, 128]; for (var i = 0; i < 16; i++) for (var j = 0; j < 128; j++) a[i, j] = -1; return a; }

    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
    {
        var bank = _bank.Current;
        // Sound engine None: silence. The caller cleared the buffers (TrackChain clears its mix buffer before
        // the synth runs), so there is nothing to do: no voices, no render, one volatile read per block.
        if (bank.Silent) return;
        // The sound bank was switched (Settings > Sound engine): a new synthesizer on the new bank, with the channel state the song already sent replayed.
        if (_synth is null || bank.Version != _bankVersion)
        {
            try { _synth?.NoteOffAll(true); } catch (Exception) { }   // swallow: best-effort note-off on the replaced synth
            _synth = NewSynth(SampleRate, bank, out _bankVersion);
            ReplayChannelState();
        }
        var synth = _synth;
        var left = output[0].AsSpan(0, frames);
        var right = output[1].AsSpan(0, frames);
        var at = 0;
        foreach (var e in midi)
        {
            var frame = Math.Clamp(e.Frame, at, frames);
            if (frame > at) { synth.Render(left[at..frame], right[at..frame]); at = frame; }
            var command = e.Status & 0xF0;
            var chn = e.Status & 0x0F;
            if (command == 0xC0) _program[chn] = e.Data1;
            else if (command == 0xB0 && e.Data1 < 120) _cc[chn, e.Data1 & 0x7F] = e.Data2;
            else if (command == 0xE0) _bend[chn] = (e.Data1 & 0x7F) | ((e.Data2 & 0x7F) << 7);
            if (command is >= 0x80 and <= 0xE0) synth.ProcessMidiMessage(e.Status & 0x0F, command, e.Data1, e.Data2);
        }
        if (at < frames) synth.Render(left[at..], right[at..]);
    }

    public byte[]? GetState() => null;
    public void SetState(byte[] state) { }
    public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
    public void CloseEditor() { }
    public void EditorIdle() { }
    public void Dispose() => _synth?.NoteOffAll(true);
}

/// <summary>Level and effect settings that make the rendered sound match the Microsoft GS Wavetable Synth.</summary>
public static class GmSynthTuning
{
    // Measured against the Microsoft GS Wavetable Synth through the same output path: MeltySynth with the
    // converted gm.dls was 6.3 dB quieter at 0.5 on every instrument tested (piano, guitars, bass, strings, drums).
    public static readonly float MasterVolume = 1.03f;
    // The Windows GS synth plays no reverb / chorus tail even when a track sends CC91 / CC93 (measured: drums are
    // silent 0.5 s after the hit), so the engine's copy runs dry too: reverb and chorus are fixed off (which also saves their CPU per track).
    public const bool ReverbAndChorus = false;
    /// <summary>Applies the measured per-program / per-drum-note level calibration.</summary>
    public static readonly bool Calibrate = true;

    /// <summary>Measured correction per GM program, dB (positive = make the engine louder); NaN = not measured (family average).</summary>
    // Measurement method: engine minus GS negated, mean of velocities 40/64/90/110/127
    // (the velocity curves are identical: spread <= 0.2 dB per program), path offset from a steady tone (-32.21 dB).
    // The engine plays the sound bank as it is, except for the two things heard as different from the Windows synth:
    // snares quieter (+2.5 dB) and crashes louder (-4 dB), set by ear. The program table is empty: a full measured
    // per-program / per-drum table (kick +14.6 dB etc.) made the engine sound wrong.
    public static readonly IReadOnlyList<double> ProgramGainDb = BuildProgramTable(new Dictionary<int, double>()).ToImmutableArray();
    /// <summary>Correction per drum note, dB (positive = louder in the engine); notes not listed get 0.</summary>
    public static readonly IReadOnlyDictionary<int, double> DrumGainDb = new Dictionary<int, double>
    {
        [38] = 2.5, [40] = 2.5,                              // snares (user, by ear)
        [49] = -4.0, [52] = -4.0, [55] = -4.0, [57] = -4.0,  // crashes, China 52, splash 55 (user, by ear)
    }.ToFrozenDictionary();
    /// <summary>Overall trim of the engine synth after calibration, dB (0: engine on and off must sound the same).</summary>
    public static readonly double OverallTrimDb = 0;
    // A large correction (kick 35 +14.6 dB) is not clipping: the GS synth's own kick peaks about +8 dBFS on
    // the same scale, and Windows applies its volume (and a further ~-18 dB, measured) in float before the final clip. The engine
    // matches that: WASAPI shared passes floats above 1 to the Windows mixer (MixEngine.Ceiling), ASIO applies the same Windows
    // attenuation first (EngineHost.FollowWindowsVolume).
    // Corrections are capped at ±6 dB: the measured kick boosts (+14.6 / +11.7 dB) sounded far too loud through the
    // engine, and ±6 dB is the level confirmed to sound right; larger measured values are clamped.
    public const double MaxCorrectionDb = 6;

    /// <summary>
    /// Stereo width of region pans relative to MeltySynth's: GS pans the kit (hats, toms, cymbals) narrower. Measured right-minus-left
    /// dB, GS / engine: hats +5.3 / +8.3, toms -7.8 / -13.2, crash 57 -5.1 / -8.3, 58 -9.5 / -16.1: a steady 0.61 in dB.
    /// </summary>
    public const double PanWidth = 1.0;   // the sound bank's own stereo placement (a narrower 0.61 was tried and removed)

    /// <summary>Region pan (SF2 0.1 % units, -500..500) mapped so MeltySynth's constant-power law gives the GS left/right balance.</summary>
    public static double MapPan(double pan)
    {
        if (!Calibrate || pan == 0) return pan;
        var theta = Math.PI / 2 * (Math.Clamp(pan, -499, 499) / 1000 + 0.5);
        var db = Gain.ToDb(Math.Tan(theta)) * PanWidth;
        return (Math.Atan(Gain.FromDb(db)) / (Math.PI / 2) - 0.5) * 1000;
    }

    private static double[] BuildProgramTable(Dictionary<int, double> measured)
    {
        var table = new double[128];
        for (var p = 0; p < 128; p++)
        {
            if (measured.TryGetValue(p, out var v)) { table[p] = v; continue; }
            var family = measured.Where(kv => kv.Key >> 3 == p >> 3).Select(kv => kv.Value).ToList();
            table[p] = family.Count > 0 ? Math.Round(family.Average(), 1) : 0;
        }
        return table;
    }

    /// <summary>The calibrated gain for a converter region, clamped to the maximum correction.</summary>
    public static double CalibrationGainDb(bool isDrum, int program, int key) =>
        Math.Clamp(isDrum ? DrumGainDb.GetValueOrDefault(key) : ProgramGainDb[program & 0x7F], -MaxCorrectionDb, MaxCorrectionDb);

    private static double MaxBoostDb => Math.Max(0, Math.Max(ProgramGainDb.Max(), DrumGainDb.Values.DefaultIfEmpty(0).Max()));

    /// <summary>For the converter: extra attenuation (>= 0) = headroom minus the region's gain; the headroom goes back in the master.</summary>
    public static double CalibrationAttenuationDb(bool isDrum, int program, int key) => Math.Min(MaxCorrectionDb, MaxBoostDb) - CalibrationGainDb(isDrum, program, key);

    /// <summary>Master volume the synth actually uses: the base level, plus the calibration headroom and trim when calibrated.</summary>
    public static float EffectiveMasterVolume => (float)(MasterVolume * Gain.FromDb(Calibrate ? Math.Min(MaxCorrectionDb, MaxBoostDb) + OverallTrimDb : 0));
}
