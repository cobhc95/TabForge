using System.IO;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;

namespace TabForge.AudioEngine;

// Owns: reader-thread parsing of the engine commands with lists, tails or several range checks (the rest sit in EngineHost.HandleCommand).
// Does not own: the handlers themselves (EngineSession); the wire layout (EngineMessages.cs, EngineMessageLists.cs).
// Tests: TestCommandFrameRobustness, TestEngineMessagesRoundTrip.

public static partial class EngineHost
{
    private static void ReadSetMidiProcessors(BinaryReader r)
    {
        var m = SetMidiProcessorsMessage.Read(r, MixEngine.TotalSlots, out var valid);
        if (!valid) return;
        var slot = m.Slot;
        var lists = new Dictionary<int, List<MidiProcSpec>>();
        foreach (var (index, specs) in m.Lists) if (index is >= 0 and < 64 && specs.Count > 0) lists[index] = specs.ToList();
        EngineThreads.Post(() => _session.SetMidiProcessors(slot, lists));
    }

    private static void ReadSetGraph(BinaryReader r)
    {
        static int Slot(int s, int max) => s >= 0 && s < max ? s : -1;
        var dests = new Dictionary<int, int>(); var sides = new Dictionary<(int, int), int>(); var fwds = new Dictionary<(int, int), int>();
        var m = SetGraphMessage.Read(r, MixEngine.MaxSlots);
        foreach (var (s, d) in m.Dests) if (Slot(s, MixEngine.MaxSlots) >= 0 && d >= MixEngine.BusBase && d < MixEngine.MasterSlot) dests[s] = d;
        foreach (var (s, x, src) in m.Sidechains) if (Slot(s, MixEngine.MaxSlots) >= 0 && Slot(src, MixEngine.MaxSlots) >= 0) sides[(s, x)] = src;
        foreach (var (s, x, dst) in m.Forwards) if (Slot(s, MixEngine.MaxSlots) >= 0 && Slot(dst, MixEngine.MaxSlots) >= 0) fwds[(s, x)] = dst;
        EngineThreads.Post(() => _session.SetGraph(dests, sides, fwds));
    }

    private static void ReadSetClips(BinaryReader r)
    {
        var (slot, clips, owner) = SetClipsMessage.Read(r);
        if (slot < 0 || slot >= MixEngine.MaxSlots) throw new InvalidDataException($"SetClips: track slot {slot} out of range");
        EngineThreads.Post(() => _session.SetClips(slot, clips, owner));
    }

    private static void ReadSetPosition(BinaryReader r)
    {
        var (playing, songSec, stamp, owner) = SetPositionMessage.Read(r);
        // Reader thread. The position lives in the owner's SongTransport (not in the MixEngine, which Configure
        // replaces on the main thread), published as one immutable record: never torn, never lost to a mixer swap.
        if (double.IsFinite(songSec)) _session.Transports[owner].SetPosition(playing, Math.Max(0, songSec), stamp);
    }

    private static void ReadRecord(BinaryReader r)
    {
        var (start, folder, tracks, rawOffsetMs, owner) = RecordMessage.Read(r);   // an older sender has no offset or owner tail (offset 0, owner 0)
        var names = new Dictionary<int, string>();
        foreach (var (slot, name) in tracks)
        {
            if (slot < 0 || slot >= MixEngine.MaxSlots) throw new InvalidDataException($"Record: track slot {slot} out of range");
            names[slot] = name;
        }
        if (!double.IsFinite(rawOffsetMs)) throw new InvalidDataException("Record: offset is not a number");
        var offsetMs = Math.Clamp(rawOffsetMs, -Audio.TakeAlignment.MaxOffsetMs, Audio.TakeAlignment.MaxOffsetMs);
        EngineThreads.Post(() => _session.Record(start, folder, names, offsetMs, owner));
    }

    private static void ReadPanicSlots(BinaryReader r)
    {
        var s = _session;
        var slots = PanicSlotsMessage.Read(r).Slots;
        var mix = s.RenderActive ? null : Volatile.Read(ref s.Mix);
        foreach (var slot in slots) mix?.ChainAt(slot)?.Panic();
    }

    private static void ReadSetTransport(BinaryReader r)
    {
        var (tempo, _, bars, owner) = SetTransportMessage.Read(r);   // playing: SetPosition carries it; an older sender has no bar map or owner
        var transport = _session.Transports[owner];
        if (double.IsFinite(tempo)) transport.SetTempo(Math.Clamp(tempo, 1, 2000));
        if (bars is not null) transport.SetMap(bars);
    }
}
