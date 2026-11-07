using TabForge.Sf2Synth.Dsp;

namespace TabForge.Sf2Synth.Guitar;

// Owns: one channel's rig behind the strings: pre-EQ, 2x-oversampled amp, tone stack, cabinet FIR, level. Mono, in place, no allocation.
// Does not own: the strings (PluckedString), values (Voicings), cabinet design (CabinetImpulse), volume/pan (GuitarLayer).
// Tests: GuitarLayerTests.
internal sealed class GuitarBus(double rate, int maxBlock, float[] guitarCab, float[] bassCab)
{
    private readonly Biquad[] _pre = new Biquad[Voicings.MaxBands], _tone = new Biquad[Voicings.MaxBands];
    private int _preN, _toneN;
    private readonly IAmpModel _amp = new StageAmp();
    private bool _hasAmp;
    private readonly Oversampler2x _os = new();
    private readonly float[] _doubled = new float[2 * maxBlock];
    private readonly FirHistory _cab = new(CabinetImpulse.MaxTaps);
    private float[]? _kernel;
    private float _level;

    public void Configure(RigSpec rig)
    {
        _preN = Math.Min(rig.PreEq.Length, _pre.Length);
        for (var i = 0; i < _preN; i++) _pre[i].C = BiquadCoefs.Of(rig.PreEq[i], rate);
        _toneN = Math.Min(rig.Tone.Length, _tone.Length);
        for (var i = 0; i < _toneN; i++) _tone[i].C = BiquadCoefs.Of(rig.Tone[i], rate);
        _hasAmp = rig.Stages.Length > 0;
        if (_hasAmp) _amp.Configure(rig, 2 * rate);
        _kernel = rig.Cab switch { CabKind.Guitar => guitarCab, CabKind.Bass => bassCab, _ => null };
        _level = rig.Level;
        Reset();
    }

    public void Reset()
    {
        for (var i = 0; i < _pre.Length; i++) { _pre[i].Reset(); _tone[i].Reset(); }
        _amp.Reset();
        _os.Reset();
        _cab.Clear();
    }

    public void Process(Span<float> mono)
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
        var kernel = _kernel;
        for (var n = 0; n < mono.Length; n++)
        {
            var x = mono[n];
            for (var i = 0; i < _toneN; i++) x = _tone[i].Process(x);
            if (kernel is not null) { _cab.Push(x); x = _cab.Dot(kernel); }
            mono[n] = x * _level;
        }
    }
}
