namespace TabForge.AudioEngine.Audio;

/// <summary>
/// Monophonic pitch detector for the tuner: YIN (de Cheveigne and Kawahara 2002) on a decimated copy of the window, with a
/// parabolic refinement of the period. Every buffer is preallocated, so <see cref="Detect"/> allocates nothing. It runs on the
/// engine main thread (never on the audio callback). Range about 30 Hz (5-string bass low B) to 1.4 kHz.
/// </summary>
public sealed class PitchDetector
{
    private const int Window = 2048;            // integration window, decimated samples
    private const float Threshold = 0.15f;      // YIN absolute threshold on the normalised difference
    private const float MaxAperiodicity = 0.4f; // above this the best dip is not a pitch
    private const float SilenceRms = 0.004f;

    private readonly int _rate, _factor, _tauMin, _tauMax;
    private readonly float[] _x, _d, _n;

    public PitchDetector(int sampleRate)
    {
        _rate = sampleRate;
        _factor = Math.Max(1, (int)Math.Round(sampleRate / 24000.0));
        var r = (double)sampleRate / _factor;
        _tauMin = Math.Max(2, (int)(r / 1400));
        _tauMax = (int)Math.Ceiling(r / 30);
        _x = new float[Window + _tauMax + 2];
        _d = new float[_tauMax + 2];
        _n = new float[_tauMax + 2];
    }

    /// <summary>Raw (engine-rate) samples <see cref="Detect"/> needs, newest last.</summary>
    public int RawWindow => _x.Length * _factor;

    /// <summary>Pitch in Hz of the newest <see cref="RawWindow"/> samples, or 0 when silent / not pitched; <paramref name="clarity"/> is 0..1.</summary>
    public float Detect(ReadOnlySpan<float> raw, out float clarity)
    {
        clarity = 0;
        if (raw.Length < RawWindow) return 0;
        var start = raw.Length - RawWindow;
        // Decimate by averaging (a crude low-pass); remove the DC offset; measure the level.
        double mean = 0;
        for (var i = 0; i < _x.Length; i++)
        {
            var s = 0f;
            for (var k = 0; k < _factor; k++) s += raw[start + i * _factor + k];
            _x[i] = s / _factor;
            mean += _x[i];
        }
        var dc = (float)(mean / _x.Length);
        double energy = 0;
        for (var i = 0; i < _x.Length; i++) { _x[i] -= dc; energy += _x[i] * _x[i]; }
        if (!(Math.Sqrt(energy / _x.Length) >= SilenceRms)) return 0;   // also false for NaN

        // Difference function and its cumulative-mean normalisation (tau up to tauMax + 1 for the refinement).
        for (var tau = 1; tau <= _tauMax + 1; tau++)
        {
            float sum = 0;
            for (var j = 0; j < Window; j++) { var diff = _x[j] - _x[j + tau]; sum += diff * diff; }
            _d[tau] = sum;
        }
        double run = 0;
        _n[0] = 1;
        for (var tau = 1; tau <= _tauMax + 1; tau++) { run += _d[tau]; _n[tau] = run > 0 ? (float)(_d[tau] * tau / run) : 1f; }

        var best = -1;
        for (var tau = _tauMin; tau <= _tauMax; tau++)
        {
            if (_n[tau] >= Threshold) continue;
            while (tau + 1 <= _tauMax && _n[tau + 1] < _n[tau]) tau++;
            best = tau; break;
        }
        if (best < 0)
        {
            var low = float.MaxValue;
            for (var tau = _tauMin; tau <= _tauMax; tau++) if (_n[tau] < low) { low = _n[tau]; best = tau; }
            if (best < 0 || low > MaxAperiodicity) return 0;
        }
        clarity = Math.Clamp(1f - _n[best], 0f, 1f);

        // Parabolic interpolation of the raw difference function around the dip.
        double refined = best;
        if (best > 1)
        {
            double a = _d[best - 1], b = _d[best], c = _d[best + 1];
            var denom = a - 2 * b + c;
            if (denom > 1e-12) refined = best + 0.5 * (a - c) / denom;
        }
        return (float)((double)_rate / _factor / refined);
    }

    /// <summary>Nearest equal-tempered note (A4 = <paramref name="a4"/> Hz): MIDI number and the offset from it in cents (-50..50).</summary>
    public static (int Midi, double Cents) NearestNote(double hz, double a4 = 440)
    {
        var m = 69 + 12 * Math.Log2(hz / a4);
        var midi = (int)Math.Round(m);
        return (midi, (m - midi) * 100);
    }
}
