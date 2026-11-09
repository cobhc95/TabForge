using TabForge.Sf2Synth.Dsp;

namespace TabForge.Sf2Synth.Nam;

// Owns: one channel's NAM amp at the engine rate: input level from input_level_dbu (our DI: 0 dBFS = DiDbu), output from
//   loudness (-18 - loudness dB), the model's output at silence removed (its DC would click when the bus stops), resampling to and
//   from the model rate when they differ, prewarm on construction and Reset. No allocation.
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
    private float _rest;

    public NamAmp(NamModel m, double rate, int maxBlock)
    {
        _net = m.Build();
        _in = m.InputDbu is { } d ? (float)Math.Pow(10, (DiDbu - d) / 20) : 1;
        _out = (float)Math.Pow(10, (-18 - (m.Loudness ?? -18)) / 20);
        _buf = new float[Math.Max(Prewarm, (int)(maxBlock * m.Rate / rate) + 8)];
        if (Math.Abs(m.Rate - rate) >= 0.5) (_up, _down) = (new Resampler(rate, m.Rate), new Resampler(m.Rate, rate));
        Reset();
    }

    // Back to the prewarmed state (a few ms of model time: call only for an amp that has played).
    public void Reset()
    {
        _net.Reset();
        Array.Clear(_buf);
        _net.Process(_buf.AsSpan(0, Prewarm));
        _rest = _buf[Prewarm - 1];
        if (_up is null || _down is null) return;
        _up.Reset();
        _down.Reset();
        Array.Clear(_buf);
        _up.Write(_buf.AsSpan(0, Prime));
        _down.Write(_buf.AsSpan(0, Prime));
    }

    public void Process(Span<float> mono)
    {
        for (var i = 0; i < mono.Length; i++) mono[i] *= _in;
        if (_up is null || _down is null) Net(mono);
        else
        {
            _up.Write(mono);
            var m = _up.Read(_buf);
            Net(_buf.AsSpan(0, m));
            _down.Write(_buf.AsSpan(0, m));
            mono[_down.Read(mono)..].Clear();
        }
        for (var i = 0; i < mono.Length; i++) mono[i] *= _out;
    }

    private void Net(Span<float> x)
    {
        _net.Process(x);
        for (var i = 0; i < x.Length; i++) x[i] -= _rest;
    }
}
