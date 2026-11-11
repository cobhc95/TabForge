using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Reverb;

// Owns: the reverb audio path: a buffered predelay ring, two partitioned convolvers fed by the synthesized room (built from the Size knob),
//   stereo width and low/high cuts on the tail only, a mix and a dry-untouched bypass.
// Does not own: the parameter list (ReverbEffect.cs), the room (ReverbIr), publishing the room (ReverbEffect.cs).
// Tests: BuiltinReverbTests.
// Threads: Process on the audio thread, no allocation.
public sealed partial class ReverbEffect
{
    private const double SizeGlideMs = 25, GlideMs = 20;
    private const int PredelayCapacity = 65536, MaxPredelaySamples = 65535;

    private PartitionedConvolver? _convL, _convR;
    private readonly float[] _predL = new float[PredelayCapacity], _predR = new float[PredelayCapacity];
    private readonly float[] _bufL = new float[PredelayCapacity], _bufR = new float[PredelayCapacity];
    private int _predWrite;
    private Smoothed _size, _predelay, _mix, _lowHz, _highHz, _width;
    private BiquadCoef _highPass = BiquadCoef.Identity, _lowPass = BiquadCoef.Identity;
    private BiquadState _hpL, _hpR, _lpL, _lpR;
    private bool _primed;

    private void InitialiseReverbDsp()
    {
        Array.Clear(_predL); Array.Clear(_predR); Array.Clear(_bufL); Array.Clear(_bufR);
        _predWrite = 0;
        _size = new Smoothed(40, SizeGlideMs, SampleRate);
        _predelay = new Smoothed(15, GlideMs, SampleRate);
        _mix = new Smoothed(30, GlideMs, SampleRate);
        _lowHz = new Smoothed(MinLowCut, GlideMs, SampleRate);
        _highHz = new Smoothed(8000, GlideMs, SampleRate);
        _width = new Smoothed(50, GlideMs, SampleRate);
        (_hpL, _hpR, _lpL, _lpR) = (default, default, default, default);
        _primed = false;
    }

    public override int TailSamples => Volatile.Read(ref _tailSamples);

    protected override void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames)
    {
        var bypass = On(BypassIndex);
        _size.Target = Value(SizeIndex);
        _predelay.Target = Value(PredelayIndex);
        _mix.Target = bypass ? 0 : Value(MixIndex) / 100;
        _lowHz.Target = Value(LowCutIndex);
        _highHz.Target = Value(HighCutIndex);
        _width.Target = Value(WidthIndex);
        if (!_primed)
        {
            _size.Snap(_size.Target); _predelay.Snap(_predelay.Target); _mix.Snap(_mix.Target);
            _lowHz.Snap(_lowHz.Target); _highHz.Snap(_highHz.Target); _width.Snap(_width.Target);
            _highPass = BiquadCoef.HighPass(SampleRate, _lowHz.Value, BiquadCoef.Butterworth);
            _lowPass = BiquadCoef.LowPass(SampleRate, _highHz.Value, BiquadCoef.Butterworth);
            _primed = true;
        }

        var convL = Volatile.Read(ref _convL);
        var convR = Volatile.Read(ref _convR);
        var delay = (int)Math.Clamp(_predelay.Advance(frames) / 1000 * SampleRate, 0, MaxPredelaySamples);
        var lowHz = _lowHz.Advance(frames);
        var highHz = _highHz.Advance(frames);
        if (!_lowHz.Settled) _highPass = BiquadCoef.HighPass(SampleRate, lowHz, BiquadCoef.Butterworth);
        if (!_highHz.Settled) _lowPass = BiquadCoef.LowPass(SampleRate, highHz, BiquadCoef.Butterworth);
        var lowOn = lowHz > MinLowCut + 0.5;
        var highOn = highHz < MaxHighCut - 0.5;
        var mix = (float)Math.Min(1, _mix.Advance(frames));
        var width = _width.Advance(frames) / 100;
        var wetL = mix * (0.5f + (float)width);
        var wetR = mix * (1.5f - (float)width);

        // Predelay into the ring, then read back one frame at a time for the convolvers.
        for (var i = 0; i < frames; i++)
        {
            var pw = _predWrite % PredelayCapacity;
            _predL[pw] = inLeft[i];
            _predR[pw] = inRight[i];
            _bufL[i] = _predL[(pw - delay + PredelayCapacity * 2) % PredelayCapacity];
            _bufR[i] = _predR[(pw - delay + PredelayCapacity * 2) % PredelayCapacity];
            if (++_predWrite >= PredelayCapacity) _predWrite = 0;
        }
        if (convL is not null && convR is not null)
        {
            var n = Math.Min(frames, _bufL.Length);
            convL.Process(new ReadOnlySpan<float>(_bufL, 0, n), new Span<float>(outLeft, 0, n));
            convR.Process(new ReadOnlySpan<float>(_bufR, 0, n), new Span<float>(outRight, 0, n));
        }
        else
        {
            Array.Clear(outLeft, 0, frames);
            Array.Clear(outRight, 0, frames);
        }
        for (var i = 0; i < frames; i++)
        {
            double wl = outLeft[i], wr = outRight[i];
            if (lowOn) { wl = _hpL.Process(_highPass, wl); wr = _hpR.Process(_highPass, wr); }
            if (highOn) { wl = _lpL.Process(_lowPass, wl); wr = _lpR.Process(_lowPass, wr); }
            outLeft[i] = (float)(inLeft[i] + wl * wetL);
            outRight[i] = (float)(inRight[i] + wr * wetR);
        }
    }
}
