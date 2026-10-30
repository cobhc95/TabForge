using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Delay;

// Owns: the delay parameter list (ids and keys are saved in songs and presets: never change them) and the value texts (division names, percent).
// Does not own: the audio path (DelayEffect.Process.cs), the note lengths (DelayDivision), the window.
// Tests: BuiltinDelayTests.
// Threads: parameters and state on the engine main thread; Process on the audio thread.

/// <summary>Stereo delay: time in milliseconds or synced to the song tempo, feedback, wet level, ping-pong, low cut and high cut on the repeats. The dry sound is untouched.</summary>
public sealed partial class DelayEffect : BuiltinEffect
{
    public const uint BypassId = DelayParameters.BypassId, SyncId = DelayParameters.SyncId, TimeId = DelayParameters.TimeId,
        DivisionId = DelayParameters.DivisionId, FeedbackId = DelayParameters.FeedbackId, MixId = DelayParameters.MixId,
        PingPongId = DelayParameters.PingPongId, LowCutId = DelayParameters.LowCutId, HighCutId = DelayParameters.HighCutId;
    public const int BypassIndex = 0, SyncIndex = 1, TimeIndex = 2, DivisionIndex = 3, FeedbackIndex = 4, MixIndex = 5, PingPongIndex = 6, LowCutIndex = 7, HighCutIndex = 8;
    public const double MinTimeMs = DelayParameters.MinTimeMs, MaxTimeMs = DelayParameters.MaxTimeMs, MaxFeedback = DelayParameters.MaxFeedback,
        MinLowCut = DelayParameters.MinLowCut, MaxLowCut = DelayParameters.MaxLowCut, MinHighCut = DelayParameters.MinHighCut, MaxHighCut = DelayParameters.MaxHighCut;

    public static IReadOnlyList<BuiltinParameter> Descriptors => DelayParameters.Descriptors;

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
