using System.Collections.Concurrent;
using System.Diagnostics;

namespace TabForge.AudioEngine;

// Owns: one engine session's two threads (main-thread queue, audio device callback) and the work posting between them; the per-thread role flags.
// Does not own: the OS thread setup (AudioThreadSetup, stateless).

/// <summary>
/// An engine session with two threads: the main thread (commands, plug-in loading, editor windows, message loop) and the
/// audio thread (the device callback). Work for the main thread is queued with <see cref="Post"/>. The queue is unbounded
/// (a dropped command would change behaviour); past <see cref="HighWaterMark"/> queued items one warning is logged per crossing.
/// </summary>
public sealed class EngineThreads
{
    /// <summary>Queued items past which one warning is logged (again only after the queue has drained to this level and crossed it anew).</summary>
    public const int HighWaterMark = 1000;

    private readonly EngineLogWriter _log;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ThreadLocal<bool> _isAudio = new();
    private readonly ThreadLocal<bool> _isOffline = new();
    private int _queued, _warned, _mainThreadId;

    public EngineThreads(EngineLogWriter log) => _log = log;

    /// <summary>The log of the session these threads belong to.</summary>
    public EngineLogWriter Log => _log;

    public bool IsAudioThread => _isAudio.Value;

    /// <summary>Items queued for the main thread now.</summary>
    public int Queued => Volatile.Read(ref _queued);

    /// <summary>
    /// Marks the calling thread as the engine main thread (EngineHost.Run, or the thread that attaches the headless harness).
    /// Read only by the Debug-build checks in <see cref="EngineSession"/>.
    /// </summary>
    public void MarkMainThread() => Volatile.Write(ref _mainThreadId, Environment.CurrentManagedThreadId);

    /// <summary>True on the thread last marked with <see cref="MarkMainThread"/>.</summary>
    public bool IsMainThread => Environment.CurrentManagedThreadId == Volatile.Read(ref _mainThreadId);

    /// <summary>True on an offline-render worker: VST2 plug-ins are told the process level is "offline" (4).</summary>
    public bool IsOfflineWorker => _isOffline.Value;

    /// <summary>Sets or clears the offline process level for the calling thread.</summary>
    public void SetOfflineFlag(bool on) => _isOffline.Value = on;

    /// <summary>Called once by each offline-render worker thread: audio-thread setup (flush-to-zero included) plus the offline process level.</summary>
    public void MarkOfflineWorker()
    {
        MarkAudioThread();
        _isOffline.Value = true;
    }

    /// <summary>Called once by each audio callback thread (and each plug-in host / render worker audio thread).</summary>
    public void MarkAudioThread()
    {
        _isAudio.Value = true;
        AudioThreadSetup.PrepareAudioThread();
    }

    /// <summary>For a device-driver thread the engine does not create itself: see <see cref="AudioThreadSetup.RaiseCritical"/>.</summary>
    public void RaiseRenderPriority()
    {
        MarkAudioThread();
        AudioThreadSetup.RaiseCritical();
    }

    /// <summary>Any thread: queues work for the main thread. Never dropped; a flood is logged once per crossing of <see cref="HighWaterMark"/>.</summary>
    public void Post(Action action)
    {
        _queue.Enqueue(action);
        if (Interlocked.Increment(ref _queued) > HighWaterMark && Interlocked.Exchange(ref _warned, 1) == 0)
            _log.Write($"main-thread queue over {HighWaterMark} items ({Queued} queued): commands are arriving faster than the main thread runs them");
    }

    /// <summary>Main thread: runs queued work (bounded per call so the message loop stays responsive).</summary>
    public void RunPending(int max = 64)
    {
        // Parameter edits / resize requests VST2 plug-ins flagged from their callbacks (allocation-free there), raised here once.
        Plugins.Vst2Plugin.DeliverHostNotifications();
        for (var i = 0; i < max && _queue.TryDequeue(out var action); i++)
        {
            if (Interlocked.Decrement(ref _queued) <= HighWaterMark) Volatile.Write(ref _warned, 0);
            try { action(); }
            catch (Exception ex) { _log.Write($"main-thread task failed: {ex.GetBaseException().Message}"); }
        }
    }
}

/// <summary>
/// The OS side of thread setup: no state shared between sessions (the MMCSS handle belongs to the calling thread).
/// </summary>
public static partial class AudioThreadSetup
{

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
    public static bool DenormalsFlushed()
    {
        try { return _controlfp_s(out var current, 0, 0) == 0 && (current & McwDn) == DnFlush; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>The calling thread: denormals flushed, and registered once with the Windows "Pro Audio" scheduling class (MMCSS, time critical): fewer dropouts at small buffers.</summary>
    internal static void PrepareAudioThread()
    {
        FlushDenormals();
        if (_mmcssHandle != IntPtr.Zero) return;   // already registered on this thread
        try { var index = 0; _mmcssHandle = AvSetMmThreadCharacteristicsW("Pro Audio", ref index); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [ThreadStatic]
    private static IntPtr _mmcssHandle;

    /// <summary>
    /// For a device-driver thread the engine does not create itself (NAudio's DirectSound polling thread runs at normal
    /// priority): MMCSS "Pro Audio" at critical priority plus the highest thread priority, so a busy UI or GC thread cannot delay a refill.
    /// </summary>
    internal static void RaiseCritical()
    {
        try { Thread.CurrentThread.Priority = ThreadPriority.Highest; } catch (Exception ex) when (ex is ThreadStateException or InvalidOperationException) { }
        try { if (_mmcssHandle != IntPtr.Zero) AvSetMmThreadPriority(_mmcssHandle, 2); }   // AVRT_PRIORITY_CRITICAL
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }



    /// <summary>
    /// The engine is a background process behind TabForge's window: without this Windows 11 may put it on efficiency cores
    /// ("EcoQoS") and share its CPU with whatever else runs (a plug-in's editor redrawing while a knob is dragged made the
    /// audio stutter). High priority and no power throttling keep the audio steady, as in a DAW.
    /// </summary>
    public static void PrepareProcess()
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { } // swallow: best-effort priority boost; the engine runs at normal priority when it is refused
        try
        {
            var state = new ProcessPowerThrottlingState { Version = 1, ControlMask = 1, StateMask = 0 };   // execution speed: never throttle
            SetProcessInformation(Process.GetCurrentProcess().Handle, 4, ref state, System.Runtime.InteropServices.Marshal.SizeOf<ProcessPowerThrottlingState>());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }


}
