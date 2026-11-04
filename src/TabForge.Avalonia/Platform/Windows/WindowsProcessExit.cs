using System;
using System.Runtime.InteropServices;

namespace TabForge.Avalonia.Platform;

// Owns: ending the one-shot matrix process without running plug-in DLL detach, which can block for ever.
// It lives in the Windows platform folder so the portability guard does not count the kernel32 imports.
// Does not own: the matrix run itself (Program routes it; the Diagnostics runner writes the result first).
// Tests: HelperModeEntryTests (the matrix mode refuses without the opt-in; the exit path runs in a child process).
internal static class WindowsProcessExit
{
    internal static void Terminate(int code)
    {
        try { TerminateProcess(GetCurrentProcess(), (uint)code); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Environment.Exit(code); }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
