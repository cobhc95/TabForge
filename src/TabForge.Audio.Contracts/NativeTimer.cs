using System.Runtime.InteropServices;

namespace TabForge.Audio.Contracts;

/// <summary>
/// D10 / RT-10: the one owner of the process's 1 ms Windows timer resolution (<c>timeBeginPeriod(1)</c>). Reference-counted: the first
/// <see cref="Acquire"/> raises it, the last <see cref="Release"/> gives it back, so callers (the engine for its lifetime, the playback
/// scheduler while playing, the MIDI delay line while it has messages due) never undo each other. Not for the audio thread (takes a lock).
/// </summary>
public static class NativeTimer
{
    private static readonly object Gate = new();
    private static int _holds;
    private static bool _raised;

    /// <summary>Current number of holders (the resolution is raised while it is above zero).</summary>
    public static int Holds { get { lock (Gate) return _holds; } }

    /// <summary>True while this process holds the 1 ms resolution.</summary>
    public static bool Raised { get { lock (Gate) return _raised; } }

    public static void Acquire()
    {
        lock (Gate)
        {
            if (_holds++ > 0) return;
            try { _raised = timeBeginPeriod(1) == 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _raised = false; }
        }
    }

    /// <summary>Releases one <see cref="Acquire"/>; extra releases are ignored.</summary>
    public static void Release()
    {
        lock (Gate)
        {
            if (_holds == 0 || --_holds > 0) return;
            if (!_raised) return;
            _raised = false;
            try { timeEndPeriod(1); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
    }

    /// <summary>A holder that is either holding or not (idempotent on/off): used by owners with a playing / stopped state.</summary>
    public sealed class Hold
    {
        private int _held;
        public bool IsHeld => Volatile.Read(ref _held) != 0;

        public void Set(bool on)
        {
            if (on) { if (Interlocked.Exchange(ref _held, 1) == 0) Acquire(); }
            else if (Interlocked.Exchange(ref _held, 0) != 0) Release();
        }
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
