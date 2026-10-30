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
    public const uint BypassId = CompressorParameters.BypassId, ThresholdId = CompressorParameters.ThresholdId, RatioId = CompressorParameters.RatioId,
        KneeId = CompressorParameters.KneeId, AttackId = CompressorParameters.AttackId, ReleaseId = CompressorParameters.ReleaseId,
        MakeupId = CompressorParameters.MakeupId, AutoMakeupId = CompressorParameters.AutoMakeupId;
    public const uint GainReductionId = CompressorParameters.GainReductionId, InputLevelId = CompressorParameters.InputLevelId;
    public const int BypassIndex = 0, ThresholdIndex = 1, RatioIndex = 2, KneeIndex = 3, AttackIndex = 4, ReleaseIndex = 5, MakeupIndex = 6, AutoMakeupIndex = 7,
        GainReductionIndex = 8, InputLevelIndex = 9;
    public const double MinThreshold = CompressorParameters.MinThreshold, MaxRatio = CompressorParameters.MaxRatio, MaxKnee = CompressorParameters.MaxKnee,
        MinAttack = CompressorParameters.MinAttack, MaxAttack = CompressorParameters.MaxAttack, MinRelease = CompressorParameters.MinRelease,
        MaxRelease = CompressorParameters.MaxRelease, MaxMakeup = CompressorParameters.MaxMakeup, MaxReduction = CompressorParameters.MaxReduction,
        MeterFloorDb = CompressorParameters.MeterFloorDb, MeterCeilingDb = CompressorParameters.MeterCeilingDb;

    public static IReadOnlyList<BuiltinParameter> Descriptors => CompressorParameters.Descriptors;

    public CompressorEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.CompressorId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseDsp();
    }

    protected override void OnReconfigured() => InitialiseDsp();
}
