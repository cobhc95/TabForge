namespace TabForge.AudioEngine;

// Owns: the Windows process-termination call behind EngineHost.HardExit.
public static partial class EngineHost
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
