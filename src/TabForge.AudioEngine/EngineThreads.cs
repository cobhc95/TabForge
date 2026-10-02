using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace TabForge.AudioEngine;

/// <summary>
/// The engine's two threads: the main thread (commands, plug-in loading, editor windows, message loop) and the
/// audio thread (the device callback). Work for the main thread is queued with <see cref="Post"/>.
/// </summary>
public static class EngineThreads
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    [ThreadStatic] private static bool _isAudio;

    public static bool IsAudioThread => _isAudio;

    private static int _mainThreadId;

    /// <summary>
    /// Marks the calling thread as the engine main thread (EngineHost.Run, or the thread that attaches the headless harness).
    /// Read only by the Debug-build checks in <see cref="EngineSession"/>.
    /// </summary>
    public static void MarkMainThread() => Volatile.Write(ref _mainThreadId, Environment.CurrentManagedThreadId);

    /// <summary>True on the thread last marked with <see cref="MarkMainThread"/>.</summary>
    public static bool IsMainThread => Environment.CurrentManagedThreadId == Volatile.Read(ref _mainThreadId);

    [ThreadStatic] private static bool _isOffline;
    /// <summary>True on an offline-render worker: VST2 plug-ins are told the process level is "offline" (4).</summary>
    public static bool IsOfflineWorker => _isOffline;

    /// <summary>Sets or clears the offline process level for the calling thread.</summary>
    public static void SetOfflineFlag(bool on) => _isOffline = on;

    /// <summary>Called once by each offline-render worker thread: audio-thread setup (flush-to-zero included) plus the offline process level.</summary>
    public static void MarkOfflineWorker()
    {
        MarkAudioThread();
        _isOffline = true;
    }

    [System.Runtime.InteropServices.DllImport("ucrtbase.dll")]
    private static extern int _controlfp_s(out uint current, uint newControl, uint mask);

    private const uint DnFlush = 0x01000000, McwDn = 0x03000000;

    /// <summary>
    /// Denormals flushed to zero on the calling thread (_DN_FLUSH sets both SSE flags, FTZ and DAZ), as a DAW's audio threads: a
    /// decaying reverb tail or filter state otherwise turns into denormals that cost up to 100x per operation (CPU spikes at small buffers).
    /// Returns false when the C runtime call is not available.
    /// </summary>
    public static bool FlushDenormals()
    {
        try { return _controlfp_s(out _, DnFlush, McwDn) == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>The calling thread flushes denormals (both SSE flags set), per <see cref="FlushDenormals"/>.</summary>
    public static bool DenormalsFlushed
    {
        get
        {
            try { return _controlfp_s(out var current, 0, 0) == 0 && (current & McwDn) == DnFlush; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
        }
    }

    /// <summary>Called once by each audio callback thread (and each plug-in host / render worker audio thread).</summary>
    public static void MarkAudioThread()
    {
        _isAudio = true;
        FlushDenormals();
        if (_mmcssHandle != IntPtr.Zero) return;   // already registered on this thread
        // The Windows "Pro Audio" scheduling class (MMCSS, time critical): fewer dropouts at small buffers.
        try { var index = 0; _mmcssHandle = AvSetMmThreadCharacteristicsW("Pro Audio", ref index); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [ThreadStatic] private static IntPtr _mmcssHandle;

    /// <summary>
    /// For a device-driver thread the engine does not create itself (NAudio's DirectSound polling thread runs at normal
    /// priority): MMCSS "Pro Audio" at critical priority plus the highest thread priority, so a busy UI or GC thread cannot delay a refill.
    /// </summary>
    public static void RaiseRenderPriority()
    {
        MarkAudioThread();
        try { Thread.CurrentThread.Priority = ThreadPriority.Highest; } catch (Exception ex) when (ex is ThreadStateException or InvalidOperationException) { }
        try { if (_mmcssHandle != IntPtr.Zero) AvSetMmThreadPriority(_mmcssHandle, 2); }   // AVRT_PRIORITY_CRITICAL
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [System.Runtime.InteropServices.DllImport("avrt.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref int taskIndex);

    [System.Runtime.InteropServices.DllImport("avrt.dll")]
    private static extern bool AvSetMmThreadPriority(IntPtr handle, int priority);

    /// <summary>
    /// The engine is a background process behind TabForge's window: without this Windows 11 may put it on efficiency cores
    /// ("EcoQoS") and share its CPU with whatever else runs (a plug-in's editor redrawing while a knob is dragged made the
    /// audio stutter). High priority and no power throttling keep the audio steady, as in a DAW.
    /// </summary>
    public static void PrepareProcess()
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }
        try
        {
            var state = new ProcessPowerThrottlingState { Version = 1, ControlMask = 1, StateMask = 0 };   // execution speed: never throttle
            SetProcessInformation(Process.GetCurrentProcess().Handle, 4, ref state, System.Runtime.InteropServices.Marshal.SizeOf<ProcessPowerThrottlingState>());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref ProcessPowerThrottlingState info, int size);

    public static void Post(Action action) => Queue.Enqueue(action);

    /// <summary>Main thread: runs queued work (bounded per call so the message loop stays responsive).</summary>
    public static void RunPending(int max = 64)
    {
        // Parameter edits / resize requests VST2 plug-ins flagged from their callbacks (allocation-free there), raised here once.
        Plugins.Vst2Plugin.DeliverHostNotifications();
        for (var i = 0; i < max && Queue.TryDequeue(out var action); i++)
        {
            try { action(); }
            catch (Exception ex) { EngineLog.Write($"main-thread task failed: {ex.GetBaseException().Message}"); }
        }
    }
}

/// <summary>
/// The engine log (a file in %TEMP%, shared by the engine, every plug-in host and TabForge), for diagnosing device and plug-in problems.
/// <see cref="Write"/> only queues the line (bounded: past <see cref="MaxQueued"/> lines are counted and dropped, never
/// blocking); one background thread appends whole batches. Every line carries the writing process's id. At <see cref="MaxBytes"/> the
/// file rotates to <c>.1.log</c> (the previous history is kept, not deleted). <see cref="Flush"/> writes what is queued now (before a
/// watchdog exit, at shutdown, at process exit).
/// </summary>
public static class EngineLog
{
    public const int MaxQueued = 4096;
    private const long DefaultMaxBytes = 1_000_000;
    private static readonly ConcurrentQueue<string> Pending = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static readonly object FileGate = new();
    private static readonly int Pid = Environment.ProcessId;
    private static int _queued, _dropped, _started;
    private static string _path = Path.Combine(Path.GetTempPath(), "tabforge-audioengine.log");
    private static long _maxBytes = DefaultMaxBytes;

    /// <summary>The log file in use (the rotated history is next to it, <see cref="RotatedPath"/>).</summary>
    public static string LogPath => Volatile.Read(ref _path);
    public static string RotatedPath => Path.ChangeExtension(LogPath, ".1.log");

    /// <summary>Lines dropped because the queue was full (reported in the log once there is room again).</summary>
    public static int Dropped => Volatile.Read(ref _dropped);

    /// <summary>Tests only: another file and rotation size (null: the default %TEMP% file).</summary>
    public static void UseFileForTests(string? path, long maxBytes = DefaultMaxBytes)
    {
        Flush();
        lock (FileGate)
        {
            Volatile.Write(ref _path, path ?? Path.Combine(Path.GetTempPath(), "tabforge-audioengine.log"));
            _maxBytes = Math.Max(1024, maxBytes);
        }
    }

    /// <summary>Queues one line (any thread; formatting happens here, the file work on the writer thread).</summary>
    public static void Write(string line)
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

    private static void Start()
    {
        new Thread(() => { while (true) { Wake.WaitOne(); Flush(); } }) { IsBackground = true, Name = "TabForge engine log", Priority = ThreadPriority.BelowNormal }.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(500);
    }

    /// <summary>Writes every queued line now (any thread; waits up to <paramref name="timeoutMs"/> for a batch another thread is writing).</summary>
    public static void Flush(int timeoutMs = Timeout.Infinite)
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
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > _maxBytes)
                {
                    // Rotate: keep the history (the crash loop it may show is exactly what is needed), start a new file.
                    try { File.Move(path, RotatedPath, overwrite: true); }
                    catch (IOException) { }   // another process is appending right now: rotate on a later batch
                }
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(batch.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally { Monitor.Exit(FileGate); }
    }
}
