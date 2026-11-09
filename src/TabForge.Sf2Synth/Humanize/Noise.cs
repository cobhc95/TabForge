using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: the seeded random sources: Rng (xorshift; uniform, unit-variance near-Gaussian bounded at 3.5 sigma) and Pink (1/f-like
//   noise of unit variance: octave-spaced reverting walks, time constants 1..256 notes, stage variance ~ 2^(-i*slope); slope 1.2
//   gives a DFA exponent of about 0.65, 1.0 about 0.7, over 16-256 notes).
// Does not own: what the noise drives (Phrase, Strum), the amounts (HumanTable).
// Tests: HumanizerTests.
internal sealed class Rng
{
    private uint _s = 1;

    public void Seed(uint s) => _s = s | 1;
    public double U() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / (double)0x800000 - 1; }
    public double U01() => 0.5 + 0.5 * U();
    public double G() => (U() + U() + U() + U()) * 0.8660254037844386;
}

internal sealed class Pink
{
    private readonly double[] _x = new double[PinkStages], _a = new double[PinkStages], _b = new double[PinkStages], _v = new double[PinkStages];

    public Pink(double slope)
    {
        double sum = 0;
        for (var i = 0; i < PinkStages; i++) sum += _v[i] = Math.Pow(2, -i * slope);
        for (var i = 0; i < PinkStages; i++)
        {
            _v[i] /= sum;
            _a[i] = 1 - Math.Pow(2, -i);
            _b[i] = Math.Sqrt((1 - _a[i] * _a[i]) * _v[i]);
        }
    }

    /// <summary>Starts from a stationary state drawn from <paramref name="r"/>.</summary>
    public void Reset(Rng r) { for (var i = 0; i < PinkStages; i++) _x[i] = Math.Sqrt(_v[i]) * r.G(); }

    public double Next(Rng r)
    {
        double s = 0;
        for (var i = 0; i < PinkStages; i++) s += _x[i] = _a[i] * _x[i] + _b[i] * r.G();
        return s;
    }
}
