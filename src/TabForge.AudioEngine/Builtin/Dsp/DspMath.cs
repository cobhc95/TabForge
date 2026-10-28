namespace TabForge.AudioEngine.Builtin.Dsp;

// Owns: the small maths every built-in effect shares: dB and linear conversion, one-pole time constants from milliseconds (sample-rate independent),
//   denormal flushing, and the Smoothed value that glides to its target (no zipper noise when a knob moves).
// Does not own: filters (Biquad), any effect's sound.
// Tests: BuiltinDynamicsTests, BuiltinEqTests.
public static class DspMath
{
    /// <summary>Below this magnitude a filter or envelope state is set to zero: denormal numbers make the audio thread slow.</summary>
    public const double DenormalFloor = 1e-20;
    private const double FloorLinear = 1e-9;

    public static double DbToLin(double db) => Math.Exp(db * 0.115129254649702284);   // ln(10) / 20

    /// <summary>Linear amplitude to dB; silence reads -180 dB, never minus infinity.</summary>
    public static double LinToDb(double lin) => 20 * Math.Log10(Math.Max(lin, FloorLinear));

    public static double Flush(double x) => Math.Abs(x) < DenormalFloor ? 0 : x;

    /// <summary>The "keep" factor of a one-pole follower with time constant <paramref name="ms"/> (63 % of the way there in that time): y = target + keep * (y - target).</summary>
    public static double Keep(double ms, double sampleRate) => ms <= 0 ? 0 : Math.Exp(-1000.0 / (ms * sampleRate));
}

/// <summary>A value that glides to its target with a one-pole time constant; <see cref="Advance"/> moves it a whole block at once.</summary>
public struct Smoothed
{
    private double _value, _target, _keep;

    public Smoothed(double value, double ms, double sampleRate)
    {
        _value = _target = value;
        _keep = DspMath.Keep(ms, sampleRate);
    }

    public readonly double Value => _value;
    public readonly bool Settled => _value == _target;

    public double Target { readonly get => _target; set => _target = value; }

    public void Configure(double ms, double sampleRate) => _keep = DspMath.Keep(ms, sampleRate);

    public void Snap(double value) => _value = _target = value;

    /// <summary>One sample.</summary>
    public double Next()
    {
        if (_value == _target) return _value;
        _value = _target + _keep * (_value - _target);
        if (Math.Abs(_value - _target) < 1e-7 * (1 + Math.Abs(_target))) _value = _target;
        return _value;
    }

    /// <summary><paramref name="samples"/> samples at once.</summary>
    public double Advance(int samples)
    {
        if (_value == _target) return _value;
        _value = _target + Math.Pow(_keep, samples) * (_value - _target);
        if (Math.Abs(_value - _target) < 1e-7 * (1 + Math.Abs(_target))) _value = _target;
        return _value;
    }
}
