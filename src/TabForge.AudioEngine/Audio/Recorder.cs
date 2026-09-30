using NAudio.Wave;

namespace TabForge.AudioEngine.Audio;

/// <summary>
/// Writes the input of each armed track to its own WAV file (32-bit float, the engine rate). The capture callback
/// (which for ASIO is the driver's real-time thread) only copies into a preallocated lock-free queue
/// (<see cref="Enqueue"/>); a dedicated disk thread writes the files. If the disk falls behind and the queue fills,
/// the lost frames are replaced by silence at the position where they were lost (ordered gap markers, so the take stays in time) and reported once.
/// </summary>
public sealed class Recorder : IDisposable
{
    public sealed record Take(int Slot, string Path, double StartSec, int Mode);

    private readonly List<(Take Take, WaveFileWriter Writer)> _writers = new();
    private readonly object _gate = new();
    private readonly int _rate;
    private long _frames;
    private readonly float[] _scratch = new float[8192 * 2];

    // Single-producer (capture thread) / single-consumer (disk thread) queue of interleaved stereo frames.
    private readonly float[] _queue;
    private readonly long _queueMask;          // in frames (capacity is a power of two)
    private long _qWrite, _qRead;              // frame counters
    private long _droppedTotal;
    // A5-02: ordered gap markers (SPSC ring). A drop is recorded as (position in the queued-audio stream, length) so the disk
    // thread writes the audio queued before the drop, then the silence, then the audio queued after it: the take stays in time.
    // The producer merges a drop into the newest marker while nothing was queued since (CAS on its length); the consumer claims
    // a marker by swapping its length to -1, so a merge never lands on a marker already being written.
    private readonly long[] _gapPos, _gapLen;
    private readonly long _gapMask;
    private long _gWrite, _gRead;              // marker counters
    private long _lastGapPos = -1;             // producer only: queue position of the newest marker
    private long _lateSilence;                 // fallback: drops that fit no marker (ring full, newest marker already claimed): written as soon as the disk thread is idle
    private long _gapOverflows;
    private readonly float[] _drain = new float[8192 * 2];
    private readonly Thread? _diskThread;
    private volatile bool _stopping;
    private bool _reportedDrop, _reportedOverflow;

    /// <param name="startWriter">False (tests only): no disk thread; the caller drives the writer with <see cref="PumpForTests"/>.</param>
    /// <param name="gapCapacity">Gap markers kept between the capture thread and the disk thread (rounded up to a power of two).</param>
    public Recorder(string folder, IEnumerable<(int Slot, string TrackName, int Mode)> tracks, double startSec, int rate, bool startWriter = true, int gapCapacity = 256)
    {
        _rate = rate;
        var gaps = 1;
        while (gaps < Math.Max(1, gapCapacity)) gaps <<= 1;
        _gapPos = new long[gaps]; _gapLen = new long[gaps]; _gapMask = gaps - 1;
        // ~4 s of stereo input between the callback and the disk.
        var capacity = 1L;
        while (capacity < rate * 4L) capacity <<= 1;
        _queue = new float[capacity * 2];
        _queueMask = capacity - 1;
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            foreach (var (slot, name, mode) in tracks)
            {
                var safe = TabForge.Audio.Contracts.SafeFileNames.SafeFileName(name, "Track");
                var channels = mode == 2 ? 2 : 1;
                var (path, stream) = CreateUnique(folder, $"{safe} {stamp} s{slot}");
                _writers.Add((new Take(slot, path, startSec, mode), new WaveFileWriter(stream, WaveFormat.CreateIeeeFloatWaveFormat(rate, channels))));
            }
        }
        catch
        {
            foreach (var (_, w) in _writers) { try { w.Dispose(); } catch { } }
            _writers.Clear();
            throw;
        }
        if (!startWriter) return;
        _diskThread = new Thread(DiskLoop) { IsBackground = true, Name = "TabForge recorder disk", Priority = ThreadPriority.AboveNormal };
        _diskThread.Start();
    }

    /// <summary>Tests only (recorder built with <c>startWriter: false</c>): runs the disk thread's work until everything queued is written.</summary>
    public void PumpForTests() { while (Pump()) { } }

    /// <summary>Gap markers that could not be kept exactly (merged into an earlier marker or written late): timing after such a gap is approximate.</summary>
    public long GapOverflows => Interlocked.Read(ref _gapOverflows);

    /// <summary>
    /// A new file that did not exist before (create-new semantics): "name.wav", else "name-2.wav", "name-3.wav"… so two
    /// armed tracks with the same (or identically sanitised) name, or two takes in the same second, never share a file.
    /// </summary>
    internal static (string Path, FileStream Stream) CreateUnique(string folder, string baseName)
    {
        for (var n = 1; n < 1000; n++)
        {
            var path = Path.Combine(folder, n == 1 ? $"{baseName}.wav" : $"{baseName}-{n}.wav");
            try { return (path, new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read)); }
            catch (IOException) when (File.Exists(path)) { }
        }
        var fallback = Path.Combine(folder, $"{baseName}-{Guid.NewGuid():N}.wav");
        return (fallback, new FileStream(fallback, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read));
    }

    private bool _failed;
    /// <summary>True once a write failed (disk full / drive removed); writing stops, finalised files are kept.</summary>
    public bool Failed => _failed;
    /// <summary>Raised once per problem (on the disk thread) with a message: a write failure, or input lost to a slow disk.</summary>
    public event Action<string>? Error;
    /// <summary>Frames of input that could not be queued (replaced by silence).</summary>
    public long DroppedFrames => Interlocked.Read(ref _droppedTotal);

    /// <summary>
    /// Capture thread (real-time for ASIO): queues one block of interleaved stereo input. Never blocks, never allocates,
    /// never touches the disk.
    /// </summary>
    public void Enqueue(float[] block, int frames)
    {
        if (frames <= 0 || _stopping) return;
        var write = _qWrite;
        var free = (_queueMask + 1) - (write - Volatile.Read(ref _qRead));
        if (free < frames)
        {
            Interlocked.Add(ref _droppedTotal, frames);
            RecordGap(write, frames);
            return;
        }
        for (var i = 0; i < frames; i++)
        {
            var at = ((write + i) & _queueMask) * 2;
            _queue[at] = block[i * 2];
            _queue[at + 1] = block[i * 2 + 1];
        }
        Volatile.Write(ref _qWrite, write + frames);
    }

    /// <summary>Capture thread, allocation-free and lock-free: records <paramref name="frames"/> lost at queue position <paramref name="pos"/>.</summary>
    private void RecordGap(long pos, int frames)
    {
        var gw = _gWrite;   // producer-owned
        // Nothing was queued since the newest marker: the drop continues it.
        if (gw > 0 && _lastGapPos == pos && TryExtend((int)((gw - 1) & _gapMask), frames)) return;
        if (gw - Volatile.Read(ref _gRead) >= _gapMask + 1)
        {
            // Marker ring full: merge into the newest marker (the silence lands at its position; later timing is approximate),
            // or, if the disk thread already claimed it, write the silence when the writer is next idle. Reported once.
            Interlocked.Increment(ref _gapOverflows);
            if (gw > 0 && TryExtend((int)((gw - 1) & _gapMask), frames)) return;
            Interlocked.Add(ref _lateSilence, frames);
            return;
        }
        var slot = (int)(gw & _gapMask);
        _gapPos[slot] = pos;
        Volatile.Write(ref _gapLen[slot], frames);
        _lastGapPos = pos;
        Volatile.Write(ref _gWrite, gw + 1);   // published after the marker's fields; audio queued later sits after it
    }

    private bool TryExtend(int slot, long frames)
    {
        while (true)
        {
            var current = Volatile.Read(ref _gapLen[slot]);
            if (current < 0) return false;   // the disk thread already claimed it
            if (Interlocked.CompareExchange(ref _gapLen[slot], current + frames, current) == current) return true;
        }
    }

    private void DiskLoop()
    {
        while (true)
        {
            var stopping = _stopping;
            var moved = Pump();
            if (stopping && !moved && Idle) return;
            if (!moved) Thread.Sleep(5);
        }
    }

    private bool Idle => Volatile.Read(ref _qWrite) == Volatile.Read(ref _qRead) && Volatile.Read(ref _gWrite) == Volatile.Read(ref _gRead)
        && Interlocked.Read(ref _lateSilence) == 0;

    /// <summary>Disk thread: writes the queued audio and the gaps in order (audio up to a marker, then that marker's silence). True if anything was written.</summary>
    private bool Pump()
    {
        var moved = false;
        while (true)
        {
            var q = Volatile.Read(ref _qWrite);   // read before the marker counter: a marker published after this lies at or beyond q
            var gw = Volatile.Read(ref _gWrite);
            var gr = _gRead;
            var read = _qRead;
            if (gr != gw)
            {
                var slot = (int)(gr & _gapMask);
                var pos = _gapPos[slot];
                if (read < pos)
                {
                    var upTo = Math.Min(pos, q);
                    if (upTo > read) { DrainOnce(upTo - read); moved = true; }
                    continue;   // q may still trail pos (published since): read it again
                }
                var len = Interlocked.Exchange(ref _gapLen[slot], -1);   // claim: the producer no longer merges into it
                if (len > 0) { WriteSilence(len); ReportDrop(len); moved = true; }
                Volatile.Write(ref _gRead, gr + 1);
                continue;
            }
            if (q > read) { DrainOnce(q - read); moved = true; continue; }
            var late = Interlocked.Exchange(ref _lateSilence, 0);
            if (late > 0) { WriteSilence(late); ReportDrop(late); moved = true; }
            return moved;
        }
    }

    private void ReportDrop(long frames)
    {
        if (!_reportedDrop)
        {
            _reportedDrop = true;
            try { Error?.Invoke($"The disk could not keep up; {frames / (double)_rate:0.00} s of input was replaced by silence."); } catch { }
        }
        if (!_reportedOverflow && Interlocked.Read(ref _gapOverflows) > 0)
        {
            _reportedOverflow = true;
            try { Error?.Invoke("The disk fell far behind; timing after the lost input may be slightly off."); } catch { }
        }
    }

    private void DrainOnce(long limit)
    {
        var read = _qRead;
        var frames = (int)Math.Min(limit, _drain.Length / 2);
        for (var i = 0; i < frames; i++)
        {
            var at = ((read + i) & _queueMask) * 2;
            _drain[i * 2] = _queue[at];
            _drain[i * 2 + 1] = _queue[at + 1];
        }
        Volatile.Write(ref _qRead, read + frames);
        Write(_drain, frames);
    }

    private void WriteSilence(long frames)
    {
        Array.Clear(_drain);
        while (frames > 0)
        {
            var n = (int)Math.Min(frames, _drain.Length / 2);
            Write(_drain, n);
            frames -= n;
        }
    }

    /// <summary>Disk thread (or tests): appends one block (interleaved stereo input) to every take.</summary>
    public void Write(float[] block, int frames)
    {
        string? error = null;
        lock (_gate)
        {
            if (_failed) return;
            try
            {
                foreach (var (take, writer) in _writers)
                {
                    if (take.Mode == 2) { writer.WriteSamples(block, 0, frames * 2); continue; }
                    var channel = take.Mode == 1 ? 1 : 0;
                    for (var start = 0; start < frames; start += _scratch.Length)
                    {
                        var n = Math.Min(_scratch.Length, frames - start);
                        for (var i = 0; i < n; i++) _scratch[i] = block[(start + i) * 2 + channel];
                        writer.WriteSamples(_scratch, 0, n);
                    }
                }
                _frames += frames;
            }
            catch (Exception ex)
            {
                _failed = true;
                error = "Recording stopped: " + ex.Message;
            }
        }
        if (error is not null) { try { Error?.Invoke(error); } catch { } }
    }

    /// <summary>Closes the files; returns each take with its length in seconds.</summary>
    public List<(Take Take, double LengthSec)> Finish()
    {
        // Let the disk thread write everything still queued, then close the files.
        _stopping = true;
        if (_diskThread is null) PumpForTests();
        else if (_diskThread.IsAlive && _diskThread != Thread.CurrentThread) _diskThread.Join(TimeSpan.FromSeconds(10));
        lock (_gate)
        {
            var length = (double)_frames / _rate;
            var result = _writers.Select(w => (w.Take, length)).ToList();
            foreach (var (_, writer) in _writers) { try { writer.Dispose(); } catch { } }
            _writers.Clear();
            return result;
        }
    }

    public void Dispose() => Finish();
}
