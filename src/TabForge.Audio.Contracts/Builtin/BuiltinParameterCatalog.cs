namespace TabForge.Audio.Contracts.Builtin;

// Owns: the parameter ids, keys and descriptor lists of the built-in effects, so the app's editors and the engine read one table.
// Does not own: the effects or their DSP (TabForge.AudioEngine.Builtin), the value texts only an effect can produce (the engine overrides TextOf).
// Tests: BuiltinEffectSetTests (the engine's descriptors must match this table).

/// <summary>The parameters of the built-in compressor.</summary>
public static class CompressorParameters{
    public const uint BypassId = 1, ThresholdId = 2, RatioId = 3, KneeId = 4, AttackId = 5, ReleaseId = 6, MakeupId = 7, AutoMakeupId = 8;
    public const uint GainReductionId = 100, InputLevelId = 101;
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
}

/// <summary>The parameters of the built-in limiter.</summary>
public static class LimiterParameters
{
    public const uint BypassId = 1, InputGainId = 2, CeilingId = 3, ReleaseId = 4, AutoReleaseId = 5;
    public const uint GainReductionId = 100, OutputLevelId = 101, LatencyId = 102;
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
}

/// <summary>The parameters of the built-in noise gate.</summary>
public static class GateParameters
{
    public const uint BypassId = 1, ThresholdId = 2, AttackId = 3, HoldId = 4, ReleaseId = 5, RangeId = 6, OpenId = 100, LevelId = 101;
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
}

/// <summary>The parameters of the built-in EQ, including the band layout its curve editor uses.</summary>
public static class EqParameters
{
    public const uint BypassId = 1, OutputId = 2, LowCutFreqId = 3, LowCutSlopeId = 4, HighCutFreqId = 5, HighCutSlopeId = 6, Band1TypeId = 7, Band4TypeId = 8;
    public const int Bands = 4;
    /// <summary>The fields of a band, in the order of its parameters.</summary>
    public const int OnField = 0, FreqField = 1, GainField = 2, QField = 3;
    public const double MinHz = 20, MaxHz = 20000, MaxGainDb = 18, MinQ = 0.1, MaxQ = 18, ShelfQ = 0.71, BellQ = 1.0;
    /// <summary>Slope values: Off, 12 dB per octave, 24 dB per octave. Type values: shelf, bell.</summary>
    public const int SlopeOff = 0, Slope12 = 1, Slope24 = 2, TypeShelf = 0, TypeBell = 1;

    private const int FirstBand = 6, FieldsPerBand = 4, Band1TypeIndex = FirstBand + Bands * FieldsPerBand, Band4TypeIndex = Band1TypeIndex + 1;
    private static readonly IReadOnlyList<double> DefaultFreq = [100, 400, 2500, 8000];

    public static IReadOnlyList<BuiltinParameter> Descriptors { get; } = Build();

    /// <summary>The parameter id of a band's field (band 0..3).</summary>
    public static uint BandId(int band, int field) => (uint)(10 + 10 * band + field);

    /// <summary>The index in <see cref="Descriptors"/> of a band's field.</summary>
    public static int Index(int band, int field) => FirstBand + band * FieldsPerBand + field;

    /// <summary>The index of the type (shelf or bell) of band 0 or band 3.</summary>
    public static int TypeIndex(int band) => band == 0 ? Band1TypeIndex : Band4TypeIndex;

    public static bool HasType(int band) => band is 0 or Bands - 1;

    private static IReadOnlyList<BuiltinParameter> Build()
    {
        var list = new List<BuiltinParameter>
        {
            BuiltinParameter.Switch(BypassId, "bypass", "Bypass", false),
            new(OutputId, "output", "Output gain", -MaxGainDb, MaxGainDb, 0, Unit: "dB"),
            new(LowCutFreqId, "lcFreq", "Low cut frequency", MinHz, 1000, 80, Unit: "Hz"),
            new(LowCutSlopeId, "lcSlope", "Low cut slope", SlopeOff, Slope24, SlopeOff, Stepped: true),
            new(HighCutFreqId, "hcFreq", "High cut frequency", 1000, MaxHz, 12000, Unit: "Hz"),
            new(HighCutSlopeId, "hcSlope", "High cut slope", SlopeOff, Slope24, SlopeOff, Stepped: true),
        };
        for (var b = 0; b < Bands; b++)
        {
            var n = b + 1;
            list.Add(BuiltinParameter.Switch(BandId(b, OnField), $"b{n}on", $"Band {n} on", true));
            list.Add(new(BandId(b, FreqField), $"b{n}freq", $"Band {n} frequency", MinHz, MaxHz, DefaultFreq[b], Unit: "Hz"));
            list.Add(new(BandId(b, GainField), $"b{n}gain", $"Band {n} gain", -MaxGainDb, MaxGainDb, 0, Unit: "dB"));
            list.Add(new(BandId(b, QField), $"b{n}q", $"Band {n} Q", MinQ, MaxQ, HasType(b) ? ShelfQ : BellQ));
        }
        list.Add(new(Band1TypeId, "b1type", "Band 1 type (shelf or bell)", TypeShelf, TypeBell, TypeShelf, Stepped: true));
        list.Add(new(Band4TypeId, "b4type", "Band 4 type (shelf or bell)", TypeShelf, TypeBell, TypeShelf, Stepped: true));
        return list;
    }
}

/// <summary>The parameters of the built-in delay.</summary>
public static class DelayParameters
{
    public const uint BypassId = 1, SyncId = 2, TimeId = 3, DivisionId = 4, FeedbackId = 5, MixId = 6, PingPongId = 7, LowCutId = 8, HighCutId = 9;
    public const double MinTimeMs = 1, MaxTimeMs = 2000, MaxFeedback = 95, MinLowCut = 20, MaxLowCut = 2000, MinHighCut = 1000, MaxHighCut = 20000;

    public static IReadOnlyList<BuiltinParameter> Descriptors { get; } =
    [
        BuiltinParameter.Switch(BypassId, "bypass", "Bypass", false),
        BuiltinParameter.Switch(SyncId, "sync", "Sync to tempo", true),
        new(TimeId, "time", "Time", MinTimeMs, MaxTimeMs, 350, Unit: "ms"),
        new(DivisionId, "division", "Note length", 0, DelayDivision.Count - 1, DelayDivision.Eighth, Stepped: true),
        new(FeedbackId, "feedback", "Feedback", 0, MaxFeedback, 35, Unit: "%"),
        new(MixId, "mix", "Mix", 0, 100, 30, Unit: "%"),
        BuiltinParameter.Switch(PingPongId, "pingPong", "Ping-pong", false),
        new(LowCutId, "lowCut", "Low cut", MinLowCut, MaxLowCut, MinLowCut, Unit: "Hz"),
        new(HighCutId, "highCut", "High cut", MinHighCut, MaxHighCut, MaxHighCut, Unit: "Hz"),
    ];
}

/// <summary>The parameters and file keys of the built-in IR Loader.</summary>
public static class IrLoaderParameters
{
    public const uint MixId = 1, GainId = 2, AutoLevelId = 3, TrimId = 4, SeparateId = 5, BypassId = 6;
    public const uint LatencyId = 100, CpuId = 101, LengthId = 102, AutoGainId = 103, StatusId = 104, FileId = 105, LeftFileId = 106, RightFileId = 107;
    public const string FileKey = "file", LeftKey = "left", RightKey = "right";
    /// <summary>The value of <see cref="StatusId"/>: no file chosen, the IR is loaded, or the file is missing / unreadable (its text says which).</summary>
    public const int StatusNone = 0, StatusLoaded = 1, StatusProblem = 2;

    public static IReadOnlyList<BuiltinParameter> Descriptors { get; } =
    [
        new(MixId, "mix", "Mix", 0, 100, 100, Unit: "%"),
        new(GainId, "gain", "Gain", -24, 24, 0, Unit: "dB"),
        BuiltinParameter.Switch(AutoLevelId, "autoLevel", "Auto level", true),
        BuiltinParameter.Switch(TrimId, "trim", "Trim silence", false),
        BuiltinParameter.Switch(SeparateId, "separate", "Separate Left / Right files", false),
        BuiltinParameter.Switch(BypassId, "bypass", "A/B: bypass the IR", false),
        BuiltinParameter.Readout(LatencyId, "latency", "Latency", 1_000_000, "samples"),
        BuiltinParameter.Readout(CpuId, "cpu", "CPU", 100, "%"),
        BuiltinParameter.Readout(LengthId, "length", "IR length", 1_000_000, "ms"),
        new(AutoGainId, "autoGain", "Auto level gain", -200, 200, 0, ReadOnly: true, Unit: "dB"),
        new(StatusId, "status", "IR", 0, 2, 0, Stepped: true, ReadOnly: true),
        new(FileId, "fileShown", "IR file", 0, 1, 0, Stepped: true, ReadOnly: true),
        new(LeftFileId, "leftShown", "Left IR file", 0, 1, 0, Stepped: true, ReadOnly: true),
        new(RightFileId, "rightShown", "Right IR file", 0, 1, 0, Stepped: true, ReadOnly: true),
    ];
}

/// <summary>The parameter descriptors of a built-in effect by its stable id (empty for another path).</summary>
public static class BuiltinParameters
{
    public static IReadOnlyList<BuiltinParameter> Of(string? id) => BuiltinEffects.Find(id)?.Id switch
    {
        BuiltinEffects.EqId => EqParameters.Descriptors,
        BuiltinEffects.CompressorId => CompressorParameters.Descriptors,
        BuiltinEffects.LimiterId => LimiterParameters.Descriptors,
        BuiltinEffects.GateId => GateParameters.Descriptors,
        BuiltinEffects.DelayId => DelayParameters.Descriptors,
        BuiltinEffects.IrLoaderId => IrLoaderParameters.Descriptors,
        _ => [],
    };
}
