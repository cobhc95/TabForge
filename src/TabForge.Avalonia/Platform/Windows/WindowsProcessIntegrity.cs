using System;
using System.Runtime.InteropServices;

namespace TabForge.Avalonia.Platform;

// Owns: the Windows token calls that read this process's integrity level (advapi32 / kernel32), behind IProcessIntegrity.
// Tests: TabForge.Avalonia.Tests.ProcessIntegrityTests.
/// <summary>The Windows adapter of <see cref="IProcessIntegrity"/>.</summary>
internal sealed class WindowsProcessIntegrity : IProcessIntegrity
{
    public int? CurrentRid()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var token = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) return null;
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return null;
            buffer = Marshal.AllocHGlobal(needed);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, needed, out _)) return null;
            var sid = Marshal.ReadIntPtr(buffer);   // TOKEN_MANDATORY_LABEL.Label.Sid
            int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count == 0 ? null : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
