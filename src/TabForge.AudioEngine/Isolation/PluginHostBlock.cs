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
/// is waiting for, so a late answer to an earlier block is never taken for the current one. Fixed layout.
/// </summary>
public sealed unsafe class PluginHostBlock : IDisposable
{
    public const int MaxEvents = 512;
    private const int Header = 64;
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _p;
    public readonly int MaxBlock;
    private readonly BlockSignal _request, _done;

    public static string Name(string id) => $"Local\\TabForge.PluginHost.{id}";

    private PluginHostBlock(MemoryMappedFile file, int maxBlock, Func<PluginHostBlock, (BlockSignal Request, BlockSignal Done)> signals)
    {
        _file = file;
        MaxBlock = maxBlock;
        _view = file.CreateViewAccessor(0, SizeFor(maxBlock));
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _p = p;
        (_request, _done) = signals(this);
    }

    private static long SizeFor(int maxBlock) => Header + MaxEvents * sizeof(BlockMidi) + 4L * maxBlock * sizeof(float);

    // Windows: named page-file map and named events, as always. Elsewhere (or when portable is forced, as the tests do): a file-backed map and polled flags in the header's spare ints.
    public static PluginHostBlock Create(string id, int maxBlock) => Create(id, maxBlock, portable: !OperatingSystem.IsWindows());

    internal static PluginHostBlock Create(string id, int maxBlock, bool portable) => !portable && OperatingSystem.IsWindows()
        ? CreateWindows(id, maxBlock)
        : new(PortableMemory.Create(Name(id), SizeFor(maxBlock)), maxBlock, b => (b.Flag(RequestFlag), b.Flag(DoneFlag)));

    [SupportedOSPlatform("windows")]
    private static PluginHostBlock CreateWindows(string id, int maxBlock) =>
        new(MemoryMappedFile.CreateNew(Name(id), SizeFor(maxBlock)), maxBlock,
            _ => (EventBlockSignal.Create(Name(id) + ".req"), EventBlockSignal.Create(Name(id) + ".done")));

    public static PluginHostBlock Open(string id, int maxBlock) => Open(id, maxBlock, portable: !OperatingSystem.IsWindows());

    internal static PluginHostBlock Open(string id, int maxBlock, bool portable) => !portable && OperatingSystem.IsWindows()
        ? OpenWindows(id, maxBlock)
        : new(PortableMemory.Open(Name(id)), maxBlock, b => (b.Flag(RequestFlag), b.Flag(DoneFlag)));

    [SupportedOSPlatform("windows")]
    private static PluginHostBlock OpenWindows(string id, int maxBlock) =>
        new(MemoryMappedFile.OpenExisting(Name(id)), maxBlock,
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
    /// so a coarse system timer can neither end the wait early nor stretch it by more than one timer tick.
    /// </summary>
    public bool WaitDone(int generation, double budgetMs)
    {
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        while (true)
        {
            var remaining = (deadline - System.Diagnostics.Stopwatch.GetTimestamp()) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (remaining <= 0) return Volatile.Read(ref DoneGeneration) == generation;
            if (_done.WaitOne((int)remaining) && Volatile.Read(ref DoneGeneration) == generation) return true;
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
    public Span<float> Channel(int index) => new(_p + Header + MaxEvents * sizeof(BlockMidi) + (long)index * MaxBlock * sizeof(float), MaxBlock);
    // Channels: 0 = in L, 1 = in R, 2 = out L, 3 = out R.

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
        _request.Dispose();
        _done.Dispose();
    }
}
