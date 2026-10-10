using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace TabForge.Services.Video;

/// <summary>Audio layout of the encoded track: interleaved float samples.</summary>
public readonly record struct VideoAudioFormat(int SampleRate, int Channels);

/// <summary>An encoder failure with a message the UI can show as it is.</summary>
public sealed class VideoEncoderException : Exception
{
    public VideoEncoderException(string message, Exception? inner = null) : base(message, inner) { }
}

// Owns: writing an MP4 (H.264 video, AAC audio) through Media Foundation's sink writer; the bounded frame queue and its
// fixed pool of native frame buffers (one per queue slot); the dropped-frame count; the bitrate rule. A worker thread owns
// every COM object, so callers may be on any thread; a caller only copies pixels into a buffer the worker has locked for it.
// Does not own: capturing frames or audio, file naming, settings, UI.
// Tests: TestVideoEncoderMp4, TestVideoEncoder4k60, TestVideoEncoderGolden, TestVideoEncoderOptions.
/// <summary>
/// Live mode (<c>live: true</c>) never blocks the caller: a full queue drops the frame and counts it. Offline mode waits for room.
/// </summary>
public sealed class VideoEncoder : IDisposable
{
    public const int QueueFrames = 6;

    private static readonly (int W, int H)[] Sizes = { (1920, 1080), (3840, 2160), (1080, 1920), (2160, 3840) };   // landscape and portrait
    private static readonly int[] Rates = { 30, 60, 120 };

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
    private volatile Exception? _error;
    private long _dropped, _audioFrames;
    private bool _finished;
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

    /// <summary>True when the video encoder in use is a hardware encoder.</summary>
    public bool UsesHardware { get; private set; }

    /// <summary>Video bits per second for a size and frame rate (about 0.1 bit per pixel, capped at 100 Mbit/s).</summary>
    public static int VideoBitrate(int width, int height, int fps) => (int)Math.Min(100_000_000.0, width * (double)height * fps * 0.1);

    /// <summary>Opens the file and starts the encoder thread. Throws <see cref="VideoEncoderException"/> when the size, frame rate or encoder is not usable.</summary>
    public static VideoEncoder Open(string path, int width, int height, int fps, VideoAudioFormat? audioFormat = null, bool live = false, VideoEncoderOptions? options = null)
    {
        if (!Sizes.Contains((width, height))) throw new VideoEncoderException($"Video size {width}x{height} is not supported (use 1080p or 4K, landscape or portrait).");
        if (!Rates.Contains(fps)) throw new VideoEncoderException($"{fps} frames per second is not supported (use 30, 60 or 120).");
        if (audioFormat is { } a && (a.Channels is < 1 or > 2 || a.SampleRate is not (44100 or 48000))) throw new VideoEncoderException("The audio must be 44.1 or 48 kHz, mono or stereo.");
        var encoder = new VideoEncoder(path, width, height, fps, audioFormat, live, options ?? new VideoEncoderOptions());
        encoder._worker.Start();
        encoder._ready.Wait();
        if (encoder._error is { } e) { encoder._worker.Join(); encoder._queue.Dispose(); throw e as VideoEncoderException ?? new VideoEncoderException(e.Message, e); }
        return encoder;
    }

    /// <summary>Queues one top-down BGRA frame (width x height x 4 bytes). Live mode returns false when the frame was dropped.</summary>
    public bool WriteFrame(ReadOnlySpan<byte> bgra, TimeSpan timestamp)
    {
        ThrowIfFaulted();
        var size = _width * _height * 4;
        if (bgra.Length < size) throw new ArgumentException("The frame is smaller than width x height x 4 bytes.", nameof(bgra));
        if (_live) { if (!_room.Wait(0)) { Interlocked.Increment(ref _dropped); return false; } }
        else while (!_room.Wait(50)) ThrowIfFaulted();
        var slot = TakeSlot();
        CopyInto(slot, bgra[..size]);
        if (!TryQueue(new Item(slot, timestamp.Ticks, null, size))) { ReturnSlot(slot); _room.Release(); throw ClosedError(); }
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
        _queue.CompleteAdding();
        _worker.Join();
        _queue.Dispose();
        ThrowIfFaulted();
    }

    public void Dispose() { try { Finish(); } catch (VideoEncoderException) { } }

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

    private static unsafe void CopyInto(VideoSlot slot, ReadOnlySpan<byte> bgra) => bgra.CopyTo(new Span<byte>((void*)slot.Pointer, bgra.Length));

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
            var hardware = _options.Encoder switch
            {
                VideoEncoderChoices.Software => false,
                VideoEncoderChoices.Hardware => true,
                _ => Mf.HardwareH264Available(),
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
                    writer = BeginTuned(false, out video, out audio);
                }
                Inspect(writer, video);
            }
            catch (COMException e) { throw new VideoEncoderException(Describe("start the H.264 encoder") + $" (0x{e.HResult:X8})", e); }
            for (var i = 0; i < QueueFrames; i++) _free.Push(NewSlot());
            _ready.Set();
            Pump(writer, video, audio);
            if (_poolError is { } pe) throw pe;
            writer.FinalizeWriter();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _error ??= e is COMException c ? new VideoEncoderException(Describe("encode") + $" (0x{c.HResult:X8})", e) : e;
            _queue.CompleteAdding();
        }
        finally
        {
            if (writer is not null) Marshal.ReleaseComObject(writer);
            ReleasePool();
            if (started) Mf.MFShutdown();
            _ready.Set();
        }
    }

    /// <summary>What the encoder did that the caller may want to see: the encoder in use, rejected properties, a hardware retry.</summary>
    public IReadOnlyList<string> Notes { get { lock (_notes) return _notes.ToArray(); } }

    /// <summary>The encoder transform the sink writer built ("name, hardware" or "name, software"), or "unknown".</summary>
    public string EncoderName { get; private set; } = "unknown";

    private void Note(string text)
    {
        lock (_notes) _notes.Add(text);
        Trace.Write(Trace.Engine, "VideoEncoder: " + text);
    }

    // With codec properties asked for, a writer that rejects them is rebuilt without: the export never fails over a tuning property.
    private IMFSinkWriter BeginTuned(bool hardware, out int videoStream, out int audioStream)
    {
        if (hardware && _options.SimulateHardwareFailure) throw new COMException("simulated hardware failure", unchecked((int)0x8000FFFF));
        if (_options.Properties(_live).Count > 0)
        {
            try { return Begin(hardware, true, out videoStream, out audioStream); }
            catch (COMException e) { Note($"codec properties rejected (0x{e.HResult:X8}), encoding without them"); }
        }
        return Begin(hardware, false, out videoStream, out audioStream);
    }

    // Reads back which transform the sink writer really built; any failure leaves the request as the answer.
    private void Inspect(IMFSinkWriter writer, int video)
    {
        try
        {
            if (writer is not IMFSinkWriterEx ex) return;
            // The chain holds a colour converter first (RGB32 in) and then the encoder: pick the one in the encoder category.
            for (var i = 0; i < 6 && ex.GetTransformForStream(video, i, out var category, out var transform) >= 0; i++)
            {
                try
                {
                    if (category != Mf.VideoEncoderCategory || transform is null) continue;
                    // Hardware encoders are asynchronous MFTs, which are event generators; the software encoder is a plain synchronous transform.
                    UsesHardware = transform is IMFMediaEventGenerator;
                }
                finally { if (transform is not null && Marshal.IsComObject(transform)) Marshal.ReleaseComObject(transform); }
                var names = UsesHardware ? Mf.H264EncoderNames(4).Distinct().ToList() : new List<string> { "H264 Encoder MFT" };
                EncoderName = (UsesHardware ? "hardware: " : "software: ") + (names.Count > 0 ? string.Join(" or ", names) : "name unknown");
                Note("encoder " + EncoderName);
                return;
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException) { Note("encoder not identified: " + e.Message); }
    }

    private IMFSinkWriter Begin(bool hardware, bool tune, out int videoStream, out int audioStream)
    {
        Check(Mf.MFCreateAttributes(out var attrs, 3));
        attrs.SetUINT32(Mf.EnableHardware, hardware ? 1 : 0);
        attrs.SetUINT32(Mf.DisableThrottling, 1);
        if (tune && _live && _options.LowLatency) attrs.SetUINT32(VideoEncoderOptions.LowLatencyMode, 1);
        var temps = new List<object> { attrs };   // released with the call: the writer keeps its own references
        try
        {
            Check(Mf.MFCreateSinkWriterFromURL(_path, IntPtr.Zero, attrs, out var writer));
            try
            {
                var t = Own(temps, VideoType(Mf.H264, VideoBitrate(_width, _height, _fps)));
                writer.AddStream(t, out videoStream);
                writer.SetInputMediaType(videoStream, Own(temps, VideoType(Mf.Rgb32, 0)), tune ? Own(temps, Tuning()) : null);
                audioStream = -1;
                if (_audio is { } a)
                {
                    writer.AddStream(Own(temps, AudioType(Mf.Aac, a, 20000)), out audioStream);
                    writer.SetInputMediaType(audioStream, Own(temps, AudioType(Mf.Pcm, a, a.SampleRate * a.Channels * 2)), null);
                }
                writer.BeginWriting();
                return writer;
            }
            catch { Marshal.ReleaseComObject(writer); throw; }
        }
        finally { foreach (var o in temps) Marshal.ReleaseComObject(o); }
    }

    private static T Own<T>(List<object> temps, T com) where T : class { temps.Add(com); return com; }

    private IMFAttributes Tuning()
    {
        var props = _options.Properties(_live);
        Check(Mf.MFCreateAttributes(out var a, props.Count));
        foreach (var (name, key, value) in props) { a.SetUINT32(key, value); Trace.Write(Trace.Engine, $"VideoEncoder: asking for {name}={value}"); }
        return a;
    }

    private IMFMediaType VideoType(Guid subtype, int bitrate)
    {
        Check(Mf.MFCreateMediaType(out var t));
        t.SetGUID(Mf.MajorType, Mf.Video);
        t.SetGUID(Mf.Subtype, subtype);
        t.SetUINT64(Mf.FrameSize, Mf.Pack(_width, _height));
        t.SetUINT64(Mf.FrameRate, Mf.Pack(_fps, 1));
        t.SetUINT64(Mf.PixelAspect, Mf.Pack(1, 1));
        t.SetUINT32(Mf.InterlaceMode, 2);
        if (bitrate > 0) { t.SetUINT32(Mf.AvgBitrate, bitrate); t.SetUINT32(Mf.Mpeg2Profile, 100); }
        else t.SetUINT32(Mf.DefaultStride, _width * 4);   // positive: top-down rows
        return t;
    }

    private static IMFMediaType AudioType(Guid subtype, VideoAudioFormat a, int bytesPerSecond)
    {
        Check(Mf.MFCreateMediaType(out var t));
        t.SetGUID(Mf.MajorType, Mf.Audio);
        t.SetGUID(Mf.Subtype, subtype);
        t.SetUINT32(Mf.AudioBits, 16);
        t.SetUINT32(Mf.AudioRate, a.SampleRate);
        t.SetUINT32(Mf.AudioChannels, a.Channels);
        t.SetUINT32(Mf.AudioBytesPerSec, bytesPerSecond);
        if (subtype == Mf.Pcm) t.SetUINT32(Mf.AudioBlockAlign, a.Channels * 2);
        else { t.SetUINT32(Mf.AacPayload, 0); t.SetUINT32(Mf.AacProfileLevel, 0x29); }
        return t;
    }

    private void Pump(IMFSinkWriter writer, int video, int audio)
    {
        var frameTicks = 10_000_000L / _fps;
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (item.Slot is { } slot)
            {
                try { WriteVideo(writer, video, slot, item.Count, item.Time, frameTicks); }
                finally
                {
                    try { Recycle(slot); }
                    finally { _room.Release(); }
                }
            }
            else if (item.Audio is { } au)
            {
                try
                {
                    var pcm = new byte[item.Count * 2];
                    for (var i = 0; i < item.Count; i++) BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), (short)Math.Round(Math.Clamp(au[i], -1f, 1f) * 32767f));
                    var frames = item.Count / _audio!.Value.Channels;
                    var rate = _audio.Value.SampleRate;
                    Write(writer, audio, pcm, pcm.Length, _audioFrames * 10_000_000L / rate, frames * 10_000_000L / rate);
                    _audioFrames += frames;
                }
                finally { ArrayPool<float>.Shared.Return(au); }
            }
        }
    }

    private static void WriteVideo(IMFSinkWriter writer, int stream, VideoSlot slot, int length, long time, long duration)
    {
        slot.Buffer.Unlock();
        slot.Buffer.SetCurrentLength(length);
        Check(Mf.MFCreateSample(out var sample));
        try
        {
            sample.AddBuffer(slot.Buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            writer.WriteSample(stream, sample);
        }
        finally { Marshal.ReleaseComObject(sample); }
    }

    private static void Write(IMFSinkWriter writer, int stream, byte[] data, int length, long time, long duration)
    {
        Check(Mf.MFCreateMemoryBuffer(length, out var buffer));
        buffer.Lock(out var ptr, out _, out _);
        Marshal.Copy(data, 0, ptr, length);
        buffer.Unlock();
        buffer.SetCurrentLength(length);
        Check(Mf.MFCreateSample(out var sample));
        sample.AddBuffer(buffer);
        sample.SetSampleTime(time);
        sample.SetSampleDuration(duration);
        try { writer.WriteSample(stream, sample); }
        finally { Marshal.ReleaseComObject(sample); Marshal.ReleaseComObject(buffer); }
    }

    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
}
