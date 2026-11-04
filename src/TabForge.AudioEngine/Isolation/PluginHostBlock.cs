using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

// Owns: the shared memory for one isolated plug-in (audio in/out, MIDI and transport, generation echo, fixed layout).

/// <summary>
/// Shared memory for one plug-in running in its own process: the block's audio in/out, its MIDI and transport.
/// The engine fills it, stamps a new request generation and signals "request"; the plug-in host processes, echoes
/// that generation as "done generation" and signals "done". The engine accepts only the answer for the generation it
/// is waiting for, so a late answer to an earlier block is never taken for the current one. With two slots each
/// generation owns its own four channels, so a timed-out block is never overwritten while the child still owns it.
/// Fixed layout; the 64-bit host keeps one slot and the 32-bit helper uses two (see <see cref="BufferSlots"/>).
/// </summary>
public sealed unsafe class PluginHostBlock : IDisposable
{
    public const int MaxEvents = 512;
    private const int Header = 64;
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _p;
    public readonly int MaxBlock;
    public readonly int BufferSlots;
    private readonly BlockSignal _request, _done;
    private int _active, _aborted, _disposed;

    public static string Name(string id) => $"Local\\TabForge.PluginHost.{id}";

    private PluginHostBlock(MemoryMappedFile file, int maxBlock, int bufferSlots, Func<PluginHostBlock, (BlockSignal Request, BlockSignal Done)> signals)
    {
        _file = file;
        MaxBlock = maxBlock;
        BufferSlots = Math.Clamp(bufferSlots, 1, 2);
        _view = file.CreateViewAccessor(0, SizeFor(maxBlock, BufferSlots));
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _p = p;
        (_request, _done) = signals(this);
    }

    private static long SizeFor(int maxBlock, int bufferSlots) =>
        Header + MaxEvents * sizeof(BlockMidi) + (long)Math.Clamp(bufferSlots, 1, 2) * 4 * maxBlock * sizeof(float);

    // Windows: named page-file map and named events, as always. Elsewhere (or when portable is forced, as the tests do): a file-backed map and polled flags in the header's spare ints.
    public static PluginHostBlock Create(string id, int maxBlock) => Create(id, maxBlock, portable: !OperatingSystem.IsWindows());

    /// <summary>The bridge opens two slots; the existing 64-bit isolation host stays on one (the default).</summary>
    public static PluginHostBlock Create(string id, int maxBlock, int bufferSlots) => Create(id, maxBlock, !OperatingSystem.IsWindows(), bufferSlots);

    /// <summary>
    /// The Wine bridge block: an explicit file at <paramref name="path"/> (not a hashed name), with the request/done flags
    /// in the header polled by both sides. The caller deletes the file after <see cref="Dispose"/>.
    /// </summary>
    public static PluginHostBlock CreateFile(string path, int maxBlock, int bufferSlots) =>
        new(PortableMemory.CreateAt(path, SizeFor(maxBlock, bufferSlots)), maxBlock, bufferSlots, b => (b.Flag(RequestFlag), b.Flag(DoneFlag)));

    /// <summary>Opens an explicit file block (tests and tools); the file must already have the exact size.</summary>
    public static PluginHostBlock OpenFile(string path, int maxBlock, int bufferSlots) =>
        new(PortableMemory.OpenAt(path), maxBlock, bufferSlots, b => (b.Flag(RequestFlag), b.Flag(DoneFlag)));

    internal static PluginHostBlock Create(string id, int maxBlock, bool portable, int bufferSlots = 1) => !portable && OperatingSystem.IsWindows()
        ? CreateWindows(id, maxBlock, bufferSlots)
        : new(PortableMemory.Create(Name(id), SizeFor(maxBlock, bufferSlots)), maxBlock, bufferSlots, b => (b.Flag(RequestFlag), b.Flag(DoneFlag)));

    [SupportedOSPlatform("windows")]
    private static PluginHostBlock CreateWindows(string id, int maxBlock, int bufferSlots) =>
        new(MemoryMappedFile.CreateNew(Name(id), SizeFor(maxBlock, bufferSlots)), maxBlock, bufferSlots,
            _ => (EventBlockSignal.Create(Name(id) + ".req"), EventBlockSignal.Create(Name(id) + ".done")));

    public static PluginHostBlock Open(string id, int maxBlock) => Open(id, maxBlock, portable: !OperatingSystem.IsWindows());

    internal static PluginHostBlock Open(string id, int maxBlock, bool portable, int bufferSlots = 1) => !portable && OperatingSystem.IsWindows()
        ? OpenWindows(id, maxBlock, bufferSlots)
        : new(PortableMemory.Open(Name(id)), maxBlock, bufferSlots, b => (b.Flag(RequestFlag), b.Flag(DoneFlag)));

    [SupportedOSPlatform("windows")]
    private static PluginHostBlock OpenWindows(string id, int maxBlock, int bufferSlots) =>
        new(MemoryMappedFile.OpenExisting(Name(id)), maxBlock, bufferSlots,
            _ => (EventBlockSignal.Open(Name(id) + ".req"), EventBlockSignal.Open(Name(id) + ".done")));

    private const int RequestFlag = 56, DoneFlag = 60;   // the header's two unused ints

    private BlockSignal Flag(int offset) => new FlagBlockSignal((int*)(_p + offset));

    public ref int Frames => ref *(int*)_p;
    public ref int EventCount => ref *(int*)(_p + 4);
    public ref double Tempo => ref *(double*)(_p + 8);
    public ref double Ppq => ref *(double*)(_p + 16);
    public ref int Playing => ref *(int*)(_p + 24);
    /// <summary>1 while the engine renders offline (the host switches the plug-in's mode before the next block).</summary>
    public ref int Offline => ref *(int*)(_p + 28);
    /// <summary>Generation of the request the engine last sent (written before "request" is signalled).</summary>
    public ref int RequestGeneration => ref *(int*)(_p + 32);
    /// <summary>Generation of the request the plug-in host last finished (written before "done" is signalled).</summary>
    public ref int DoneGeneration => ref *(int*)(_p + 36);
    /// <summary>The block's bar start (ppq) and time signature (numerator 0: unknown).</summary>
    public ref double BarStartPpq => ref *(double*)(_p + 40);
    public ref int TimeSigNumerator => ref *(int*)(_p + 48);
    public ref int TimeSigDenominator => ref *(int*)(_p + 52);

    /// <summary>Engine side: publishes <paramref name="generation"/> and signals the plug-in host. The block's inputs must be written first.</summary>
    public void Send(int generation)
    {
        Volatile.Write(ref RequestGeneration, generation);
        _request.Set();
    }

    /// <summary>
    /// Engine side, allocation-free: waits up to <paramref name="budgetMs"/> for the answer to <paramref name="generation"/>.
    /// A "done" for any other (earlier, late) generation is ignored and the wait continues. The deadline is measured,
    /// so a coarse system timer can neither end the wait early nor stretch it by more than one timer tick. A positive
    /// wait is never rounded down to zero: <see cref="BlockSignal.WaitOne"/> gets at least 1 ms, so the audio thread
    /// cannot spin through sub-millisecond iterations of this loop.
    /// </summary>
    public bool WaitDone(int generation, double budgetMs)
    {
        if (Volatile.Read(ref _aborted) != 0) return false;
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        while (true)
        {
            var remaining = (deadline - System.Diagnostics.Stopwatch.GetTimestamp()) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (remaining <= 0) return Volatile.Read(ref _aborted) == 0 && Volatile.Read(ref DoneGeneration) == generation;
            var waitMs = remaining >= 1 ? (int)remaining : 1;
            bool signalled;
            try { signalled = _done.WaitOne(waitMs); }
            catch (ObjectDisposedException) { return false; }
            if (Volatile.Read(ref _aborted) != 0) return false;
            if (signalled && Volatile.Read(ref DoneGeneration) == generation) return true;
        }
    }

    /// <summary>Plug-in host side: waits for a request and returns its generation.</summary>
    public bool TakeRequest(int timeoutMs, out int generation)
    {
        generation = 0;
        if (!_request.WaitOne(timeoutMs)) return false;
        generation = Volatile.Read(ref RequestGeneration);
        return true;
    }

    /// <summary>Plug-in host side: the outputs for <paramref name="generation"/> are written; tells the engine.</summary>
    public void Complete(int generation)
    {
        Volatile.Write(ref DoneGeneration, generation);
        _done.Set();
    }
    public Span<BlockMidi> Events => new(_p + Header, MaxEvents);
    /// <summary>The channels of the first slot (the existing 64-bit host's layout).</summary>
    public Span<float> Channel(int index) => ChannelSlot(0, index);

    /// <summary>
    /// The four channels (in L, in R, out L, out R) of the slot owned by <paramref name="generation"/>: slot 0 for a
    /// one-slot block, otherwise the generation's parity, so a late block never shares buffers with the next request.
    /// </summary>
    public Span<float> ChannelSlot(int generation, int index)
    {
        var slot = BufferSlots > 1 ? generation & 1 : 0;
        return new(_p + Header + MaxEvents * sizeof(BlockMidi) + (long)slot * 4 * MaxBlock * sizeof(float) + (long)index * MaxBlock * sizeof(float), MaxBlock);
    }
    // Channels: 0 = in L, 1 = in R, 2 = out L, 3 = out R.

    /// <summary>True once <see cref="Dispose"/> started: a processing call must not touch the mapped view after this.</summary>
    internal bool Disposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Audio thread: marks one processing call in flight, so <see cref="Dispose"/> cannot unmap under it. Allocation-free.</summary>
    internal void EnterProcess() => Interlocked.Increment(ref _active);

    /// <summary>Audio thread: ends the call marked with <see cref="EnterProcess"/>.</summary>
    internal void ExitProcess() => Interlocked.Decrement(ref _active);

    /// <summary>Wakes a waiter and refuses later answers before teardown, so a call inside <see cref="WaitDone"/> returns at once.</summary>
    internal void Abort()
    {
        Volatile.Write(ref _aborted, 1);
        try { _done.Set(); }
        catch (ObjectDisposedException) { /* swallow: already torn down. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Abort();   // a call already inside WaitDone must not wait for its whole budget during teardown
        var spin = new SpinWait();
        while (Volatile.Read(ref _active) != 0)
        {
            if (spin.NextSpinWillYield) Thread.Sleep(1); else spin.SpinOnce();
        }
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
        _request.Dispose();
        _done.Dispose();
    }
}
