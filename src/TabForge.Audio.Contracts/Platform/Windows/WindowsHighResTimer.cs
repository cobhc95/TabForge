using System.Runtime.InteropServices;

namespace TabForge.Audio.Contracts;

// Owns: the Windows timer-resolution calls (winmm timeBeginPeriod / timeEndPeriod) behind IHighResTimer.

/// <summary>The Windows adapter of <see cref="IHighResTimer"/>: <c>timeBeginPeriod(1)</c> / <c>timeEndPeriod(1)</c>.</summary>
public sealed class WindowsHighResTimer : IHighResTimer
{
    public bool Begin()
    {
        try { return timeBeginPeriod(1) == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public void End()
    {
        try { timeEndPeriod(1); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
