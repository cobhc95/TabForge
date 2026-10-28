using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Delay;

// Owns: the delay parameter list (ids and keys are saved in songs and presets: never change them) and the value texts (division names, percent).
// Does not own: the audio path (DelayEffect.Process.cs), the note lengths (DelayDivision), the window.
// Tests: BuiltinDelayTests.
// Threads: parameters and state on the engine main thread; Process on the audio thread.

/// <summary>Stereo delay: time in milliseconds or synced to the song tempo, feedback, wet level, ping-pong, low cut and high cut on the repeats. The dry sound is untouched.</summary>
public sealed partial class DelayEffect : BuiltinEffect
{
    public const uint BypassId = 1, SyncId = 2, TimeId = 3, DivisionId = 4, FeedbackId = 5, MixId = 6, PingPongId = 7, LowCutId = 8, HighCutId = 9;
    public const int BypassIndex = 0, SyncIndex = 1, TimeIndex = 2, DivisionIndex = 3, FeedbackIndex = 4, MixIndex = 5, PingPongIndex = 6, LowCutIndex = 7, HighCutIndex = 8;
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

    public DelayEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.DelayId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseDsp();
    }

    protected override void OnReconfigured() => InitialiseDsp();

    protected override string TextOf(BuiltinParameter parameter, double value) => parameter.Id == DivisionId
        ? DelayDivision.Names[(int)Math.Clamp(value, 0, DelayDivision.Count - 1)]
        : base.TextOf(parameter, value);
}
