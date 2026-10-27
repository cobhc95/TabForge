using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TabForge.Platform;

namespace TabForge.Services.Video;

// Owns: writing an H.264/AAC MP4 through an external ffmpeg: raw BGRA frames piped to its stdin, audio spooled to a raw file and muxed in at the end;
// the bounded frame queue, the dropped-frame count, repeating the last frame over gaps so picture and sound stay in step.
// Does not own: finding ffmpeg (FfmpegLocator), the command lines (FfmpegArguments), choosing an encoder (VideoEncoderFactory), capturing frames.
// Tests: VideoEncoderPortTests.
/// <summary>The portable <see cref="IVideoEncoder"/>. A worker thread owns the ffmpeg process, so callers may be on any thread.</summary>
public sealed class FfmpegVideoEncoder : IVideoEncoder
{
    public const int QueueFrames = 6;
    private static readonly TimeSpan FinishAllowance = TimeSpan.FromSeconds(120);   // the queued frames, the flush of the encoder and the mux

    private readonly string _exe, _path, _videoPath, _audioPath;
    private readonly int _width, _height, _fps;
    private readonly VideoAudioFormat? _audio;
    private readonly bool _live;
    private readonly BlockingCollection<Item> _queue = new();
    private readonly SemaphoreSlim _room = new(QueueFrames);
    private readonly Thread _worker;
    private readonly List<string> _notes = new();
    private readonly Queue<string> _stderr = new();
    private readonly ManualResetEventSlim _abandoned = new();
    private Process? _process;
    private FileStream? _audioFile;
    private Exception? _error;
    private long _dropped, _written, _beat;
    private bool _finished;
    private byte[]? _lastFrame;

    private readonly record struct Item(byte[]? Frame, long Index, float[]? Samples, int Count);

    private FfmpegVideoEncoder(string exe, string path, int width, int height, int fps, VideoAudioFormat? audio, bool live)
    {
        _exe = exe; _path = path; _width = width; _height = height; _fps = fps; _audio = audio; _live = live;
        _videoPath = audio is null ? path : path + ".video.part";
        _audioPath = path + ".audio.part";
        _worker = new Thread(Run) { IsBackground = true, Name = "FfmpegVideoEncoder" };
    }

    public long DroppedFrames => Interlocked.Read(ref _dropped);
    public bool UsesHardware { get; private set; }

    /// <summary>Changes whenever a frame or sound is handed to ffmpeg or its output file grows.</summary>
    public long Heartbeat => Interlocked.Read(ref _beat) * 1_000_003L + VideoEncoderRules.FileLength(_videoPath) + VideoEncoderRules.FileLength(_path);

    // Remembers the last time the heartbeat moved.
    private sealed class Stall
    {
        private readonly FfmpegVideoEncoder _owner;
        private long _last, _moved = Environment.TickCount64;
        public Stall(FfmpegVideoEncoder owner) { _owner = owner; _last = owner.Heartbeat; }
        public bool Stalled()
        {
            var now = Environment.TickCount64;
            var beat = _owner.Heartbeat;
            if (beat != _last) { _last = beat; _moved = now; }
            return now - _moved > FinishAllowance.TotalMilliseconds;
        }
    }

    public string EncoderName { get; private set; } = "ffmpeg";
    public IReadOnlyList<string> Notes { get { lock (_notes) return _notes.ToArray(); } }

    /// <summary>Opens the file and starts ffmpeg. Throws <see cref="VideoEncoderException"/> when the size, rate or audio is not usable or no H.264 encoder of this ffmpeg starts.</summary>
    public static FfmpegVideoEncoder Open(string ffmpegPath, string path, int width, int height, int fps, VideoAudioFormat? audioFormat = null, bool live = false, VideoEncoderOptions? options = null)
    {
        VideoEncoderRules.Validate(width, height, fps, audioFormat);
        var choice = options?.Encoder ?? VideoEncoderChoices.Auto;
        var candidates = FfmpegArguments.Candidates(FfmpegProbe.EncoderListing(ffmpegPath), choice, HostSystem.Current);
        if (candidates.Count == 0) throw new VideoEncoderException("This ffmpeg has no usable H.264 encoder. Install an ffmpeg build with libx264 or a hardware H.264 encoder.");
        var encoder = new FfmpegVideoEncoder(ffmpegPath, path, width, height, fps, audioFormat, live);
        var failure = "";
        foreach (var name in candidates)
        {
            if (encoder.TryStart(name, out failure)) break;
            encoder.Note($"{name} did not start: {failure}");
        }
        if (encoder._process is null) { encoder.Cleanup(); throw new VideoEncoderException("ffmpeg could not start an H.264 encoder: " + failure); }
        if (audioFormat is not null) encoder._audioFile = new FileStream(encoder._audioPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        encoder._worker.Start();
        return encoder;
    }

    private bool TryStart(string encoder, out string failure)
    {
        var info = new ProcessStartInfo(_exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
        foreach (var a in FfmpegArguments.Video(_width, _height, _fps, encoder, _videoPath)) info.ArgumentList.Add(a);
        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, e) => KeepLine(e.Data);
        lock (_stderr) _stderr.Clear();
        try { process.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { failure = e.Message; process.Dispose(); return false; }
        process.BeginErrorReadLine();
        if (process.WaitForExit(300)) { process.WaitForExit(); failure = StderrTail(); process.Dispose(); return false; }   // a missing GPU or a bad option stops it at once
        _process = process;
        UsesHardware = FfmpegArguments.HardwareEncoders(HostSystem.Current).Contains(encoder);
        EncoderName = $"ffmpeg {encoder} ({(UsesHardware ? "hardware" : "software")})";
        Note("encoder: " + EncoderName);
        failure = "";
        return true;
    }

    public bool WriteFrame(ReadOnlySpan<byte> bgra, TimeSpan timestamp)
    {
        ThrowIfFaulted();
        var size = _width * _height * 4;
        if (bgra.Length < size) throw new ArgumentException("The frame is smaller than width x height x 4 bytes.", nameof(bgra));
        if (_live) { if (!_room.Wait(0)) { Interlocked.Increment(ref _dropped); return false; } }
        else while (!_room.Wait(50)) ThrowIfFaulted();
        var copy = ArrayPool<byte>.Shared.Rent(size);
        bgra[..size].CopyTo(copy);
        var index = (timestamp.Ticks * _fps + 5_000_000) / 10_000_000;
        if (!TryQueue(new Item(copy, index, null, size))) { ArrayPool<byte>.Shared.Return(copy); _room.Release(); throw ClosedError(); }
        return true;
    }

    /// <summary>Offline mode: true once a frame slot is free (the caller is not blocked while waiting); false after the timeout.</summary>
    public async ValueTask<bool> WaitForRoomAsync(TimeSpan timeout, CancellationToken cancel)
    {
        ThrowIfFaulted();
        if (_live) return true;
        if (!await _room.WaitAsync(timeout, cancel).ConfigureAwait(false)) { ThrowIfFaulted(); return false; }
        _room.Release();   // only looked: the one producer takes it in WriteFrame
        return true;
    }

    public void WriteAudio(ReadOnlySpan<float> samples)
    {
        ThrowIfFaulted();
        if (_audio is null || samples.IsEmpty) return;
        var copy = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.CopyTo(copy);
        if (!TryQueue(new Item(null, 0, copy, samples.Length))) { ArrayPool<float>.Shared.Return(copy); throw ClosedError(); }
    }

    private bool TryQueue(Item item)
    {
        try { return _queue.TryAdd(item); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { return false; }
    }

    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        _queue.CompleteAdding();
        var watch = new Stall(this);   // FinishAllowance is how long ffmpeg may show no progress, not how long finishing may take
        while (!_worker.Join(200))
        {
            if (_abandoned.IsSet || watch.Stalled())
            {
                Abort($"ffmpeg made no progress for {FinishAllowance.TotalSeconds:0} s");
                throw ClosedError();
            }
        }
        _queue.Dispose();
        ThrowIfFaulted();
    }

    public void Abort(string why)
    {
        _error ??= new VideoEncoderException("The video encoder stopped responding while saving, so the recording was lost (" + why + ").") { KeptFile = _videoPath };
        Note($"abandoned: {why}; the unfinished file {_path} is kept");
        _abandoned.Set();
        try { _process?.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { Note("kill: " + e.Message); }
        try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { } // swallow: already finished
    }

    public void Dispose() { try { Finish(); } catch (VideoEncoderException) { } } // swallow: finishing an already-finished encoder is a no-op

    private void Run()
    {
        try
        {
            var stdin = _process!.StandardInput.BaseStream;
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                if (item.Frame is { } frame) { try { PutFrame(stdin, frame, item.Index, item.Count); } finally { _room.Release(); } }
                else if (item.Samples is { } samples)
                {
                    try { _audioFile!.Write(MemoryMarshal.AsBytes(samples.AsSpan(0, item.Count))); }
                    finally { ArrayPool<float>.Shared.Return(samples); }
                }
                Interlocked.Increment(ref _beat);
            }
            stdin.Close();
            Await(_process, "encoding");
            _audioFile?.Dispose();
            if (_audio is { } audio) Mux(audio);
            Cleanup();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _error ??= e as VideoEncoderException ?? new VideoEncoderException("ffmpeg stopped while writing the video" + TailText() + ": " + e.Message, e);
            Note("encoder failed: " + e.Message);
            try { _process?.Kill(true); } catch (Exception k) when (k is InvalidOperationException or System.ComponentModel.Win32Exception) { Note("kill: " + k.Message); }
            try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { } // swallow: already disposed
            Drain();
            if (!_abandoned.IsSet) Cleanup();
        }
        finally { _audioFile?.Dispose(); _process?.Dispose(); }
    }

    // Repeats the last frame over a gap (a dropped live frame) so the video keeps its length; a frame that arrives for a slot already filled is skipped.
    private void PutFrame(Stream stdin, byte[] frame, long index, int size)
    {
        try
        {
            if (index < _written) return;
            if (_written == 0) _written = index;   // the first frame sets the start; no padding in front of it
            var gap = Math.Min(index - _written, _fps * 5L);
            var filler = _lastFrame ?? frame;
            for (var i = 0L; i < gap; i++) stdin.Write(filler, 0, size);
            stdin.Write(frame, 0, size);
            _written = index + 1;
            if (_lastFrame is null || _lastFrame.Length < size) _lastFrame = new byte[size];
            Buffer.BlockCopy(frame, 0, _lastFrame, 0, size);
        }
        finally { ArrayPool<byte>.Shared.Return(frame); }
    }

    private void Drain()
    {
        while (_queue.TryTake(out var item))
        {
            if (item.Frame is { } f) { ArrayPool<byte>.Shared.Return(f); try { _room.Release(); } catch (SemaphoreFullException) { } } // swallow: the room is already full
            if (item.Samples is { } s) ArrayPool<float>.Shared.Return(s);
        }
    }

    private void Mux(VideoAudioFormat audio)
    {
        var info = new ProcessStartInfo(_exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in FfmpegArguments.Mux(_videoPath, _audioPath, audio, _path)) info.ArgumentList.Add(a);
        var mux = new Process { StartInfo = info };
        lock (_stderr) _stderr.Clear();
        mux.ErrorDataReceived += (_, e) => KeepLine(e.Data);
        mux.Start();
        mux.BeginErrorReadLine();
        _process = mux;
        Await(mux, "adding the sound");
        Note("muxed the audio");
    }

    private void Await(Process process, string what)
    {
        var watch = new Stall(this);
        while (!process.WaitForExit(500))
        {
            if (_abandoned.IsSet || watch.Stalled()) { process.Kill(true); throw new VideoEncoderException($"ffmpeg made no progress {what} for {FinishAllowance.TotalSeconds:0} s.") { KeptFile = _videoPath }; }
        }
        process.WaitForExit();   // flushes the redirected stderr
        if (process.ExitCode != 0) throw new VideoEncoderException($"ffmpeg failed while {what} (exit {process.ExitCode}){TailText()}");
    }

    private void Cleanup()
    {
        foreach (var part in new[] { _audioPath, _videoPath == _path ? "" : _videoPath })
        {
            try { if (part.Length > 0 && File.Exists(part)) File.Delete(part); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Note($"could not delete {part}: {e.Message}"); }
        }
    }

    private void KeepLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (_stderr) { _stderr.Enqueue(line); while (_stderr.Count > 12) _stderr.Dequeue(); }
    }

    private string StderrTail() { lock (_stderr) return string.Join(" | ", _stderr); }
    private string TailText() => StderrTail() is { Length: > 0 } t ? ": " + t : "";
    private void Note(string text) { lock (_notes) _notes.Add(text); }
    private void ThrowIfFaulted() { if (_error is { } e) throw e as VideoEncoderException ?? new VideoEncoderException(e.Message, e); }
    private Exception ClosedError() => _error is { } e ? e as VideoEncoderException ?? new VideoEncoderException(e.Message, e) : new VideoEncoderException("The video encoder is closed.");
}
