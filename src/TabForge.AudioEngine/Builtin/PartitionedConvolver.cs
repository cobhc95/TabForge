using System.Numerics;

namespace TabForge.AudioEngine.Builtin;

// Owns: zero-latency convolution of one channel with one impulse response: the first HeadLength taps by direct FIR, the rest by uniformly-partitioned
//   overlap-save FFT stages (a small partition close to the start, a large one for the long tail), each stage's latency hidden by where its taps start.
// Does not own: reading, resampling or levelling the IR (IrPrep), mixing or the swap of a new IR (IrLoaderEffect).
// Tests: BuiltinEffectTests (against direct convolution).
// Threads: built on any thread (all buffers allocated here); Process on the audio thread only, no allocation.

/// <summary>
/// One channel of convolution with no added latency, for any host block size. Taps [0, <see cref="HeadLength"/>) run as a direct FIR; taps
/// [<see cref="HeadLength"/>, <see cref="SmallEnd"/>) in partitions of <see cref="SmallBlock"/>; the tail in partitions of <see cref="LargeBlock"/>.
/// A stage of partition N whose taps start at a multiple of N (at least N) has its block ready before its first output is due, so nothing is delayed.
/// </summary>
public sealed partial class PartitionedConvolver
{
    public const int HeadLength = 128, SmallBlock = 128, SmallEnd = 2048, LargeBlock = 1024;

    private readonly float[] _headReversed;
    private readonly float[] _history;   // the last HeadLength inputs, written twice so one window is contiguous
    private int _historyPos;
    private readonly Stage? _small, _large;
    private int _smallPos;               // samples into the current SmallBlock (LargeBlock is a multiple: its block ends on a small one)

    public int Length { get; }

    public PartitionedConvolver(ReadOnlySpan<float> ir)
    {
        Length = ir.Length;
        var head = Math.Min(ir.Length, HeadLength);
        _headReversed = new float[HeadLength];
        for (var i = 0; i < head; i++) _headReversed[HeadLength - 1 - i] = ir[i];
        _history = new float[2 * HeadLength];
        if (ir.Length > HeadLength) _small = new Stage(ir, SmallBlock, HeadLength, Math.Min(ir.Length, SmallEnd));
        if (ir.Length > SmallEnd) _large = new Stage(ir, LargeBlock, SmallEnd, ir.Length);
    }

    /// <summary>Audio thread: writes the convolution of <paramref name="input"/> to <paramref name="output"/> (the same length; may be the same memory).</summary>
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var frames = Math.Min(input.Length, output.Length);
        var done = 0;
        while (done < frames)
        {
            var run = Math.Min(frames - done, SmallBlock - _smallPos);
            for (var i = 0; i < run; i++)
            {
                var x = input[done + i];
                var y = Direct(x);
                if (_small is { } s) y += s.Push(x);
                if (_large is { } l) y += l.Push(x);
                output[done + i] = y;
            }
            done += run;
            _smallPos += run;
            if (_smallPos < SmallBlock) continue;
            _smallPos = 0;
            _small?.BlockIfFull();
            _large?.BlockIfFull();
        }
    }

    private float Direct(float x)
    {
        _historyPos = (_historyPos + 1) % HeadLength;
        _history[_historyPos] = x;
        _history[_historyPos + HeadLength] = x;
        // Window [pos+1, pos+HeadLength] is the last HeadLength inputs oldest first; the newest meets tap 0 (stored last).
        return Dot(new ReadOnlySpan<float>(_history, _historyPos + 1, HeadLength), _headReversed);
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = Vector<float>.Zero;
        var width = Vector<float>.Count;
        var i = 0;
        for (; i <= a.Length - width; i += width) sum += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
        var total = Vector.Dot(sum, Vector<float>.One);
        for (; i < a.Length; i++) total += a[i] * b[i];
        return total;
    }
}
