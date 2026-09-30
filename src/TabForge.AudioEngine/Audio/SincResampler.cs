namespace TabForge.AudioEngine.Audio;

/// <summary>
/// Streaming stereo windowed-sinc resampler for the capture path (RT-09): 48 taps, Blackman-Harris window, 256 kernel phases with
/// linear interpolation between them, cutoff at 92 % of the lower Nyquist (anti-aliasing when downsampling). Input-driven: every
/// input frame is consumed; <see cref="Taps"/> / 2 input frames stay buffered as look-ahead. All buffers are allocated in the
/// constructor, so <see cref="Process"/> never allocates (NAudio's WDL resampler resizes its input buffer as the carried-over
/// sample count changes). Accumulates in double. One thread at a time.
/// </summary>
public sealed class SincResampler
{
    public const int Taps = 48;
    private const int Half = Taps / 2;
    private const int Phases = 256;
    private readonly float[] _table = new float[(Phases + 1) * Taps];
    private readonly double _step;     // input frames per output frame
    private readonly float[] _buf;     // interleaved stereo: history + look-ahead + the current chunk
    private int _count;                // frames in _buf
    private double _pos;               // _buf position (frames) of the next output frame

    public int MaxInputFrames { get; }

    public SincResampler(int inRate, int outRate, int maxInputFrames)
    {
        _step = (double)inRate / outRate;
        MaxInputFrames = Math.Max(1, maxInputFrames);
        var cutoff = Math.Min(1.0, (double)outRate / inRate) * 0.92;   // fraction of the input Nyquist
        for (var p = 0; p <= Phases; p++)
        {
            var frac = (double)p / Phases;
            double sum = 0;
            var row = p * Taps;
            for (var k = 0; k < Taps; k++)
            {
                var t = k - (Half - 1) - frac;                         // distance (input frames) from the output position
                var x = cutoff * t;
                var sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                var u = (t + Half) / Taps;                             // 0..1 across the window
                var window = u <= 0 || u >= 1 ? 0 : 0.35875 - 0.48829 * Math.Cos(2 * Math.PI * u) + 0.14128 * Math.Cos(4 * Math.PI * u) - 0.01168 * Math.Cos(6 * Math.PI * u);
                var h = sinc * window;
                _table[row + k] = (float)h;
                sum += h;
            }
            for (var k = 0; k < Taps; k++) _table[row + k] = (float)(_table[row + k] / sum);   // unity gain at DC for every phase
        }
        _buf = new float[(MaxInputFrames + Taps + 8) * 2];
        _count = Half - 1;          // silent history before the first frame
        _pos = Half - 1;            // the first output is centred on the first input frame
    }

    /// <summary>Resamples <paramref name="input"/> (interleaved stereo, at most <see cref="MaxInputFrames"/> frames) into <paramref name="output"/>; returns the frames written.</summary>
    public int Process(ReadOnlySpan<float> input, float[] output, int outputCapacityFrames)
    {
        var n = Math.Min(input.Length / 2, _buf.Length / 2 - _count);
        input[..(n * 2)].CopyTo(_buf.AsSpan(_count * 2));
        _count += n;
        var produced = 0;
        while (produced < outputCapacityFrames)
        {
            var i0 = (int)_pos;
            if (i0 + Half >= _count) break;                           // not enough look-ahead yet
            var phase = (_pos - i0) * Phases;
            var p = (int)phase;
            var a = phase - p;
            var row0 = p * Taps;
            var row1 = row0 + Taps;
            var start = (i0 - (Half - 1)) * 2;
            double l = 0, r = 0;
            for (var k = 0; k < Taps; k++)
            {
                var c = _table[row0 + k] + (_table[row1 + k] - _table[row0 + k]) * a;
                l += c * _buf[start + k * 2];
                r += c * _buf[start + k * 2 + 1];
            }
            output[produced * 2] = (float)l;
            output[produced * 2 + 1] = (float)r;
            produced++;
            _pos += _step;
        }
        // Drop the frames no later output can reach (keep the history the next one needs).
        var drop = Math.Min((int)_pos - (Half - 1), _count);
        if (drop > 0)
        {
            Array.Copy(_buf, drop * 2, _buf, 0, (_count - drop) * 2);
            _count -= drop;
            _pos -= drop;
        }
        return produced;
    }
}
