using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.IrLoader;

// Owns: the IR Loader's parameters and file keys (ids and keys are saved in songs and presets: never change them), and getting a new IR ready: the files
//   are read, resampled, trimmed, levelled and split into partitions on a worker, then handed to the audio thread in one reference swap (IrKernel).
// Does not own: the audio path (IrLoaderEffect.Process.cs), the maths (IrPrep, PartitionedConvolver), the editor (Views/Builtin in the app).
// Tests: BuiltinEffectTests.
// Threads: parameters, state and Rebuild on the engine's main thread; Build on a thread-pool worker; Process on the audio thread.

/// <summary>Convolution with an impulse response WAV: one file for both sides, or separate Left / Right files. Zero latency.</summary>
public sealed partial class IrLoaderEffect : BuiltinEffect
{
    public const uint MixId = IrLoaderParameters.MixId, GainId = IrLoaderParameters.GainId, AutoLevelId = IrLoaderParameters.AutoLevelId,
        TrimId = IrLoaderParameters.TrimId, SeparateId = IrLoaderParameters.SeparateId, BypassId = IrLoaderParameters.BypassId;
    public const uint LatencyId = IrLoaderParameters.LatencyId, CpuId = IrLoaderParameters.CpuId, LengthId = IrLoaderParameters.LengthId,
        AutoGainId = IrLoaderParameters.AutoGainId, StatusId = IrLoaderParameters.StatusId, FileId = IrLoaderParameters.FileId,
        LeftFileId = IrLoaderParameters.LeftFileId, RightFileId = IrLoaderParameters.RightFileId;
    public const string FileKey = IrLoaderParameters.FileKey, LeftKey = IrLoaderParameters.LeftKey, RightKey = IrLoaderParameters.RightKey;
    /// <summary>The value of <see cref="StatusId"/>: no file chosen, the IR is loaded, or the file is missing / unreadable (its text says which).</summary>
    public const int StatusNone = IrLoaderParameters.StatusNone, StatusLoaded = IrLoaderParameters.StatusLoaded, StatusProblem = IrLoaderParameters.StatusProblem;
    private const int Mix = 0, Gain = 1, AutoLevel = 2, Trim = 3, Separate = 4, Bypass = 5, Latency = 6, Cpu = 7, Length = 8, AutoGain = 9, Status = 10;
    private static readonly TimeSpan OfflineWait = TimeSpan.FromSeconds(10);

    public static IReadOnlyList<BuiltinParameter> Descriptors => IrLoaderParameters.Descriptors;

    private readonly object _cacheGate = new();
    private readonly Dictionary<string, (DateTime Written, IrAudio Audio)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private Task? _building;
    private volatile string _statusText = "No IR file chosen";

    public IrLoaderEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.IrLoaderId)!, Descriptors, [FileKey, LeftKey, RightKey], sampleRate, maxBlock) { }

    /// <summary>Main thread (tests, offline renders): the build started last, done or not.</summary>
    public Task Ready => _building ?? Task.CompletedTask;

    protected override void OnChanged(BuiltinParameter parameter)
    {
        if (parameter.Id is AutoLevelId or TrimId or SeparateId) Rebuild();
    }

    protected override void OnTextChanged(string key) => Rebuild();

    protected override void OnReconfigured() => Rebuild();

    /// <summary>A render waits for an IR still being prepared, so the file starts with it.</summary>
    public override void SetOfflineMode(bool offline)
    {
        if (offline) _building?.Wait(OfflineWait);
    }

    protected override string TextOf(BuiltinParameter parameter, double value) => parameter.Id switch
    {
        StatusId => _statusText,
        FileId => Text(FileKey),
        LeftFileId => Text(LeftKey),
        RightFileId => Text(RightKey),
        LatencyId => "0 samples (zero latency)",
        _ => base.TextOf(parameter, value),
    };

    private sealed record BuildRequest(string File, string Left, string Right, bool Separate, bool AutoLevel, bool Trim, int Rate);

    /// <summary>Main thread: prepares the IR for the current files and options on a worker; an older build still running is discarded when it ends.</summary>
    private void Rebuild()
    {
        var generation = Interlocked.Increment(ref _generation);
        var request = new BuildRequest(Text(FileKey), Text(LeftKey), Text(RightKey), On(Separate), On(AutoLevel), On(Trim), (int)Math.Round(SampleRate));
        _building = Task.Run(() => Build(generation, request));
    }

    private void Build(int generation, BuildRequest request)
    {
        var path = "";
        try
        {
            IrAudio? main = null, left = null, right = null;
            if (request.Separate) { path = request.Left; left = Load(request.Left); path = request.Right; right = Load(request.Right); }
            else { path = request.File; main = Load(request.File); }
            if (main is null && left is null && right is null) { Publish(generation, IrKernel.Empty, StatusNone, "No IR file chosen", 0); return; }
            var prepared = IrPrep.Prepare(main, left, right, request.Rate, request.AutoLevel, request.Trim);
            var kernel = new IrKernel(prepared);
            var name = request.Separate ? $"{Name(request.Left)} | {Name(request.Right)}" : Name(request.File);
            Publish(generation, kernel, StatusLoaded, $"{name} · {kernel.Length * 1000.0 / request.Rate:0} ms", prepared.AutoGainDb);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OutOfMemoryException)
        {
            var missing = ex is FileNotFoundException or DirectoryNotFoundException;
            var text = missing ? $"Missing: {path}" : $"{Name(path)}: {ex.Message}";
            Publish(generation, IrKernel.Empty, StatusProblem, text, 0);
            if (generation == Volatile.Read(ref _generation))
                Report(path, missing ? "the IR file was not found. The IR Loader passes the sound through unchanged; choose the file again in its window." : ex.Message);
        }
    }

    private void Publish(int generation, IrKernel kernel, int status, string text, double autoGainDb)
    {
        if (generation != Volatile.Read(ref _generation)) return;   // a newer request is on its way
        _statusText = text;
        SetReadout(Status, status);
        SetReadout(Length, kernel.Length * 1000.0 / Math.Max(1, SampleRate));
        SetReadout(AutoGain, autoGainDb);
        Volatile.Write(ref _tailSamples, kernel.Length);
        Volatile.Write(ref _pending, kernel);
    }

    /// <summary>Worker: the decoded file, from the cache while it is unchanged on disk (toggling Auto level does not read it again).</summary>
    private IrAudio? Load(string path)
    {
        if (path.Length == 0) return null;
        var written = File.GetLastWriteTimeUtc(path);
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(path, out var hit) && hit.Written == written) return hit.Audio;
        }
        var audio = IrWaveFile.Read(path);
        lock (_cacheGate)
        {
            if (_cache.Count >= 4) _cache.Clear();
            _cache[path] = (written, audio);
        }
        return audio;
    }

    private static string Name(string path) => System.IO.Path.GetFileName(path);

    public override int TailSamples => Volatile.Read(ref _tailSamples);

    public override void Dispose()
    {
        Interlocked.Increment(ref _generation);   // a build still running publishes nothing
        base.Dispose();
    }
}
