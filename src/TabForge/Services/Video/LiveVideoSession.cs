using System.Diagnostics;
using TabForge.Audio.Contracts;

namespace TabForge.Services.Video;

// Owns: one live recording: the encoder (opened when the first audio chunk tells the sample rate), the audio clock built from the master
// tap's chunks, the capture thread that grabs a frame per tick and stamps it from that clock, and the gap-filling of lost audio.
// Does not own: where pixels come from (the grabber is passed in), the engine tap, settings, UI.
// Tests: TestLiveVideoRecord.
/// <summary>
/// Audio frame 0 is time 0. A video frame is stamped with the audio frames received so far plus the time since the last chunk (capped), so
/// picture and sound stay locked to the sound card's sample clock whatever the timer does. No frame is taken before the encoder is open.
/// </summary>
public sealed class LiveVideoSession : IDisposable
{
    /// <summary>Largest time the clock runs ahead of the last chunk by wall time (a chunk is about 5 ms; a stalled pipe must not stretch time).</summary>
    private const double MaxInterpolation = 0.05;

    private readonly string _path;
    private readonly int _width, _height, _fps;
    private readonly Func<byte[], bool> _grab;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly List<float[]> _early = new();   // audio that arrived while the encoder was opening
    private readonly VideoEncoderOptions? _options;
    private VideoEncoder? _encoder;                  // set under _gate once open
    private int _rate;                               // the first chunk's rate
    private Task? _opening;
    private long _audioEnd;                          // frames received (gaps included)
    private long _arrivedTicks;                      // Stopwatch ticks of the last chunk
    private long _lastStampTicks;
    private volatile bool _stop, _closed;
    private volatile string? _error;
    private long _frames;

    private LiveVideoSession(string path, int width, int height, int fps, Func<byte[], bool> grab, VideoEncoderOptions? options)
    {
        _path = path; _width = width; _height = height; _fps = fps; _grab = grab; _options = options;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "TabForge video capture", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>
    /// Starts waiting for audio: the MP4 opens when the first chunk tells the sample rate (44.1 or 48 kHz) and capturing starts then.
    /// <paramref name="grab"/> fills a top-down BGRA buffer (width x height x 4) and returns false for "no picture this tick".
    /// </summary>
    public static LiveVideoSession Start(string path, int width, int height, int fps, Func<byte[], bool> grab, VideoEncoderOptions? options = null)
    {
        var session = new LiveVideoSession(path, width, height, fps, grab, options);
        NativeTimer.Acquire();
        session._thread.Start();
        return session;
    }

    /// <summary>Time since audio frame 0 (zero until audio flows).</summary>
    public TimeSpan Elapsed => _rate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Volatile.Read(ref _audioEnd) / (double)_rate);

    public long DroppedFrames => _encoder?.DroppedFrames ?? 0;
    public long FramesWritten => Interlocked.Read(ref _frames);
    public string? Error => _error;

    /// <summary>Any thread: one master tap chunk (interleaved stereo floats). A gap before it is filled with silence; a chunk at another rate is refused; the run must begin at frame 0.</summary>
    public void OnAudio(int rate, long firstFrame, float[] data, int frames)
    {
        if (_closed) return;
        try
        {
            lock (_gate)
            {
                if (_rate == 0)
                {
                    if (firstFrame != 0) return;   // a leftover of an earlier tap run
                    _rate = rate;
                    _opening = Task.Run(Open);
                }
                if (rate != _rate) { _error ??= "The audio sample rate changed while recording."; return; }
                var end = _audioEnd;
                if (firstFrame < end) return;   // a repeat: already taken, and the numbers of a restarted tap run (they begin again at 0)
                if (firstFrame > end) Emit(new float[(firstFrame - end) * 2]);
                Emit(frames * 2 == data.Length ? data : data.AsSpan(0, frames * 2).ToArray());
                _arrivedTicks = Stopwatch.GetTimestamp();
                _audioEnd = firstFrame + frames;
            }
        }
        catch (Exception ex) when (ex is VideoEncoderException or ObjectDisposedException) { _error ??= ex.Message; }
    }

    private void Emit(float[] samples)
    {
        if (_encoder is { } encoder) encoder.WriteAudio(samples);
        else _early.Add(samples);
    }

    private void Open()
    {
        try
        {
            var encoder = VideoEncoder.Open(_path, _width, _height, _fps, new VideoAudioFormat(_rate, 2), live: true, options: _options);
            lock (_gate)
            {
                foreach (var chunk in _early) encoder.WriteAudio(chunk);
                _early.Clear();
                _encoder = encoder;
            }
        }
        catch (VideoEncoderException ex) { _error ??= ex.Message; }
    }

    /// <summary>The audio-clock time for a frame taken now, or false while the encoder or the audio is not ready.</summary>
    public bool TryStamp(out TimeSpan time)
    {
        time = default;
        if (_encoder is null) return false;
        var end = Volatile.Read(ref _audioEnd);
        if (end == 0) return false;
        var ahead = Math.Min((Stopwatch.GetTimestamp() - Volatile.Read(ref _arrivedTicks)) / (double)Stopwatch.Frequency, MaxInterpolation);
        var ticks = (long)((end / (double)_rate + ahead) * TimeSpan.TicksPerSecond);
        if (ticks <= _lastStampTicks) ticks = _lastStampTicks + 1;   // strictly increasing
        _lastStampTicks = ticks;
        time = TimeSpan.FromTicks(ticks);
        return true;
    }

    private void CaptureLoop()
    {
        var buffer = new byte[_width * _height * 4];
        var interval = Stopwatch.Frequency / (double)_fps;
        var next = Stopwatch.GetTimestamp();
        while (!_stop && _error is null)
        {
            var wait = next - Stopwatch.GetTimestamp();
            if (wait > 0) { Thread.Sleep(wait > Stopwatch.Frequency / 500 ? 1 : 0); continue; }
            next += (long)interval;
            if (next < Stopwatch.GetTimestamp()) next = Stopwatch.GetTimestamp();   // behind: skip the missed ticks instead of bursting
            try
            {
                if (!TryStamp(out var time) || !_grab(buffer)) continue;
                _encoder!.WriteFrame(buffer, time);
                Interlocked.Increment(ref _frames);
            }
            catch (Exception ex) when (ex is VideoEncoderException or ObjectDisposedException) { _error ??= ex.Message; }
        }
    }

    /// <summary>Stops capturing and finalises the MP4. Throws <see cref="VideoEncoderException"/> when encoding failed or no audio ever arrived.</summary>
    public void Finish()
    {
        if (_closed) return;
        _stop = true;
        _thread.Join();
        _closed = true;
        NativeTimer.Release();
        _opening?.Wait();
        _encoder?.Finish();
        if (_error is { } e) throw new VideoEncoderException(e);
        if (_encoder is null) throw new VideoEncoderException("No audio reached the recorder, so no video was saved.");
    }

    public void Dispose() { try { Finish(); } catch (VideoEncoderException) { } }
}
