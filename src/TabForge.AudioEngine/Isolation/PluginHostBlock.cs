using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

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
    public readonly EventWaitHandle Request, Done;

    public static string Name(string id) => $"Local\\TabForge.PluginHost.{id}";

    private PluginHostBlock(MemoryMappedFile file, int maxBlock, EventWaitHandle request, EventWaitHandle done)
    {
        _file = file;
        MaxBlock = maxBlock;
        _view = file.CreateViewAccessor(0, SizeFor(maxBlock));
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _p = p;
        Request = request;
        Done = done;
    }

    private static long SizeFor(int maxBlock) => Header + MaxEvents * sizeof(BlockMidi) + 4L * maxBlock * sizeof(float);

    public static PluginHostBlock Create(string id, int maxBlock) =>
        new(MemoryMappedFile.CreateNew(Name(id), SizeFor(maxBlock)), maxBlock,
            new EventWaitHandle(false, EventResetMode.AutoReset, Name(id) + ".req"), new EventWaitHandle(false, EventResetMode.AutoReset, Name(id) + ".done"));

    public static PluginHostBlock Open(string id, int maxBlock) =>
        new(MemoryMappedFile.OpenExisting(Name(id)), maxBlock,
            EventWaitHandle.OpenExisting(Name(id) + ".req"), EventWaitHandle.OpenExisting(Name(id) + ".done"));

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
    /// <summary>RT-04: the block's bar start (ppq) and time signature (numerator 0: unknown).</summary>
    public ref double BarStartPpq => ref *(double*)(_p + 40);
    public ref int TimeSigNumerator => ref *(int*)(_p + 48);
    public ref int TimeSigDenominator => ref *(int*)(_p + 52);

    /// <summary>Engine side: publishes <paramref name="generation"/> and signals the plug-in host. The block's inputs must be written first.</summary>
    public void Send(int generation)
    {
        Volatile.Write(ref RequestGeneration, generation);
        Request.Set();
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
            if (Done.WaitOne((int)remaining) && Volatile.Read(ref DoneGeneration) == generation) return true;
        }
    }

    /// <summary>Plug-in host side: waits for a request and returns its generation.</summary>
    public bool TakeRequest(int timeoutMs, out int generation)
    {
        generation = 0;
        if (!Request.WaitOne(timeoutMs)) return false;
        generation = Volatile.Read(ref RequestGeneration);
        return true;
    }

    /// <summary>Plug-in host side: the outputs for <paramref name="generation"/> are written; tells the engine.</summary>
    public void Complete(int generation)
    {
        Volatile.Write(ref DoneGeneration, generation);
        Done.Set();
    }
    public Span<BlockMidi> Events => new(_p + Header, MaxEvents);
    public Span<float> Channel(int index) => new(_p + Header + MaxEvents * sizeof(BlockMidi) + (long)index * MaxBlock * sizeof(float), MaxBlock);
    // Channels: 0 = in L, 1 = in R, 2 = out L, 3 = out R.

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
        Request.Dispose();
        Done.Dispose();
    }
}
