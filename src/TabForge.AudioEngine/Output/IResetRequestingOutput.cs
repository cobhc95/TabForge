namespace TabForge.AudioEngine.Output;

// Owns: the one shape of "the device asks to be reopened" that an output other than ASIO's own driver can raise (the headless output, in tests).

/// <summary>An output that can ask the engine to reopen it, as an ASIO driver does with its reset request.</summary>
public interface IResetRequestingOutput
{
    /// <summary>The device wants to be closed and opened again (a driver panel change, a sample rate change).</summary>
    event EventHandler? ResetRequested;
}
