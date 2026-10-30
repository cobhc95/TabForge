using System.Diagnostics;
using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Services;

namespace TabForge.Services.Video;

// Owns: one live recording: the encoder (opened when the first audio chunk tells the sample rate), the audio clock built from the master
// tap's chunks, the capture thread that grabs a frame per tick and stamps it from that clock, and the gap-filling of lost audio.
// Does not own: where pixels come from (the grabber is passed in), the engine tap, settings, UI.
// Tests: LiveVideoRecordTests.
/// <summary>
/// Audio frame 0 is time 0. A video frame is stamped with the audio frames received so far plus the time since the last chunk (capped), so
/// picture and sound stay locked to the sound card's sample clock whatever the timer does. No frame is taken before the encoder is open.
/// </summary>
public sealed class LiveVideoSession : IDisposable
{
    /// <summary>Largest time the clock runs ahead of the last chunk by wall time (a chunk is about 5 ms; a stalled pipe must not stretch time).</summary>
    private const double MaxInterpolation = 0.05;

    /// <summary>Longest Finish waits for the capture thread or for the encoder to open.</summary>
    private const int StageTimeout = 5000;

    /// <summary>Longest Finish waits for an encoder that is still opening (the first-run hardware self-test runs inside it).</summary>
    private const int OpenTimeout = 12000;

    private readonly string _path;
    private readonly string _staging;
    private readonly int _width, _height, _fps;
    private readonly Func<byte[], bool> _grab;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly List<float[]> _early = new();   // audio that arrived while the encoder was opening
    private readonly VideoEncoderOptions? _options;
    private IVideoEncoder? _encoder;                 // set under _gate once open (Volatile.Write); the capture thread reads it with Volatile.Read
    private int _rate;                               // the first chunk's rate
    private Task? _opening;
    private long _audioEnd;                          // frames received (gaps included)
    private long _arrivedTicks;                      // Stopwatch ticks of the last chunk
    private long _lastStampTicks;
    private long _lastFrameTicks;                    // the stamp of the last frame the encoder took (capture thread; read after it ends)
    private readonly object _commit = new();        // commit and abort exclude each other: after an abort nothing is moved
    private bool _aborted;                           // under _commit
    private volatile bool _stop, _closed, _giveUpOpen;   // _giveUpOpen: nobody waits for the opening encoder any more
    private volatile string? _error;
    private long _frames;
    private volatile bool _fellBack;
    private volatile bool _allBlack;                 // the first BlackCheckFrames grabbed frames were all black
    private int _checked, _lit;                      // capture thread: frames sampled so far / frames with any visible pixel

    /// <summary>Frames sampled at the start of a recording to notice a picture that is black throughout.</summary>
    private const int BlackCheckFrames = 90;

    private LiveVideoSession(string path, string staging, int width, int height, int fps, Func<byte[], bool> grab, VideoEncoderOptions? options)
    {
        _path = path; _staging = staging; _width = width; _height = height; _fps = fps; _grab = grab; _options = options;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "TabForge video capture", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>
    /// Starts waiting for audio: the MP4 opens when the first chunk tells the sample rate (44.1 or 48 kHz) and capturing starts then.
    /// <paramref name="grab"/> fills a top-down BGRA buffer (width x height x 4) and returns false for "no picture this tick".
    /// </summary>
    public static LiveVideoSession Start(string path, int width, int height, int fps, Func<byte[], bool> grab, VideoEncoderOptions? options = null)
    {
        var session = new LiveVideoSession(path, StagingPath(path, Guid.NewGuid().ToString("N")[..8]), width, height, fps, grab, options);
        NativeTimer.Acquire();
        session._thread.Start();
        return session;
    }

    /// <summary>
    /// The staging file the encoder writes (".&lt;name&gt;.tfvideo-&lt;job&gt;.tmp.mp4" beside the final MP4, the RenderStaging
    /// pattern but not its path): the final name only ever appears with a finished MP4 (see <see cref="Finish"/>).
    /// </summary>
    internal static string StagingPath(string finalPath, string jobId)
    {
        var dir = Path.GetDirectoryName(finalPath) ?? "";
        return Path.Combine(dir, "." + Path.GetFileNameWithoutExtension(finalPath) + ".tfvideo-" + jobId + ".tmp" + Path.GetExtension(finalPath));
    }

    /// <summary>Time since audio frame 0 (zero until audio flows).</summary>
    public TimeSpan Elapsed => _rate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Volatile.Read(ref _audioEnd) / (double)_rate);

    public long DroppedFrames => _encoder?.DroppedFrames ?? 0;
    public long FramesWritten => Interlocked.Read(ref _frames);
    public string? Error => _error;
    /// <summary>True once the capture could not keep up at 60 fps and switched to 30 fps for the rest of this recording.</summary>
    public bool FellBackTo30 => _fellBack;
    /// <summary>True when every one of the first frames grabbed was black (sampled): the output would play as a black picture.</summary>
    public bool AllBlack => _allBlack;

    /// <summary>True when a handful of pixels spread over the frame show any colour (cheap: 256 samples).</summary>
    internal static bool HasPicture(byte[] bgra)
    {
        var pixels = bgra.Length / 4;
        if (pixels == 0) return true;
        var step = Math.Max(1, pixels / 256);
        for (var p = 0; p < pixels; p += step)
            if ((bgra[p * 4] | bgra[p * 4 + 1] | bgra[p * 4 + 2]) > 8) return true;
        return false;
    }

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
            var options = _options;
            if (options is not null)   // first recording on this PC: one self-test, remembered (the rule export shares)
                options = VideoEncoderFactory.WithHardwareProbe(options, StagingPath(_path, "probe" + Guid.NewGuid().ToString("N")[..8]), _width, _height, _fps);
            var encoder = VideoEncoderFactory.Open(_staging, _width, _height, _fps, new VideoAudioFormat(_rate, 2), live: true, options: options);
            var orphan = false;
            lock (_gate)
            {
                orphan = _giveUpOpen;   // the recording was stopped or abandoned while this opened: nobody will finish it
                if (!orphan)
                {
                foreach (var chunk in _early) encoder.WriteAudio(chunk);
                _early.Clear();
                Volatile.Write(ref _encoder, encoder);
                }
            }
            if (orphan)
            {
                Trace.Error(Trace.Ui, "video: encoder opened after the recording was abandoned; closing it");
                encoder.Dispose();
                lock (_commit) { if (!_aborted) TryDelete(_staging); }   // an abandoned save keeps its file; a stop that gave up waiting has nothing worth keeping
            }
        }
        catch (VideoEncoderException ex) { _error ??= ex.Message; Trace.Error(Trace.Ui, "video: encoder open failed: " + ex.Message); TryDelete(_staging); }
    }

    /// <summary>The audio-clock time for a frame taken now, or false while the encoder or the audio is not ready.</summary>
    public bool TryStamp(out TimeSpan time)
    {
        time = default;
        if (Volatile.Read(ref _encoder) is null) return false;
        var end = Volatile.Read(ref _audioEnd);
        if (end == 0) return false;
        var ahead = Math.Min((Stopwatch.GetTimestamp() - Volatile.Read(ref _arrivedTicks)) / (double)Stopwatch.Frequency, MaxInterpolation);
        var ticks = (long)((end / (double)_rate + ahead) * TimeSpan.TicksPerSecond);
        if (ticks <= _lastStampTicks) ticks = _lastStampTicks + 1;   // strictly increasing
        _lastStampTicks = ticks;
        time = TimeSpan.FromTicks(ticks);
        return true;
    }

    // Constant frame rate: frame slot n = floor(audio ms * fps / 1000), stamped n / fps. A skipped slot repeats the previous frame; a second
    // frame for a slot already taken is not grabbed at all. Two buffers swap, so nothing is allocated per frame.
    private void CaptureLoop()
    {
        var current = new byte[_width * _height * 4];
        var previous = new byte[current.Length];
        var slots = new VideoFrameSlots(_fps);
        var monitor = _fps >= 60 ? new CaptureRateMonitor(_fps) : null;
        var clock = Stopwatch.StartNew();
        var finalPass = false;   // one more pass after the stop, so the last slots up to the audio end are not lost to a slow grab
        while (_error is null)
        {
            if (_stop) { if (finalPass) break; finalPass = true; }
            try
            {
                if (monitor is not null && !_fellBack && monitor.Tick(clock.ElapsedMilliseconds)) { _fellBack = true; slots.Stride = 2; }
                if (!TryStamp(out var time) || !slots.TryNext(time.TotalMilliseconds, out var slot, out var firstFill) || !_grab(current)) { if (finalPass) break; Thread.Sleep(1); continue; }
                if (_checked < BlackCheckFrames)
                {
                    _checked++;
                    if (HasPicture(current)) _lit++;
                    if (_checked == BlackCheckFrames && _lit == 0)
                    {
                        _allBlack = true;
                        Trace.Error(Trace.Ui, $"video: the first {BlackCheckFrames} captured frames are all black ({_width}x{_height}); the file will play black");
                    }
                }
                var wrote = false;
                for (var fill = firstFill; fill <= slot; fill++)   // the first frame fills from slot 0, so picture and sound start together
                {
                    var frame = fill < slot && slots.HasFrame ? previous : current;
                    var stamp = TimeSpan.FromTicks(fill * TimeSpan.TicksPerSecond / _fps);
                    if (!Volatile.Read(ref _encoder)!.WriteFrame(frame, stamp)) continue;   // a refused frame (queue full) is a dropped frame, not a written one
                    Interlocked.Increment(ref _frames);
                    _lastFrameTicks = stamp.Ticks;
                    wrote = true;
                }
                slots.Commit(slot);
                if (wrote) monitor?.OnFrame();
                (current, previous) = (previous, current);
            }
            catch (Exception ex) { _error ??= ex.Message; }   // any grab or encoder failure ends the run: the thread must not die silently
        }
        lock (_loopGate) { _loopExited = true; if (_finishDeferred) FinishEncoder(); }
    }

    private readonly object _loopGate = new();
    private bool _loopExited, _finishDeferred;

    // The encoder is finished only once the capture loop can no longer be inside WriteFrame; after a join timeout the loop does it itself on exit.
    private void FinishEncoder()
    {
        try { _encoder?.Finish(); }
        catch (VideoEncoderException ex) { _error ??= ex.Message; Trace.Error(Trace.Ui, "video: finish: encoder failed: " + ex.Message); }
    }

    /// <summary>Stops capturing and finalises the MP4: the staging file is moved to its final name only on success. Throws <see cref="VideoEncoderException"/> when encoding failed or no audio ever arrived.</summary>
    public void Finish()
    {
        if (_closed) return;
        _stop = true;
        var clock = Stopwatch.StartNew();
        if (!_thread.Join(StageTimeout)) { _error ??= "The capture did not stop in time."; Trace.Error(Trace.Ui, $"video: finish: capture thread still running after {clock.ElapsedMilliseconds} ms"); }
        else Trace.Error(Trace.Ui, $"video: finish: capture stopped in {clock.ElapsedMilliseconds} ms, {_frames} frames");
        _closed = true;
        NativeTimer.Release();
        if (_opening is { } opening && !opening.Wait(OpenTimeout)) { _giveUpOpen = true; _error ??= "The encoder did not open in time."; Trace.Error(Trace.Ui, "video: finish: encoder still opening"); }
        // Pre-finish probe: the folder is verified before the file is finalised, with the probe's detail when it is gone.
        var folder = Path.GetDirectoryName(_path) ?? "";
        if (folder.Length > 0 && !FilePathPolicy.TryProbeWritableFolder(folder, out var probeFailure) && probeFailure is not null)
            _error ??= "The video folder could not be verified before saving: " + probeFailure.Message;
        PadAudioToLastFrame();
        try
        {
            clock.Restart();
            bool deferred;
            lock (_loopGate) { deferred = !_loopExited; if (deferred) _finishDeferred = true; }
            if (deferred) { _error ??= "The capture did not stop in time."; Trace.Error(Trace.Ui, "video: finish: encoder finish deferred to the capture loop"); }
            else _encoder?.Finish();
            Trace.Error(Trace.Ui, $"video: finish: encoder finished in {clock.ElapsedMilliseconds} ms");
            if (_error is null && _encoder is not null) CommitStaging();
            Trace.Error(Trace.Ui, $"video: finish: file in place at {_path}");
        }
        catch (VideoEncoderException ex) { _error ??= ex.Message; Trace.Error(Trace.Ui, "video: finish: encoder failed: " + ex.Message); }
        if (_error is not null && !Volatile.Read(ref _aborted)) TryDelete(_staging);   // an abandoned recording's file is kept for the user
        if (_error is { } e) throw new VideoEncoderException(e);
        if (_encoder is null) throw new VideoEncoderException("No audio reached the recorder, so no video was saved.");
    }

    /// <summary>Gives up on a finish that does not return (any thread): the encoder is abandoned and deletes its partial file when it lets go.</summary>
    public void Abort(string why)
    {
        lock (_commit) _aborted = true;
        _giveUpOpen = true;
        _error ??= why;
        _encoder?.Abort(why);
        Trace.Error(Trace.Ui, $"video: abandoned ({why}); the unfinished file is kept at {_staging}");
    }

    /// <summary>The file the encoder writes until the recording is finished (kept when saving is abandoned).</summary>
    public string StagingFile => _staging;

    /// <summary>Publishes the finished staging file at its final name (never overwriting; the next free "name (n).mp4" wins a race).</summary>
    private void CommitStaging()
    {
        var target = _path;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                lock (_commit)
                {
                    if (_aborted) throw new VideoEncoderException("Saving was abandoned before the file could be put in place.");
                    File.Move(_staging, target, overwrite: false);
                }
                return;
            }
            catch (IOException) when (File.Exists(target)) { target = NextFreePath(target); continue; }   // lost the race: the next free name
        }
        throw new VideoEncoderException($"The video folder has no free file name for {Path.GetFileName(_path)}.");
    }

    private static string NextFreePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; n < 1000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{stem}-{Guid.NewGuid():N}{ext}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); Trace.Error(Trace.Ui, "video: deleted leftover " + path); } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Ui, $"video: could not delete leftover {path}: {ex.Message}"); }
    }

    // The frames taken after the last chunk (the tap goes off before the capture stops) run past the sound: silence fills the sound up to the
    // end of the last frame, so picture and sound end together.
    private void PadAudioToLastFrame()
    {
        if (_encoder is not { } encoder || _error is not null || _lastFrameTicks == 0) return;
        var videoEnd = (long)Math.Ceiling((_lastFrameTicks + TimeSpan.TicksPerSecond / (double)_fps) * _rate / TimeSpan.TicksPerSecond);
        lock (_gate)
        {
            if (videoEnd <= _audioEnd) return;
            try { encoder.WriteAudio(new float[(videoEnd - _audioEnd) * 2]); _audioEnd = videoEnd; }
            catch (VideoEncoderException ex) { _error ??= ex.Message; }
        }
    }

    public void Dispose() => VideoDisposeHelper.TryFinish(Finish);
}
