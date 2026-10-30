using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

// Owns: the range-limited Standard MIDI File chunks (a conductor and track slice starting at tick 0).
// Does not own: the range math (MidiExportRange), the tick map or the whole-song encoding (MidiExportService),
//   or the low-level encoding helpers (MidiExportService.Write*).
// Tests: MidiExportRangeTests.
internal static class MidiExportSlices
{
    internal static byte[] BuildConductorSlice(SongProject project, ScoreTimeline timeline, MidiExportService.TickMap map, int startTick, int endTick, bool writeSections = true)
    {
        var events = new List<(int Tick, byte[] Bytes)>();
        var nameBytes = new List<byte>();
        MidiExportService.WriteMeta(nameBytes, 0, 0x03, System.Text.Encoding.ASCII.GetBytes("TabForge " + project.Title));
        events.Add((0, nameBytes.ToArray()));
        // The time signature in force at the range start is written first, later changes keep their relative ticks.
        var lastNum = project.TimeSignatureNumerator;
        var lastDen = project.TimeSignatureDenominator;
        var inForce = (Num: lastNum, Den: lastDen);
        var later = new List<(int Tick, int Num, int Den)>();
        for (var i = 0; i < timeline.Bars.Count; i++)
        {
            var bar = timeline.Bars[i];
            var measure = MusicTime.BarOf(project, bar.Bar);
            var num = measure?.TimeSigNum ?? project.TimeSignatureNumerator;
            var den = measure?.TimeSigDenom ?? project.TimeSignatureDenominator;
            if (num == lastNum && den == lastDen) continue;
            if (map.TickOfBar(i) <= startTick) inForce = (num, den);
            else later.Add((map.TickOfBar(i), num, den));
            lastNum = num; lastDen = den;
        }
        var sigBytes = new List<byte>();
        MidiExportService.WriteTimeSig(sigBytes, 0, inForce.Num, inForce.Den);
        events.Add((0, sigBytes.ToArray()));
        foreach (var sig in later)
        {
            if (sig.Tick > endTick) break;
            var bytes = new List<byte>();
            MidiExportService.WriteTimeSig(bytes, 0, sig.Num, sig.Den);
            events.Add((sig.Tick - startTick, bytes.ToArray()));
        }
        // The tempo in force at the range start is written first, later changes keep their relative ticks.
        var mpq = 500_000;
        foreach (var (tick, value) in map.TempoChanges.OrderBy(t => t.Tick))
        {
            if (tick <= startTick) { mpq = value; continue; }
            if (tick > endTick) break;
            var bytes = new List<byte>();
            MidiExportService.WriteTempoMpq(bytes, 0, value);
            events.Add((tick - startTick, bytes.ToArray()));
        }
        var first = new List<byte>();
        MidiExportService.WriteTempoMpq(first, 0, mpq);
        events.Add((0, first.ToArray()));
        if (writeSections)
            foreach (var (tick, name) in MidiExportService.SectionEvents(project, timeline, map, startTick, endTick))
            {
                var bytes = new List<byte>();
                MidiExportService.WriteMeta(bytes, 0, 0x06, System.Text.Encoding.UTF8.GetBytes(name));
                events.Add((tick - startTick, bytes.ToArray()));
            }

        var body = new List<byte>();
        var cursor = 0;
        foreach (var (tick, bytes) in events.OrderBy(e => e.Tick))   // stable: same-tick events keep their order
        {
            var delta = Math.Max(0, tick - cursor);
            cursor = Math.Max(cursor, tick);
            MidiExportService.WriteVarLen(body, delta);
            body.AddRange(bytes.Skip(1));
        }
        return MidiExportService.BuildChunk(body, Math.Max(0, endTick - startTick - cursor));
    }

    // Channel messages that chase: the last value at the range start is written at tick 0.
    private static bool IsChaseKind(int kind) => kind is 0xA0 or 0xB0 or 0xC0 or 0xD0 or 0xE0;

    private static (int Channel, int Key) ChaseKey(ScoreEvent e)
    {
        var kind = e.Status & 0xF0;
        return kind is 0xC0 or 0xD0 or 0xE0 ? (e.Status & 0x0F, 0) : (e.Status & 0x0F, e.Data1 & 0x7F);
    }

    internal static byte[] BuildTrackSlice(SongProject project, ScoreTimeline timeline, MidiExportService.TickMap map, int trackIndex,
        double startMs, double endMs, int startTick, int endTick, bool includeArticulations = false, bool keepTechniques = false)
    {
        var markers = keepTechniques ? MidiExportService.TechniqueMarkers(timeline) : null;
        var items = new List<(int Tick, byte[] Bytes)>();
        var ordered = timeline.Events
            .Where(x => x.TrackIndex == trackIndex && !x.IsMetronome && !x.IsCountInClick && (includeArticulations || !x.IsArticulation))
            .OrderBy(x => x.TimeMs).ToList();   // stable: same-time events keep their order
        var chase = new Dictionary<(int Channel, int Kind, int Key), ScoreEvent>();
        var pending = new Dictionary<(int Channel, int Pitch), Queue<ScoreEvent>>();   // note-ons waiting for their note-off
        var clipped = new List<(ScoreEvent On, ScoreEvent Off)>();   // notes crossing the range start: sound from tick 0
        var inside = new List<(ScoreEvent On, ScoreEvent Off, bool CutEnd)>();   // attacks inside the range
        foreach (var e in ordered)
        {
            var kind = e.Status & 0xF0;
            if (e.IsNoteOn || e.IsNoteOff)
            {
                var key = (e.Channel, e.Data1 & 0x7F);
                if (e.IsNoteOn)
                {
                    if (!pending.TryGetValue(key, out var queue)) pending[key] = queue = new Queue<ScoreEvent>();
                    queue.Enqueue(e);
                    continue;
                }
                if (pending.TryGetValue(key, out var ons) && ons.Count > 0)
                {
                    var on = ons.Dequeue();
                    if (on.TimeMs < startMs - 1e-6)
                    {
                        if (e.TimeMs > startMs + 1e-6) clipped.Add((on, e));   // sounding at the range start: not dropped
                        continue;
                    }
                    if (on.TimeMs >= endMs - 1e-6) continue;
                    inside.Add((on, e, e.TimeMs > endMs - 1e-6));
                    continue;
                }
                continue;   // a release with no attack in the file: nothing to write
            }
            if (IsChaseKind(kind))
            {
                if (e.TimeMs < startMs - 1e-6) chase[(e.Channel, kind, ChaseKey(e).Key)] = e;
                else if (e.TimeMs < endMs - 1e-6) AddMessage(items, map, e, e.TimeMs, startTick);
                continue;
            }
            if (e.TimeMs >= startMs - 1e-6 && e.TimeMs < endMs - 1e-6) AddMessage(items, map, e, e.TimeMs, startTick);
        }
        // Attacks still held at the range end are cut there; an attack inside with no release in the file ends there too.
        foreach (var queue in pending.Values)
            while (queue.Count > 0)
            {
                var on = queue.Dequeue();
                if (on.TimeMs < startMs - 1e-6 || on.TimeMs >= endMs - 1e-6) continue;
                inside.Add((on, new ScoreEvent { Status = 0x80 | (on.Status & 0x0F), Channel = on.Channel, Data1 = on.Data1, Data2 = 0x40 }, true));
            }
        var head = new List<(int Tick, byte[] Bytes)>();
        foreach (var e in chase.Values.OrderBy(e => e.TimeMs)) AddMessage(head, e, 0);
        // A latched keyswitch in force at the range start is re-sent there (playback's chase), so the instrument is in the right articulation.
        if (includeArticulations)
            foreach (var on in timeline.Events
                .Where(e => e.TrackIndex == trackIndex && e.IsArticulation && e.IsNoteOn && e.TimeMs < startMs - 1e-6)
                .GroupBy(e => (e.DeviceId, e.Channel)).Select(g => g.Last()).OrderBy(e => e.TimeMs))
                if (!clipped.Any(pair => ReferenceEquals(pair.On, on)))
                {
                    AddNoteOn(head, on, 0);
                    AddNoteOff(head, map, new ScoreEvent { Status = 0x80 | (on.Status & 0x0F), Channel = on.Channel, Data1 = on.Data1, Data2 = 0 }, startMs, startTick, 0);
                }
        foreach (var (on, off) in clipped.OrderBy(n => n.On.TimeMs))
        {
            AddMarker(head, markers, on, 0);
            AddNoteOn(head, on, 0);
            AddNoteOff(head, map, off, Math.Min(off.TimeMs, endMs), startTick, 0);
        }
        foreach (var (on, off, cutEnd) in inside)
        {
            var onTick = Math.Max(0, map.TickOf(on.TimeMs) - startTick);
            AddMarker(items, markers, on, onTick);
            AddNoteOn(items, on, onTick);
            AddNoteOff(items, map, off, cutEnd ? endMs : off.TimeMs, startTick, onTick);
        }
        // Chase and clipped notes first (all at tick 0), then everything in tick order.
        head.AddRange(items.OrderBy(i => i.Tick));   // stable: same-tick events keep their order
        var track = project.Tracks[trackIndex];
        var nameBytes = new List<byte>();
        MidiExportService.WriteMeta(nameBytes, 0, 0x03, System.Text.Encoding.ASCII.GetBytes(string.IsNullOrWhiteSpace(track.Name) ? "Track" : track.Name));
        head.Insert(0, (0, nameBytes.ToArray()));

        var body = new List<byte>();
        var cursor = 0;
        foreach (var (tick, bytes) in head)
        {
            var delta = Math.Max(0, tick - cursor);
            cursor = Math.Max(cursor, tick);
            MidiExportService.WriteVarLen(body, delta);
            body.AddRange(bytes.Skip(1));
        }
        return MidiExportService.BuildChunk(body, Math.Max(0, endTick - startTick - cursor));
    }

    private static void AddMessage(List<(int Tick, byte[] Bytes)> items, MidiExportService.TickMap map, ScoreEvent e, double atMs, int startTick) =>
        AddMessage(items, e, Math.Max(0, map.TickOf(atMs) - startTick));

    private static void AddMessage(List<(int Tick, byte[] Bytes)> items, ScoreEvent e, int tick)
    {
        var bytes = new List<byte>();
        MidiExportService.WriteVarLen(bytes, 0);
        bytes.Add((byte)(e.Status & 0xFF));
        bytes.Add((byte)(e.Data1 & 0x7F));
        if ((e.Status & 0xF0) != 0xC0 && (e.Status & 0xF0) != 0xD0) bytes.Add((byte)(e.Data2 & 0x7F));
        items.Add((tick, bytes.ToArray()));
    }

    private static void AddMarker(List<(int Tick, byte[] Bytes)> items, Dictionary<(int Track, double Ms, int Channel, int Pitch), string>? markers, ScoreEvent on, int tick)
    {
        if (markers is null || !markers.TryGetValue((on.TrackIndex, on.TimeMs, on.Channel, on.Data1), out var payload)) return;
        items.Add((tick, MidiExportService.MarkerChunk(on, payload)));
    }

    private static void AddNoteOn(List<(int Tick, byte[] Bytes)> items, ScoreEvent on, int tick)    {
        var bytes = new List<byte>();
        MidiExportService.WriteVarLen(bytes, 0);
        bytes.Add((byte)(on.Status & 0xFF));
        bytes.Add((byte)(on.Data1 & 0x7F));
        bytes.Add((byte)(on.Data2 & 0x7F));
        items.Add((tick, bytes.ToArray()));
    }

    private static void AddNoteOff(List<(int Tick, byte[] Bytes)> items, MidiExportService.TickMap map, ScoreEvent off, double atMs, int startTick, int onTick)
    {
        var tick = Math.Max(onTick, map.TickOf(Math.Max(0, atMs)) - startTick);
        var bytes = new List<byte>();
        MidiExportService.WriteVarLen(bytes, 0);
        bytes.Add((byte)((off.Status & 0xF0) == 0x90 ? 0x80 | (off.Status & 0x0F) : off.Status & 0xFF));
        bytes.Add((byte)(off.Data1 & 0x7F));
        bytes.Add((byte)(off.Data2 & 0x7F));
        items.Add((tick, bytes.ToArray()));
    }
}
