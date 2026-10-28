using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Compressor;

// Owns: the compressor audio path: stereo-linked peak detection in dB, the static curve, attack and release of the gain reduction (one pole each, in dB),
//   makeup (manual or automatic, glided), a fading bypass, and the two meters (gain reduction and input level, written once per block, falling slowly).
// Does not own: the parameter list (CompressorEffect.cs), the curve (CompressorCurve).
// Tests: BuiltinDynamicsTests.
public sealed partial class CompressorEffect
{
    private const double MakeupGlideMs = 30, BypassFadeMs = 10, MeterFallMs = 300;
    private Smoothed _makeup, _wet;
    private double _reduction, _gainReductionMeter, _levelMeter;

    private void InitialiseDsp()
    {
        _makeup = new Smoothed(0, MakeupGlideMs, SampleRate);
        _wet = new Smoothed(1, BypassFadeMs, SampleRate);
        _reduction = _gainReductionMeter = 0;
        _levelMeter = MeterFloorDb;
    }

    protected override void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames)
    {
        double threshold = Value(ThresholdIndex), ratio = Value(RatioIndex), knee = Value(KneeIndex);
        double attack = DspMath.Keep(Value(AttackIndex), SampleRate), release = DspMath.Keep(Value(ReleaseIndex), SampleRate);
        _makeup.Target = On(AutoMakeupIndex) ? Math.Min(MaxMakeup, CompressorCurve.ReductionDb(0, threshold, ratio, knee)) : Value(MakeupIndex);
        _wet.Target = On(BypassIndex) ? 0 : 1;
        double blockPeak = 0, blockReduction = 0;
        for (var i = 0; i < frames; i++)
        {
            double l = inLeft[i], r = inRight[i];
            var peak = Math.Max(Math.Abs(l), Math.Abs(r));
            if (peak > blockPeak) blockPeak = peak;
            var wanted = CompressorCurve.ReductionDb(DspMath.LinToDb(peak), threshold, ratio, knee);
            var keep = wanted > _reduction ? attack : release;
            _reduction = DspMath.Flush(wanted + keep * (_reduction - wanted));
            if (_reduction > blockReduction) blockReduction = _reduction;
            var gain = DspMath.DbToLin(_makeup.Next() - _reduction);
            var wet = _wet.Next();
            outLeft[i] = (float)(l + (l * gain - l) * wet);
            outRight[i] = (float)(r + (r * gain - r) * wet);
        }
        var fall = Math.Exp(-1000.0 * frames / (MeterFallMs * SampleRate));
        _gainReductionMeter = Math.Max(blockReduction, _gainReductionMeter * fall);
        _levelMeter = Math.Max(DspMath.LinToDb(blockPeak), MeterFloorDb + (_levelMeter - MeterFloorDb) * fall);
        SetReadout(GainReductionIndex, Math.Min(MaxReduction, _gainReductionMeter));
        SetReadout(InputLevelIndex, Math.Clamp(_levelMeter, MeterFloorDb, MeterCeilingDb));
    }
}
