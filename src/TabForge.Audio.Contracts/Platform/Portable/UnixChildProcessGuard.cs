using System.Diagnostics;

namespace TabForge.Audio.Contracts;

// Owns: ending the engine, plug-in host and import-worker processes when their owner ends, on macOS and Linux: the owner kills the children it holds when it
// shuts down normally (dispose or process exit), and a child-side ParentWatchdog ends a child whose owner died hard (crash, kill -9).
// Does not own: the Windows Job Object (ChildProcessJob, kill-on-close by the kernel; stays the Windows guard) or memory/CPU limits (a Unix guard has none, so the import worker still asks before parsing in-process).
// Why no prctl(PR_SET_PDEATHSIG): the signal fires when the THREAD that started the child exits, and .NET starts processes from pool threads that come and go; it would kill a healthy engine.
// Tests: CrossPlatformStage5Tests.
/// <summary>The non-Windows <see cref="IChildProcessGuard"/>: kills every process added to it when disposed, and when the owner process exits.</summary>
public sealed class UnixChildProcessGuard : IChildProcessGuard
{
    private readonly List<Process> _children = new();
    private bool _disposed;

    public UnixChildProcessGuard() => AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

    public bool IsActive => !_disposed;

    public bool Add(Process process)
    {
        lock (_children)
        {
            if (_disposed) return false;
            _children.RemoveAll(p => p.HasExited);
            _children.Add(process);
            return true;
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        Process[] doomed;
        lock (_children)
        {
            if (_disposed) return;
            _disposed = true;
            doomed = _children.ToArray();
            _children.Clear();
        }
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        foreach (var process in doomed)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { Trace.TraceWarning("Could not end a child process: " + ex.Message); }
        }
    }
}

/// <summary>The guard of this OS for the processes this process starts: the Windows Job Object there, a <see cref="UnixChildProcessGuard"/> elsewhere.</summary>
public static class ChildProcessGuards
{
#if WINDOWS
    public static IChildProcessGuard ForThisProcess => ChildProcessJob.ForThisProcess;
#else
    public static IChildProcessGuard ForThisProcess { get; } = new UnixChildProcessGuard();
#endif
}

/// <summary>Child side: ends the child when the process that started it is gone (checked by process id and start time, so a reused id does not keep it alive).</summary>
public sealed class ParentWatchdog : IDisposable
{
    private readonly Timer _timer;

    /// <summary>Calls <paramref name="onGone"/> once when the parent has exited. <paramref name="isAlive"/> is a test seam; the default asks the OS.</summary>
    public ParentWatchdog(int parentPid, Action onGone, TimeSpan? interval = null, Func<int, DateTime?, bool>? isAlive = null)
    {
        var alive = isAlive ?? IsRunning;
        DateTime? started = null;
        try { started = Process.GetProcessById(parentPid).StartTime; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { started = null; } // swallow: unknown start time, the id alone is checked
        var fired = 0;
        _timer = new Timer(_ =>
        {
            if (alive(parentPid, started) || Interlocked.Exchange(ref fired, 1) == 1) return;
            onGone();
        }, null, interval ?? TimeSpan.FromSeconds(1), interval ?? TimeSpan.FromSeconds(1));
    }

    internal static bool IsRunning(int pid, DateTime? startedAt)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && (startedAt is null || process.StartTime == startedAt);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; } // swallow: no such process
    }

    public void Dispose() => _timer.Dispose();
}
