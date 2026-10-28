using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Gate;

// Owns: the noise gate audio path: a stereo-linked peak follower, an open threshold with 3 dB of hysteresis for closing, the hold time, the gain gliding to
//   unity (attack) or to the range (release) in a one-pole, a fading bypass, and the readouts (open, input level) once per block.
// Does not own: the parameter list (GateEffect.cs).
// Tests: BuiltinDynamicsTests.
public sealed partial class GateEffect
{
    private const double HysteresisDb = 3, FollowerFallMs = 5, BypassFadeMs = 10, MeterFallMs = 300;
    private Smoothed _wet;
    private double _follower, _gain = 1, _levelMeter;
    private bool _open;
    private int _holdLeft;

    private void InitialiseDsp()
    {
        _wet = new Smoothed(1, BypassFadeMs, SampleRate);
        (_follower, _gain, _levelMeter, _open, _holdLeft) = (0, 1, MeterFloorDb, false, 0);
    }

    protected override void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames)
    {
        var openAt = DspMath.DbToLin(Value(ThresholdIndex));
        var closeAt = DspMath.DbToLin(Value(ThresholdIndex) - HysteresisDb);
        var floor = DspMath.DbToLin(Value(RangeIndex));
        var attack = 1 - DspMath.Keep(Value(AttackIndex), SampleRate);
        var release = 1 - DspMath.Keep(Value(ReleaseIndex), SampleRate);
        var holdSamples = (int)(Value(HoldIndex) * SampleRate / 1000);
        var fall = DspMath.Keep(FollowerFallMs, SampleRate);
        _wet.Target = On(BypassIndex) ? 0 : 1;
        double blockPeak = 0;
        var anyOpen = _open;
        for (var i = 0; i < frames; i++)
        {
            double l = inLeft[i], r = inRight[i];
            var peak = Math.Max(Math.Abs(l), Math.Abs(r));
            if (peak > blockPeak) blockPeak = peak;
            _follower = DspMath.Flush(peak > _follower ? peak : peak + fall * (_follower - peak));
            if (_follower >= openAt) { _open = true; _holdLeft = holdSamples; anyOpen = true; }
            else if (_open)
            {
                if (_follower >= closeAt) _holdLeft = holdSamples;
                else if (_holdLeft > 0) _holdLeft--;
                else _open = false;
            }
            var target = _open ? 1.0 : floor;
            _gain += (target - _gain) * (_open ? attack : release);
            var wet = _wet.Next();
            outLeft[i] = (float)(l + (l * _gain - l) * wet);
            outRight[i] = (float)(r + (r * _gain - r) * wet);
        }
        var meterFall = Math.Exp(-1000.0 * frames / (MeterFallMs * SampleRate));
        _levelMeter = Math.Max(DspMath.LinToDb(blockPeak), MeterFloorDb + (_levelMeter - MeterFloorDb) * meterFall);
        SetReadout(OpenIndex, anyOpen ? 1 : 0);
        SetReadout(LevelIndex, Math.Clamp(_levelMeter, MeterFloorDb, MeterCeilingDb));
    }
}
