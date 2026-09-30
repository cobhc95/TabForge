using NAudio.Wave;

namespace TabForge.AudioEngine.Audio;

/// <summary>
/// Writes the input of each armed track to its own WAV file (32-bit float, the engine rate). The capture callback
/// (which for ASIO is the driver's real-time thread) only copies into a preallocated lock-free queue
/// (<see cref="Enqueue"/>); a dedicated disk thread writes the files. If the disk falls behind and the queue fills,
/// the lost frames are replaced by silence (so the take stays in time) and reported once.
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
    private long _dropped;                     // frames the producer could not queue (written as silence later)
    private long _droppedTotal;
    private readonly float[] _drain = new float[8192 * 2];
    private readonly Thread _diskThread;
    private volatile bool _stopping;

    public Recorder(string folder, IEnumerable<(int Slot, string TrackName, int Mode)> tracks, double startSec, int rate)
    {
        _rate = rate;
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
        _diskThread = new Thread(DiskLoop) { IsBackground = true, Name = "TabForge recorder disk", Priority = ThreadPriority.AboveNormal };
        _diskThread.Start();
    }

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
            Interlocked.Add(ref _dropped, frames);
            Interlocked.Add(ref _droppedTotal, frames);
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

    private void DiskLoop()
    {
        var reportedDrop = false;
        while (true)
        {
            var stopping = _stopping;
            var moved = DrainOnce();
            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                WriteSilence(dropped);
                if (!reportedDrop)
                {
                    reportedDrop = true;
                    try { Error?.Invoke($"The disk could not keep up; {dropped / (double)_rate:0.00} s of input was replaced by silence."); } catch { }
                }
            }
            if (stopping && !moved && Volatile.Read(ref _qWrite) == Volatile.Read(ref _qRead)) return;
            if (!moved) Thread.Sleep(5);
        }
    }

    private bool DrainOnce()
    {
        var read = _qRead;
        var available = Volatile.Read(ref _qWrite) - read;
        if (available <= 0) return false;
        var frames = (int)Math.Min(available, _drain.Length / 2);
        for (var i = 0; i < frames; i++)
        {
            var at = ((read + i) & _queueMask) * 2;
            _drain[i * 2] = _queue[at];
            _drain[i * 2 + 1] = _queue[at + 1];
        }
        Volatile.Write(ref _qRead, read + frames);
        Write(_drain, frames);
        return true;
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
        if (_diskThread.IsAlive && _diskThread != Thread.CurrentThread) _diskThread.Join(TimeSpan.FromSeconds(10));
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
