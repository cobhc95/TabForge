using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Reverb;

// Owns: the reverb parameter list (ids and keys are saved in songs and presets: never change them) and the room build (from the Size knob,
//   on a worker thread), with the published convolvers the audio path reads and swaps on the audio thread.
// Does not own: the audio path (ReverbEffect.Process.cs), the room (ReverbIr), the editor window (ReverbEditor).
// Tests: BuiltinReverbTests.
// Threads: parameters and state on the engine main thread; Process on the audio thread.

/// <summary>Algorithmic Schroeder reverb (sophisticated synthesized room through a partitioned convolver, fine-grained control over the tail).</summary>
public sealed partial class ReverbEffect : BuiltinEffect
{
    public const uint BypassId = ReverbParameters.BypassId, SizeId = ReverbParameters.SizeId, PredelayId = ReverbParameters.PredelayId,
        MixId = ReverbParameters.MixId, HighCutId = ReverbParameters.HighCutId, LowCutId = ReverbParameters.LowCutId, WidthId = ReverbParameters.WidthId;
    public const int BypassIndex = 0, SizeIndex = 1, PredelayIndex = 2, MixIndex = 3, HighCutIndex = 4, LowCutIndex = 5, WidthIndex = 6;
    public const double MinSize = ReverbParameters.MinSize, MaxSize = ReverbParameters.MaxSize, MinPredelayMs = ReverbParameters.MinPredelayMs, MaxPredelayMs = ReverbParameters.MaxPredelayMs,
        MinHighCut = ReverbParameters.MinHighCut, MaxHighCut = ReverbParameters.MaxHighCut, MinLowCut = ReverbParameters.MinLowCut, MaxLowCut = ReverbParameters.MaxLowCut;

    public static IReadOnlyList<BuiltinParameter> Descriptors => ReverbParameters.Descriptors;

    private int _generation;
    private int _tailSamples;
    private sealed record BuildRequest(double Size, int Rate);

    public ReverbEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.ReverbId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseReverbDsp();
        SynthPublish(0, (int)Math.Round(SampleRate), Value(SizeIndex));   // the first room is built now, later size changes rebuild on a worker
    }

    protected override void OnReconfigured() { InitialiseReverbDsp(); Rebuild(); }

    /// <summary>Main thread: builds the room for the current Size and rate on a worker; an older build still running is discarded when it ends.</summary>
    private void Rebuild()
    {
        var generation = Interlocked.Increment(ref _generation);
        var request = new BuildRequest(Value(SizeIndex), (int)Math.Round(SampleRate));
        _ = Task.Run(() => Build(generation, request));
    }

    private void Build(int generation, BuildRequest request) => SynthPublish(generation, request.Rate, request.Size);

    private void SynthPublish(int generation, int rate, double size)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        var ir = ReverbIr.Build(size, rate);
        var left = new PartitionedConvolver(ir.Left);
        var right = new PartitionedConvolver(ir.Right);
        if (generation != Volatile.Read(ref _generation)) return;   // a newer request is on its way
        Volatile.Write(ref _convL, left);
        Volatile.Write(ref _convR, right);
        Volatile.Write(ref _tailSamples, ir.Left.Length);
    }

    private void Publish(int generation, PartitionedConvolver left, PartitionedConvolver right, int length)
    {
        if (generation != Volatile.Read(ref _generation)) return;   // a newer request is on its way
        Volatile.Write(ref _convL, left);
        Volatile.Write(ref _convR, right);
        Volatile.Write(ref _tailSamples, length);
    }

    protected override void OnChanged(BuiltinParameter parameter)
    {
        if (parameter.Id == SizeId) Rebuild();
    }
}
