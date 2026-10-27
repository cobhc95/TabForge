using System.Diagnostics;
using System.Runtime.Versioning;

namespace TabForge.AudioEngine.Isolation;

// Owns: the two wake-up signals of a plug-in host block ("request" and "done"): a named Windows event, or on other systems a flag inside the shared block that the waiter polls.
// Does not own: the block layout (PluginHostBlock) or the generation protocol.
// Tests: CrossPlatformStage5Tests (the flag signal and a block round trip run on Windows too).
/// <summary>An auto-reset signal between the engine and a plug-in host process.</summary>
internal abstract class BlockSignal : IDisposable
{
    public abstract void Set();

    /// <summary>Waits up to <paramref name="timeoutMs"/> (-1: for ever); true when the signal was set, which also resets it.</summary>
    public abstract bool WaitOne(int timeoutMs);

    public virtual void Dispose() { }
}

/// <summary>The Windows signal: a named, auto-reset event (unchanged from before the portable path existed).</summary>
[SupportedOSPlatform("windows")]
internal sealed class EventBlockSignal : BlockSignal
{
    private readonly EventWaitHandle _event;

    private EventBlockSignal(EventWaitHandle handle) => _event = handle;

    public static EventBlockSignal Create(string name) => new(new EventWaitHandle(false, EventResetMode.AutoReset, name));
    public static EventBlockSignal Open(string name) => new(EventWaitHandle.OpenExisting(name));

    public override void Set() => _event.Set();
    public override bool WaitOne(int timeoutMs) => _event.WaitOne(timeoutMs);
    public override void Dispose() => _event.Dispose();
}

/// <summary>
/// The portable signal: an int in the shared block. Set writes 1; the waiter takes it back to 0. The waiter spins briefly (a block is due within milliseconds),
/// then sleeps a millisecond at a time, so an idle plug-in host costs almost no CPU.
/// </summary>
internal sealed unsafe class FlagBlockSignal : BlockSignal
{
    private const double SpinMilliseconds = 0.2;
    private readonly int* _flag;

    public FlagBlockSignal(int* flag) => _flag = flag;

    public override void Set() => Interlocked.Exchange(ref *_flag, 1);

    public override bool WaitOne(int timeoutMs)
    {
        var start = Stopwatch.GetTimestamp();
        var spin = new SpinWait();
        while (true)
        {
            if (Interlocked.CompareExchange(ref *_flag, 0, 1) == 1) return true;
            var waited = Stopwatch.GetElapsedTime(start);
            if (timeoutMs >= 0 && waited.TotalMilliseconds >= timeoutMs) return false;
            if (waited.TotalMilliseconds < SpinMilliseconds) spin.SpinOnce(-1);   // spin and yield for the first 0.2 ms
            else Thread.Sleep(1);
        }
    }
}
