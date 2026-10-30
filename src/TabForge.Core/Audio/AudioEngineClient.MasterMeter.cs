using System;
using TabForge.Audio.Metering;
using TabForge.Models;

namespace TabForge.Audio;

// Owns: reading the engine's master meter from the shared block (the transport bar's level meter).
// Does not own: the measuring (TabForge.AudioEngine.Mixing.MasterMeter, on the engine's audio thread), the bars and their hold (MasterMeterModel).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
public sealed partial class AudioEngineClient
{
    /// <summary>The master output levels since the last call (peaks), smoothed RMS and short-term loudness; null while no engine runs. One reader: the master meter's tick.</summary>
    public MasterMeterReading? ReadMasterMeter()
    {
        if (_shared is not { } shared) return null;
        var (left, right) = shared.TakeMasterPeaks();
        var (rmsLeft, rmsRight, lufs, clips) = shared.MasterLevels;
        return new MasterMeterReading(left, right, rmsLeft, rmsRight, lufs, clips);
    }

    /// <summary>One track's meter at the chosen tap; null while no engine runs or the track has no slot yet. One reader per track: the track row's tick.</summary>
    public MasterMeterReading? ReadTrackMeter(TrackModel track, bool preFader)
    {
        if (_shared is not { } shared || !_slots.TryGetValue(track, out var slot)) return null;
        var (left, right, rmsLeft, rmsRight, clips) = shared.TrackLevels(slot, preFader);
        // Per-track measurement has no K-weighted loudness window: the smoothed RMS stands in for it, in the same dB scale.
        var lufs = rmsLeft + rmsRight > 0 ? (float)(10 * Math.Log10((rmsLeft * rmsLeft + rmsRight * rmsRight) / 2.0) - 0.691) : MasterMeterReading.SilentLufs;
        return new MasterMeterReading(left, right, rmsLeft, rmsRight, lufs, clips);
    }
}
