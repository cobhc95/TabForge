using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Audio;

namespace TabForge.AudioEngine.Output;

// Owns: one open miniaudio capture stream: it hands every captured block to an InputCapture in feed mode (which resamples, rings and records).
// Does not own: choosing the device (MiniaudioBackend) or the ring and recorder (InputCapture).
// Threads: the data callback runs on miniaudio's capture thread and allocates nothing; device events are reported through InputCapture.ReportFault.
// Tests: PortableBackendTests (on miniaudio's null device).

/// <summary>A capture stream on the miniaudio backend, opened at the device's own channel count and rate.</summary>
public sealed unsafe class MiniaudioInput : IDisposable
{
    private readonly TfPortable _lib;
    private GCHandle _self;
    private nint _stream;
    private int _closing;
    private InputCapture? _sink;

    /// <summary>Opens (does not start) the capture stream. <paramref name="deviceName"/> empty: the default device. Throws when it cannot be opened.</summary>
    internal MiniaudioInput(TfPortable lib, nint context, string deviceName, int channels, int periodFrames)
    {
        _lib = lib;
        _self = GCHandle.Alloc(this);
        _stream = lib.AudioStreamOpen(context, true, deviceName, channels, 0, periodFrames, &OnData, &OnEvent, GCHandle.ToIntPtr(_self),
            out var actualChannels, out var rate, out var latencyFrames, out var error);
        if (_stream == 0)
        {
            _self.Free();
            throw new InvalidOperationException($"The audio input could not be opened (miniaudio error {error}).");
        }
        Channels = Math.Max(1, actualChannels);
        SampleRate = rate;
        LatencyFrames = latencyFrames;
    }

    public int Channels { get; }
    public int SampleRate { get; }
    public int LatencyFrames { get; }

    /// <summary>Where the captured blocks go; set before <see cref="Start"/>.</summary>
    internal void Attach(InputCapture sink) => Volatile.Write(ref _sink, sink);

    public void Start()
    {
        if (_stream != 0 && _lib.AudioStreamStart(_stream) != 0) throw new InvalidOperationException("The audio input could not be started.");
    }

    public void Dispose()
    {
        var stream = _stream;
        if (stream == 0) return;
        Volatile.Write(ref _closing, 1);
        _stream = 0;
        _ = _lib.AudioStreamStop(stream);
        _lib.AudioStreamClose(stream);
        Volatile.Write(ref _sink, null);
        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnData(nint user, float* output, float* input, uint frames)
    {
        if (GCHandle.FromIntPtr(user).Target is MiniaudioInput self && Volatile.Read(ref self._sink) is { } sink && input != null)
            sink.Feed(new ReadOnlySpan<float>(input, (int)frames * self.Channels));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnEvent(nint user, int reason)
    {
        if (reason == 1 && GCHandle.FromIntPtr(user).Target is MiniaudioInput self && Volatile.Read(ref self._closing) == 0)
            Volatile.Read(ref self._sink)?.ReportFault("the audio input device stopped");
    }
}
