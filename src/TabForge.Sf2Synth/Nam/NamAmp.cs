using TabForge.Sf2Synth.Dsp;

namespace TabForge.Sf2Synth.Nam;

// Owns: one channel's NAM amp at the engine rate: input level from input_level_dbu (our DI: 0 dBFS = DiDbu), output from
//   loudness (-18 - loudness dB), resampling to and from the model rate when they differ, prewarm on construction. No allocation.
// Does not own: the model (NamFile), where it sits in the rig (Guitar/GuitarBus).
// Tests: NamTests.
internal sealed class NamAmp
{
    public const double DiDbu = 12;
    private const int Prime = 20, Prewarm = 8192;

    private readonly INamNet _net;
    private readonly float _in, _out;
    private readonly Resampler? _up, _down;
    private readonly float[] _buf;

    public NamAmp(NamModel m, double rate, int maxBlock)
    {
        _net = m.Build();
        _in = m.InputDbu is { } d ? (float)Math.Pow(10, (DiDbu - d) / 20) : 1;
        _out = (float)Math.Pow(10, (-18 - (m.Loudness ?? -18)) / 20);
        _buf = new float[Math.Max(Prewarm, (int)(maxBlock * m.Rate / rate) + 8)];
        _net.Process(_buf.AsSpan(0, Prewarm));
        if (Math.Abs(m.Rate - rate) < 0.5) return;
        (_up, _down) = (new Resampler(rate, m.Rate), new Resampler(m.Rate, rate));
        Array.Clear(_buf);
        _up.Write(_buf.AsSpan(0, Prime));
        _down.Write(_buf.AsSpan(0, Prime));
    }

    public void Process(Span<float> mono)
    {
        for (var i = 0; i < mono.Length; i++) mono[i] *= _in;
        if (_up is null || _down is null) _net.Process(mono);
        else
        {
            _up.Write(mono);
            var m = _up.Read(_buf);
            _net.Process(_buf.AsSpan(0, m));
            _down.Write(_buf.AsSpan(0, m));
            mono[_down.Read(mono)..].Clear();
        }
        for (var i = 0; i < mono.Length; i++) mono[i] *= _out;
    }
}
