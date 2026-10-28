using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Limiter;

// Owns: the limiter parameter list (ids and keys are saved in songs and presets: never change them), the readouts the editor shows (gain reduction, output
//   level, latency), the look-ahead length and the latency it reports to the host.
// Does not own: the audio path (LimiterEffect.Process.cs), the peak detector (TruePeakDetector), the window.
// Tests: BuiltinDynamicsTests.
// Threads: parameters and state on the engine main thread; Process on the audio thread (it writes the readouts once per block).

/// <summary>Look-ahead true-peak limiter: input gain, ceiling in dBTP, release (or automatic release); about 1.5 ms of look-ahead, reported as latency.</summary>
public sealed partial class LimiterEffect : BuiltinEffect
{
    public const uint BypassId = 1, InputGainId = 2, CeilingId = 3, ReleaseId = 4, AutoReleaseId = 5;
    public const uint GainReductionId = 100, OutputLevelId = 101, LatencyId = 102;
    public const int BypassIndex = 0, InputGainIndex = 1, CeilingIndex = 2, ReleaseIndex = 3, AutoReleaseIndex = 4, GainReductionIndex = 5, OutputLevelIndex = 6, LatencyIndex = 7;
    public const double LookAheadMs = 1.5, MinCeiling = -12, MaxInputGain = 24, MinReleaseMs = 1, MaxReleaseMs = 1000, MaxReduction = 30, MeterFloorDb = -100, MeterCeilingDb = 6;

    public static IReadOnlyList<BuiltinParameter> Descriptors { get; } =
    [
        BuiltinParameter.Switch(BypassId, "bypass", "Bypass", false),
        new(InputGainId, "inputGain", "Input gain", -12, MaxInputGain, 0, Unit: "dB"),
        new(CeilingId, "ceiling", "Ceiling", MinCeiling, 0, -1.0, Unit: "dBTP"),
        new(ReleaseId, "release", "Release", MinReleaseMs, MaxReleaseMs, 100, Unit: "ms"),
        BuiltinParameter.Switch(AutoReleaseId, "autoRelease", "Auto release", false),
        new(GainReductionId, "gainReduction", "Gain reduction", 0, MaxReduction, 0, ReadOnly: true, Unit: "dB"),
        new(OutputLevelId, "outputLevel", "Output level", MeterFloorDb, MeterCeilingDb, MeterFloorDb, ReadOnly: true, Unit: "dBFS"),
        BuiltinParameter.Readout(LatencyId, "latency", "Latency", 100000, "samples"),
    ];

    private int _lookAhead;

    public LimiterEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.LimiterId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseDsp();
    }

    /// <summary>The look-ahead in samples: the audio is delayed by this much, and the host is told so it can line up other tracks.</summary>
    public override int LatencySamples => _lookAhead;

    protected override void OnReconfigured() => InitialiseDsp();

    protected override string TextOf(BuiltinParameter parameter, double value) => parameter.Id == LatencyId
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_lookAhead} samples ({_lookAhead * 1000.0 / SampleRate:0.0} ms)")
        : base.TextOf(parameter, value);
}
