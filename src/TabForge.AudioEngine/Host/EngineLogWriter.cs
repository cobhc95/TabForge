using System.Collections.Concurrent;
using System.Text;

namespace TabForge.AudioEngine;

// Owns: one engine log (queue, file, rotation, writer thread). The engine session and each plug-in host create their own and hand it to what they build.

/// <summary>
/// The engine log (one file per engine run in the user's Diagnostics folder, shared by the engine and its plug-in hosts), for diagnosing device and plug-in problems.
/// The file is named by TabForge when it starts the engine and created new (<see cref="CreateForRun"/>); no process writes to a predictable name.
/// <see cref="Write"/> only queues the line (bounded: past <see cref="MaxQueued"/> lines are counted and dropped, never
/// blocking); one background thread appends whole batches. Every line carries the writing process's id. At <see cref="MaxBytes"/> the
/// file rotates to <c>.1.log</c> (the previous history is kept, not deleted). <see cref="Flush"/> writes what is queued now (before a
/// watchdog exit, at shutdown, at process exit).
/// </summary>
public sealed class EngineLogWriter
{
    public const int MaxQueued = 4096;
    private const long DefaultMaxBytes = 1_000_000;
    private readonly ConcurrentQueue<string> Pending = new();
    private readonly AutoResetEvent Wake = new(false);
    private readonly object FileGate = new();
    private readonly int Pid = Environment.ProcessId;
    private int _queued, _dropped, _started;
    // Key: this writer (one per engine session / plug-in host). Ceiling: one path, set once at start (CreateForRun / Attach) or by tests. Thread: any (read and set atomically).
    // Null: this writer has no log file and its lines are dropped.
    private string? _path;
    private long _maxBytes = DefaultMaxBytes;

    /// <summary>The log file in use, or null when this process keeps none (the rotated history is next to it, <see cref="RotatedPath"/>).</summary>
    public string? LogPath => Volatile.Read(ref _path);
    public string? RotatedPath => LogPath is { } path ? Path.ChangeExtension(path, ".1.log") : null;

    /// <summary>Lines dropped because the queue was full (reported in the log once there is room again).</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>
    /// Engine start: creates this run's log file (always a new one; an existing file is never reused) and makes it the file in use.
    /// Returns false when it cannot be created: the engine then runs without a log, because diagnostics must not stop audio.
    /// </summary>
    public bool CreateForRun(string path)
    {
        var full = ValidLogPath(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using (new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
            Volatile.Write(ref _path, full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not logged: the log cannot be written, so the process keeps no log file (the audio path is not affected).
            return false;
        }
    }

    /// <summary>A plug-in host: appends to the engine's log file for this run (the engine created it).</summary>
    public void Attach(string path) => Volatile.Write(ref _path, ValidLogPath(path));

    /// <summary>Only a full path to a .log file is used: the path came from a command line and is never a relative or device path.</summary>
    private string ValidLogPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".log", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The engine log must be a full path to a .log file.", nameof(path));
        return Path.GetFullPath(path);
    }

    /// <summary>Tests only: another file and rotation size (null: no log file).</summary>
    public void UseFileForTests(string? path, long maxBytes = DefaultMaxBytes)
    {
        Flush();
        lock (FileGate)
        {
            Volatile.Write(ref _path, path);
            _maxBytes = Math.Max(1024, maxBytes);
        }
    }

    /// <summary>Queues one line (any thread; formatting happens here, the file work on the writer thread).</summary>
    public void Write(string line)
    {
        if (Interlocked.Increment(ref _queued) > MaxQueued)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _dropped);
            return;
        }
        Pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff} [{Pid}] {line}");
        if (Interlocked.Exchange(ref _started, 1) == 0) Start();
        Wake.Set();
    }

    private void Start()
    {
        new Thread(() => { while (true) { Wake.WaitOne(); Flush(); } }) { IsBackground = true, Name = "TabForge engine log", Priority = ThreadPriority.BelowNormal }.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(500);
    }

    /// <summary>Writes every queued line now (any thread; waits up to <paramref name="timeoutMs"/> for a batch another thread is writing).</summary>
    public void Flush(int timeoutMs = Timeout.Infinite)
    {
        if (!Monitor.TryEnter(FileGate, timeoutMs)) return;
        try
        {
            if (Pending.IsEmpty) return;
            var batch = new StringBuilder();
            while (Pending.TryDequeue(out var line)) { Interlocked.Decrement(ref _queued); batch.Append(line).Append(Environment.NewLine); }
            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0) batch.Append($"{DateTime.Now:HH:mm:ss.fff} [{Pid}] ({dropped} log lines dropped: the log queue was full){Environment.NewLine}");
            var path = LogPath;
            if (path is null) return;   // this process keeps no log file: the batch is dropped
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > _maxBytes)
                {
                    // Rotate: keep the history (the crash loop it may show is exactly what is needed), start a new file.
                    try { File.Move(path, RotatedPath!, overwrite: true); }
                    catch (IOException) { } // swallow: another process is appending right now: rotate on a later batch
                }
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(batch.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (IOException) { } // swallow: best-effort crash-log batch write; the entry is dropped when the file is unavailable
            catch (UnauthorizedAccessException) { } // swallow: best-effort crash-log batch write; the entry is dropped when the file is unavailable
        }
        finally { Monitor.Exit(FileGate); }
    }
}
