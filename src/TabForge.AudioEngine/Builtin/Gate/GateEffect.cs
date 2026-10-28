using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Gate;

// Owns: the noise gate parameter list (ids and keys are saved in songs and presets: never change them) and the readouts the editor shows (open light, level).
// Does not own: the audio path (GateEffect.Process.cs), the window.
// Tests: BuiltinDynamicsTests.
// Threads: parameters and state on the engine main thread; Process on the audio thread (it writes the readouts once per block).

/// <summary>Noise gate: opens when the stereo-linked peak passes the threshold, stays open for the hold time, closes by the release to the range (how far it turns the sound down).</summary>
public sealed partial class GateEffect : BuiltinEffect
{
    public const uint BypassId = 1, ThresholdId = 2, AttackId = 3, HoldId = 4, ReleaseId = 5, RangeId = 6, OpenId = 100, LevelId = 101;
    public const int BypassIndex = 0, ThresholdIndex = 1, AttackIndex = 2, HoldIndex = 3, ReleaseIndex = 4, RangeIndex = 5, OpenIndex = 6, LevelIndex = 7;
    public const double MinThreshold = -80, MinRange = -80, MeterFloorDb = -100, MeterCeilingDb = 6;

    public static IReadOnlyList<BuiltinParameter> Descriptors { get; } =
    [
        BuiltinParameter.Switch(BypassId, "bypass", "Bypass", false),
        new(ThresholdId, "threshold", "Threshold", MinThreshold, 0, -50, Unit: "dB"),
        new(AttackId, "attack", "Attack", 0.1, 100, 1, Unit: "ms"),
        new(HoldId, "hold", "Hold", 0, 500, 50, Unit: "ms"),
        new(ReleaseId, "release", "Release", 5, 3000, 150, Unit: "ms"),
        new(RangeId, "range", "Range", MinRange, 0, -60, Unit: "dB"),
        new(OpenId, "open", "Gate open", 0, 1, 0, Stepped: true, ReadOnly: true),
        new(LevelId, "level", "Input level", MeterFloorDb, MeterCeilingDb, MeterFloorDb, ReadOnly: true, Unit: "dBFS"),
    ];

    public GateEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.GateId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseDsp();
    }

    protected override void OnReconfigured() => InitialiseDsp();

    protected override string TextOf(BuiltinParameter parameter, double value) => parameter.Id == OpenId ? (value >= 0.5 ? "Open" : "Closed") : base.TextOf(parameter, value);
}
