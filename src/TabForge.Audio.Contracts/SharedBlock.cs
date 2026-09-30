using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace TabForge.Audio.Contracts;

/// <summary>One timed MIDI message for a track slot in the engine (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TimedMidi
{
    /// <summary>When it should sound, in Stopwatch (QPC) ticks — the same clock in both processes.</summary>
    public long Timestamp;
    public int Slot;
    public byte Status, Data1, Data2, Flags;
}

/// <summary>
/// Shared memory between TabForge and the engine:
/// - a lock-free single-producer / single-consumer ring of <see cref="TimedMidi"/> (TabForge writes, the engine's
///   audio thread reads), so notes never wait on a pipe or a lock;
/// - a crash "breadcrumb": the plug-in call in progress, readable by TabForge after the engine dies;
/// - status written by the engine (latency, CPU load).
/// Layout is fixed; both sides map the same named block.
/// </summary>
public sealed unsafe class SharedBlock : IDisposable
{
    public const int RingCapacity = 8192; // power of two
    private const int HeaderBytes = 256;
    private const int BreadcrumbChars = 520;
    public const int MeterSlots = 256;
    public static readonly long Size = HeaderBytes + RingCapacity * sizeof(TimedMidi) + 2 * MeterSlots * sizeof(float);

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _base;

    // Header offsets
    private const int OffWrite = 0, OffRead = 8, OffLatencyTicks = 16, OffCpu = 24, OffCrumbActive = 32, OffCrumbSlot = 36,
        OffCrumbIndex = 40, OffCrumbLen = 44, OffSampleRate = 48;
    private readonly char[] _crumb = new char[BreadcrumbChars];
    private readonly byte* _crumbText;

    // Offline render: one breadcrumb per worker thread (the single-slot one above cannot say which of several parallel
    // plug-in calls crashed). Entry: int active, slot, index, length, then BreadcrumbChars chars.
    public const int MaxRenderWorkers = 32;
    private const int RenderCrumbStride = 16 + BreadcrumbChars * 2;
    private static long TotalSize => Size + BreadcrumbChars * 2 + MaxRenderWorkers * RenderCrumbStride + MainCrumbStride;
    private byte* RenderCrumb(int worker) => _base + Size + BreadcrumbChars * 2 + worker * RenderCrumbStride;

    // The engine MAIN thread's own breadcrumb (loads, state calls, editors), separate from the audio thread's per-block one above, so a
    // running audio callback cannot clear it. Entry: int active, slot, index, length, kind, pad; long start ticks, long left ticks
    // (Stopwatch, the same clock in both processes); then BreadcrumbChars chars.
    private const int MainCrumbStride = 40 + BreadcrumbChars * 2;
    private byte* MainCrumb => _base + Size + BreadcrumbChars * 2 + MaxRenderWorkers * RenderCrumbStride;

    private SharedBlock(MemoryMappedFile file)
    {
        _file = file;
        _view = file.CreateViewAccessor(0, TotalSize);
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _base = p;
        _crumbText = p + Size;
    }

    public static SharedBlock Create(string name) =>
        new(MemoryMappedFile.CreateNew(name, TotalSize, MemoryMappedFileAccess.ReadWrite));

    public static SharedBlock Open(string name) =>
        new(MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite));

    private long* WriteIndex => (long*)(_base + OffWrite);
    private long* ReadIndex => (long*)(_base + OffRead);
    private TimedMidi* Ring => (TimedMidi*)(_base + HeaderBytes);

    /// <summary>Producer (TabForge): false when the ring is full (the event is dropped rather than blocking).</summary>
    public bool TryWrite(in TimedMidi e)
    {
        var write = Volatile.Read(ref *WriteIndex);
        var read = Volatile.Read(ref *ReadIndex);
        if (write - read >= RingCapacity) return false;
        Ring[write & (RingCapacity - 1)] = e;
        Volatile.Write(ref *WriteIndex, write + 1);
        return true;
    }

    /// <summary>Consumer (engine audio thread): allocation-free.</summary>
    public bool TryRead(out TimedMidi e)
    {
        var read = Volatile.Read(ref *ReadIndex);
        if (read >= Volatile.Read(ref *WriteIndex)) { e = default; return false; }
        e = Ring[read & (RingCapacity - 1)];
        Volatile.Write(ref *ReadIndex, read + 1);
        return true;
    }

    /// <summary>Engine: how far behind "now" plug-in audio is heard (TabForge delays Windows MIDI by this).</summary>
    public long LatencyTicks { get => Volatile.Read(ref *(long*)(_base + OffLatencyTicks)); set => Volatile.Write(ref *(long*)(_base + OffLatencyTicks), value); }

    /// <summary>Engine: audio processing load, 0..1 of the available time per block.</summary>
    public double CpuLoad { get => *(double*)(_base + OffCpu); set => *(double*)(_base + OffCpu) = value; }

    public int SampleRate { get => Volatile.Read(ref *(int*)(_base + OffSampleRate)); set => Volatile.Write(ref *(int*)(_base + OffSampleRate), value); }

    // Tuner (header bytes 56..71): TabForge sets TunerOn while its tuner window is open; the engine then writes the detected pitch.
    private const int OffTunerOn = 56, OffTunerHz = 60, OffTunerClarity = 64, OffTunerSeq = 68;
    public bool TunerOn { get => Volatile.Read(ref *(int*)(_base + OffTunerOn)) != 0; set => Volatile.Write(ref *(int*)(_base + OffTunerOn), value ? 1 : 0); }

    /// <summary>Engine: the tuner's latest reading (Hz = 0: silent or not pitched).</summary>
    public void SetTuner(float hz, float clarity)
    {
        *(float*)(_base + OffTunerHz) = hz; *(float*)(_base + OffTunerClarity) = clarity;
        Volatile.Write(ref *(int*)(_base + OffTunerSeq), *(int*)(_base + OffTunerSeq) + 1);
    }

    /// <summary>TabForge: the tuner's latest reading and a counter that changes with every new one.</summary>
    public (float Hz, float Clarity, int Seq) Tuner => (*(float*)(_base + OffTunerHz), *(float*)(_base + OffTunerClarity), Volatile.Read(ref *(int*)(_base + OffTunerSeq)));

    private float* Meters => (float*)(_base + HeaderBytes + RingCapacity * sizeof(TimedMidi));

    /// <summary>Engine: a track slot's output peak for the last block (0..1+).</summary>
    public void SetPeak(int slot, float peak) { if (slot is >= 0 and < MeterSlots) Meters[slot] = peak; }

    /// <summary>TabForge: a track slot's latest output peak.</summary>
    public float Peak(int slot) => slot is >= 0 and < MeterSlots ? Meters[slot] : 0;

    private float* InputMeters => Meters + MeterSlots;

    /// <summary>Engine: an armed track's input peak (before its effects), for the record-arm level meter.</summary>
    public void SetInputPeak(int slot, float peak) { if (slot is >= 0 and < MeterSlots) InputMeters[slot] = peak; }

    /// <summary>TabForge: an armed track's latest input peak.</summary>
    public float InputPeak(int slot) => slot is >= 0 and < MeterSlots ? InputMeters[slot] : 0;

    // RT-07 (audio thread only): the path string whose text the breadcrumb holds now. A plug-in's Path is one string instance for its
    // lifetime, so a reference compare skips the copy (up to 520 chars) on every call but the first after a change.
    private string? _crumbPath;

    /// <summary>Engine audio thread: marks a plug-in call as in progress (cheap: the path text is copied only when the path instance changes).</summary>
    public void EnterPlugin(int slot, int index, string path)
    {
        *(int*)(_base + OffCrumbSlot) = slot;
        *(int*)(_base + OffCrumbIndex) = index;
        if (!ReferenceEquals(path, _crumbPath))
        {
            var length = Math.Min(path.Length, BreadcrumbChars);
            var text = (char*)_crumbText;
            for (var i = 0; i < length; i++) text[i] = path[i];
            *(int*)(_base + OffCrumbLen) = length;
            _crumbPath = path;
        }
        Volatile.Write(ref *(int*)(_base + OffCrumbActive), 1);
    }

    public void LeavePlugin() => Volatile.Write(ref *(int*)(_base + OffCrumbActive), 0);

    /// <summary>Engine, offline render worker <paramref name="worker"/> (0..31): marks its plug-in call as in progress.</summary>
    public void EnterRender(int worker, int slot, int index, string path)
    {
        if (worker is < 0 or >= MaxRenderWorkers) return;
        var p = RenderCrumb(worker);
        *(int*)(p + 4) = slot; *(int*)(p + 8) = index;
        var length = Math.Min(path.Length, BreadcrumbChars);
        var text = (char*)(p + 16);
        for (var i = 0; i < length; i++) text[i] = path[i];
        *(int*)(p + 12) = length;
        Volatile.Write(ref *(int*)p, 1);
    }

    public void LeaveRender(int worker)
    {
        if (worker is >= 0 and < MaxRenderWorkers) Volatile.Write(ref *(int*)RenderCrumb(worker), 0);
    }

    /// <summary>The plug-in calls in progress on the render workers (the engine's watchdog and TabForge after a crash read it).</summary>
    public List<(int Slot, int Index, string Path)> ActiveRenderCalls()
    {
        var list = new List<(int, int, string)>();
        for (var w = 0; w < MaxRenderWorkers; w++)
        {
            var p = RenderCrumb(w);
            if (Volatile.Read(ref *(int*)p) == 0) continue;
            var length = Math.Clamp(*(int*)(p + 12), 0, BreadcrumbChars);
            list.Add((*(int*)(p + 4), *(int*)(p + 8), new string((char*)(p + 16), 0, length)));
        }
        return list;
    }

    /// <summary>Engine main thread: a plug-in call of <paramref name="kind"/> starts (the watchdog allows it <see cref="EngineWatchdog.LimitSec"/>).</summary>
    /// <param name="blame">
    /// False for a call into an isolated plug-in: a bounded wait on its own process. It marks the main thread as busy for a reason
    /// (not deaf, not an unattributed hang) but is never blamed for a crash or a hang, and the per-kind limit does not apply.
    /// </param>
    public void EnterMain(int slot, int index, string path, PluginCallKind kind, bool blame = true)
    {
        var p = MainCrumb;
        *(int*)(p + 4) = slot; *(int*)(p + 8) = index; *(int*)(p + 16) = (int)kind; *(int*)(p + 20) = blame ? 1 : 0;
        var length = Math.Min(path.Length, BreadcrumbChars);
        var text = (char*)(p + 40);
        for (var i = 0; i < length; i++) text[i] = path[i];
        *(int*)(p + 12) = length;
        Volatile.Write(ref *(long*)(p + 24), System.Diagnostics.Stopwatch.GetTimestamp());
        Volatile.Write(ref *(int*)p, 1);
    }

    /// <summary>Engine main thread: the call ended (its end time is kept: a chain load is many short attributed calls).</summary>
    public void LeaveMain()
    {
        var p = MainCrumb;
        Volatile.Write(ref *(long*)(p + 32), System.Diagnostics.Stopwatch.GetTimestamp());
        Volatile.Write(ref *(int*)p, 0);
    }

    /// <summary>The engine main thread's plug-in call in progress (with its kind and start), if any.</summary>
    public (int Slot, int Index, string Path, PluginCallKind Kind, long StartTicks, bool Blame)? MainCall()
    {
        var p = MainCrumb;
        if (Volatile.Read(ref *(int*)p) == 0) return null;
        var length = Math.Clamp(*(int*)(p + 12), 0, BreadcrumbChars);
        return (*(int*)(p + 4), *(int*)(p + 8), new string((char*)(p + 40), 0, length), (PluginCallKind)(*(int*)(p + 16)),
            Volatile.Read(ref *(long*)(p + 24)), *(int*)(p + 20) != 0);
    }

    /// <summary>When the engine main thread's last plug-in call ended (Stopwatch ticks; 0 = never).</summary>
    public long MainCallLeftTicks => Volatile.Read(ref *(long*)(MainCrumb + 32));

    /// <summary>True while a main-thread plug-in call runs or one ended within <paramref name="seconds"/> (TabForge: the engine is busy, not deaf).</summary>
    public bool MainCallRecent(double seconds)
    {
        if (Volatile.Read(ref *(int*)MainCrumb) != 0) return true;
        var left = MainCallLeftTicks;
        return left != 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - left) < seconds * System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>TabForge, after the audio-thread watchdog ended the engine: the audio thread's plug-in call, if any.</summary>
    public (int Slot, int Index, string Path)? AudioPluginCall()
    {
        if (Volatile.Read(ref *(int*)(_base + OffCrumbActive)) == 0) return null;
        var length = Math.Clamp(*(int*)(_base + OffCrumbLen), 0, BreadcrumbChars);
        return (*(int*)(_base + OffCrumbSlot), *(int*)(_base + OffCrumbIndex), new string((char*)_crumbText, 0, length));
    }

    /// <summary>TabForge, after the engine died: the plug-in call that was running, if any.</summary>
    /// <remarks>While rendering in parallel several calls run at once: the answer is null unless exactly one was (retry the render single-threaded to pin it down).</remarks>
    public (int Slot, int Index, string Path)? LastPluginCall()
    {
        var renders = ActiveRenderCalls();
        if (renders.Count == 1) return renders[0];
        if (renders.Count > 1) return null;
        // A main-thread call (load, state, editor) is the prime suspect: it is also what a hang kill is about.
        if (MainCall() is { } main)
        {
            if (main.Blame) return (main.Slot, main.Index, main.Path);
            // A bounded wait on an isolated plug-in: not a suspect; fall through to the audio thread's crumb.
        }
        if (Volatile.Read(ref *(int*)(_base + OffCrumbActive)) == 0) return null;
        var length = Math.Clamp(*(int*)(_base + OffCrumbLen), 0, BreadcrumbChars);
        var text = (char*)_crumbText;
        for (var i = 0; i < length; i++) _crumb[i] = text[i];
        return (*(int*)(_base + OffCrumbSlot), *(int*)(_base + OffCrumbIndex), new string(_crumb, 0, length));
    }

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
    }
}
