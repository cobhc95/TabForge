using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Eq;

// Owns: the EQ audio path: the knob values read once per block, the glides advanced every 32 samples, low cut, the four bands, high cut, output gain, and a
//   bypass that fades (no click). No allocation, no locks.
// Does not own: the parameter list (EqEffect.cs), the filter maths (EqDesign, Biquad).
// Tests: BuiltinEqTests.
public sealed partial class EqEffect
{
    private const int SubBlock = 32;
    private const double GainGlideMs = 20, BypassFadeMs = 10;
    private EqBand[] _bands = [];
    private EqCut _low = null!, _high = null!;
    private Smoothed _output, _wet;

    private void InitialiseDsp()
    {
        var rate = SampleRate;
        _bands = new EqBand[Bands];
        for (var b = 0; b < Bands; b++) _bands[b] = new EqBand(b, rate, Descriptors[Index(b, FreqField)].Default, Descriptors[Index(b, QField)].Default, HasType(b));
        _low = new EqCut(false, rate, Descriptors[LowCutFreqIndex].Default);
        _high = new EqCut(true, rate, Descriptors[HighCutFreqIndex].Default);
        _output = new Smoothed(1, GainGlideMs, rate);
        _wet = new Smoothed(1, BypassFadeMs, rate);
    }

    private void Aim()
    {
        _wet.Target = On(BypassIndex) ? 0 : 1;
        _output.Target = DspMath.DbToLin(Value(OutputIndex));
        _low.Aim(Value(LowCutFreqIndex), Value(LowCutSlopeIndex));
        _high.Aim(Value(HighCutFreqIndex), Value(HighCutSlopeIndex));
        for (var b = 0; b < Bands; b++)
        {
            var on = On(Index(b, OnField));
            _bands[b].Aim(Value(Index(b, FreqField)), on ? Value(Index(b, GainField)) : 0, Value(Index(b, QField)), HasType(b) && Value(TypeIndex(b)) < 0.5);
        }
    }

    protected override void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames)
    {
        Aim();
        for (var done = 0; done < frames;)
        {
            var n = Math.Min(SubBlock, frames - done);
            _low.Advance(n);
            _high.Advance(n);
            foreach (var band in _bands) band.Advance(n);
            for (var i = done; i < done + n; i++)
            {
                double dryL = inLeft[i], dryR = inRight[i], l = dryL, r = dryR;
                _low.Process(ref l, ref r);
                foreach (var band in _bands) band.Process(ref l, ref r);
                _high.Process(ref l, ref r);
                var gain = _output.Next();
                var wet = _wet.Next();
                outLeft[i] = (float)(dryL + (l * gain - dryL) * wet);
                outRight[i] = (float)(dryR + (r * gain - dryR) * wet);
            }
            done += n;
        }
    }
}
