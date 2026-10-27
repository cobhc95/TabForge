using System.Diagnostics;
using System.IO.Pipes;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Midi;
using TabForge.AudioEngine.Mixing;

namespace TabForge.AudioEngine;

// Owns: the audio engine process entry point and IPC side (pipe, shared block, command reader, watchdogs, main loop).

/// <summary>
/// Entry point of the audio engine process (TabForge.exe --audio-engine &lt;session&gt; &lt;parent pid&gt; &lt;log file&gt;).
/// Connects to TabForge's pipe, opens the shared block, and runs: commands and plug-in windows on the main thread,
/// audio on the device thread. Exits when TabForge closes the pipe or its process ends.
/// This class keeps the process and IPC side only (pipe, shared block, command reader, watchdogs, main loop). The engine
/// state and the command handlers live in one <see cref="EngineSession"/> per run; the reader thread parses each command and posts
/// the matching session handler to the main thread.
/// </summary>
public static partial class EngineHost
{
    private static SharedBlock? _shared;
    private static NamedPipeClientStream? _pipe;
    private static volatile bool _exit;

    /// <summary>The per-session state and handlers (see <see cref="EngineSession"/>); <see cref="Run"/> starts a fresh one.</summary>
    private static EngineSession _session = new(new EngineLogWriter());
    private static EngineLogWriter Log => _session.Log;
    private static EngineThreads Threads => _session.Threads;

    /// <summary>The shared block with TabForge (null in a process that is neither the engine nor the headless harness).</summary>
    internal static SharedBlock? Shared => _shared;

    /// <summary>True once the engine is ending (pipe closed, Shutdown command, parent gone).</summary>
    internal static bool Exiting => _exit;

    public static int Run(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], out var parentId)) return 2;
        // The run's log file is named by TabForge (args[3]) and created new here; without one this run keeps no log.
        // The composition root of the engine process: one log writer, handed to the session (which owns the main-thread queue and passes both on).
        var log = new EngineLogWriter();
        if (args.Length > 3) log.CreateForRun(args[3]);
        _session = new EngineSession(log);   // before any thread starts: the session owns every per-run collection
        Threads.MarkMainThread();
        var s = _session;
        var session = args[1];
        AudioThreadSetup.PrepareProcess();
        // 1 ms timer resolution for the engine's lifetime: isolated plug-in waits and the main loop's timed waits stay precise.
        NativeTimer.Acquire();
        _timerHeld = true;
        if (session.Length is < 8 or > 64 || !session.All(char.IsLetterOrDigit)) return 2;
        if (args.Length < 5 || !PipeAuth.IsWellFormed(args[4])) return 2;   // no launch token: not started by TabForge
        Process? parent;
        try { parent = Process.GetProcessById(parentId); s.UiProcessId = parentId; }
        catch (ArgumentException) { return 3; }

        try
        {
            _shared = SharedBlock.Open(EngineNames.SharedMemory(session));
            // Asynchronous (overlapped) handle: a pending read must never block replies written from another thread.
            _pipe = new NamedPipeClientStream(".", EngineNames.Pipe(session), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _pipe.Connect(5000);
            PipeAuth.WriteHello(_pipe, args[4]);   // first bytes: TabForge refuses a peer without the launch token
        }
        catch (Exception ex) { Log.Write($"engine could not connect: {ex.Message}"); return 4; }

        // Markers that tell a crash from an orderly end: the log says how this run began, why it ended, and any exception that nobody caught.
        Log.Write($"engine started (pid {Environment.ProcessId}, TabForge pid {parentId})");
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        var reader = new Thread(ReadCommands) { IsBackground = true, Name = "TabForge engine commands" };
        reader.Start();
        var lastIdle = Stopwatch.GetTimestamp();
        var lastLog = lastIdle;
        new Thread(MainThreadWatchdog) { IsBackground = true, Name = "TabForge engine watchdog" }.Start();
        Func<bool> idle = new IdleWork(s, parent).Tick;
        while (!_exit)
        {
            Volatile.Write(ref _busySince, Stopwatch.GetTimestamp());
            Threads.RunPending();
            Volatile.Write(ref _busySince, 0);
            if (s.Retired.Count > 0) s.Retired.Collect();
            s.ReportMisbehaving();
            if (s.LogWatch.Count > 0 && Stopwatch.GetElapsedTime(lastLog).TotalMilliseconds >= 50) { lastLog = Stopwatch.GetTimestamp(); s.PumpMidiLog(); }
            if (!_session.Editors.PumpOnce(ref lastIdle, idle)) break;
        }
        Shutdown();
        return 0;
    }

    /// <summary>The ~30 Hz housekeeping of the main loop (created once: the loop allocates nothing per turn): tuner, input faults, volume follow, callback metrics and the watchdogs.</summary>
    private sealed class IdleWork
    {
        private readonly EngineSession _s;
        private readonly Process _parent;
        private long _lastMetrics = Stopwatch.GetTimestamp();
        private long _lastSessionCheck = Stopwatch.GetTimestamp();
        // Tuner: while the tuner window of TabForge is open and an input is open (a track armed), detect the pitch ~25 times a second.
        private Audio.PitchDetector? _tunerDetector; private float[]? _tunerWindow; private int _tunerRate; private long _lastTuner; private bool _tunerShownSilence;

        public IdleWork(EngineSession s, Process parent) { _s = s; _parent = parent; }

        private void TunerTick()
        {
            if (_shared is not { } sh) return;
            if (!sh.TunerOn || _s.Input is not { } input)
            {
                if (!_tunerShownSilence) { sh.SetTuner(0, 0); _tunerShownSilence = true; }
                return;
            }
            if (Stopwatch.GetElapsedTime(_lastTuner).TotalMilliseconds < 40) return;
            _lastTuner = Stopwatch.GetTimestamp();
            if (_tunerDetector is null || _tunerRate != _s.SampleRate)
            {
                _tunerRate = _s.SampleRate; _tunerDetector = new Audio.PitchDetector(_tunerRate); _tunerWindow = new float[_tunerDetector.RawWindowMax];
            }
            _tunerDetector.WindowMs = _s.TunerWindowMs;
            var clarity = 0f;
            var hz = input.CopyLatestMono(_tunerWindow!) ? _tunerDetector.Detect(_tunerWindow!, out clarity) : 0f;
            if (hz == 0) clarity = 0;
            sh.SetTuner(hz, clarity);
            _tunerShownSilence = hz == 0;
        }

        public bool Tick()
        {
            TunerTick();
            // An input that stopped (a fault in its callback, the device gone): one notice; the output keeps playing.
            if (_s.Input?.TakeFault() is { } inputFault) { Log.Write($"input stopped: {inputFault}"); Send(EngineEvent.InputError, w => w.WriteString($"Audio input stopped: {inputFault}")); }
            // Every 10 s while audio runs: callback timing in the engine log (p95 / p99 / max against the block
            // deadline, deadline misses = likely dropouts, late calls = the device starved us, audio-thread allocations).
            if (_s.FollowingWindowsVolume && Stopwatch.GetElapsedTime(_lastSessionCheck).TotalSeconds >= 1)
            {
                _lastSessionCheck = Stopwatch.GetTimestamp();
                _s.RefreshSessionVolume();
            }
            if (_s.Mix is { } metricsMix && Stopwatch.GetElapsedTime(_lastMetrics).TotalSeconds >= 10)
            {
                _lastMetrics = Stopwatch.GetTimestamp();
                var m = metricsMix.Metrics.TakeAndReset();
                // MIDI dropped (chain buffers full) / deferred (pending list full, ring kept it) / ring dropped (producer found the
                // shared ring full): late vs lost, each with its own fix (chain size, pending size, producer rate).
                if (m.Calls > 0)
                    Log.Write($"audio callback: {m.Calls} calls, p95 {m.P95Ms:0.0} ms, p99 {m.P99Ms:0.0} ms, max {m.MaxMs:0.0} ms, " +
                                    $"deadline misses {m.DeadlineMisses}, late calls {m.LateCalls}, allocated {m.AllocatedBytes} B, " +
                                    $"MIDI dropped {m.MidiDropped}, MIDI deferred {m.MidiDeferred}, ring dropped {(_shared is not null ? _shared.DroppedWrites : 0)}, device block {m.BlockFrames} frames");   // the granted size, which can differ from the requested one (WASAPI exclusive)
            }
            if (_parent.HasExited) { Log.Write("TabForge has exited; the engine ends"); return false; }
            // Watchdog: a plug-in that freezes the audio thread would silence everything. End the engine; the
            // breadcrumb names the plug-in, TabForge switches it off and restarts the engine without it.
            if (_s.Player?.PlaybackState == PlaybackState.Playing && _s.Mix is { } mix
                && Stopwatch.GetElapsedTime(mix.Heartbeat).TotalSeconds > 4)
            {
                Log.Write("audio thread stopped responding; ending the engine so TabForge can recover");
                HardExit(EngineWatchdog.ExitAudioHung);   // TabForge blames the audio thread's breadcrumb, not a main-thread load
            }
            return true;
        }
    }

    private static long _busySince;

    /// <summary>
    /// Separate thread: judges the engine main thread twice a second (<see cref="EngineWatchdog.Decide"/>). A plug-in call past
    /// its kind's limit (load / SetState 90 s, GetState 30 s, editor 20 s, other 10 s) ends the engine with 70 (TabForge blames that
    /// plug-in); a call past <see cref="EngineWatchdog.SlowNoticeSec"/> is reported once as slow ("still loading", not a crash); the main
    /// thread stuck 30 s outside any plug-in call ends it with 71 (unattributed: nothing is quarantined).
    /// </summary>
    private static void MainThreadWatchdog()
    {
        long notified = 0, notifiedLong = 0;
        while (!_exit)
        {
            Thread.Sleep(500);
            try
            {
                var since = Volatile.Read(ref _busySince);
                if (since == 0 || _session is { } session && EngineWatchdog.PanelHolds(Volatile.Read(ref session.PanelOpenedTicks), Stopwatch.GetTimestamp()) || _shared is not { } shared) continue;   // an ASIO control panel is modal: the user, not a hang, holds the main thread (up to its cap)
                var busySec = Stopwatch.GetElapsedTime(since).TotalSeconds;
                var call = shared.MainCall();
                if (call is { Blame: false }) continue;   // a bounded wait on an isolated plug-in: its own timeout applies
                var left = shared.MainCallLeftTicks;
                var sinceCallSec = left == 0 ? double.MaxValue : Stopwatch.GetElapsedTime(left).TotalSeconds;
                double? callSec = call is { } c ? Stopwatch.GetElapsedTime(c.StartTicks).TotalSeconds : null;
                switch (EngineWatchdog.Decide(busySec, callSec, call?.Kind ?? PluginCallKind.Other, sinceCallSec))
                {
                    // Reported once past SlowNoticeSec and once more past LongNoticeSec (TabForge offers to disable a very slow load).
                    case EngineWatchdog.Verdict.Slow when call is { } slow && (slow.StartTicks != notified
                        || (callSec >= EngineWatchdog.LongNoticeSec && slow.StartTicks != notifiedLong)):
                        notified = slow.StartTicks;
                        if (callSec >= EngineWatchdog.LongNoticeSec) notifiedLong = slow.StartTicks;
                        var seconds = (int)(callSec ?? 0);
                        Log.Write($"plug-in call slow ({slow.Kind}, {seconds} s so far, limit {EngineWatchdog.LimitSec(slow.Kind)} s): {slow.Path}");
                        Send(EngineEvent.PluginSlow, w => { w.Write(slow.Slot); w.Write(slow.Index); w.WriteString(slow.Path); w.Write((int)slow.Kind); w.Write(seconds); });
                        break;
                    case EngineWatchdog.Verdict.PluginHung when call is { } hung:
                        Log.Write($"plug-in stuck for over {EngineWatchdog.LimitSec(hung.Kind)} s ({hung.Kind}): {hung.Path}; ending the engine so TabForge can recover");
                        HardExit(EngineWatchdog.ExitPluginHung);
                        break;
                    case EngineWatchdog.Verdict.UnattributedHang:
                        Log.Write($"engine main thread busy for over {EngineWatchdog.UnattributedLimitSec} s outside any plug-in call; ending the engine (unattributed, nothing is blamed)");
                        HardExit(EngineWatchdog.ExitUnattributedHang);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Write("watchdog: check failed: " + ex); }
        }
    }

    /// <summary>
    /// Watchdog exit: TerminateProcess ends the process at once with <paramref name="exitCode"/> (which TabForge reads). Unlike
    /// Environment.Exit it runs no ProcessExit handlers or finalizers, which could block on the very thread or lock that is stuck.
    /// Environment.Exit stays for the orderly path and is only the fallback here.
    /// </summary>
    internal static void HardExit(int exitCode)
    {
        Log.Flush(1000);   // the reason for the exit must reach the log (TerminateProcess runs no handlers)
        try { TerminateProcess(GetCurrentProcess(), (uint)exitCode); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        Environment.Exit(exitCode);
    }


    /// <summary>Consecutive malformed frames after which the reader gives up (fail closed: the engine ends and TabForge restarts it).</summary>
    private const int MaxBadFrames = 16;

    private static void ReadCommands()
    {
        var bad = 0;
        try
        {
            while (!_exit && _pipe is { IsConnected: true })
            {
                var next = Frames.Read(_pipe, FrameLimits.ForCommand);   // a broken stream (not a bad payload) ends the loop below
                if (next is null) break;
                using var frame = next.Value;
                var (type, r) = frame;
                if (HandleFrame(type, r)) { bad = 0; continue; }
                if (++bad >= MaxBadFrames) { Log.Write($"{bad} malformed commands in a row; closing the command pipe"); break; }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException)
        {
            Log.Write($"command pipe closed: {ex.Message}");
        }
        _exit = true;
    }

    /// <summary>
    /// One command frame, parsed on the reader thread (bounded reads; the work is posted to the main thread). A payload that does
    /// not parse or is out of range (any exception, version skew included) drops only that frame and is logged with its command; the
    /// reader thread survives. Returns false for a dropped frame.
    /// </summary>
    private static bool HandleFrame(byte type, BinaryReader r)
    {
        try { HandleCommand(type, r); return true; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write($"command {(EngineCommand)type} dropped: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

#if TABFORGE_TEST_HOOKS
    /// <summary>Self-test hooks are on only when the engine process was started with TABFORGE_ENGINE_TEST_HOOKS=1 (and this build has them).</summary>
    internal static bool TestHooks => Environment.GetEnvironmentVariable("TABFORGE_ENGINE_TEST_HOOKS") == "1";
#endif

    /// <summary>Any thread: one event frame to TabForge (dropped when there is no pipe, as in the headless harness).</summary>
    internal static void Send(EngineEvent type, Action<BinaryWriter>? payload = null)
    {
        try { if (_pipe is { IsConnected: true } pipe) Frames.Write(pipe, (byte)type, payload); }
        catch (IOException) { _exit = true; }
    }

    private static void SendMasterAudio(int rate, long firstFrame, float[] data, int frames) =>
        Send(EngineEvent.MasterAudio, w => { w.Write(rate); w.Write(firstFrame); w.Write(frames); w.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan(0, frames * 2))); });

    private static void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        Log.Write("unhandled exception: " + e.ExceptionObject);
        Log.Flush(1000);   // the process is ending: the line must reach the file
    }

    private static void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => Log.Write("unobserved task exception: " + e.Exception);

    private static void Shutdown()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
        Log.Write("engine stopped");
        _exit = true;
        _session.Shutdown();
        try { _pipe?.Dispose(); } catch (Exception ex) { Log.Write($"shutdown: pipe: {ex.Message}"); }
        try { _shared?.Dispose(); } catch (Exception ex) { Log.Write($"shutdown: shared: {ex.Message}"); }
        if (_timerHeld) { _timerHeld = false; NativeTimer.Release(); }
        Log.Flush();
    }

    private static bool _timerHeld;
}
