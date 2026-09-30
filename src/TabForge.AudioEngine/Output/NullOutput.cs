using System.Diagnostics;
using NAudio.Wave;

namespace TabForge.AudioEngine.Output;

/// <summary>
/// The headless "device" (driver <see cref="AudioOutputFactory.Null"/>): no sound card, the mix is read and discarded.
/// Clocked: its own thread calls the source at the block cadence (block frames / sample rate) while playing, like a
/// real driver callback. Manual (device name <see cref="AudioOutputFactory.NullManual"/>): no thread; a test calls
/// <see cref="Pump"/> to run exactly the blocks it wants, in process. Either way the callback buffer is allocated once.
/// </summary>
public sealed class NullOutput : IWavePlayer
{
    private readonly ISampleProvider _source;
    private readonly float[] _buffer;
    private readonly bool _manual;
    private readonly object _pumpGate = new();
    private Thread? _thread;
    private volatile PlaybackState _state = PlaybackState.Stopped;
    private long _blocks;

    public NullOutput(ISampleProvider source, int blockFrames, bool manual)
    {
        _source = source;
        BlockFrames = Math.Clamp(blockFrames, 16, 8192);
        _buffer = new float[BlockFrames * source.WaveFormat.Channels];
        _manual = manual;
    }

    public int BlockFrames { get; }
    /// <summary>Blocks read from the source so far.</summary>
    public long Blocks => Interlocked.Read(ref _blocks);
    public bool IsManual => _manual;

    public PlaybackState PlaybackState => _state;
    public float Volume { get; set; } = 1f;
    public WaveFormat OutputWaveFormat => _source.WaveFormat;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public void Init(IWaveProvider waveProvider) { }

    public void Play()
    {
        if (_state == PlaybackState.Playing) return;
        _state = PlaybackState.Playing;
        if (_manual || _thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "TabForge null output", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Pause() => _state = PlaybackState.Paused;

    public void Stop()
    {
        if (_state == PlaybackState.Stopped && _thread is null) return;
        _state = PlaybackState.Stopped;
        var thread = _thread;
        _thread = null;
        // Returns only once no callback runs (as a real driver's Stop does).
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(5000);
        lock (_pumpGate) { }
        PlaybackStopped?.Invoke(this, new StoppedEventArgs());
    }

    /// <summary>Manual mode (any thread, one at a time): reads <paramref name="blocks"/> blocks now; returns how many ran.</summary>
    public int Pump(int blocks = 1)
    {
        if (!_manual) throw new InvalidOperationException("A clocked null output pumps itself.");
        var done = 0;
        lock (_pumpGate)
            for (; done < blocks && _state == PlaybackState.Playing; done++) ReadBlock();
        return done;
    }

    private void ReadBlock()
    {
        _source.Read(_buffer, 0, _buffer.Length);
        Interlocked.Increment(ref _blocks);
    }

    private void Run()
    {
        var period = BlockFrames * (double)Stopwatch.Frequency / OutputWaveFormat.SampleRate;
        var next = (double)Stopwatch.GetTimestamp();
        while (_state != PlaybackState.Stopped)
        {
            if (_state == PlaybackState.Playing) lock (_pumpGate) ReadBlock();
            next += period;
            var now = Stopwatch.GetTimestamp();
            if (now > next + period * 4) next = now;   // fell far behind (debugger, starved machine): no burst to catch up
            while (_state != PlaybackState.Stopped)
            {
                var remainingMs = (next - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
                if (remainingMs <= 0) break;
                if (remainingMs > 2) Thread.Sleep(1); else Thread.Yield();
            }
        }
    }

    public void Dispose() => Stop();
}
