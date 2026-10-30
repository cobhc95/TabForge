using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Delay;

// Owns: the delay audio path: a ring buffer per channel (up to 4 seconds), the delay time from milliseconds or from the song tempo and a note length (a longer
//   one plays at half length until it fits), a time that glides (the repeats bend like a tape delay instead of clicking), 4-point interpolation (exact on whole
//   samples), feedback with optional ping-pong, low cut and high cut on the repeats, and a fading bypass. No allocation, no locks.
// Does not own: the parameter list (DelayEffect.cs), the note lengths (DelayDivision).
// Tests: BuiltinDelayTests.
public sealed partial class DelayEffect
{
    private const double MaxSeconds = 4, TimeGlideMs = 60, MixGlideMs = 20, PingPongGlideMs = 30, FeedbackGlideMs = 20, CutGlideMs = 20, MinDelaySamples = 3, SilentRepeat = 0.001;
    private const int SubBlock = 32, MaxRepeats = 60, GuardSamples = 8;
    private float[] _left = [], _right = [];
    private int _write, _delayWhole;
    private Smoothed _time, _mix, _pingPong, _feedback, _lowHz, _highHz;
    private BiquadCoef _highPass = BiquadCoef.Identity, _lowPass = BiquadCoef.Identity;
    private BiquadState _highPassLeft, _highPassRight, _lowPassLeft, _lowPassRight;
    private bool _primed;

    private void InitialiseDsp()
    {
        var size = (int)(MaxSeconds * SampleRate) + GuardSamples;
        _left = new float[size];
        _right = new float[size];
        _write = 0;
        _time = new Smoothed(0, TimeGlideMs, SampleRate);
        _mix = new Smoothed(0, MixGlideMs, SampleRate);
        _pingPong = new Smoothed(0, PingPongGlideMs, SampleRate);
        _feedback = new Smoothed(0, FeedbackGlideMs, SampleRate);
        _lowHz = new Smoothed(MinLowCut, CutGlideMs, SampleRate);
        _highHz = new Smoothed(MaxHighCut, CutGlideMs, SampleRate);
        (_highPassLeft, _highPassRight, _lowPassLeft, _lowPassRight) = (default, default, default, default);
        _primed = false;
    }

    /// <summary>The delay in samples the knobs ask for now (milliseconds, or the note length at the song tempo).</summary>
    private double WantedSamples()
    {
        var seconds = On(SyncIndex) ? DelayDivision.Seconds((int)Math.Round(Value(DivisionIndex)), Tempo) : Value(TimeIndex) / 1000;
        var samples = seconds * SampleRate;
        var longest = _left.Length - GuardSamples;
        while (samples > longest) samples *= 0.5;
        return Math.Max(MinDelaySamples, samples);
    }

    public override int TailSamples
    {
        get
        {
            var feedback = Value(FeedbackIndex) / 100;
            var repeats = feedback < 0.05 ? 1 : Math.Min(MaxRepeats, 1 + Math.Log(SilentRepeat) / Math.Log(feedback));
            return (int)Math.Min(_left.Length, Volatile.Read(ref _delayWhole) * repeats);
        }
    }

    protected override void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames)
    {
        var wanted = WantedSamples();
        Volatile.Write(ref _delayWhole, (int)wanted);
        var bypass = On(BypassIndex);
        var lowOn = Value(LowCutIndex) > MinLowCut + 0.5;
        var highOn = Value(HighCutIndex) < MaxHighCut - 0.5;
        _time.Target = wanted;
        _mix.Target = bypass ? 0 : Value(MixIndex) / 100;
        _pingPong.Target = On(PingPongIndex) ? 1 : 0;
        _feedback.Target = Value(FeedbackIndex) / 100;
        _lowHz.Target = Value(LowCutIndex);
        _highHz.Target = Value(HighCutIndex);
        if (!_primed)
        {
            _time.Snap(wanted); _mix.Snap(_mix.Target); _pingPong.Snap(_pingPong.Target); _feedback.Snap(_feedback.Target);
            _lowHz.Snap(_lowHz.Target); _highHz.Snap(_highHz.Target);
            _highPass = BiquadCoef.HighPass(SampleRate, _lowHz.Value, BiquadCoef.Butterworth);
            _lowPass = BiquadCoef.LowPass(SampleRate, _highHz.Value, BiquadCoef.Butterworth);
            _primed = true;
        }
        for (var done = 0; done < frames;)
        {
            var n = Math.Min(SubBlock, frames - done);
            if (!_lowHz.Settled) _highPass = BiquadCoef.HighPass(SampleRate, _lowHz.Advance(n), BiquadCoef.Butterworth);
            if (!_highHz.Settled) _lowPass = BiquadCoef.LowPass(SampleRate, _highHz.Advance(n), BiquadCoef.Butterworth);
            for (var i = done; i < done + n; i++) Tick(inLeft, inRight, outLeft, outRight, i, lowOn, highOn);
            done += n;
        }
    }

    private void Tick(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int i, bool lowOn, bool highOn)
    {
        var size = _left.Length;
        var readAt = _write - _time.Next();
        var whole = Math.Floor(readAt);
        var at = (int)whole;
        var frac = readAt - whole;
        if (at < 0) at += size;
        double wetL = Hermite(_left, at, frac), wetR = Hermite(_right, at, frac);
        if (lowOn) { wetL = _highPassLeft.Process(_highPass, wetL); wetR = _highPassRight.Process(_highPass, wetR); }
        if (highOn) { wetL = _lowPassLeft.Process(_lowPass, wetL); wetR = _lowPassRight.Process(_lowPass, wetR); }
        double dryL = inLeft[i], dryR = inRight[i];
        var pingPong = _pingPong.Next();
        var feedback = _feedback.Next();
        var mono = 0.5 * (dryL + dryR);
        var writeL = dryL + (mono - dryL) * pingPong + feedback * (wetL + (wetR - wetL) * pingPong);
        var writeR = dryR * (1 - pingPong) + feedback * (wetR + (wetL - wetR) * pingPong);
        _left[_write] = (float)DspMath.Flush(writeL);
        _right[_write] = (float)DspMath.Flush(writeR);
        if (++_write == size) _write = 0;
        var mix = _mix.Next();
        outLeft[i] = (float)(dryL + wetL * mix);
        outRight[i] = (float)(dryR + wetR * mix);
    }

    /// <summary>Catmull-Rom interpolation between the sample at <paramref name="at"/> and the next one (<paramref name="t"/> from 0 to 1); exactly the sample at 0.</summary>
    private static double Hermite(float[] buffer, int at, double t)
    {
        var size = buffer.Length;
        double before = buffer[at == 0 ? size - 1 : at - 1], y0 = buffer[at];
        var a = at + 1 >= size ? at + 1 - size : at + 1;
        var b = a + 1 >= size ? a + 1 - size : a + 1;
        double y1 = buffer[a], y2 = buffer[b];
        var c1 = 0.5 * (y1 - before);
        var c2 = before - 2.5 * y0 + 2 * y1 - 0.5 * y2;
        var c3 = 0.5 * (y2 - before) + 1.5 * (y0 - y1);
        return ((c3 * t + c2) * t + c1) * t + y0;
    }
}
