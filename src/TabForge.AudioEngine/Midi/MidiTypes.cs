using System.Text.Json;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Midi;

/// <summary>A fixed-capacity list of block MIDI events. Preallocated; adding never allocates and silently drops past capacity.</summary>
public sealed class MidiBuffer
{
    public readonly BlockMidi[] Items;
    public int Count;
    /// <summary>Set by processors that emit events out of frame order (delay, humanize); the chain sorts before the next stage.</summary>
    public bool Unsorted;

    public MidiBuffer(int capacity = 1024) => Items = new BlockMidi[capacity];

    public void Clear() { Count = 0; Unsorted = false; }

    public bool Add(int frame, byte status, byte data1, byte data2)
    {
        if (Count >= Items.Length) return false;
        Items[Count++] = new BlockMidi { Frame = frame, Status = status, Data1 = data1, Data2 = data2 };
        return true;
    }

    public bool Add(in BlockMidi e)
    {
        if (Count >= Items.Length) return false;
        Items[Count++] = e;
        return true;
    }

    public ReadOnlySpan<BlockMidi> Span => new(Items, 0, Count);

    /// <summary>Stable insertion sort by frame (events are nearly sorted; no allocation).</summary>
    public void Sort()
    {
        for (var i = 1; i < Count; i++)
        {
            var x = Items[i];
            var j = i - 1;
            while (j >= 0 && Items[j].Frame > x.Frame) { Items[j + 1] = Items[j]; j--; }
            Items[j + 1] = x;
        }
        Unsorted = false;
    }
}

/// <summary>What a processor may know about the current block.</summary>
public readonly struct MidiContext
{
    public int Frames { get; init; }
    public int SampleRate { get; init; }
    public double Tempo { get; init; }
    public bool Playing { get; init; }
    /// <summary>Transport just started / just stopped in this block (P3 generators will key off these).</summary>
    public bool PlayStarted { get; init; }
    public bool PlayStopped { get; init; }
    public double SongSec { get; init; }
    /// <summary>Absolute sample position of frame 0 of this block on the chain's own clock (monotonic, carried across edits).</summary>
    public long BlockStart { get; init; }
    public MidiLogRing? Log { get; init; }
}

/// <summary>
/// One MIDI processing stage. <see cref="Process"/> runs on the audio thread and must not allocate or lock: it reads
/// <paramref name="input"/> and appends to <paramref name="output"/>. Instances are immutable in their parameters: an edit
/// builds a new instance on the engine thread, and the audio thread swaps it in (<see cref="Adopt"/> carries the state over).
/// </summary>
public interface IMidiProcessor
{
    void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx);
    /// <summary>Audio thread, once, when this instance replaces <paramref name="previous"/> (same type): take over held notes / pending events.</summary>
    void Adopt(IMidiProcessor previous, bool sameParameters);
    /// <summary>Drop all state (transport stop, panic). Sounding notes are silenced by the chain, not by the processor.</summary>
    void Reset();
}

/// <summary>Reads a processor's JSON parameters (a flat object of numbers, booleans and strings); anything missing or wrong gives the default.</summary>
public readonly struct MidiParams
{
    private readonly JsonElement _root;
    private readonly bool _ok;
    private MidiParams(JsonElement root) { _root = root; _ok = true; }

    public static MidiParams Parse(JsonDocument? doc) => doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object ? new MidiParams(doc.RootElement) : default;

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        return _ok && _root.TryGetProperty(name, out value);
    }

    public double Double(string name, double def, double min, double max)
    {
        if (!TryGet(name, out var v)) return def;
        double d = 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)) d = n;
        else if (v.ValueKind == JsonValueKind.True) d = 1;
        else if (v.ValueKind == JsonValueKind.False) d = 0;
        else return def;
        return double.IsFinite(d) ? Math.Clamp(d, min, max) : def;
    }

    public int Int(string name, int def, int min, int max) => (int)Math.Round(Double(name, def, min, max));

    public string String(string name, string def) => TryGet(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

    public bool Bool(string name, bool def) => Double(name, def ? 1 : 0, 0, 1) >= 0.5;

    /// <summary>A number array of exactly <paramref name="length"/> entries, or null.</summary>
    public int[]? IntArray(string name, int length, int min, int max)
    {
        if (!TryGet(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != length) return null;
        var result = new int[length];
        var i = 0;
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var d) || !double.IsFinite(d)) return null;
            result[i++] = (int)Math.Clamp(Math.Round(d), min, max);
        }
        return result;
    }
}

/// <summary>Held-note table: (input channel, input note) to (output channel, output note). Note-offs follow it, so they always leave with what their note-on left with.</summary>
public sealed class HeldNotes
{
    private readonly int[] _v = new int[2048];
    public HeldNotes() => Array.Fill(_v, -1);

    public bool TryGet(int ch, int note, out int outCh, out int outNote)
    {
        var v = _v[(ch << 7) | note];
        outCh = v >> 8; outNote = v & 0xFF;
        return v >= 0;
    }

    public void Set(int ch, int note, int outCh, int outNote) => _v[(ch << 7) | note] = (outCh << 8) | outNote;
    public void Clear(int ch, int note) => _v[(ch << 7) | note] = -1;
    public void CopyFrom(HeldNotes other) => Array.Copy(other._v, _v, _v.Length);
    public void Reset() => Array.Fill(_v, -1);
}

/// <summary>
/// Preallocated event scheduler with a carry-over ring: events scheduled past the end of the block wait for a later one.
/// Notes stay paired: a note-off is never placed before its own note-on, however the delay changed in between.
/// </summary>
public sealed class EventScheduler
{
    private const int Capacity = 2048;
    private readonly long[] _abs = new long[Capacity];
    private readonly byte[] _st = new byte[Capacity], _d1 = new byte[Capacity], _d2 = new byte[Capacity];
    private readonly long[] _lastOn = new long[2048];
    private int _n;

    public EventScheduler() => Array.Fill(_lastOn, long.MinValue);

    public bool HasPending => _n > 0;
    public int Pending => _n;

    /// <summary>Schedules at an absolute sample time; false when the ring is full (the caller then emits immediately).</summary>
    public bool Schedule(long abs, byte status, byte d1, byte d2)
    {
        var kind = status & 0xF0;
        if (kind is 0x90 or 0x80)
        {
            var key = ((status & 0x0F) << 7) | (d1 & 0x7F);
            if (kind == 0x90 && d2 > 0) _lastOn[key] = abs;
            else if (abs < _lastOn[key]) abs = _lastOn[key];
        }
        if (_n >= Capacity) return false;
        _abs[_n] = abs; _st[_n] = status; _d1[_n] = d1; _d2[_n] = d2; _n++;
        return true;
    }

    /// <summary>Moves everything due before the end of the block into <paramref name="output"/> (in scheduled order), keeps the rest.</summary>
    public void Drain(long blockStart, int frames, MidiBuffer output)
    {
        if (_n == 0) return;
        var end = blockStart + frames;
        var keep = 0;
        for (var i = 0; i < _n; i++)
        {
            if (_abs[i] < end)
            {
                output.Add((int)Math.Max(0, _abs[i] - blockStart), _st[i], _d1[i], _d2[i]);
                output.Unsorted = true;
            }
            else
            {
                if (keep != i) { _abs[keep] = _abs[i]; _st[keep] = _st[i]; _d1[keep] = _d1[i]; _d2[keep] = _d2[i]; }
                keep++;
            }
        }
        _n = keep;
    }

    public void CopyFrom(EventScheduler o)
    {
        _n = o._n;
        Array.Copy(o._abs, _abs, _n); Array.Copy(o._st, _st, _n); Array.Copy(o._d1, _d1, _n); Array.Copy(o._d2, _d2, _n);
        Array.Copy(o._lastOn, _lastOn, _lastOn.Length);
    }

    public void Reset() { _n = 0; Array.Fill(_lastOn, long.MinValue); }
}

/// <summary>Small allocation-free random source for the audio thread.</summary>
public struct MidiRandom
{
    private uint _s;
    public MidiRandom(uint seed) => _s = seed == 0 ? 0x9E3779B9u : seed;
    public uint Next() { if (_s == 0) _s = 0x9E3779B9u; _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
    /// <summary>Uniform in [-1, 1].</summary>
    public double Bipolar() => Next() / (double)uint.MaxValue * 2 - 1;
}

public struct MidiLogEntry
{
    public float TimeSec;
    public byte Status, Data1, Data2, Stage;
}

/// <summary>
/// Audio thread to engine thread log of MIDI events (single producer, single consumer, fixed size). Only filled while
/// the UI is watching (<see cref="Watching"/>); when it is full new entries are dropped, never blocked on.
/// </summary>
public sealed class MidiLogRing
{
    private const int Capacity = 2048;
    private readonly MidiLogEntry[] _items = new MidiLogEntry[Capacity];
    private int _head, _tail;
    public volatile bool Watching;

    public void Write(float timeSec, byte status, byte d1, byte d2, byte stage)
    {
        if (!Watching) return;
        var tail = Volatile.Read(ref _tail);
        var next = (tail + 1) % Capacity;
        if (next == Volatile.Read(ref _head)) return;
        _items[tail] = new MidiLogEntry { TimeSec = timeSec, Status = status, Data1 = d1, Data2 = d2, Stage = stage };
        Volatile.Write(ref _tail, next);
    }

    /// <summary>Engine thread: takes up to <paramref name="max"/> entries.</summary>
    public int Read(List<MidiLogEntry> into, int max)
    {
        var head = Volatile.Read(ref _head);
        var tail = Volatile.Read(ref _tail);
        var n = 0;
        while (head != tail && n < max) { into.Add(_items[head]); head = (head + 1) % Capacity; n++; }
        Volatile.Write(ref _head, head);
        return n;
    }
}
