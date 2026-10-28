namespace TabForge.AudioEngine.Mixing;

// Owns: the K-weighting filter of the loudness measurement (ITU-R BS.1770 / EBU R128): a high shelf (+4 dB above 1.7 kHz) followed by a 38 Hz high-pass,
//   its coefficients worked out for any sample rate from the analogue prototype (the same maths libebur128 uses).
// Does not own: the loudness window and the level reading (MasterMeter).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
/// <summary>One channel's K-weighting: two biquads in transposed direct form II; allocation-free, audio-thread safe.</summary>
public sealed class KWeightingFilter
{
    private const double ShelfHz = 1681.974450955533, ShelfGainDb = 3.999843853973347, ShelfQ = 0.7071752369554196;
    private const double HighPassHz = 38.13547087602444, HighPassQ = 0.5003270373238773;
    private const double ShelfBassExponent = 0.4996667741545416;

    private readonly double _shelfB0, _shelfB1, _shelfB2, _shelfA1, _shelfA2, _highA1, _highA2;
    private double _shelf1, _shelf2, _high1, _high2;

    public KWeightingFilter(int sampleRate)
    {
        var vh = Math.Pow(10, ShelfGainDb / 20);
        var vb = Math.Pow(vh, ShelfBassExponent);
        var k = Math.Tan(Math.PI * ShelfHz / sampleRate);
        var a0 = 1 + k / ShelfQ + k * k;
        _shelfB0 = (vh + vb * k / ShelfQ + k * k) / a0;
        _shelfB1 = 2 * (k * k - vh) / a0;
        _shelfB2 = (vh - vb * k / ShelfQ + k * k) / a0;
        _shelfA1 = 2 * (k * k - 1) / a0;
        _shelfA2 = (1 - k / ShelfQ + k * k) / a0;
        k = Math.Tan(Math.PI * HighPassHz / sampleRate);
        a0 = 1 + k / HighPassQ + k * k;
        _highA1 = 2 * (k * k - 1) / a0;
        _highA2 = (1 - k / HighPassQ + k * k) / a0;
    }

    /// <summary>One sample through both stages.</summary>
    public double Process(double x)
    {
        var y = _shelfB0 * x + _shelf1;
        _shelf1 = _shelfB1 * x - _shelfA1 * y + _shelf2;
        _shelf2 = _shelfB2 * x - _shelfA2 * y;
        var z = y + _high1;   // the high-pass numerator is 1, -2, 1
        _high1 = -2 * y - _highA1 * z + _high2;
        _high2 = y - _highA2 * z;
        return z;
    }

    /// <summary>Clears tiny leftovers of a silent stretch (they would slow the processor down as denormal numbers).</summary>
    public void FlushTiny()
    {
        if (Math.Abs(_shelf1) + Math.Abs(_shelf2) + Math.Abs(_high1) + Math.Abs(_high2) < 1e-20) _shelf1 = _shelf2 = _high1 = _high2 = 0;
    }
}
