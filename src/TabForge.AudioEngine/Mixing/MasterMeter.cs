using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Mixing;

// Owns: the measuring of the master output for the transport bar's level meter: peak and clip count per block, RMS (300 ms smoothing) per channel and the
//   short-term loudness (EBU R128: K-weighted, 3 s window of 100 ms blocks), and writing them to the shared block. Audio thread; no allocation, no lock.
// Does not own: the output mix (MixEngine), the filter (KWeightingFilter), the scales, the hold and the clip light (TabForge.Audio.Metering, in TabForge).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
/// <summary>Measures every finished master block before the output level (Windows volume, path offset) and the clamp: the level the plug-in meters show.</summary>
public sealed class MasterMeter
{
    public const int BlocksInWindow = 30;          // 30 x 100 ms = the 3 s short-term window
    public const double BlockSeconds = 0.1;
    public const double RmsSeconds = 0.3;
    public const double LoudnessOffsetDb = -0.691;  // BS.1770: a 1 kHz tone through the K-weighting reads +0.691 dB; this makes a full-scale mono sine -3.01 LUFS
    public const float ClipLevel = 1f;

    private readonly SharedBlock? _shared;
    private readonly KWeightingFilter _kLeft, _kRight;
    private readonly int _blockFrames;
    private readonly double _rmsCoefficientPerFrame;
    private readonly double[] _window = new double[BlocksInWindow];   // per 100 ms block: the K-weighted mean squares of both channels, added
    private int _windowAt, _windowFilled, _inBlock, _clips;
    private double _blockSumL, _blockSumR, _rmsSquareL, _rmsSquareR, _windowSum;
    private float _lufs = SharedBlock.SilentLufs;

    public MasterMeter(int sampleRate, SharedBlock? shared = null)
    {
        _shared = shared;
        _kLeft = new KWeightingFilter(sampleRate);
        _kRight = new KWeightingFilter(sampleRate);
        _blockFrames = Math.Max(1, (int)Math.Round(sampleRate * BlockSeconds));
        _rmsCoefficientPerFrame = 1 - Math.Exp(-1.0 / (RmsSeconds * sampleRate));
    }

    /// <summary>Peak (linear) of the last block, per channel.</summary>
    public float PeakLeft { get; private set; }
    public float PeakRight { get; private set; }

    /// <summary>RMS (linear) of each channel over about 300 ms.</summary>
    public float RmsLeft => (float)Math.Sqrt(_rmsSquareL);
    public float RmsRight => (float)Math.Sqrt(_rmsSquareR);

    /// <summary>Short-term loudness in LUFS (3 s window; <see cref="SharedBlock.SilentLufs"/> for silence).</summary>
    public float ShortTermLufs => _lufs;

    /// <summary>Samples at or above full scale since the start.</summary>
    public int ClipCount => _clips;

    /// <summary>Measures the first <paramref name="frames"/> samples of <paramref name="left"/> and <paramref name="right"/> (before the output level), then publishes.</summary>
    public void Process(float[] left, float[] right, int frames)
    {
        float peakL = 0, peakR = 0;
        double sumL = 0, sumR = 0;
        var clips = 0;
        for (var i = 0; i < frames; i++)
        {
            var l = left[i]; var r = right[i];
            var al = Math.Abs(l); var ar = Math.Abs(r);
            if (al > peakL) peakL = al;
            if (ar > peakR) peakR = ar;
            if (al >= ClipLevel) clips++;
            if (ar >= ClipLevel) clips++;
            sumL += (double)l * l; sumR += (double)r * r;
            var kl = _kLeft.Process(l); var kr = _kRight.Process(r);
            _blockSumL += kl * kl; _blockSumR += kr * kr;
            if (++_inBlock == _blockFrames) EndBlock();
        }
        PeakLeft = peakL; PeakRight = peakR;
        _clips += clips;
        if (frames > 0)
        {
            var keep = Math.Pow(1 - _rmsCoefficientPerFrame, frames);   // the block's mean square joins the 300 ms average as if frame by frame
            _rmsSquareL = _rmsSquareL * keep + sumL / frames * (1 - keep);
            _rmsSquareR = _rmsSquareR * keep + sumR / frames * (1 - keep);
        }
        if (_shared is not { } shared) return;
        shared.RaiseMasterPeaks(peakL, peakR);
        shared.SetMasterLevels(RmsLeft, RmsRight, _lufs);
        shared.SetMasterClipCount(_clips);
    }

    // A 100 ms block is full: its mean squares replace the oldest in the 3 s window, and the loudness is read from the window's mean.
    private void EndBlock()
    {
        var power = (_blockSumL + _blockSumR) / _blockFrames;
        _blockSumL = _blockSumR = 0; _inBlock = 0;
        _windowSum += power - _window[_windowAt];
        _window[_windowAt] = power;
        _windowAt = (_windowAt + 1) % BlocksInWindow;
        _windowFilled = Math.Min(_windowFilled + 1, BlocksInWindow);
        var mean = Math.Max(0, _windowSum) / _windowFilled;
        _lufs = mean > 1e-12 ? (float)Math.Max(SharedBlock.SilentLufs, LoudnessOffsetDb + 10 * Math.Log10(mean)) : SharedBlock.SilentLufs;
        _kLeft.FlushTiny(); _kRight.FlushTiny();
    }
}
