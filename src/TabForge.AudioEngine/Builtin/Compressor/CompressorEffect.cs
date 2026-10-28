using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Compressor;

// Owns: the compressor parameter list (ids and keys are saved in songs and presets: never change them), the readouts the editor shows (gain reduction and input
//   level, polled like any parameter), and the value texts.
// Does not own: the audio path (CompressorEffect.Process.cs), the gain computer (CompressorCurve), the factory presets (BuiltinPresets in Contracts), the window.
// Tests: BuiltinDynamicsTests.
// Threads: parameters and state on the engine main thread; Process on the audio thread (it writes the two readouts once per block).

/// <summary>Feed-forward peak compressor with a soft knee, stereo linked, attack and release in the gain-reduction domain, makeup (or automatic makeup).</summary>
public sealed partial class CompressorEffect : BuiltinEffect
{
    public const uint BypassId = 1, ThresholdId = 2, RatioId = 3, KneeId = 4, AttackId = 5, ReleaseId = 6, MakeupId = 7, AutoMakeupId = 8;
    public const uint GainReductionId = 100, InputLevelId = 101;
    public const int BypassIndex = 0, ThresholdIndex = 1, RatioIndex = 2, KneeIndex = 3, AttackIndex = 4, ReleaseIndex = 5, MakeupIndex = 6, AutoMakeupIndex = 7,
        GainReductionIndex = 8, InputLevelIndex = 9;
    public const double MinThreshold = -60, MaxRatio = 20, MaxKnee = 24, MinAttack = 0.1, MaxAttack = 200, MinRelease = 5, MaxRelease = 2000, MaxMakeup = 24, MaxReduction = 40,
        MeterFloorDb = -100, MeterCeilingDb = 6;

    public static IReadOnlyList<BuiltinParameter> Descriptors { get; } =
    [
        BuiltinParameter.Switch(BypassId, "bypass", "Bypass", false),
        new(ThresholdId, "threshold", "Threshold", MinThreshold, 0, -18, Unit: "dB"),
        new(RatioId, "ratio", "Ratio", 1, MaxRatio, 4, Unit: ": 1"),
        new(KneeId, "knee", "Knee", 0, MaxKnee, 6, Unit: "dB"),
        new(AttackId, "attack", "Attack", MinAttack, MaxAttack, 10, Unit: "ms"),
        new(ReleaseId, "release", "Release", MinRelease, MaxRelease, 120, Unit: "ms"),
        new(MakeupId, "makeup", "Makeup gain", 0, MaxMakeup, 0, Unit: "dB"),
        BuiltinParameter.Switch(AutoMakeupId, "autoMakeup", "Auto makeup", false),
        new(GainReductionId, "gainReduction", "Gain reduction", 0, MaxReduction, 0, ReadOnly: true, Unit: "dB"),
        new(InputLevelId, "inputLevel", "Input level", MeterFloorDb, MeterCeilingDb, MeterFloorDb, ReadOnly: true, Unit: "dBFS"),
    ];

    public CompressorEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.CompressorId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseDsp();
    }

    protected override void OnReconfigured() => InitialiseDsp();
}
