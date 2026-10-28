using System.Diagnostics;

namespace TabForge.AudioEngine.Builtin.IrLoader;

// Owns: the IR Loader's audio path: taking a new kernel in one swap with a short crossfade from the old one, dry / wet Mix, Gain, the A/B bypass (all
//   ramped, no clicks), the pass-through while there is no IR, and the CPU readout. No allocation, no locks.
// Does not own: preparing kernels (IrLoaderEffect.cs), the convolution (PartitionedConvolver).
// Tests: BuiltinEffectTests.
public sealed partial class IrLoaderEffect
{
    private const int Scratch = 8192, FadeFrames = 512;
    private const float RampPerSample = 0.002f;   // one-pole ramp towards Mix / Gain / bypass: about 10 ms at 48 kHz

    private IrKernel? _pending;
    private IrKernel _active = IrKernel.Empty, _old = IrKernel.Empty;
    private int _tailSamples;
    private int _fade = FadeFrames;
    private float _mix = -1, _gain = 1;
    private double _cpu;
    private readonly float[] _wetL = new float[Scratch], _wetR = new float[Scratch], _oldL = new float[Scratch], _oldR = new float[Scratch];

    protected override void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames)
    {
        var started = Stopwatch.GetTimestamp();
        if (Interlocked.Exchange(ref _pending, null) is { } next)
        {
            _old = _active;
            _active = next;
            _fade = _old.IsEmpty && _mix < 0 ? FadeFrames : 0;   // the very first IR starts at once (renders begin with it); later swaps crossfade
        }
        var targetMix = On(Bypass) ? 0f : (float)(Value(Mix) / 100);
        var targetGain = (float)Math.Pow(10, Value(Gain) / 20);
        if (_mix < 0) { _mix = targetMix; _gain = targetGain; }
        for (var done = 0; done < frames;)
        {
            var n = Math.Min(Scratch, frames - done);
            Wet(_active, inLeft.AsSpan(done, n), inRight.AsSpan(done, n), _wetL, _wetR);
            var fading = _fade < FadeFrames;
            if (fading) Wet(_old, inLeft.AsSpan(done, n), inRight.AsSpan(done, n), _oldL, _oldR);
            for (var i = 0; i < n; i++)
            {
                var dryL = inLeft[done + i]; var dryR = inRight[done + i];
                var wetL = _wetL[i]; var wetR = _wetR[i];
                if (fading && _fade < FadeFrames)
                {
                    var a = (float)_fade++ / FadeFrames;
                    wetL = _oldL[i] + (wetL - _oldL[i]) * a;
                    wetR = _oldR[i] + (wetR - _oldR[i]) * a;
                }
                _mix += (targetMix - _mix) * RampPerSample;
                _gain += (targetGain - _gain) * RampPerSample;
                outLeft[done + i] = dryL + (wetL * _gain - dryL) * _mix;
                outRight[done + i] = dryR + (wetR * _gain - dryR) * _mix;
            }
            done += n;
        }
        if (_fade >= FadeFrames) _old = IrKernel.Empty;
        var budget = frames / Math.Max(1.0, SampleRate) * Stopwatch.Frequency;
        _cpu += (100.0 * (Stopwatch.GetTimestamp() - started) / Math.Max(1.0, budget) - _cpu) * 0.05;
        SetReadout(Cpu, _cpu);
    }

    /// <summary>The kernel's output for the chunk; without an IR the input passes unchanged (a missing file never silences the track).</summary>
    private static void Wet(IrKernel kernel, ReadOnlySpan<float> left, ReadOnlySpan<float> right, float[] outL, float[] outR)
    {
        if (kernel.IsEmpty) { left.CopyTo(outL); right.CopyTo(outR); return; }
        kernel.Left!.Process(left, outL.AsSpan(0, left.Length));
        kernel.Right!.Process(right, outR.AsSpan(0, right.Length));
    }
}

/// <summary>A prepared IR for both channels, with its own convolution state; made on a worker, swapped in whole. <see cref="Empty"/>: no IR.</summary>
public sealed class IrKernel
{
    public static IrKernel Empty { get; } = new();

    private IrKernel() { }

    public IrKernel(PreparedIr prepared)
    {
        Left = new PartitionedConvolver(prepared.Left);
        Right = new PartitionedConvolver(prepared.Right);
        Length = Math.Max(prepared.Left.Length, prepared.Right.Length);
    }

    public PartitionedConvolver? Left { get; }
    public PartitionedConvolver? Right { get; }
    public int Length { get; }
    public bool IsEmpty => Left is null;
}
