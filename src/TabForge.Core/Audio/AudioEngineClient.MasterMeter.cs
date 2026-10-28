using TabForge.Audio.Metering;

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
}
