using System.Diagnostics;
using System.IO.Pipes;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Midi;
using TabForge.AudioEngine.Mixing;

namespace TabForge.AudioEngine;

/// <summary>
/// Entry point of the audio engine process (TabForge.exe --audio-engine &lt;session&gt; &lt;parent pid&gt;).
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
    private static EngineSession _session = new();

    /// <summary>The shared block with TabForge (null in a process that is neither the engine nor the headless harness).</summary>
    internal static SharedBlock? Shared => _shared;

    /// <summary>True once the engine is ending (pipe closed, Shutdown command, parent gone).</summary>
    internal static bool Exiting => _exit;

    public static int Run(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], out var parentId)) return 2;
        _session = new EngineSession();   // before any thread starts: the session owns every per-run collection
        EngineThreads.MarkMainThread();
        var s = _session;
        var session = args[1];
        EngineThreads.PrepareProcess();
        // 1 ms timer resolution for the engine's lifetime: isolated plug-in waits and the main loop's timed waits stay precise.
        NativeTimer.Acquire();
        _timerHeld = true;
        if (session.Length is < 8 or > 64 || !session.All(char.IsLetterOrDigit)) return 2;
        Process? parent;
        try { parent = Process.GetProcessById(parentId); s.UiProcessId = parentId; }
        catch (ArgumentException) { return 3; }

        try
        {
            _shared = SharedBlock.Open(EngineNames.SharedMemory(session));
            // Asynchronous (overlapped) handle: a pending read must never block replies written from another thread.
            _pipe = new NamedPipeClientStream(".", EngineNames.Pipe(session), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _pipe.Connect(5000);
        }
        catch (Exception ex) { EngineLog.Write($"engine could not connect: {ex.Message}"); return 4; }

        var reader = new Thread(ReadCommands) { IsBackground = true, Name = "TabForge engine commands" };
        reader.Start();
        var lastIdle = Stopwatch.GetTimestamp();
        var lastMetrics = Stopwatch.GetTimestamp();
        var lastSessionCheck = Stopwatch.GetTimestamp();
        var lastLog = lastIdle;
        new Thread(MainThreadWatchdog) { IsBackground = true, Name = "TabForge engine watchdog" }.Start();
        // Tuner: while TabForge's tuner window is open and an input is open (a track armed), detect the pitch ~25 times a second.
        Audio.PitchDetector? tunerDetector = null; float[]? tunerWindow = null; var tunerRate = 0; var lastTuner = 0L; var tunerShownSilence = false;
        void TunerTick()
        {
            if (_shared is not { } sh) return;
            if (!sh.TunerOn || s.Input is not { } input)
            {
                if (!tunerShownSilence) { sh.SetTuner(0, 0); tunerShownSilence = true; }
                return;
            }
            if (Stopwatch.GetElapsedTime(lastTuner).TotalMilliseconds < 40) return;
            lastTuner = Stopwatch.GetTimestamp();
            if (tunerDetector is null || tunerRate != s.SampleRate)
            {
                tunerRate = s.SampleRate; tunerDetector = new Audio.PitchDetector(tunerRate); tunerWindow = new float[tunerDetector.RawWindow];
            }
            var clarity = 0f;
            var hz = input.CopyLatestMono(tunerWindow!) ? tunerDetector.Detect(tunerWindow!, out clarity) : 0f;
            if (hz == 0) clarity = 0;
            sh.SetTuner(hz, clarity);
            tunerShownSilence = hz == 0;
        }
        // ~30 Hz housekeeping (created once: the loop allocates nothing per turn); false ends the loop.
        bool IdleTick()
        {
            TunerTick();
            // Every 10 s while audio runs: callback timing in the engine log (p95 / p99 / max against the block
            // deadline, deadline misses = likely dropouts, late calls = the device starved us, audio-thread allocations).
            if (s.FollowingWindowsVolume && Stopwatch.GetElapsedTime(lastSessionCheck).TotalSeconds >= 1)
            {
                lastSessionCheck = Stopwatch.GetTimestamp();
                s.RefreshSessionVolume();
            }
            if (s.Mix is { } metricsMix && Stopwatch.GetElapsedTime(lastMetrics).TotalSeconds >= 10)
            {
                lastMetrics = Stopwatch.GetTimestamp();
                var m = metricsMix.Metrics.TakeAndReset();
                if (m.Calls > 0)
                    EngineLog.Write($"audio callback: {m.Calls} calls, p95 {m.P95Ms:0.0} ms, p99 {m.P99Ms:0.0} ms, max {m.MaxMs:0.0} ms, " +
                                    $"deadline misses {m.DeadlineMisses}, late calls {m.LateCalls}, allocated {m.AllocatedBytes} B, " +
                                    $"MIDI dropped {m.MidiDropped}, MIDI deferred {m.MidiDeferred}, device block {m.BlockFrames} frames");   // the granted size, which can differ from the requested one (WASAPI exclusive)
            }
            if (parent.HasExited) return false;
            // Watchdog: a plug-in that freezes the audio thread would silence everything. End the engine; the
            // breadcrumb names the plug-in, TabForge switches it off and restarts the engine without it.
            if (s.Player?.PlaybackState == PlaybackState.Playing && s.Mix is { } mix
                && Stopwatch.GetElapsedTime(mix.Heartbeat).TotalSeconds > 4)
            {
                EngineLog.Write("audio thread stopped responding; ending the engine so TabForge can recover");
                HardExit(EngineWatchdog.ExitAudioHung);   // TabForge blames the audio thread's breadcrumb, not a main-thread load
            }
            return true;
        }
        Func<bool> idle = IdleTick;
        while (!_exit)
        {
            Volatile.Write(ref _busySince, Stopwatch.GetTimestamp());
            EngineThreads.RunPending();
            Volatile.Write(ref _busySince, 0);
            if (s.Retired.Count > 0) s.Retired.Collect();
            s.ReportMisbehaving();
            if (s.LogWatch.Count > 0 && Stopwatch.GetElapsedTime(lastLog).TotalMilliseconds >= 50) { lastLog = Stopwatch.GetTimestamp(); s.PumpMidiLog(); }
            if (!EditorWindows.PumpOnce(ref lastIdle, idle)) break;
        }
        Shutdown();
        return 0;
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
                if (since == 0 || _shared is not { } shared) continue;
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
                        EngineLog.Write($"plug-in call slow ({slow.Kind}, {seconds} s so far, limit {EngineWatchdog.LimitSec(slow.Kind)} s): {slow.Path}");
                        Send(EngineEvent.PluginSlow, w => { w.Write(slow.Slot); w.Write(slow.Index); w.WriteString(slow.Path); w.Write((int)slow.Kind); w.Write(seconds); });
                        break;
                    case EngineWatchdog.Verdict.PluginHung when call is { } hung:
                        EngineLog.Write($"plug-in stuck for over {EngineWatchdog.LimitSec(hung.Kind)} s ({hung.Kind}): {hung.Path}; ending the engine so TabForge can recover");
                        HardExit(EngineWatchdog.ExitPluginHung);
                        break;
                    case EngineWatchdog.Verdict.UnattributedHang:
                        EngineLog.Write($"engine main thread busy for over {EngineWatchdog.UnattributedLimitSec} s outside any plug-in call; ending the engine (unattributed, nothing is blamed)");
                        HardExit(EngineWatchdog.ExitUnattributedHang);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
    }

    /// <summary>
    /// Watchdog exit: TerminateProcess ends the process at once with <paramref name="exitCode"/> (which TabForge reads). Unlike
    /// Environment.Exit it runs no ProcessExit handlers or finalizers, which could block on the very thread or lock that is stuck.
    /// Environment.Exit stays for the orderly path and is only the fallback here.
    /// </summary>
    internal static void HardExit(int exitCode)
    {
        EngineLog.Flush(1000);   // the reason for the exit must reach the log (TerminateProcess runs no handlers)
        try { TerminateProcess(GetCurrentProcess(), (uint)exitCode); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        Environment.Exit(exitCode);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    /// <summary>Consecutive malformed frames after which the reader gives up (fail closed: the engine ends and TabForge restarts it).</summary>
    private const int MaxBadFrames = 16;

    private static void ReadCommands()
    {
        var bad = 0;
        try
        {
            while (!_exit && _pipe is { IsConnected: true })
            {
                var frame = Frames.Read(_pipe);   // a broken stream (not a bad payload) ends the loop below
                if (frame is null) break;
                var (type, r) = frame.Value;
                if (HandleFrame(type, r)) { bad = 0; continue; }
                if (++bad >= MaxBadFrames) { EngineLog.Write($"{bad} malformed commands in a row; closing the command pipe"); break; }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException)
        {
            EngineLog.Write($"command pipe closed: {ex.Message}");
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
            EngineLog.Write($"command {(EngineCommand)type} dropped: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    internal static bool TestHooks => Environment.GetEnvironmentVariable("TABFORGE_ENGINE_TEST_HOOKS") == "1";

    /// <summary>
    /// Reader thread: parses one command (bounded reads) and posts its <see cref="EngineSession"/> handler to the main thread. Only the
    /// transport (SetPosition / SetTransport), chain-load assembly, RenderCancel and Panic act here, on state built for this thread.
    /// The posted lambdas read <see cref="_session"/> when they run, as before.
    /// </summary>
    private static void HandleCommand(byte type, BinaryReader r)
    {
                // Parse on this thread (bounded reads), act on the main thread.
                switch ((EngineCommand)type)
                {
                    case EngineCommand.Ping: { var seq = r.ReadInt32(); EngineThreads.Post(() => Send(EngineEvent.Pong, w => w.Write(seq))); break; }   // answered by the main thread: a deaf main thread misses it
                    case EngineCommand.TestHang:
                    {
                        var seconds = Math.Clamp(r.ReadInt32(), 1, 120);
                        if (!TestHooks) { EngineLog.Write("test hang ignored (test hooks are off)"); break; }
                        EngineThreads.Post(() => { EngineLog.Write($"test hook: main thread sleeps {seconds} s"); Thread.Sleep(seconds * 1000); });
                        break;
                    }
                    case EngineCommand.Configure: { var c = r.ReadConfig(); EngineThreads.Post(() => _session.Configure(c)); break; }
                    case EngineCommand.LoadChain:
                    {
                        _session.ChainLoads.OnLoadChain(r);   // the states follow one frame per plug-in; built on ChainCommit
                        break;
                    }
                    case EngineCommand.ChainState: _session.ChainLoads.OnChainState(r); break;
                    case EngineCommand.ChainCommit:
                    {
                        if (_session.ChainLoads.OnCommit(r) is not { } ready) break;
                        EngineThreads.Post(() => _session.CommitChain(ready));
                        break;
                    }
                    case EngineCommand.RemoveTrack: { var slot = r.ReadInt32(); EngineThreads.Post(() => _session.RemoveChain(slot)); break; }
                    case EngineCommand.OpenEditor:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var window = new IntPtr(r.ReadInt64()); var dark = r.ReadBoolean();
                        var docked = r.ReadBoolean(); var onTop = r.ReadBoolean();
                        EngineThreads.Post(() => _session.OpenEditor(slot, index, window, dark, docked, onTop));
                        break;
                    }
                    case EngineCommand.SetTrackMix:
                    {
                        var slot = r.ReadInt32(); var volume = r.ReadInt32(); var pan = r.ReadInt32();
                        if (slot < 0 || slot >= MixEngine.TotalSlots) throw new InvalidDataException($"SetTrackMix: slot {slot} out of range");
                        EngineThreads.Post(() => _session.SetTrackMix(slot, volume, pan));
                        break;
                    }
                    case EngineCommand.SetPluginGain:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var db = r.ReadDouble();
                        var gain = db <= -59.9 ? 0f : (float)Math.Pow(10, Math.Clamp(db, -60, 12) / 20);
                        EngineThreads.Post(() => _session.SetPluginGain(slot, index, gain));
                        break;
                    }
                    case EngineCommand.SetPluginBypass:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var enabled = r.ReadBoolean();
                        EngineThreads.Post(() => _session.SetPluginBypass(slot, index, enabled));
                        break;
                    }
                    case EngineCommand.SetSynth:
                    {
                        var slot = r.ReadInt32(); var on = r.ReadBoolean();
                        if (slot < 0 || slot >= MixEngine.TotalSlots) break;
                        EngineThreads.Post(() => _session.SetSynth(slot, on));
                        break;
                    }
                    case EngineCommand.SetMidiRoute:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var source = r.ReadInt32(); var mask = r.ReadInt32() & 0xFFFF;
                        if (source < -2 || source >= MixEngine.MaxSlots || slot < 0 || slot >= MixEngine.MaxSlots) break;
                        EngineThreads.Post(() => _session.SetMidiRoute(slot, index, source, mask));
                        break;
                    }
                    case EngineCommand.SetPluginWiring:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var flags = r.ReadInt32();
                        if (slot < 0 || slot >= MixEngine.TotalSlots) break;
                        EngineThreads.Post(() => _session.SetPluginWiring(slot, index, flags));
                        break;
                    }
                    case EngineCommand.SetMidiProcessors:
                    {
                        var slot = r.ReadInt32(); var count = r.ReadInt32();
                        if (slot < 0 || slot >= MixEngine.TotalSlots || count is < 0 or > 64) break;
                        var lists = new Dictionary<int, List<MidiProcSpec>>();
                        for (var i = 0; i < count; i++) { var index = r.ReadInt32(); var specs = r.ReadMidiProcs(); if (index is >= 0 and < 64 && specs.Count > 0) lists[index] = specs; }
                        EngineThreads.Post(() => _session.SetMidiProcessors(slot, lists));
                        break;
                    }
                    case EngineCommand.SetGraph:
                    {
                        static int Slot(int s, int max) => s >= 0 && s < max ? s : -1;
                        var dests = new Dictionary<int, int>(); var sides = new Dictionary<(int, int), int>(); var fwds = new Dictionary<(int, int), int>();
                        var n = Math.Clamp(r.ReadInt32(), 0, MixEngine.MaxSlots);
                        for (var i = 0; i < n; i++) { var s = r.ReadInt32(); var d = r.ReadInt32(); if (Slot(s, MixEngine.MaxSlots) >= 0 && d >= MixEngine.BusBase && d < MixEngine.MasterSlot) dests[s] = d; }
                        n = Math.Clamp(r.ReadInt32(), 0, 4096);
                        for (var i = 0; i < n; i++) { var s = r.ReadInt32(); var x = r.ReadInt32(); var src = r.ReadInt32(); if (Slot(s, MixEngine.MaxSlots) >= 0 && Slot(src, MixEngine.MaxSlots) >= 0) sides[(s, x)] = src; }
                        n = Math.Clamp(r.ReadInt32(), 0, 4096);
                        for (var i = 0; i < n; i++) { var s = r.ReadInt32(); var x = r.ReadInt32(); var dst = r.ReadInt32(); if (Slot(s, MixEngine.MaxSlots) >= 0 && Slot(dst, MixEngine.MaxSlots) >= 0) fwds[(s, x)] = dst; }
                        EngineThreads.Post(() => _session.SetGraph(dests, sides, fwds));
                        break;
                    }
                    case EngineCommand.SetMidiLogWatch:
                    {
                        var slot = r.ReadInt32(); var watch = r.ReadBoolean();
                        if (slot < 0 || slot >= MixEngine.MaxSlots) break;
                        EngineThreads.Post(() => _session.SetMidiLogWatch(slot, watch));
                        break;
                    }
                    case EngineCommand.SetClips:
                    {
                        var slot = r.ReadInt32(); var clips = r.ReadClips(); var owner = r.ReadOwnerTail();
                        if (slot < 0 || slot >= MixEngine.MaxSlots) throw new InvalidDataException($"SetClips: track slot {slot} out of range");
                        EngineThreads.Post(() => _session.SetClips(slot, clips, owner));
                        break;
                    }
                    case EngineCommand.SetArm:
                    {
                        var slot = r.ReadInt32(); var armed = r.ReadBoolean(); var mode = Math.Clamp(r.ReadInt32(), 0, 2); var monitor = r.ReadBoolean();
                        if (slot < 0 || slot >= MixEngine.MaxSlots) throw new InvalidDataException($"SetArm: track slot {slot} out of range");
                        EngineThreads.Post(() => _session.SetArm(slot, armed, mode, monitor));
                        break;
                    }
                    case EngineCommand.SetPosition:
                    {
                        var playing = r.ReadBoolean(); var songSec = r.ReadDouble(); var stamp = r.ReadInt64(); var owner = r.ReadOwnerTail();
                        // Reader thread. The position lives in the owner's SongTransport (not in the MixEngine, which Configure
                        // replaces on the main thread), published as one immutable record: never torn, never lost to a mixer swap.
                        if (double.IsFinite(songSec)) _session.Transports[owner].SetPosition(playing, Math.Max(0, songSec), stamp);
                        break;
                    }
                    case EngineCommand.Record:
                    {
                        var start = r.ReadBoolean(); var folder = r.ReadBoundedString(1024); var names = new Dictionary<int, string>();
                        var count = Math.Clamp(r.ReadInt32(), 0, 256);
                        for (var i = 0; i < count; i++)
                        {
                            var slot = r.ReadInt32(); var name = r.ReadBoundedString(256);
                            if (slot < 0 || slot >= MixEngine.MaxSlots) throw new InvalidDataException($"Record: track slot {slot} out of range");
                            names[slot] = name;
                        }
                        // The user's recording offset (ms, double) follows; an older sender has none.
                        var offsetMs = r.BaseStream.Position < r.BaseStream.Length ? r.ReadDouble() : 0;
                        if (!double.IsFinite(offsetMs)) throw new InvalidDataException("Record: offset is not a number");
                        offsetMs = Math.Clamp(offsetMs, -Audio.TakeAlignment.MaxOffsetMs, Audio.TakeAlignment.MaxOffsetMs);
                        var owner = r.ReadOwnerTail();   // the recording song's transport (absent: owner 0)
                        EngineThreads.Post(() => _session.Record(start, folder, names, offsetMs, owner));
                        break;
                    }
                    case EngineCommand.CloseEditor:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32();
                        EngineThreads.Post(() => _session.CloseEditor(slot, index));
                        break;
                    }
                    case EngineCommand.GetPrograms:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32();
                        EngineThreads.Post(() => _session.SendPrograms(slot, index));
                        break;
                    }
                    case EngineCommand.SetProgram:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var program = r.ReadInt32();
                        EngineThreads.Post(() => _session.SetProgram(slot, index, program));
                        break;
                    }
                    case EngineCommand.SetState:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var state = r.ReadBoundedString(PluginStateLimits.MaxBase64Chars);
                        EngineThreads.Post(() => _session.SetState(slot, index, state));
                        break;
                    }
                    case EngineCommand.GetStates: { var slot = r.ReadInt32(); var requestId = r.ReadInt32(); EngineThreads.Post(() => _session.SendStates(slot, requestId)); break; }
                    case EngineCommand.RenderOffline:
                    {
                        RenderSpec spec;
                        try { spec = RenderSpec.Read(r); }
                        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
                        {
                            Send(EngineEvent.RenderFailed, w => { w.WriteString("The render request was not valid: " + ex.Message); w.Write(false); w.WriteString(""); });
                            break;
                        }
                        EngineThreads.Post(() => _session.BeginRender(spec));
                        break;
                    }
                    case EngineCommand.RenderCancel: _session.RenderCancel = true; break;   // reader thread: works while the engine renders
                    case EngineCommand.Panic: { var s = _session; if (!s.RenderActive) Volatile.Read(ref s.Mix)?.Panic(); break; }
                    case EngineCommand.PanicSlots:
                    {
                        var s = _session;
                        var count = r.ReadInt32();
                        if (count is < 0 or > 4096) break;
                        var mix = s.RenderActive ? null : Volatile.Read(ref s.Mix);
                        for (var i = 0; i < count; i++) { var slot = r.ReadInt32(); mix?.ChainAt(slot)?.Panic(); }
                        break;
                    }
                    case EngineCommand.SetTransport:
                    {
                        var tempo = r.ReadDouble(); r.ReadBoolean();   // playing: SetPosition carries it
                        // The song's bar map (time signatures, bar starts, tempo per bar) follows; an older sender has none.
                        var bars = r.BaseStream.Position < r.BaseStream.Length ? TransportMap.Read(r) : null;
                        var owner = r.ReadOwnerTail();
                        var transport = _session.Transports[owner];
                        if (double.IsFinite(tempo)) transport.SetTempo(Math.Clamp(tempo, 1, 2000));
                        if (bars is not null) transport.SetMap(bars);
                        break;
                    }
                    case EngineCommand.MeasurePitch: ReadMeasurePitch(r); break;
                    case EngineCommand.SetAutoPitch: ReadSetAutoPitch(r); break;
                    case EngineCommand.SetWindowsPathOffset: { var db = Math.Clamp(r.ReadSingle(), -60f, 12f); EngineThreads.Post(() => _session.SetWindowsPathOffset(db)); break; }
                    case EngineCommand.SetLiveLimiter: { var on = r.ReadBoolean(); EngineThreads.Post(() => _session.SetLiveLimiter(on)); break; }
                    case EngineCommand.Shutdown: _exit = true; break;
                }
    }

    /// <summary>Any thread: one event frame to TabForge (dropped when there is no pipe, as in the headless harness).</summary>
    internal static void Send(EngineEvent type, Action<BinaryWriter>? payload = null)
    {
        try { if (_pipe is { IsConnected: true } pipe) Frames.Write(pipe, (byte)type, payload); }
        catch (IOException) { _exit = true; }
    }

    private static void Shutdown()
    {
        _exit = true;
        _session.Shutdown();
        try { _pipe?.Dispose(); } catch (Exception ex) { EngineLog.Write($"shutdown: pipe: {ex.Message}"); }
        try { _shared?.Dispose(); } catch (Exception ex) { EngineLog.Write($"shutdown: shared: {ex.Message}"); }
        if (_timerHeld) { _timerHeld = false; NativeTimer.Release(); }
        EngineLog.Flush();
    }

    private static bool _timerHeld;
}
