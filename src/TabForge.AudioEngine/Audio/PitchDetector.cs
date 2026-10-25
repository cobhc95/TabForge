namespace TabForge.AudioEngine.Audio;

// Owns: the monophonic tuner pitch detector (YIN on a decimated window, preallocated, main thread only).

/// <summary>
/// Monophonic pitch detector for the tuner: YIN (de Cheveigne and Kawahara 2002) on a decimated copy of the window, with a
/// parabolic refinement of the period. Every buffer is preallocated, so <see cref="Detect"/> allocates nothing. It runs on the
/// engine main thread (never on the audio callback). Range about 30 Hz (5-string bass low B) to 1.4 kHz. The analysis window
/// (<see cref="WindowMs"/>) must hold two periods of the lowest note: a shorter window answers faster but loses the low notes.
/// </summary>
public sealed class PitchDetector
{
    public const int DefaultWindowMs = 100, MaxWindowMs = 200;
    private const float Threshold = 0.15f;      // YIN absolute threshold on the normalised difference
    private const float MaxAperiodicity = 0.4f; // above this the best dip is not a pitch
    private const float SilenceRms = 0.004f;

    private readonly int _rate, _factor, _tauMin, _tauMaxLow, _maxWindow;
    private readonly float[] _x, _d, _n;
    private int _window, _tauMax;

    public PitchDetector(int sampleRate)
    {
        _rate = sampleRate;
        _factor = Math.Max(1, (int)Math.Round(sampleRate / 24000.0));
        var r = (double)sampleRate / _factor;
        _tauMin = Math.Max(2, (int)(r / 1400));
        _tauMaxLow = (int)Math.Ceiling(r / 30);
        _maxWindow = (int)Math.Ceiling(r * MaxWindowMs / 1000);
        _x = new float[_maxWindow + _tauMaxLow + 2];
        _d = new float[_tauMaxLow + 2];
        _n = new float[_tauMaxLow + 2];
        WindowMs = DefaultWindowMs;
    }

    private int _windowMs;

    /// <summary>The analysis window in ms (clamped to 1..<see cref="MaxWindowMs"/>). The lowest pitch found is two periods per window, never below 30 Hz.</summary>
    public int WindowMs
    {
        get => _windowMs;
        set
        {
            _windowMs = Math.Clamp(value, 1, MaxWindowMs);
            _window = Math.Min(_maxWindow, Math.Max(_tauMin * 2, (int)Math.Round((double)_rate / _factor * _windowMs / 1000)));
            _tauMax = Math.Clamp(_window / 2, _tauMin + 1, _tauMaxLow);
        }
    }

    /// <summary>Raw (engine-rate) samples <see cref="Detect"/> reads at the current <see cref="WindowMs"/>, newest last.</summary>
    public int RawWindow => (_window + _tauMax + 2) * _factor;

    /// <summary>Raw samples the longest window needs: the size of a buffer that serves every <see cref="WindowMs"/>.</summary>
    public int RawWindowMax => _x.Length * _factor;

    /// <summary>Pitch in Hz of the newest <see cref="RawWindow"/> samples, or 0 when silent / not pitched; <paramref name="clarity"/> is 0..1.</summary>
    public float Detect(ReadOnlySpan<float> raw, out float clarity)
    {
        clarity = 0;
        if (raw.Length < RawWindow) return 0;
        var start = raw.Length - RawWindow;
        // Decimate by averaging (a crude low-pass); remove the DC offset; measure the level.
        double mean = 0;
        var count = _window + _tauMax + 2;
        for (var i = 0; i < count; i++)
        {
            var s = 0f;
            for (var k = 0; k < _factor; k++) s += raw[start + i * _factor + k];
            _x[i] = s / _factor;
            mean += _x[i];
        }
        var dc = (float)(mean / count);
        double energy = 0;
        for (var i = 0; i < count; i++) { _x[i] -= dc; energy += _x[i] * _x[i]; }
        if (!(Math.Sqrt(energy / count) >= SilenceRms)) return 0;   // also false for NaN

        // Difference function and its cumulative-mean normalisation (tau up to tauMax + 1 for the refinement).
        for (var tau = 1; tau <= _tauMax + 1; tau++)
        {
            float sum = 0;
            for (var j = 0; j < _window; j++) { var diff = _x[j] - _x[j + tau]; sum += diff * diff; }
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
