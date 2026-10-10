using System.IO;

namespace TabForge.Audio.Contracts;

// Owns: the wire layout of the engine commands with lists, optional tails or nested records (EngineMessages.cs has the fixed-shape ones).
// Does not own: validation beyond the bounds that keep a read from allocating without limit; the engine range-checks slots and values.
// Tests: TestEngineMessagesRoundTrip, TestEngineMessagesGolden.

/// <summary><see cref="EngineCommand.SetPosition"/>: playing, song seconds, Stopwatch stamp, then the owner id (absent or out of range reads as owner 0).</summary>
public readonly record struct SetPositionMessage(bool Playing, double SongSec, long Stamp, int Owner)
{
    public void Write(BinaryWriter w) { w.Write(Playing); w.Write(SongSec); w.Write(Stamp); w.Write(Owner); }
    public static SetPositionMessage Read(BinaryReader r) => new(r.ReadBoolean(), r.ReadDouble(), r.ReadInt64(), r.ReadOwnerTail());
}

/// <summary>
/// <see cref="EngineCommand.SetTransport"/>: tempo, playing, then (optional) the bar map and the owner id. <see cref="WriteTempoOnly"/> is the short
/// form without either; <see cref="Read"/> gives <see cref="Bars"/> null for it.
/// </summary>
public readonly record struct SetTransportMessage(double Tempo, bool Playing, TransportBar[]? Bars, int Owner)
{
    public void Write(BinaryWriter w) { w.Write(Tempo); w.Write(Playing); TransportMap.Write(w, Bars ?? []); w.Write(Owner); }
    public static void WriteTempoOnly(BinaryWriter w, double tempo, bool playing) { w.Write(tempo); w.Write(playing); }
    public static SetTransportMessage Read(BinaryReader r)
    {
        var tempo = r.ReadDouble(); var playing = r.ReadBoolean();
        var bars = r.BaseStream.Position < r.BaseStream.Length ? TransportMap.Read(r) : null;
        return new(tempo, playing, bars, r.ReadOwnerTail());
    }
}

/// <summary><see cref="EngineCommand.SetClips"/>: slot, clip list, then the owner id (absent or out of range reads as owner 0).</summary>
public readonly record struct SetClipsMessage(int Slot, List<ClipSpec> Clips, int Owner)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Clips); w.Write(Owner); }
    public static SetClipsMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadClips(), r.ReadOwnerTail());
}

/// <summary>
/// <see cref="EngineCommand.Record"/>: start, folder, count + (slot, name) per armed track, then (optional) the offset in ms and the owner id.
/// <see cref="WriteStop"/> is the short stop form (no tracks, no tail); it reads with offset 0, owner 0.
/// </summary>
public readonly record struct RecordMessage(bool Start, string Folder, IReadOnlyList<(int Slot, string Name)> Tracks, double OffsetMs, int Owner)
{
    public const int MaxTracks = 256;

    public void Write(BinaryWriter w)
    {
        w.Write(Start); w.WriteString(Folder); w.Write(Tracks.Count);
        foreach (var (slot, name) in Tracks) { w.Write(slot); w.WriteString(name); }
        w.Write(OffsetMs); w.Write(Owner);
    }

    public static void WriteStop(BinaryWriter w) { w.Write(false); w.WriteString(""); w.Write(0); }

    /// <summary>Slots are returned as sent (the engine range-checks them).</summary>
    public static RecordMessage Read(BinaryReader r)
    {
        var start = r.ReadBoolean(); var folder = r.ReadBoundedString(1024);
        var count = Math.Clamp(r.ReadInt32(), 0, MaxTracks);
        var tracks = new List<(int, string)>(count);
        for (var i = 0; i < count; i++) tracks.Add((r.ReadInt32(), r.ReadBoundedString(256)));
        var offset = r.BaseStream.Position < r.BaseStream.Length ? r.ReadDouble() : 0;
        return new(start, folder, tracks, offset, r.ReadOwnerTail());
    }
}

/// <summary><see cref="EngineCommand.PanicSlots"/>: count + slots.</summary>
public readonly record struct PanicSlotsMessage(IReadOnlyCollection<int> Slots)
{
    public const int MaxSlots = 4096;
    public void Write(BinaryWriter w) { w.Write(Slots.Count); foreach (var s in Slots) w.Write(s); }

    /// <summary>A count outside 0..<see cref="MaxSlots"/> reads as an empty list.</summary>
    public static PanicSlotsMessage Read(BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > MaxSlots) return new([]);
        var slots = new int[count];
        for (var i = 0; i < count; i++) slots[i] = r.ReadInt32();
        return new(slots);
    }
}

/// <summary><see cref="EngineCommand.SetAutoPitch"/>: slot, count + (chain index, semitones).</summary>
public readonly record struct SetAutoPitchMessage(int Slot, IReadOnlyList<(int Index, int Semitones)> Transposes)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Transposes.Count); foreach (var (i, st) in Transposes) { w.Write(i); w.Write(st); } }
    public static SetAutoPitchMessage Read(BinaryReader r)
    {
        var slot = r.ReadInt32(); var count = r.ReadInt32();
        if (count is < 0 or > 64) throw new InvalidDataException("Bad auto-pitch count.");
        var list = new (int, int)[count];
        for (var i = 0; i < count; i++) list[i] = (r.ReadInt32(), r.ReadInt32());
        return new(slot, list);
    }
}

/// <summary><see cref="EngineCommand.MeasurePitch"/>: slot, chain index, request id, channel, count + notes.</summary>
public readonly record struct MeasurePitchMessage(int Slot, int Index, int RequestId, int Channel, IReadOnlyList<int> Notes)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(RequestId); w.Write(Channel); w.Write(Notes.Count); foreach (var n in Notes) w.Write(n); }
    public static MeasurePitchMessage Read(BinaryReader r)
    {
        var slot = r.ReadInt32(); var index = r.ReadInt32(); var id = r.ReadInt32(); var channel = r.ReadInt32();
        var count = r.ReadInt32();
        if (count is < 1 or > 8) throw new InvalidDataException("Bad test note count.");
        var notes = new int[count];
        for (var i = 0; i < count; i++) notes[i] = r.ReadInt32();
        return new(slot, index, id, channel, notes);
    }
}

/// <summary><see cref="EngineCommand.SetMidiProcessors"/>: slot, count + (chain index, processor list).</summary>
public readonly record struct SetMidiProcessorsMessage(int Slot, IReadOnlyList<(int Index, IReadOnlyList<MidiProcSpec> Specs)> Lists)
{
    public const int MaxLists = 64;
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Lists.Count); foreach (var (index, specs) in Lists) { w.Write(index); w.Write(specs); } }

    /// <summary>A slot outside 0..<paramref name="slotLimit"/> or a count outside 0..<see cref="MaxLists"/> is not parsed further: <paramref name="valid"/> is false and there are no lists.</summary>
    public static SetMidiProcessorsMessage Read(BinaryReader r, int slotLimit, out bool valid)
    {
        var slot = r.ReadInt32(); var count = r.ReadInt32();
        valid = slot >= 0 && slot < slotLimit && count is >= 0 and <= MaxLists;
        var lists = new List<(int, IReadOnlyList<MidiProcSpec>)>();
        if (valid) for (var i = 0; i < count; i++) lists.Add((r.ReadInt32(), r.ReadMidiProcs()));
        return new(slot, lists);
    }
}

/// <summary>
/// <see cref="EngineCommand.SetGraph"/>: bus destinations (slot, destination), sidechains (slot, plug-in index, source) and MIDI-output forwards
/// (slot, plug-in index, destination), each a count + entries. A count over the bound is clamped to it.
/// </summary>
public readonly record struct SetGraphMessage(IReadOnlyList<(int Slot, int Dest)> Dests, IReadOnlyList<(int Slot, int Index, int Other)> Sidechains, IReadOnlyList<(int Slot, int Index, int Other)> Forwards)
{
    public void Write(BinaryWriter w)
    {
        w.Write(Dests.Count); foreach (var (a, b) in Dests) { w.Write(a); w.Write(b); }
        w.Write(Sidechains.Count); foreach (var (a, b, c) in Sidechains) { w.Write(a); w.Write(b); w.Write(c); }
        w.Write(Forwards.Count); foreach (var (a, b, c) in Forwards) { w.Write(a); w.Write(b); w.Write(c); }
    }

    public static SetGraphMessage Read(BinaryReader r, int maxDests)
    {
        var n = Math.Clamp(r.ReadInt32(), 0, maxDests);
        var dests = new List<(int, int)>(n);
        for (var i = 0; i < n; i++) dests.Add((r.ReadInt32(), r.ReadInt32()));
        var sides = ReadTriples(r);
        return new(dests, sides, ReadTriples(r));
    }

    private static List<(int, int, int)> ReadTriples(BinaryReader r)
    {
        var n = Math.Clamp(r.ReadInt32(), 0, 4096);
        var list = new List<(int, int, int)>(n);
        for (var i = 0; i < n; i++) list.Add((r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
        return list;
    }
}
