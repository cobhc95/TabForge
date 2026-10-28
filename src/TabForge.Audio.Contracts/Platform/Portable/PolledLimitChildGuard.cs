using System.Diagnostics;

namespace TabForge.Audio.Contracts;

// Owns: the import worker's memory and CPU-time limits on macOS and Linux: the parent polls each child's resident memory and CPU time
// (every 100 ms) and kills the child above either limit; kill on dispose and on owner exit come from an inner UnixChildProcessGuard.
// Does not own: the Windows Job Object (ChildProcessJob, kernel-enforced limits; stays the Windows guard), the wall-clock budget (ImportWorker's deadline timer)
// or the child-side end-with-parent watch (ImportWorker.Run).
// Why no setrlimit(RLIMIT_AS): the .NET GC reserves far more address space than it commits, so an address-space cap stops a healthy worker at start.
// Tests: CrossPlatformStage5Tests (a child over either limit is killed, one under both is not, a disposed guard is closed).
/// <summary>The portable <see cref="IChildProcessGuard"/> with limits: polls resident memory and CPU time of its children and kills one over budget.</summary>
public sealed class PolledLimitChildGuard : IChildProcessGuard
{
    private readonly UnixChildProcessGuard _inner = new();
    private readonly List<Process> _watched = new();
    private readonly long _memoryLimitBytes;
    private readonly TimeSpan _cpuTimeLimit;
    private readonly Func<Process, (long ResidentBytes, TimeSpan Cpu)> _read;
    private readonly Timer _timer;

    /// <summary>Zero = no such limit. <paramref name="read"/> is a test seam; the default asks the OS (working set, total processor time).</summary>
    public PolledLimitChildGuard(long memoryLimitBytes, TimeSpan cpuTimeLimit, TimeSpan? interval = null, Func<Process, (long ResidentBytes, TimeSpan Cpu)>? read = null)
    {
        _memoryLimitBytes = memoryLimitBytes;
        _cpuTimeLimit = cpuTimeLimit;
        _read = read ?? ReadUsage;
        var every = interval ?? TimeSpan.FromMilliseconds(100);
        _timer = new Timer(_ => Poll(), null, every, every);
    }

    public bool IsActive => _inner.IsActive;

    public bool Add(Process process)
    {
        if (!_inner.Add(process)) return false;
        lock (_watched) _watched.Add(process);
        return true;
    }

    /// <summary>True when the usage breaks a limit (zero limits are off).</summary>
    internal static bool IsOverLimit(long residentBytes, TimeSpan cpu, long memoryLimitBytes, TimeSpan cpuTimeLimit) =>
        (memoryLimitBytes > 0 && residentBytes > memoryLimitBytes) || (cpuTimeLimit > TimeSpan.Zero && cpu > cpuTimeLimit);

    private void Poll()
    {
        Process[] watched;
        lock (_watched)
        {
            _watched.RemoveAll(HasEnded);
            watched = _watched.ToArray();
        }
        foreach (var process in watched)
        {
            try
            {
                var (resident, cpu) = _read(process);
                if (!IsOverLimit(resident, cpu, _memoryLimitBytes, _cpuTimeLimit)) continue;
                Trace.TraceWarning($"Child process {process.Id} went over its limits ({resident / (1024 * 1024)} MiB, {cpu.TotalSeconds:0.#} s CPU) and was ended.");
                process.Kill(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { } // swallow: the child may have exited between the check and the read
        }
    }

    private static bool HasEnded(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; } // swallow: a process that is no longer reachable is not watched
    }

    private static (long, TimeSpan) ReadUsage(Process process)
    {
        process.Refresh();
        return (process.WorkingSet64, process.TotalProcessorTime);
    }

    public void Dispose()
    {
        _timer.Dispose();
        lock (_watched) _watched.Clear();
        _inner.Dispose();
    }
}
