using TabForge.Sf2Synth.Dsp;
using TabForge.Sf2Synth.Nam;

namespace TabForge.Sf2Synth.Guitar;

// Owns: one channel's rig behind the strings: pickup resonance (the DI), then either the user's NAM amp or the built-in pre-EQ,
//   2x-oversampled StageAmp and tone stack, then the cabinet (user IR or synthetic, FFT convolution), level. Electric programs
//   take the user's files; swapping them fades out one block and in the next. Mono, in place, no allocation.
// Does not own: the strings (PluckedString), values (Voicings), loading files (GuitarRig), volume/pan (GuitarLayer).
// Tests: GuitarLayerTests.
internal sealed class GuitarBus(double rate, int maxBlock, float[] guitarCab, float[] bassCab)
{
    public const float NamLevel = 0.35f;

    private readonly Biquad[] _di = new Biquad[Voicings.MaxBands], _pre = new Biquad[Voicings.MaxBands], _tone = new Biquad[Voicings.MaxBands];
    private int _diN, _preN, _toneN;
    private readonly IAmpModel _amp = new StageAmp();
    private bool _hasAmp, _electric, _swap, _fadeIn;
    private readonly Oversampler2x _os = new();
    private readonly float[] _doubled = new float[2 * maxBlock];
    private readonly FftConvolver _gCab = new(guitarCab), _bCab = new(bassCab);
    private FftConvolver? _cab;
    private GuitarRig.Slot? _user, _next;
    private float _level;

    public bool DiOnly { get; set; }

    public void Configure(RigSpec rig)
    {
        static int Set(Biquad[] f, EqSpec[] s, double rate) { var n = Math.Min(s.Length, f.Length); for (var i = 0; i < n; i++) f[i].C = BiquadCoefs.Of(s[i], rate); return n; }
        (_diN, _preN, _toneN) = (Set(_di, rig.Pickup, rate), Set(_pre, rig.PreEq, rate), Set(_tone, rig.Tone, rate));
        _hasAmp = rig.Stages.Length > 0;
        if (_hasAmp) _amp.Configure(rig, 2 * rate);
        _cab = rig.Cab switch { CabKind.Guitar => _gCab, CabKind.Bass => _bCab, _ => null };
        _electric = rig.Cab == CabKind.Guitar;
        _level = rig.Level;
        Reset();
    }

    // Audio thread. now: the bus is idle, install at once; else fade over the next two blocks.
    public void Use(GuitarRig.Slot? slot, bool now)
    {
        if (now) { _user = slot; _swap = false; return; }
        (_next, _swap) = (slot, true);
    }

    public void Reset()
    {
        for (var i = 0; i < _pre.Length; i++) { _di[i].Reset(); _pre[i].Reset(); _tone[i].Reset(); }
        _amp.Reset();
        _os.Reset();
        _cab?.Reset();
        _user?.Cab?.Reset();
    }

    public void Process(Span<float> mono)
    {
        for (var n = 0; n < mono.Length; n++)
            for (var i = 0; i < _diN; i++) mono[n] = _di[i].Process(mono[n]);
        if (DiOnly) return;
        var user = _electric ? _user : null;
        if (user?.Amp is { } nam) nam.Process(mono);
        else BuiltIn(mono);
        (user?.Cab ?? _cab)?.Process(mono);
        var level = user?.Amp is null ? _level : NamLevel;
        if (_swap)
        {
            for (var n = 0; n < mono.Length; n++) mono[n] *= 1 - (n + 1f) / mono.Length;
            (_user, _swap, _fadeIn) = (_next, false, true);
            _user?.Cab?.Reset();
        }
        else if (_fadeIn)
        {
            for (var n = 0; n < mono.Length; n++) mono[n] *= (n + 1f) / mono.Length;
            _fadeIn = false;
        }
        for (var n = 0; n < mono.Length; n++) mono[n] *= level;
    }

    private void BuiltIn(Span<float> mono)
    {
        for (var n = 0; n < mono.Length; n++)
            for (var i = 0; i < _preN; i++) mono[n] = _pre[i].Process(mono[n]);
        if (_hasAmp)
        {
            var doubled = _doubled.AsSpan(0, 2 * mono.Length);
            _os.Up(mono, doubled);
            _amp.Process(doubled);
            _os.Down(doubled, mono);
        }
        for (var n = 0; n < mono.Length; n++)
            for (var i = 0; i < _toneN; i++) mono[n] = _tone[i].Process(mono[n]);
    }
}
