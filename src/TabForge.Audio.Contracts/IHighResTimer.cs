namespace TabForge.Audio.Contracts;

// Owns: the port for the process-wide high-resolution (about 1 ms) timer request; the reference counting stays in NativeTimer.

/// <summary>
/// Asks the operating system for a finer timer resolution and gives it back. Windows: <c>timeBeginPeriod(1)</c> (Platform/Windows/WindowsHighResTimer.cs);
/// elsewhere the system timer is already fine enough, so the stub does nothing. Not for the audio thread.
/// </summary>
public interface IHighResTimer
{
    /// <summary>Raises the resolution; true when the system accepted it.</summary>
    bool Begin();

    /// <summary>Gives back what <see cref="Begin"/> raised.</summary>
    void End();
}

/// <summary>The non-Windows stub: nothing to raise, so nothing is held.</summary>
public sealed class NullHighResTimer : IHighResTimer
{
    public bool Begin() => false;
    public void End() { }
}
