using System;
using System.Runtime.InteropServices;

namespace TabForge.Avalonia.Platform;

// Owns: the start-up check that this process runs below Medium integrity, and the notice that explains it.
// Does not own: the message box (AvaloniaMessageBox) or when it is shown (App).
// Tests: TabForge.Avalonia.Tests.ProcessIntegrityTests.
/// <summary>
/// Detects a Low-integrity start. Windows starts an exe at the lower of the parent's integrity and the exe file's label, so a
/// build inside a folder an agent sandbox labelled Low (or a launch from that sandbox) runs at Low integrity: every write to
/// Videos, Music, Documents and %LocalAppData% is then refused with "Access is denied" and Windows treats the exe as a download.
/// </summary>
internal static class ProcessIntegrity
{
    /// <summary>SECURITY_MANDATORY_MEDIUM_RID: a normal (non-elevated) user process.</summary>
    internal const int MediumRid = 0x2000;

    internal const string NoticeCaption = "TabForge is running restricted";

    internal const string Notice =
        "Windows started TabForge at Low integrity, so it cannot record audio or video, autosave, or save settings in your user folders "
        + "(you will see \"Access is denied\").\n\n"
        + "This happens when TabForge.exe was built in, or started from, a folder or terminal that an agent sandbox restricted. "
        + "Close TabForge and start it from Explorer, or clear the folder's label in a normal terminal:\n"
        + "icacls \"<TabForge folder>\" /setintegritylevel (OI)(CI)M /T /C /Q";

    /// <summary>Untrusted (0) and Low (0x1000) are below Medium; Medium, High and System are not.</summary>
    internal static bool IsBelowMedium(int rid) => rid < MediumRid;

    /// <summary>The integrity RID of this process's token, or null when it cannot be read (or not Windows).</summary>
    internal static int? CurrentRid()
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
