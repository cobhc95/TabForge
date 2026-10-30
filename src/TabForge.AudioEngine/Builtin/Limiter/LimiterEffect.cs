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
    public const uint BypassId = LimiterParameters.BypassId, InputGainId = LimiterParameters.InputGainId, CeilingId = LimiterParameters.CeilingId,
        ReleaseId = LimiterParameters.ReleaseId, AutoReleaseId = LimiterParameters.AutoReleaseId;
    public const uint GainReductionId = LimiterParameters.GainReductionId, OutputLevelId = LimiterParameters.OutputLevelId, LatencyId = LimiterParameters.LatencyId;
    public const int BypassIndex = 0, InputGainIndex = 1, CeilingIndex = 2, ReleaseIndex = 3, AutoReleaseIndex = 4, GainReductionIndex = 5, OutputLevelIndex = 6, LatencyIndex = 7;
    public const double LookAheadMs = LimiterParameters.LookAheadMs, MinCeiling = LimiterParameters.MinCeiling, MaxInputGain = LimiterParameters.MaxInputGain,
        MinReleaseMs = LimiterParameters.MinReleaseMs, MaxReleaseMs = LimiterParameters.MaxReleaseMs, MaxReduction = LimiterParameters.MaxReduction,
        MeterFloorDb = LimiterParameters.MeterFloorDb, MeterCeilingDb = LimiterParameters.MeterCeilingDb;

    public static IReadOnlyList<BuiltinParameter> Descriptors => LimiterParameters.Descriptors;

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
