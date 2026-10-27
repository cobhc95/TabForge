using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAudio.Wave;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Output;

// Owns: one open miniaudio playback stream as an IWavePlayer: the real-time callback that pulls the mix, start/stop, and the lost-device / reset signals.
// Does not own: choosing the device (MiniaudioBackend), the mix, or reopening after a loss (EngineSession.Device).
// Threads: the callbacks run on miniaudio's audio thread (no allocation, no lock in the data path); a fault or reset is raised from the thread pool so the
//   engine reacts on its main thread through OutputLossWatch.
// Tests: PortableBackendTests (on miniaudio's null device).

/// <summary>A playback stream on the miniaudio backend. Pulls <see cref="ISampleProvider"/> blocks in the device callback, stereo f32 at the mix rate.</summary>
public sealed unsafe class MiniaudioOut : IWavePlayer, IResetRequestingOutput
{
    private readonly TfPortable _lib;
    private readonly ISampleProvider _source;
    private readonly EngineThreads? _threads;
    private readonly float[] _scratch;
    private readonly int _channels;
    private GCHandle _self;
    private nint _stream;
    private volatile PlaybackState _state = PlaybackState.Stopped;
    private int _faulted, _closing;
    private bool _raised;

    /// <summary>Opens (does not start) the playback stream. <paramref name="deviceName"/> empty: the default device. Throws when it cannot be opened.</summary>
    internal MiniaudioOut(TfPortable lib, nint context, string deviceName, ISampleProvider source, int periodFrames, EngineThreads? threads)
    {
        _lib = lib; _source = source; _threads = threads;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _scratch = new float[Math.Max(8192, periodFrames * 4) * _channels];
        _self = GCHandle.Alloc(this);
        _stream = lib.AudioStreamOpen(context, false, deviceName, _channels, source.WaveFormat.SampleRate, periodFrames, &OnData, &OnEvent, GCHandle.ToIntPtr(_self),
            out var channels, out var rate, out var latencyFrames, out var error);
        if (_stream == 0)
        {
            _self.Free();
            throw new InvalidOperationException($"The audio output could not be opened (miniaudio error {error}).");
        }
        if (channels != _channels) { Dispose(); throw new InvalidOperationException($"The audio output opened with {channels} channels instead of {_channels}."); }
        ActualRate = rate;
        LatencyFrames = latencyFrames;
    }

    /// <summary>The rate the device runs at (the mix rate; miniaudio converts when the hardware differs).</summary>
    public int ActualRate { get; }

    /// <summary>The device buffer in frames (period times periods), for the latency report.</summary>
    public int LatencyFrames { get; }

    public PlaybackState PlaybackState => _state;
    public float Volume { get; set; } = 1f;
    public WaveFormat OutputWaveFormat => _source.WaveFormat;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    public event EventHandler? ResetRequested;

    public void Init(IWaveProvider waveProvider) { }   // the source is given at construction

    public void Play()
    {
        if (_state == PlaybackState.Playing || _stream == 0) return;
        Volatile.Write(ref _closing, 0);
        _state = PlaybackState.Playing;
        if (_lib.AudioStreamStart(_stream) != 0) { _state = PlaybackState.Stopped; throw new InvalidOperationException("The audio output could not be started."); }
    }

    public void Pause()
    {
        if (_state != PlaybackState.Playing) return;
        Volatile.Write(ref _closing, 1);
        _state = PlaybackState.Paused;
        if (_stream != 0) _ = _lib.AudioStreamStop(_stream);
    }

    public void Stop()
    {
        if (_state == PlaybackState.Stopped) return;
        Volatile.Write(ref _closing, 1);
        _state = PlaybackState.Stopped;
        if (_stream != 0) _ = _lib.AudioStreamStop(_stream);   // returns once no callback runs
        PlaybackStopped?.Invoke(this, new StoppedEventArgs());
    }

    public void Dispose()
    {
        var stream = _stream;
        if (stream == 0) return;
        Volatile.Write(ref _closing, 1);
        _state = PlaybackState.Stopped;
        _stream = 0;
        _lib.AudioStreamClose(stream);   // waits for a running callback
        if (_self.IsAllocated) _self.Free();
    }

    /// <summary>Audio thread: fills one device block from the mix. A failure plays silence and reports the loss once.</summary>
    private void Fill(float* output, int frames)
    {
        var total = frames * _channels;
        try
        {
            if (_state != PlaybackState.Playing) { new Span<float>(output, total).Clear(); return; }
            if (!_raised) { _raised = true; _threads?.RaiseRenderPriority(); }
            var done = 0;
            while (done < total)
            {
                var n = Math.Min(_scratch.Length, total - done);
                var got = _source.Read(_scratch, 0, n);
                var target = new Span<float>(output + done, n);
                _scratch.AsSpan(0, Math.Clamp(got, 0, n)).CopyTo(target);
                if (got < n) target[Math.Max(0, got)..].Clear();
                done += n;
            }
        }
        catch (Exception ex)
        {
            new Span<float>(output, total).Clear();
            Fault(ex);
        }
    }

    private void Fault(Exception ex)
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0) return;
        ThreadPool.UnsafeQueueUserWorkItem(_ => PlaybackStopped?.Invoke(this, new StoppedEventArgs(ex)), null);
    }

    private void Notify(int reason)
    {
        if (Volatile.Read(ref _closing) != 0) return;
        switch (reason)
        {
            case 1: Fault(new InvalidOperationException("the audio device stopped")); break;
            case 2: ThreadPool.UnsafeQueueUserWorkItem(_ => ResetRequested?.Invoke(this, EventArgs.Empty), null); break;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnData(nint user, float* output, float* input, uint frames)
    {
        if (GCHandle.FromIntPtr(user).Target is MiniaudioOut self) self.Fill(output, (int)frames);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnEvent(nint user, int reason)
    {
        if (GCHandle.FromIntPtr(user).Target is MiniaudioOut self) self.Notify(reason);
    }
}
