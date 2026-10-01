namespace TabForge.AudioEngine.Mixing;

/// <summary>
/// Transparent master safety limiter (A7-A01): a stereo-linked sample-peak limiter with a short lookahead. The required gain of each
/// sample (ceiling / peak of the louder channel) is minimum-filtered over the lookahead window, then smoothed with a moving average of
/// the lookahead length, so the gain is fully down by the time the peak leaves the delay line (an attack ramp of about 1.5 ms, no
/// overshoot). The release is a one-pole glide (about 80 ms). While the signal stays below the ceiling the gain is exactly 1, so
/// quiet material comes out bit-identical, only <see cref="Latency"/> frames late. Allocation-free and lock-free after construction
/// (audio thread); not thread-safe (one caller).
/// </summary>
public sealed class SafetyLimiter
{
    /// <summary>-0.3 dBFS, linear.</summary>
    public const float DefaultCeiling = 0.966051f;

    private readonly int _look;                 // lookahead = delay, in frames
    private readonly float _ceiling;
    private readonly float _releaseCoef;
    private readonly float[] _dl, _dr;          // delay line (look frames)
    private readonly float[] _req;              // required gain of the last look + 1 samples
    private readonly float[] _min;              // windowed minimum of the last look samples
    private int _pos, _reqPos, _minPos;
    private float _rel = 1f;
    private int _quiet;                         // consecutive samples that needed no reduction

    /// <summary>Frames the output lags the input (the lookahead).</summary>
    public int Latency => _look;
    public float Ceiling => _ceiling;

    public SafetyLimiter(int sampleRate, float ceiling = DefaultCeiling, double lookaheadMs = 1.5, double releaseMs = 80)
    {
        _look = Math.Max(2, (int)Math.Round(sampleRate * lookaheadMs / 1000.0));
        _ceiling = ceiling;
        _releaseCoef = 1f - MathF.Exp((float)(-1000.0 / (releaseMs * sampleRate)));
        _dl = new float[_look]; _dr = new float[_look];
        _req = new float[_look + 1]; _min = new float[_look];
        Reset();
    }

    /// <summary>Clears the delay line and the gain (silence in, unity gain).</summary>
    public void Reset()
    {
        Array.Clear(_dl); Array.Clear(_dr);
        Array.Fill(_req, 1f); Array.Fill(_min, 1f);
        _pos = _reqPos = _minPos = 0;
        _rel = 1f;
        _quiet = 0;
    }

    /// <summary>Limits <paramref name="n"/> frames in place. The output is the input delayed by <see cref="Latency"/> frames.</summary>
    public void Process(float[] left, float[] right, int n)
    {
        var look = _look;
        for (var i = 0; i < n; i++)
        {
            var inL = left[i]; var inR = right[i];
            var pk = MathF.Max(MathF.Abs(inL), MathF.Abs(inR));
            if (!float.IsFinite(pk)) { inL = 0; inR = 0; pk = 0; }   // never let a NaN into the gain state
            var req = pk > _ceiling ? _ceiling / pk : 1f;
            _req[_reqPos] = req;
            if (++_reqPos == _req.Length) _reqPos = 0;

            float gain;
            if (req == 1f && _quiet > 2 * look + 2 && _rel == 1f) { _min[_minPos] = 1f; if (++_minPos == look) _minPos = 0; gain = 1f; _quiet++; }
            else
            {
                _quiet = req == 1f ? _quiet + 1 : 0;
                // Minimum over the last look + 1 required gains (this sample included).
                var m = 1f;
                for (var k = 0; k < _req.Length; k++) if (_req[k] < m) m = _req[k];
                _min[_minPos] = m;
                if (++_minPos == look) _minPos = 0;
                // Moving average of the last look minima: reaches the required gain exactly when the peak leaves the delay.
                var sum = 0f;
                for (var k = 0; k < look; k++) sum += _min[k];
                var att = sum / look;
                // Release: follow the attack gain down at once (it is already smooth), climb back slowly.
                // The glide aims a hair above 1 so it reaches exactly 1 in finite time (a float one-pole toward 1 itself stalls short of it).
                _rel = att < _rel ? att : MathF.Min(1f, _rel + (1.001f - _rel) * _releaseCoef);
                gain = MathF.Min(att, _rel);
            }

            var outL = _dl[_pos]; var outR = _dr[_pos];
            _dl[_pos] = inL; _dr[_pos] = inR;
            if (++_pos == look) _pos = 0;
            outL *= gain; outR *= gain;
            left[i] = Math.Clamp(outL, -_ceiling, _ceiling);
            right[i] = Math.Clamp(outR, -_ceiling, _ceiling);
        }
    }
}
