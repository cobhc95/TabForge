namespace TabForge.AudioEngine;

// Owns: the Windows native calls of AudioThreadSetup (C runtime float control, MMCSS "Pro Audio" registration, process power throttling).
// Does not own: when they are called (EngineThreads.cs).
public static partial class AudioThreadSetup
{
    [System.Runtime.InteropServices.DllImport("ucrtbase.dll")]
    private static extern int _controlfp_s(out uint current, uint newControl, uint mask);

    [System.Runtime.InteropServices.DllImport("avrt.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref int taskIndex);

    [System.Runtime.InteropServices.DllImport("avrt.dll")]
    private static extern bool AvSetMmThreadPriority(IntPtr handle, int priority);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref ProcessPowerThrottlingState info, int size);
}
