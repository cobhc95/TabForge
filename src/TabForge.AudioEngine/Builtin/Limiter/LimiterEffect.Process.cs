using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Limiter;

// Owns: the limiter audio path. The input is delayed by the look-ahead D. Every sample the true peak gives the gain that would bring it to the ceiling; the smallest
//   gain over the last D - Delay + 2 samples is taken, released with a one-pole (fixed or automatic time), and averaged over D - Delay + 1 samples. That makes the gain arrive
//   at its value before the peak leaves the delay, so the output never passes the ceiling. A fading bypass, the meters (gain reduction, output level).
// Does not own: the parameter list (LimiterEffect.cs), the peak detector (TruePeakDetector), the window minimum (SlidingMinimum).
// Tests: BuiltinDynamicsTests.
public sealed partial class LimiterEffect
{
    private const double GainGlideMs = 5, BypassFadeMs = 5, MeterFallMs = 500, SustainMs = 600, AutoFastMs = 20, AutoSlowMs = 400, SustainedBelow = 0.98, MarginDb = 0.1;
    private const int MinLookAhead = 16;
    private float[] _delayLeft = [], _delayRight = [];
    private double[] _average = [];
    private TruePeakDetector _peakLeft = new(), _peakRight = new();
    private SlidingMinimum _minimum = new(1);
    private Smoothed _inputGain, _active;
    private double _sum, _release = 1, _sustain, _reductionMeter, _levelMeter;
    private int _delayAt, _averageAt;

    private void InitialiseDsp()
    {
        _lookAhead = Math.Max(MinLookAhead, (int)Math.Round(LookAheadMs * SampleRate / 1000));
        _delayLeft = new float[_lookAhead];
        _delayRight = new float[_lookAhead];
        _average = new double[_lookAhead - TruePeakDetector.Delay + 1];
        Array.Fill(_average, 1.0);
        _sum = _average.Length;
        _minimum = new SlidingMinimum(_lookAhead - TruePeakDetector.Delay + 2);
        _peakLeft = new TruePeakDetector();
        _peakRight = new TruePeakDetector();
        _inputGain = new Smoothed(1, GainGlideMs, SampleRate);
        _active = new Smoothed(1, BypassFadeMs, SampleRate);
        (_delayAt, _averageAt, _release, _sustain, _reductionMeter, _levelMeter) = (0, 0, 1, 0, 0, MeterFloorDb);
        SetReadout(LatencyIndex, _lookAhead);
    }

    protected override void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames)
    {
        var bypass = On(BypassIndex);
        var ceiling = DspMath.DbToLin(Value(CeilingIndex) - MarginDb);   // aims a little under the ceiling: the 4x detector can miss a fraction of a dB between its points
        _inputGain.Target = bypass ? 1 : DspMath.DbToLin(Value(InputGainIndex));
        _active.Target = bypass ? 0 : 1;
        var auto = On(AutoReleaseIndex);
        var fixedRelease = 1 - DspMath.Keep(Value(ReleaseIndex), SampleRate);
        var sustainStep = 1 - DspMath.Keep(SustainMs, SampleRate);
        var average = _average.Length;
        double lowestGain = 1, blockPeak = 0;
        for (var i = 0; i < frames; i++)
        {
            var gainIn = (float)_inputGain.Next();
            var left = inLeft[i] * gainIn;
            var right = inRight[i] * gainIn;
            var peak = Math.Max(_peakLeft.Push(left), _peakRight.Push(right));
            var wanted = _minimum.Push(peak > ceiling ? ceiling / peak : 1.0);
            var releaseStep = fixedRelease;
            if (auto)
            {
                _sustain += ((_release < SustainedBelow ? 1 : 0) - _sustain) * sustainStep;
                releaseStep = 1 - DspMath.Keep(AutoFastMs + (AutoSlowMs - AutoFastMs) * _sustain, SampleRate);
            }
            _release = wanted < _release ? wanted : _release + (wanted - _release) * releaseStep;
            _sum += _release - _average[_averageAt];
            _average[_averageAt] = _release;
            if (++_averageAt == average) _averageAt = 0;
            var gain = _sum / average;
            if (gain < lowestGain) lowestGain = gain;
            var active = _active.Next();
            var outputGain = 1 + (gain - 1) * active;
            double o1 = _delayLeft[_delayAt] * outputGain, o2 = _delayRight[_delayAt] * outputGain;
            _delayLeft[_delayAt] = left;
            _delayRight[_delayAt] = right;
            if (++_delayAt == _lookAhead) _delayAt = 0;
            outLeft[i] = (float)o1;
            outRight[i] = (float)o2;
            blockPeak = Math.Max(blockPeak, Math.Max(Math.Abs(o1), Math.Abs(o2)));
        }
        var fall = Math.Exp(-1000.0 * frames / (MeterFallMs * SampleRate));
        _reductionMeter = Math.Max(-DspMath.LinToDb(lowestGain), _reductionMeter * fall);
        _levelMeter = Math.Max(DspMath.LinToDb(blockPeak), MeterFloorDb + (_levelMeter - MeterFloorDb) * fall);
        SetReadout(GainReductionIndex, Math.Min(MaxReduction, _reductionMeter));
        SetReadout(OutputLevelIndex, Math.Clamp(_levelMeter, MeterFloorDb, MeterCeilingDb));
    }
}
