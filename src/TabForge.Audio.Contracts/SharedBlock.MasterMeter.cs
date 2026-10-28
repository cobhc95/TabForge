namespace TabForge.Audio.Contracts;

// Owns: the master meter's fields in the shared block's header (bytes 96..127): what the engine measured on the master output, for TabForge to read.
// Does not own: the measuring (TabForge.AudioEngine.Mixing.MasterMeter), the scales and the hold (TabForge.Audio.Metering), the rest of the block (SharedBlock.cs).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
public sealed unsafe partial class SharedBlock
{
    // Header bytes 96..127 (free in the fixed layout): peak L / R since the last read (float), RMS L / R (float), short-term loudness (float, LUFS), clip count (int).
    private const int OffMasterPeakL = 96, OffMasterPeakR = 100, OffMasterRmsL = 104, OffMasterRmsR = 108, OffMasterLufs = 112, OffMasterClips = 116;

    /// <summary>The loudness written while nothing sounds (LUFS has no zero).</summary>
    public const float SilentLufs = -120f;

    /// <summary>Engine audio thread: the peaks of one block; the block raises what the reader has not taken yet (a peak between two reads is never lost).</summary>
    public void RaiseMasterPeaks(float left, float right)
    {
        var l = (float*)(_base + OffMasterPeakL); var r = (float*)(_base + OffMasterPeakR);
        if (left > *l) *l = left;
        if (right > *r) *r = right;
    }

    /// <summary>TabForge: the highest master peaks (linear, 0..1+) since the last call; the next call starts again from silence.</summary>
    public (float Left, float Right) TakeMasterPeaks()
    {
        var l = (float*)(_base + OffMasterPeakL); var r = (float*)(_base + OffMasterPeakR);
        var peaks = (*l, *r);
        *l = 0; *r = 0;
        return peaks;
    }

    /// <summary>Engine audio thread: the smoothed RMS of each channel (linear) and the short-term loudness (LUFS).</summary>
    public void SetMasterLevels(float rmsLeft, float rmsRight, float shortTermLufs)
    {
        *(float*)(_base + OffMasterRmsL) = rmsLeft;
        *(float*)(_base + OffMasterRmsR) = rmsRight;
        *(float*)(_base + OffMasterLufs) = shortTermLufs;
    }

    /// <summary>Engine audio thread: the number of master samples at or above full scale so far (counts up for the engine's life).</summary>
    public void SetMasterClipCount(int count) => Volatile.Write(ref *(int*)(_base + OffMasterClips), count);

    /// <summary>TabForge: the latest RMS of each master channel (linear), the short-term loudness (LUFS) and the clip count.</summary>
    public (float RmsLeft, float RmsRight, float ShortTermLufs, int Clips) MasterLevels =>
        (*(float*)(_base + OffMasterRmsL), *(float*)(_base + OffMasterRmsR), *(float*)(_base + OffMasterLufs), Volatile.Read(ref *(int*)(_base + OffMasterClips)));
}
