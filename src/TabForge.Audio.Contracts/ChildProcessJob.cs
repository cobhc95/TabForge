using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TabForge.Audio.Contracts;

/// <summary>
/// R-08: a Windows Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE. Child processes added to it (TabForge's audio engine; the
/// engine's isolated plug-in hosts) end when the job's last handle closes, which Windows does when the owning process ends for any
/// reason (Task Manager, debugger, crash). The owners' HasExited polling stays as a second layer. Children a member starts join
/// the job too (nested jobs, Windows 8+).
/// </summary>
public sealed class ChildProcessJob : IDisposable
{
    private IntPtr _handle;

    /// <summary>This process's job, kept for its whole lifetime (closed by Windows when the process ends).</summary>
    public static ChildProcessJob ForThisProcess { get; } = new();

    public ChildProcessJob() : this(0, TimeSpan.Zero) { }

    /// <summary>
    /// A kill-on-close job that also caps the committed memory of all its processes together (<paramref name="jobMemoryLimitBytes"/>)
    /// and their total user-mode CPU time (<paramref name="cpuTimeLimit"/>); Windows ends every process in the job when either is
    /// exceeded. Zero = no such limit. Used by the Guitar Pro import worker (A5-07).
    /// </summary>
    public ChildProcessJob(long jobMemoryLimitBytes, TimeSpan cpuTimeLimit)
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero) return;   // no job (very old Windows / restricted): the polling layer still ends orphans
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = { LimitFlags = JobObjectLimitKillOnJobClose } };
        if (jobMemoryLimitBytes > 0)
        {
            info.BasicLimitInformation.LimitFlags |= JobObjectLimitJobMemory;
            info.JobMemoryLimit = (UIntPtr)(ulong)jobMemoryLimitBytes;
        }
        if (cpuTimeLimit > TimeSpan.Zero)
        {
            info.BasicLimitInformation.LimitFlags |= JobObjectLimitJobTime;
            info.BasicLimitInformation.PerJobUserTimeLimit = cpuTimeLimit.Ticks;   // 100 ns units, like TimeSpan ticks
        }
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info, (uint)size))
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    public bool IsActive => _handle != IntPtr.Zero;

    /// <summary>Puts <paramref name="process"/> into the job; false when that was not possible (it then relies on the polling layer).</summary>
    public bool Add(Process process)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return AssignProcessToJobObject(_handle, process.Handle); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>Closes the job handle: every process still in the job is ended.</summary>
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const uint JobObjectLimitJobTime = 0x4;
    private const uint JobObjectLimitJobMemory = 0x200;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
