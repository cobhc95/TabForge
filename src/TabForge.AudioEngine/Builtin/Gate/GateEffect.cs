using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Gate;

// Owns: the noise gate parameter list (ids and keys are saved in songs and presets: never change them) and the readouts the editor shows (open light, level).
// Does not own: the audio path (GateEffect.Process.cs), the window.
// Tests: BuiltinDynamicsTests.
// Threads: parameters and state on the engine main thread; Process on the audio thread (it writes the readouts once per block).

/// <summary>Noise gate: opens when the stereo-linked peak passes the threshold, stays open for the hold time, closes by the release to the range (how far it turns the sound down).</summary>
public sealed partial class GateEffect : BuiltinEffect
{
    public const uint BypassId = GateParameters.BypassId, ThresholdId = GateParameters.ThresholdId, AttackId = GateParameters.AttackId,
        HoldId = GateParameters.HoldId, ReleaseId = GateParameters.ReleaseId, RangeId = GateParameters.RangeId,
        OpenId = GateParameters.OpenId, LevelId = GateParameters.LevelId;
    public const int BypassIndex = 0, ThresholdIndex = 1, AttackIndex = 2, HoldIndex = 3, ReleaseIndex = 4, RangeIndex = 5, OpenIndex = 6, LevelIndex = 7;
    public const double MinThreshold = GateParameters.MinThreshold, MinRange = GateParameters.MinRange,
        MeterFloorDb = GateParameters.MeterFloorDb, MeterCeilingDb = GateParameters.MeterCeilingDb;

    public static IReadOnlyList<BuiltinParameter> Descriptors => GateParameters.Descriptors;

    public GateEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.GateId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseDsp();
    }

    protected override void OnReconfigured() => InitialiseDsp();

    protected override string TextOf(BuiltinParameter parameter, double value) => parameter.Id == OpenId ? (value >= 0.5 ? "Open" : "Closed") : base.TextOf(parameter, value);
}
