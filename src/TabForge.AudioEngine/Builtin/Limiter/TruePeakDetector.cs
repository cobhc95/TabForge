namespace TabForge.AudioEngine.Builtin.Limiter;

// Owns: the true-peak (inter-sample peak) detector of one channel: the signal is read at 4x its rate with a 32-tap windowed-sinc interpolator per phase, and
//   the largest magnitude of the four phases is the peak of the sample Delay positions back (half the taps). Designed per instance (no shared tables), sample-rate independent.
// Does not own: what the limiter does with the peak (LimiterEffect.Process.cs).
// Tests: BuiltinDynamicsTests.
internal sealed class TruePeakDetector
{
    private const int Taps = 32, Phases = 4, Ring = Taps;
    /// <summary>Samples between the newest input and the sample the four phases are centred on: a peak found now belongs to the span Delay back to Delay - 1 back.</summary>
    public const int Delay = Taps / 2;

    private readonly double[] _kernel = new double[(Phases - 1) * Taps];
    private readonly float[] _history = new float[Ring * 2];
    private int _position;

    public TruePeakDetector()
    {
        for (var p = 1; p < Phases; p++)
        {
            var sum = 0.0;
            for (var k = 0; k < Taps; k++)
            {
                var t = p / (double)Phases - (Delay - k);   // distance from the interpolated point to the tap, in input samples
                var tap = Sinc(t) * Blackman(t / Delay);
                _kernel[(p - 1) * Taps + k] = tap;
                sum += tap;
            }
            for (var k = 0; k < Taps; k++) _kernel[(p - 1) * Taps + k] /= sum;
        }
    }

    /// <summary>Takes the newest sample; returns the largest magnitude among the sample <see cref="Delay"/> back and the three points after it.</summary>
    public double Push(float sample)
    {
        _position = (_position + Ring - 1) % Ring;
        _history[_position] = sample;
        _history[_position + Ring] = sample;
        var peak = Math.Abs((double)_history[_position + Delay]);
        for (var p = 1; p < Phases; p++)
        {
            var sum = 0.0;
            var baseTap = (p - 1) * Taps;
            for (var k = 0; k < Taps; k++) sum += _kernel[baseTap + k] * _history[_position + k];
            peak = Math.Max(peak, Math.Abs(sum));
        }
        return peak;
    }

    private static double Sinc(double t) => Math.Abs(t) < 1e-9 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);

    private static double Blackman(double u) => Math.Abs(u) >= 1 ? 0 : 0.42 + 0.5 * Math.Cos(Math.PI * u) + 0.08 * Math.Cos(2 * Math.PI * u);
}
