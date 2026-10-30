using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace TabForge.Services.Video;

// Owns: writing an MP4 (H.264 video, AAC audio) through Media Foundation's sink writer; the bounded frame queue and its
// fixed pool of native frame buffers (one per queue slot); the dropped-frame count; the bitrate rule. A worker thread owns
// every COM object, so callers may be on any thread; a caller only copies pixels into a buffer the worker has locked for it.
// Does not own: capturing frames or audio, file naming, settings, UI.
// Tests: TestVideoEncoderGolden.
/// <summary>
/// Live mode (<c>live: true</c>) never blocks the caller: a full queue drops the frame and counts it. Offline mode waits for room.
/// </summary>
public sealed partial class VideoEncoder : IVideoEncoder
{
    public const int QueueFrames = 6;

    private readonly BlockingCollection<Item> _queue = new();
    private readonly SemaphoreSlim _room = new(QueueFrames);
    private readonly Thread _worker;
    private readonly int _width, _height, _fps;
    private readonly VideoAudioFormat? _audio;
    private readonly bool _live;
    private readonly VideoEncoderOptions _options;
    private readonly List<string> _notes = new();
    private readonly string _path;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ManualResetEventSlim _abandoned = new();   // set when Finish gave up waiting: the worker deletes the partial file if it ever returns
    private volatile Exception? _error;
    private long _dropped, _audioFrames, _beat;
    private bool _finished;
    private int _firstWrite;                        // 1 once the first frame's caller thread was logged
    private readonly object _gate = new();          // guards _free and _closed; COM calls are never made under it
    private readonly Stack<VideoSlot> _free = new(); // every buffer is in exactly one place: here, the queue, or the worker
    private bool _closed;
    private volatile Exception? _poolError;      // a failed buffer replacement; ends the pump

    private readonly record struct Item(VideoSlot? Slot, long Time, float[]? Audio, int Count);

    /// <summary>A native frame buffer, locked by the worker except while it is queued or being written. Pointer is valid while locked.</summary>
    private sealed class VideoSlot
    {
        public IMFMediaBuffer Buffer = null!;
        public IntPtr Pointer;
        public int IdleRefs;   // the buffer's COM reference count while no sample holds it
    }

    private VideoEncoder(string path, int width, int height, int fps, VideoAudioFormat? audio, bool live, VideoEncoderOptions options)
    {
        _path = path; _width = width; _height = height; _fps = fps; _audio = audio; _live = live; _options = options;
        _worker = new Thread(Run) { IsBackground = true, Name = "VideoEncoder" };
    }

    /// <summary>Frames dropped because the queue was full (live mode).</summary>
    public long DroppedFrames => Interlocked.Read(ref _dropped);

    /// <summary>Changes whenever the worker writes a sample or the output file grows (the sink writer drains its backlog into it while finalising).</summary>
    public long Heartbeat => Interlocked.Read(ref _beat) * 1_000_003L + FileLength(_path);

    internal static long FileLength(string path) => VideoEncoderRules.FileLength(path);

    /// <summary>True when the video encoder in use is a hardware encoder.</summary>
    public bool UsesHardware { get; private set; }

    /// <summary>AAC rate: 192 kbps, the top of the Media Foundation AAC encoder's rates (96/128/160/192).</summary>
    private const int AacBytesPerSecond = 24000;

    /// <summary>Video bits per second for a size and frame rate (about 0.1 bit per pixel, capped at 100 Mbit/s).</summary>
    public static int VideoBitrate(int width, int height, int fps) => VideoEncoderRules.VideoBitrate(width, height, fps);

    /// <summary>True for the sample rates the MP4 audio stream takes (44.1 and 48 kHz).</summary>
    public static bool SupportsSampleRate(int rate) => VideoEncoderRules.SupportsSampleRate(rate);

    /// <summary>Opens the file and starts the encoder thread. Throws <see cref="VideoEncoderException"/> when the size, frame rate or encoder is not usable.</summary>
    public static VideoEncoder Open(string path, int width, int height, int fps, VideoAudioFormat? audioFormat = null, bool live = false, VideoEncoderOptions? options = null)
    {
        VideoEncoderRules.Validate(width, height, fps, audioFormat);
        var encoder = new VideoEncoder(path, width, height, fps, audioFormat, live, options ?? new VideoEncoderOptions());
        encoder._worker.Start();
        // The worker always signals _ready in its finally (Start throws before this when the thread cannot start), so this
        // cannot wait forever; export and recording flows call Open on a worker, never on the UI thread.
        encoder._ready.Wait();
        if (encoder._error is { } e) { encoder._worker.Join(); encoder._queue.Dispose(); throw e as VideoEncoderException ?? new VideoEncoderException(e.Message, e); }
        return encoder;
    }

    /// <summary>Queues one top-down BGRA frame (width x height x 4 bytes). Live mode returns false when the frame was dropped.</summary>
    public bool WriteFrame(ReadOnlySpan<byte> bgra, TimeSpan timestamp)
    {
        ThrowIfFaulted();
        if (Interlocked.Exchange(ref _firstWrite, 1) == 0) Note($"write: first frame from {ThreadText()}");
        var size = _width * _height * 4;
        if (bgra.Length != size) throw new ArgumentException("The frame is not exactly width x height x 4 bytes.", nameof(bgra));
        if (_live) { if (!_room.Wait(0)) { Interlocked.Increment(ref _dropped); return false; } }
        else while (!_room.Wait(50)) ThrowIfFaulted();
        var slot = TakeSlot();
        CopyInto(slot, bgra[..size]);
        if (!TryQueue(new Item(slot, timestamp.Ticks, null, size))) { ReturnSlot(slot); _room.Release(); throw ClosedError(); }
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

    /// <summary>Queues interleaved float samples (-1..1) in the format given to <see cref="Open"/>. Audio is never dropped.</summary>
    public void WriteAudio(ReadOnlySpan<float> samples)
    {
        ThrowIfFaulted();
        if (_audio is null || samples.IsEmpty) return;
        var copy = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.CopyTo(copy);
        if (!TryQueue(new Item(null, 0, copy, samples.Length))) { ArrayPool<float>.Shared.Return(copy); throw ClosedError(); }
    }

    // TryAdd throws, rather than returning false, once adding is complete or the queue is disposed.
    private bool TryQueue(Item item)
    {
        try { return _queue.TryAdd(item); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { return false; }
    }

    // The encoder's own failure when it has one, else a closed-encoder error.
    private Exception ClosedError() => _error is { } e ? e as VideoEncoderException ?? new VideoEncoderException(e.Message, e) : new VideoEncoderException("The video encoder is closed.");

    /// <summary>Writes what is queued, finalises the MP4 and releases the encoder. Throws when encoding failed.</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        Note($"finish: called on {ThreadText()}; {Counts()}");
        CompleteAdding();
        var started = Environment.TickCount64;
        // FinalizeTimeout is how long the encoder may show NO progress (frames written, the file growing), not how long finishing may take:
        // at 4K the sink writer holds a long backlog that is encoded only now.
        var last = Heartbeat;
        var moved = started;
        while (!_worker.Join(_abandoned.IsSet ? 0 : 200))
        {
            if (_abandoned.IsSet) throw ClosedError();   // given up from outside (a cancel, a watchdog): says nothing about the hardware encoder
            var now = Environment.TickCount64;
            var beat = Heartbeat;
            if (beat != last) { last = beat; moved = now; }
            if (now - moved > _options.FinalizeTimeout.TotalMilliseconds)
            {
                Abandon($"the encoder made no progress for {_options.FinalizeTimeout.TotalSeconds:0.#} s while finishing the file", hardwareHang: true);
                throw ClosedError();   // the queue stays open: the stuck worker may still read it
            }
        }
        Note($"finish: encoder thread ended after {Environment.TickCount64 - started} ms");
        _queue.Dispose();
        ThrowIfFaulted();
    }

    /// <summary>Gives up on a stuck encoder (any thread): the file is not finished and is kept as it is; the worker is a background thread, so it never holds the app open.</summary>
    public void Abort(string why) => Abandon(why, hardwareHang: false);

    // Only the encoder's own finalize timeout says the hardware encoder hung; an abort from outside (a slow disk, a watchdog) proves nothing about it.
    // Input ends here too: a worker that is not stuck stops pumping and skips finalising (a cancelled export), so nothing waits for it.
    private void Abandon(string why, bool hardwareHang)
    {
        _error ??= new VideoEncoderException("The video encoder stopped responding while saving, so the recording was lost (" + why + ").") { KeptFile = _path, HardwareStalled = hardwareHang && UsesHardware };
        Note($"abandoned: {why}; the unfinished file {_path} is kept (the encoder thread is a background thread and cannot keep the app open); on {ThreadText()}; {Counts()}");
        if (hardwareHang && UsesHardware) _options.HardwareResult?.Invoke(false);
        _abandoned.Set();
        CompleteAdding();
    }

    // CompleteAdding throws once the queue is disposed (a finished encoder): then there is nothing left to end.
    private void CompleteAdding()
    {
        try { _queue.CompleteAdding(); }
        catch (ObjectDisposedException) { } // swallow: the encoder already finished
    }

    /// <summary>
    /// A quick self-test of the hardware encoder at this size: a short file is written and finished within a bound, then removed.
    /// The verdict goes to <see cref="VideoEncoderOptions.HardwareResult"/> and is returned.
    /// </summary>
    public static bool ProbeHardware(string path, int width, int height, int fps, VideoEncoderOptions options)
    {
        var passed = false;
        var inconclusive = false;
        var started = Environment.TickCount64;
        try
        {
            var probe = options with { Encoder = VideoEncoderChoices.Hardware, HardwareResult = null, SimulateFinalizeHang = false, FinalizeTimeout = TimeSpan.FromSeconds(5) };
            using var encoder = Open(path, width, height, fps, null, live: true, options: probe);
            var frame = new byte[width * height * 4];
            for (var i = 0; i < frame.Length; i += 4) { var x = i / 4 % width; var y = i / 4 / width; frame[i] = (byte)(x * 255 / Math.Max(1, width)); frame[i + 1] = (byte)(y * 255 / Math.Max(1, height)); frame[i + 2] = (byte)(((x / 16 + y / 16) & 1) * 200 + 30); frame[i + 3] = 255; }   // a known non-black picture, not a black one
            for (var i = 0; i < 12; i++) { encoder.WriteFrame(frame, TimeSpan.FromTicks(i * 10_000_000L / fps)); Thread.Sleep(40); }
            encoder.Finish();
            passed = new FileInfo(path).Length > 0;
        }
        catch (VideoEncoderException e) { Trace.Error(Trace.Engine, "VideoEncoder: hardware self-test failed: " + e.Message); }
        catch (IOException e) { inconclusive = true; Trace.Error(Trace.Engine, "VideoEncoder: hardware self-test could not use the disk, not counted: " + e.Message); }
        finally { DeleteFile(path, "hardware self-test file"); }
        Trace.Error(Trace.Engine, $"VideoEncoder: hardware self-test {(inconclusive ? "inconclusive" : passed ? "passed" : "failed")} in {Environment.TickCount64 - started} ms, this recording uses {(passed ? "hardware" : "software")}");
        if (!inconclusive) options.HardwareResult?.Invoke(passed);   // an unreadable disk says nothing about the encoder: tried again next time
        return passed;
    }

    private static void DeleteFile(string path, string what)
    {
        try { if (File.Exists(path)) { File.Delete(path); Trace.Error(Trace.Engine, $"VideoEncoder: deleted {what} {path}"); } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Engine, $"VideoEncoder: could not delete {what} {path}: {e.Message}"); }
    }

    public void Dispose() => VideoDisposeHelper.TryFinish(Finish);

    private void ThrowIfFaulted() { if (_error is { } e) throw e as VideoEncoderException ?? new VideoEncoderException(e.Message, e); }

    // The room semaphore has one permit per slot, so a permit taken by WriteFrame always finds a free slot.
    private VideoSlot TakeSlot()
    {
        lock (_gate)
        {
            if (_closed) { _room.Release(); throw ClosedError(); }
            if (_free.Count == 0) { ThrowIfFaulted(); throw new VideoEncoderException("The video encoder ran out of frame buffers."); }
            return _free.Pop();
        }
    }

    // A slot returned after the pool closed is released at once; nothing else would.
    private void ReturnSlot(VideoSlot slot)
    {
        lock (_gate)
        {
            if (_closed) ReleaseLocked(slot);
            else _free.Push(slot);
        }
    }

    private unsafe void CopyInto(VideoSlot slot, ReadOnlySpan<byte> bgra)
    {
        var size = _width * _height * 4;
        if (bgra.Length != size) throw new InvalidDataException("The video frame is not width x height x 4 bytes.");
        bgra.CopyTo(new Span<byte>((void*)slot.Pointer, size));
    }

    private VideoSlot NewSlot()
    {
        Check(Mf.MFCreateMemoryBuffer(_width * _height * 4, out var buffer));
        buffer.Lock(out var pointer, out _, out _);
        return new VideoSlot { Buffer = buffer, Pointer = pointer, IdleRefs = ComRefCount(buffer) };
    }

    // Runs on the worker after a frame is written. A buffer the sink writer or an encoder still holds is replaced, never written again.
    private void Recycle(VideoSlot slot)
    {
        if (ComRefCount(slot.Buffer) == slot.IdleRefs)
        {
            slot.Buffer.Lock(out var pointer, out _, out _);
            slot.Pointer = pointer;
        }
        else
        {
            Marshal.ReleaseComObject(slot.Buffer);
            try { slot = NewSlot(); }
            catch (Exception e) when (e is COMException or OutOfMemoryException)
            {
                // The pool is one buffer short: stop cleanly. Run reports this only when no earlier error exists.
                _poolError = new VideoEncoderException(Describe("allocate a frame buffer"), e);
                _queue.CompleteAdding();
                return;
            }
        }
        ReturnSlot(slot);
    }

    // Runs on the worker when it stops. Queued and free buffers are released here, so nothing waits for the finaliser.
    private void ReleasePool()
    {
        lock (_gate)
        {
            _closed = true;
            while (_queue.TryTake(out var item)) if (item.Slot is { } queued) ReleaseLocked(queued);
            while (_free.Count > 0) ReleaseLocked(_free.Pop());
        }
    }

    private static void ReleaseLocked(VideoSlot slot)
    {
        slot.Buffer.Unlock();
        Marshal.ReleaseComObject(slot.Buffer);
    }

    /// <summary>The COM reference count of a wrapped object: the wrapper's own reference plus any held by a sample or an encoder.</summary>
    internal static int ComRefCount(object com)
    {
        var unknown = Marshal.GetIUnknownForObject(com);   // adds one reference
        return Marshal.Release(unknown);                  // removes it and returns what is left
    }

    private string Describe(string what) => $"The video encoder could not {what} at {_width}x{_height} {_fps} fps on this PC. Try a lower frame rate or resolution.";

    private void Run()
    {
        var started = false;
        IMFSinkWriter? writer = null;
        try
        {
            if (Mf.MFStartup(Mf.Version, 0) < 0) throw new VideoEncoderException("Windows Media Foundation is not available.");
            started = true;
            Note($"create: {_width}x{_height} {_fps} fps {(_live ? "live" : "offline")} choice={_options.Encoder} probe='{_options.HardwareProbe}' audio={(_audio is { } af ? $"{af.SampleRate} Hz x{af.Channels}" : "none")} on {ThreadText()} -> {_path}");
            var hardware = _options.Encoder switch
            {
                VideoEncoderChoices.Software => false,
                VideoEncoderChoices.Hardware => true,
                _ => Mf.HardwareH264Available() && (!_live || _options.HardwareProbe == VideoEncoderOptions.ProbePass),   // live Auto: hardware only once it passed the self-test
            };
            // Auto keeps today's retry with software in both modes; an explicit Hardware choice retries offline only (a live file cannot restart).
            var retrySoftware = _options.Encoder == VideoEncoderChoices.Auto || !_live;
            int video, audio;
            try
            {
                try { writer = BeginTuned(hardware, out video, out audio); UsesHardware = hardware; }
                catch (COMException e) when (hardware && retrySoftware)
                {
                    Note($"hardware encoder failed (0x{e.HResult:X8}), retrying with software");
                    _options.HardwareResult?.Invoke(false);
                    writer = BeginTuned(false, out video, out audio);
                }
                Inspect(writer, video);
            }
            catch (COMException e) { throw new VideoEncoderException(Describe("start the H.264 encoder") + $" (0x{e.HResult:X8})", e); }
            for (var i = 0; i < QueueFrames; i++) _free.Push(NewSlot());
            _ready.Set();
            Pump(writer, video, audio);
            if (_poolError is { } pe) throw pe;
            FinishSequence(writer, video, audio);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _error ??= e is COMException c ? new VideoEncoderException(Describe("encode") + $" (0x{c.HResult:X8})", e) : e;
            Note("encoder failed: " + e.Message);
            if (UsesHardware && e is COMException) _options.HardwareResult?.Invoke(false);   // only a hardware/COM error blames the hardware encoder
            _queue.CompleteAdding();
        }
        finally
        {
            if (writer is not null) Marshal.ReleaseComObject(writer);
            if (_abandoned.IsSet) Trace.Error(Trace.Engine, $"VideoEncoder: the abandoned encoder let go of {_path}; the file is kept for the user");
            ReleasePool();
            if (started) Mf.MFShutdown();
            _ready.Set();
        }
    }

    /// <summary>What the encoder did that the caller may want to see: the encoder in use, rejected properties, a hardware retry.</summary>
    public IReadOnlyList<string> Notes { get { lock (_notes) return _notes.ToArray(); } }

    /// <summary>The encoder transform the sink writer built ("name, hardware" or "name, software"), or "unknown".</summary>
    public string EncoderName { get; private set; } = "unknown";

}
