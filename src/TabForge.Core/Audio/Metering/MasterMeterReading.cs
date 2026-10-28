namespace TabForge.Audio.Metering;

// Owns: one reading of the engine's master meter, as plain numbers.
// Does not own: the measuring (TabForge.AudioEngine.Mixing.MasterMeter), the shared block it travels in (SharedBlock) or what the bars show (MasterMeterModel).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
/// <summary>The master output as measured by the engine: peaks since the last reading, smoothed RMS per channel (all linear, 0..1+), short-term loudness in LUFS and the clip count.</summary>
public readonly record struct MasterMeterReading(float PeakLeft, float PeakRight, float RmsLeft, float RmsRight, float ShortTermLufs, int Clips)
{
    /// <summary>The loudness that means silence.</summary>
    public const float SilentLufs = TabForge.Audio.Contracts.SharedBlock.SilentLufs;

    /// <summary>No engine, or nothing playing: every level silent.</summary>
    public static MasterMeterReading Silent(int clips = 0) => new(0, 0, 0, 0, SilentLufs, clips);
}
