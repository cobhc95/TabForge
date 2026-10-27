namespace TabForge.Audio;

// Owns: the Windows high-resolution waitable timer of the MIDI delay line (kernel32 CreateWaitableTimerEx / SetWaitableTimer).
// Does not own: when the delay line waits on it (RoutedMidiOutput.cs).
public sealed partial class RoutedMidiOutput
{
    /// <summary>A one-shot wake-up that is not rounded to the system timer tick (null where the system has none).</summary>
    private sealed class HighResolutionTimer : WaitHandle
    {
        private const uint HighResolution = 0x2, AllAccess = 0x1F0003;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern Microsoft.Win32.SafeHandles.SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetWaitableTimer(Microsoft.Win32.SafeHandles.SafeWaitHandle timer, in long dueTime, int period, IntPtr routine, IntPtr argument, bool resume);

        private HighResolutionTimer(Microsoft.Win32.SafeHandles.SafeWaitHandle handle) => SafeWaitHandle = handle;

        public static HighResolutionTimer? TryCreate()
        {
            try
            {
                var handle = CreateWaitableTimerExW(IntPtr.Zero, null, HighResolution, AllAccess);
                if (!handle.IsInvalid) return new HighResolutionTimer(handle);
                handle.Dispose();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            return null;
        }

        /// <summary>Signals once, this many 100 ns units from now; false when the timer could not be armed.</summary>
        public bool Set(long hundredNs) => SetWaitableTimer(SafeWaitHandle, -Math.Max(1, hundredNs), 0, IntPtr.Zero, IntPtr.Zero, false);
    }
}
