using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

// Owns: the Linux VST3 host run loop entry point of libtfvst3.so (tfv3_run_loop_wait: the timers and file descriptors the open editors registered).
// Does not own: the editor windows or the loop that calls it (Editors/X11EditorHost), the registry itself (native/tfvst3/posix/tfv3_runloop_linux.cpp).
// Threads: the engine's main thread only (the thread that opened the views); never the audio thread.
public sealed unsafe partial class Vst3Plugin
{
    /// <summary>tfv3_run_loop_wait of the loaded shim (zero: not there, e.g. the Windows tfvst3.dll or an older libtfvst3.so); probed once.</summary>
    private static readonly Lazy<IntPtr> RunLoopExport = new(() =>
        NativeLibrary.TryLoad(LibPath, out var lib) && NativeLibrary.TryGetExport(lib, "tfv3_run_loop_wait", out var export) ? export : IntPtr.Zero);

    /// <summary>
    /// Waits up to <paramref name="waitMs"/> for a plug-in descriptor, <paramref name="extraFd"/> (-1: none) or the next plug-in timer, and runs the
    /// handlers that are due. False (nothing waited) when this shim has no run loop: the caller then sleeps itself.
    /// </summary>
    internal static bool RunLoopWait(int extraFd, int waitMs)
    {
        var export = RunLoopExport.Value;
        if (export == IntPtr.Zero) return false;
        ((delegate* unmanaged<int, int, int>)export)(extraFd, waitMs);
        return true;
    }
}
