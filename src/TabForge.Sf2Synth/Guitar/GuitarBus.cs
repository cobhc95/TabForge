using TabForge.Sf2Synth.Dsp;
using TabForge.Sf2Synth.Nam;

namespace TabForge.Sf2Synth.Guitar;

// Owns: one channel's rig behind the strings: pickup resonance (the DI), the acoustic body (BodyModes), then the user's NAM amp,
//   or on the distorted programs the captured amp (trimmed DI, DC blocker, cabinet optional) or the clean DI alone, else the built-in compressor,
//   pre-EQ, 2x-oversampled StageAmp and tone stack; then the cabinet (user IR or synthetic, FFT convolution), level. Electric
//   programs take the user's files; a new Setup fades out one block and in the next. Mono, in place, no allocation.
// Does not own: the strings (PluckedString), values (Voicings), loading files (GuitarRig), volume/pan and options (GuitarLayer).
// Tests: GuitarLayerTests.
internal sealed class GuitarBus(double rate, int maxBlock, float[] guitarCab, float[] bassCab, float[] roomCab, NamAmp? captured)
{
    public const float NamLevel = 0.35f;

    // What plays: the user's files, the distorted programs' amp and whether the captured amp gets the built-in cabinet.
    internal readonly record struct Setup(GuitarRig.Slot? User, DistAmp Amp, bool CapturedCab);

    private readonly Biquad[] _di = new Biquad[Voicings.MaxBands], _pre = new Biquad[Voicings.MaxBands], _tone = new Biquad[Voicings.MaxBands];
    private int _diN, _preN, _toneN;
    private readonly IAmpModel _amp = new StageAmp();
    private readonly BodyModes _body = new();
    private readonly Compressor _comp = new();
    private bool _hasAmp, _electric, _swap, _fadeIn, _capturedPlayed;
    private readonly Oversampler2x _os = new();
    private readonly float[] _doubled = new float[2 * maxBlock];
    private readonly FftConvolver _gCab = new(guitarCab), _bCab = new(bassCab), _rCab = new(roomCab);
    private FftConvolver? _cab;
    private Setup _now, _next;
    private CaptureSpec? _capture;
    private Biquad _dc = new() { C = BiquadCoefs.Of(new EqSpec(EqKind.HighPass, 15, 0.7), rate) };   // the model's DC while playing
    private float _level, _drive;

    public bool DiOnly { get; set; }

    public void Configure(RigSpec rig)
    {
        static int Set(Biquad[] f, EqSpec[] s, double rate) { var n = Math.Min(s.Length, f.Length); for (var i = 0; i < n; i++) f[i].C = BiquadCoefs.Of(s[i], rate); return n; }
        (_diN, _preN, _toneN) = (Set(_di, rig.Pickup, rate), Set(_pre, rig.PreEq, rate), Set(_tone, rig.Tone, rate));
        _hasAmp = rig.Stages.Length > 0;
        if (_hasAmp) _amp.Configure(rig, 2 * rate);
        _body.Configure(rig.Body, rate);
        _comp.Configure(rig.Comp, rate);
        _cab = rig.Cab switch { CabKind.Guitar => _gCab, CabKind.Bass => _bCab, CabKind.Room => _rCab, _ => null };
        _electric = rig.Cab is CabKind.Guitar or CabKind.Room;
        (_level, _drive, _capture) = (rig.Level, rig.Drive, rig.Captured);
        Reset();
    }

    // Audio thread. now: the bus is idle, install at once; else fade over the next two blocks.
    public void Use(Setup setup, bool now)
    {
        if (now) { Install(setup); _swap = false; return; }
        (_next, _swap) = (setup, true);
    }

    private void Install(Setup setup)
    {
        static bool Captures(Setup s) => s.Amp == DistAmp.Captured && s.User?.Amp is null;
        if (Captures(setup) && !Captures(_now)) Settle();   // its history is from an earlier passage
        _now = setup;
        _now.User?.Cab?.Reset();
        _cab?.Reset();
    }

    // The captured amp back to its prewarmed state, only if it played (a few ms of model time).
    private void Settle()
    {
        if (!_capturedPlayed) return;
        captured?.Reset();
        _capturedPlayed = false;
    }

    public void Reset()
    {
        for (var i = 0; i < _pre.Length; i++) { _di[i].Reset(); _pre[i].Reset(); _tone[i].Reset(); }
        _amp.Reset();
        _dc.Reset();
        _body.Reset();
        _comp.Reset();
        _os.Reset();
        _cab?.Reset();
        _now.User?.Cab?.Reset();
    }

    // Reset plus the captured amp's state: renders after it repeat bit for bit.
    public void ResetAll()
    {
        Reset();
        Settle();
    }

    public void Process(Span<float> mono)
    {
        for (var n = 0; n < mono.Length; n++)
            for (var i = 0; i < _diN; i++) mono[n] = _di[i].Process(mono[n]);
        _body.Process(mono);
        if (DiOnly) return;
        var level = Chain(mono);
        if (_swap)
        {
            for (var n = 0; n < mono.Length; n++) mono[n] *= 1 - (n + 1f) / mono.Length;
            Install(_next);
            (_swap, _fadeIn) = (false, true);
        }
        else if (_fadeIn)
        {
            for (var n = 0; n < mono.Length; n++) mono[n] *= (n + 1f) / mono.Length;
            _fadeIn = false;
        }
        for (var n = 0; n < mono.Length; n++) mono[n] *= level;
    }

    // Amp and cabinet as the setup says; returns the output level.
    private float Chain(Span<float> mono)
    {
        var user = _electric ? _now.User : null;
        if (user?.Amp is { } nam)
        {
            nam.Process(mono);
            (user.Cab ?? _cab)?.Process(mono);
            return NamLevel;
        }
        if (_capture is { } cap && _now.Amp != DistAmp.Stage && (captured is not null || _now.Amp == DistAmp.CleanOnly))
        {
            var trim = cap.Trim * _drive;
            for (var n = 0; n < mono.Length; n++) mono[n] *= trim;
            if (_now.Amp == DistAmp.CleanOnly) return 1;
            captured!.Process(mono);
            for (var n = 0; n < mono.Length; n++) mono[n] = _dc.Process(mono[n]);
            _capturedPlayed = true;
            (user?.Cab ?? (_now.CapturedCab ? _cab : null))?.Process(mono);
            return cap.Level;
        }
        BuiltIn(mono);
        (user?.Cab ?? _cab)?.Process(mono);
        return _level;
    }

    private void BuiltIn(Span<float> mono)
    {
        _comp.Process(mono);
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
