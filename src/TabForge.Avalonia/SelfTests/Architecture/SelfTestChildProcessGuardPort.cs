using System;
using TabForge.Audio.Contracts;

namespace TabForge;

/// <summary>
/// The child-process guard port: the process-wide guard and a guard from the import worker's default factory are live after creation,
/// and a disposed guard reports closed. Read-only: no process is started or added to a job.
/// Owns: the adapter's live and closed states. Does not own: the port (src/TabForge.Audio.Contracts/IChildProcessGuard.cs), the Job Object
/// calls (src/TabForge.Audio.Contracts/ChildProcessJob.cs) or the import worker's use of the guard (src/TabForge.Core/Services/ImportWorker.cs).
/// Tests: TestChildProcessGuardPort.
/// </summary>
public static partial class SelfTest
{
    private static void TestChildProcessGuardPort()
    {
        IChildProcessGuard processGuard = ChildProcessJob.ForThisProcess;
        Check("the process-wide guard is live", processGuard.IsActive);

        var limited = TabForge.Services.ImportWorkerOptions.Default.CreateJobGuard(64L * 1024 * 1024, TimeSpan.FromSeconds(10));
        Check("a guard with memory and CPU limits is live", limited.IsActive);
        limited.Dispose();
        Check("a disposed guard reports closed", !limited.IsActive);
    }
}
