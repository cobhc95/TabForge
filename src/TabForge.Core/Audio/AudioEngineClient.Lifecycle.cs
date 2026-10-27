using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: the engine process lifecycle of the client (start, pipe connect, liveness, stop, crash restart, cleanup).
// Does not own: the command surface and event dispatch (AudioEngineClient.cs) or the engine process itself (TabForge.AudioEngine/EngineHost).
// Tests: AvaloniaEngineRoutingTests, AudioSmokeTests.
public sealed partial class AudioEngineClient
{
    private bool Start()
    {
        _stopping = false;
        ForgetSingleRateAsio();
        var session = Guid.NewGuid().ToString("N");
        var token = PipeAuth.NewToken();
        lock (_writeGate) { _retiredShared?.Dispose(); _retiredShared = null; }
        try
        {
            PublishShared(SharedBlock.Create(EngineNames.SharedMemory(session)));
            _pipe = new NamedPipeServerStream(EngineNames.Pipe(session), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);   // fail closed: a taken name fails the start
            var exe = EngineExeForTest ?? Environment.ProcessPath ?? throw new InvalidOperationException("TabForge's own path is unknown.");
            _process = Process.Start(EngineStartInfo.For(exe, session, Environment.ProcessId, UserPaths.Diagnostics, token)) ?? throw new InvalidOperationException("The audio engine did not start.");
            ChildProcessGuards.ForThisProcess.Add(_process);   // R-08: ends with TabForge, however TabForge ends
            _process.EnableRaisingEvents = true;
            // R-08: no blocking wait here. Commands sent until the engine connects are queued in order and flushed on connect
            // (Configure first), so Sync returns at once; the engine answers Ready once its device is open.
            lock (_sendGate) _queued = new List<byte[]>();
            _deafKill = false; _resyncNeeded = false; _readySeen = false;   // a fresh engine starts empty: nothing dropped for it, no earlier Ready
            IsRunning = true;
            _config = null; Output = null;
            var process = _process;
            var pipe = _pipe;
            process.Exited += (_, _) => OnEngineEnded(process);
            _ = ConnectAsync(pipe, process, token);
            RaiseOnUi(() => StatusChanged?.Invoke());
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or AggregateException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Services.Trace.Error(Services.Trace.Engine, "engine: start: " + ex.Message);
            RaiseOnUi(() => DeviceError?.Invoke($"The audio engine could not start: {ex.GetBaseException().Message}"));
            KillQuietly(_process);
            Cleanup();
            return false;
        }
    }

    /// <summary>Makes <paramref name="block"/> the running engine's block. A fresh block starts with the tuner off: whoever still wants it gets it on the new engine too.</summary>
    private void PublishShared(SharedBlock block)
    {
        block.TunerOn = TunerWanted;
        Volatile.Write(ref _shared, block);
    }

    /// <summary>Waits (off the UI thread) for the engine to connect: then flushes the queued commands and starts reading and pinging.</summary>
    private async Task ConnectAsync(NamedPipeServerStream pipe, Process process, string token)
    {
        var connected = false;
        try
        {
            // The peer must open with the launch token: a same-user process that only found the pipe name is refused and dropped (a silent one
            // after PipeAuth.PerPeerMs) and the pipe listens again until the real engine arrives or the start times out.
            connected = await PipeAuth.AcceptAsync(pipe, token, ConnectTimeoutMs).ConfigureAwait(false);
            if (!connected) Services.Trace.Error(Services.Trace.Engine, "engine: no pipe peer presented the launch token in time; refused");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { Services.Trace.Error(Services.Trace.Engine, "engine: wait for pipe connection: " + ex.Message); }
        if (!connected)
        {
            if (!ReferenceEquals(pipe, _pipe)) return;   // stopped meanwhile
            KillQuietly(process);                         // R-08: the timed-out child does not linger
            RaiseOnUi(() =>
            {
                if (!ReferenceEquals(pipe, _pipe)) return;
                DeviceError?.Invoke("The audio engine could not start: it did not answer.");
                Cleanup();
                StatusChanged?.Invoke();
            });
            return;
        }
        lock (_sendGate)
        {
            if (!ReferenceEquals(pipe, _pipe)) return;
            try { foreach (var frame in _queued ?? new List<byte[]>()) pipe.Write(frame); pipe.Flush(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Services.Trace.Error(Services.Trace.Engine, "engine: send queued frames: " + ex.Message); /* the engine is gone; OnEngineEnded handles it */ }
            _queued = null;
        }
        Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp());
        new Thread(() => ReadEvents(pipe)) { IsBackground = true, Name = "TabForge engine events" }.Start();
        var timer = new Timer(_ => CheckLiveness(pipe, process), null, 1000, 1000);
        Interlocked.Exchange(ref _liveness, timer)?.Dispose();
    }

    private const int ConnectTimeoutMs = 8000;
    private readonly object _sendGate = new();
    /// <summary>Frames sent while the engine is still connecting (null once connected).</summary>
    private List<byte[]>? _queued;
    private Timer? _liveness;
    private long _lastAlive;
    private int _pingSeq;
    private volatile bool _deafKill, _awaitingReady;

    /// <summary>
    /// R-05, once a second (timer thread): pings the engine, whose main thread answers. No answer for
    /// <see cref="EngineWatchdog.DeafLimitSec"/> while no plug-in call was in progress, no device was being opened and nothing renders:
    /// the engine is deaf; it is killed and the crash path restarts it, quarantining nothing (see <see cref="OnEngineEnded"/>). Work
    /// that is attributed to a plug-in call is judged by the engine's own watchdog (per-kind limits).
    /// </summary>
    private void CheckLiveness(NamedPipeServerStream pipe, Process process)
    {
        if (!ReferenceEquals(pipe, _pipe) || !IsRunning || _stopping || _deafKill) return;
        var now = Stopwatch.GetTimestamp();
        var busyAttributed = Volatile.Read(ref _shared)?.MainCallRecent(EngineWatchdog.DeafLimitSec) ?? false;
        if (busyAttributed || _awaitingReady || PanelHeldAt(now) || Rendering || _renderTask is { Task.IsCompleted: false }) Volatile.Write(ref _lastAlive, now);
        if (now - Volatile.Read(ref _lastAlive) > EngineWatchdog.DeafLimitSec * Stopwatch.Frequency)
        {
            _deafKill = true;
            Services.Trace.Error(Services.Trace.Engine, $"audio engine: no answer for {EngineWatchdog.DeafLimitSec} s outside plug-in calls; restarting it");
            KillQuietly(process);
            return;
        }
        Send(EngineCommand.Ping, new PingMessage(Interlocked.Increment(ref _pingSeq)).Write);
    }

    private static void KillQuietly(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { Services.Trace.Error(Services.Trace.Engine, "engine: kill process: " + ex.Message); }
    }

    /// <summary>How long a stopped engine may take to exit after Shutdown before it is killed.</summary>
    private const int ShutdownWaitMs = 1500;

    /// <summary>
    /// Stops the engine now (app exit, or the warm period ended; see <see cref="WarmIdle"/>). A Sync without engine tracks no longer does.
    /// The state changes at once; the wait for the process to exit (and the kill when it does not) runs on a worker unless
    /// <paramref name="waitForExit"/> is set (app exit, where nothing else is left to keep responsive).
    /// </summary>
    public void Stop(bool waitForExit = false)
    {
        if (!IsRunning) return;
        _stopping = true;
        Send(EngineCommand.Shutdown);
        var process = _process;
        _process = null;   // handed to the wait below, which disposes it; Cleanup leaves it alone
        if (waitForExit) WaitForExitOrKill(process);
        else _ = Task.Run(() => WaitForExitOrKill(process));
        Cleanup();
        RaiseOnUi(() => StatusChanged?.Invoke());
    }

    private static void WaitForExitOrKill(Process? process)
    {
        try { if (process is { HasExited: false } p && !p.WaitForExit(ShutdownWaitMs)) p.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Services.Trace.Error(Services.Trace.Engine, "engine: wait for exit: " + ex.Message); }
        finally { process?.Dispose(); }
    }

    /// <summary>
    /// Process.Exited (a thread-pool thread): only the breadcrumb is read here (the block stays mapped until the next Start).
    /// Everything else, including Cleanup of the UI-owned dictionaries, runs on the UI thread, and only if that engine is
    /// still the current one (a Stop or a restart in between wins).
    /// </summary>
    private void OnEngineEnded(Process? process)
    {
        if (_stopping || !IsRunning) return;
        // Unexpected exit: which plug-in call was running? Only a crash or an attributed hang blames one. A deaf-engine restart (R-05)
        // or the engine's own unattributed-hang exit (71) quarantines nothing, even if an audio block happened to be in a plug-in.
        var exitCode = ExitCodeOf(process);
        var unattributed = _deafKill || exitCode == EngineWatchdog.ExitUnattributedHang;
        var shared = Volatile.Read(ref _shared);
        var crumb = unattributed ? null : exitCode == EngineWatchdog.ExitAudioHung ? shared?.AudioPluginCall() : shared?.LastPluginCall();
        var crumbPath = crumb?.Path ?? "";
        RaiseOnUi(() =>
        {
            if (_stopping || !IsRunning || !ReferenceEquals(process, _process)) return;
            FailRender(new RenderException("The audio engine stopped while rendering" + (crumbPath.Length > 0 ? $" (in {System.IO.Path.GetFileName(crumbPath)})" : "") + ".", pluginPath: crumbPath));
            Cleanup();
            var path = crumb?.Path;
            if (path is { Length: > 0 })
            {
                var quarantine = Quarantine?.Invoke();
                if (quarantine is not null && !quarantine.Contains(path, StringComparer.OrdinalIgnoreCase)) quarantine.Add(path);
            }
            CrashLeftEngineRunning = false;
            PluginCrashed?.Invoke(path ?? "");
            _crashes.Add(DateTime.UtcNow);
            _crashes.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(1));
            StatusChanged?.Invoke();
        });
    }

    private static int? ExitCodeOf(Process? process)
    {
        try { return process is { HasExited: true } ? process.ExitCode : null; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; } // Not logged: exit code probe: null when not exited or not accessible
    }

    /// <summary>After a crash the app calls Sync again; this says whether restarting is still sensible.</summary>
    public bool TooManyCrashes => _crashes.Count >= 3;

    private void Cleanup()
    {
        IsRunning = false;
        FailRender(new RenderException("The audio engine was stopped."));
        _slots.Clear();
        _sentTrackMix.Clear();
        _sentChains.Clear();
        _sentChainIds.Clear();
        lock (_chainLock) { _chainRequests.Clear(); _lastAcks.Clear(); }
        _sentAudio.Clear(); _audioContext.Clear();
        _sentRoutes.Clear();
        _sentProcessors.Clear();
        _sentWiring.Clear();
        _sentBypass.Clear();
        _sentGain.Clear();
        _sentSynth.Clear();
        _sentGraph = "";
        ResetWarmState();   // Stop, a crash or a failed start: nothing stays parked, no warm timer is left armed
        lock (_gate)
        {
            foreach (var request in _stateRequests.Values) request.Done.TrySetResult(false);   // unanswered plug-ins report as timed out
            _stateRequests.Clear();
            foreach (var request in _infoRequests.Values) request.Done.TrySetResult(false);   // unanswered: not available
            _infoRequests.Clear();
            foreach (var panel in _panelAsked.Values) panel.Asked.TrySetResult("The audio engine stopped before the control panel was shown.");   // a pending ShowAsioPanelAsync must not wait for a reply that cannot come
            _panelAsked.Clear();
        }
        Interlocked.Exchange(ref _liveness, null)?.Dispose();
        lock (_sendGate)
        {
            _queued = null;
            try { _pipe?.Dispose(); } catch (IOException) { } // swallow: pipe dispose during teardown: the pipe may already be closed.
            _pipe = null;
        }
        _awaitingReady = false; _resyncNeeded = false; _readySeen = false; _fakeLinkUp = false;
        AsioChannels = null;
        _engineProtocol = 0;
        // Unpublished, not disposed: a producer (scheduler thread) may have read it just before; see Write / Start.
        lock (_writeGate)
        {
            if (Volatile.Read(ref _shared) is { } shared) { _retiredShared?.Dispose(); _retiredShared = shared; }
            Volatile.Write(ref _shared, null);
        }
        _process?.Dispose();
        _process = null;
        _config = null; Output = null;
    }
}
