using System.Diagnostics;

namespace TabForge.Audio.Contracts;

/// <summary>
/// Puts child processes under a kill-on-close guard, so they end with their owner however the owner ends.
/// Owns: the calls the owners make (add a process, ask whether the guard is live) and the disposal that ends every process in it.
/// Does not own: the Windows Job Object calls (the adapter is <see cref="ChildProcessJob"/>, in Platform/Windows/ChildProcessJob.cs).
/// Tests: TestChildProcessGuardPort (a live guard after creation, a closed guard after disposal).
/// </summary>
public interface IChildProcessGuard : IDisposable
{
    /// <summary>False when no guard was created; the owners then rely on their own polling to end orphans.</summary>
    bool IsActive { get; }

    /// <summary>Puts <paramref name="process"/> under the guard; false when that was not possible.</summary>
    bool Add(Process process);
}

/// <summary>The non-Windows stub: no kill-on-close guard exists, so nothing is added and the owners' HasExited polling ends orphans.</summary>
public sealed class NullChildProcessGuard : IChildProcessGuard
{
    public bool IsActive => false;
    public bool Add(Process process) => false;
    public void Dispose() { }
}
