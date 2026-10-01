using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

/// <summary>
/// Standard MIDI File export. The file is written from the canonical playback timeline, so an
/// exported file plays exactly what TabForge plays (repeats, ties, techniques, tempo map included).
/// Format 1 with a conductor track (tempo + time signature) and one chunk per score track.
/// </summary>
public static class MidiExportService
{
    private const int Division = 480;   // ticks per quarter note

    public static void Export(SongProject project, string path)
    {
        path = FilePathPolicy.OutputFile(path, "MIDI export", ".mid", ".midi");
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions
        {
            // An exported file represents the score, not the current practice state: ignore mute/solo.
            RespectMuteSolo = false
        });
        var map = new TickMap(timeline, project);

        var chunks = new List<byte[]> { BuildConductorTrack(project, timeline, map) };
        for (var trackIndex = 0; trackIndex < project.Tracks.Count; trackIndex++)
            chunks.Add(BuildTrackChunk(project, timeline, map, trackIndex));

        FilePathPolicy.WriteAtomically(path, fs =>
        {
            fs.Write(Encoding.ASCII.GetBytes("MThd"));
            WriteBe32(fs, 6);
            WriteBe16(fs, (ushort)(chunks.Count > 1 ? 1 : 0));
            WriteBe16(fs, (ushort)chunks.Count);
            WriteBe16(fs, (ushort)Division);
            foreach (var chunk in chunks) fs.Write(chunk, 0, chunk.Length);
        });
    }

    private static byte[] BuildConductorTrack(SongProject project, ScoreTimeline timeline, TickMap map)
    {
        var events = new List<(int Tick, byte[] Bytes)>();
        var nameBytes = new List<byte>();
        WriteMeta(nameBytes, 0, 0x03, Encoding.ASCII.GetBytes("TabForge " + project.Title));
        events.Add((0, nameBytes.ToArray()));
        var first = new List<byte>();
        WriteTimeSig(first, 0, project.TimeSignatureNumerator, project.TimeSignatureDenominator);
        events.Add((0, first.ToArray()));

        // Seed from the meta written above so bar 1 does not duplicate the same time signature.
        var lastNum = project.TimeSignatureNumerator;
        var lastDen = project.TimeSignatureDenominator;
        for (var i = 0; i < timeline.Bars.Count; i++)
        {
            var bar = timeline.Bars[i];
            var measure = MusicTime.BarOf(project, bar.Bar);
            var num = measure?.TimeSigNum ?? project.TimeSignatureNumerator;
            var den = measure?.TimeSigDenom ?? project.TimeSignatureDenominator;
            if (num == lastNum && den == lastDen) continue;
            var bytes = new List<byte>();
            WriteTimeSig(bytes, 0, num, den);
            events.Add((map.TickOfBar(i), bytes.ToArray()));
            lastNum = num; lastDen = den;
        }
        // The tempo map comes from the timeline itself (see TickMap.TempoChanges): tempo steps, ramps and fermata holds are all
        // written as the exact microseconds per quarter that put every tick at the time playback reaches it.
        foreach (var (tick, mpq) in map.TempoChanges)
        {
            var bytes = new List<byte>();
            WriteTempoMpq(bytes, 0, mpq);
            events.Add((tick, bytes.ToArray()));
        }

        var body = new List<byte>();
        var cursor = 0;
        foreach (var (tick, bytes) in events.OrderBy(e => e.Tick))   // stable: same-tick events keep their order
        {
            // each entry was written with delta 0 (one byte); replace it with the real delta
            var delta = Math.Max(0, tick - cursor);
            cursor = Math.Max(cursor, tick);
            WriteVarLen(body, delta);
            body.AddRange(bytes.Skip(1));
        }
        return BuildChunk(body, Math.Max(0, map.EndTick - cursor));
    }

    private static byte[] BuildTrackChunk(SongProject project, ScoreTimeline timeline, TickMap map, int trackIndex)
    {
        var events = new List<byte>();
        var track = project.Tracks[trackIndex];
        WriteMeta(events, 0, 0x03, Encoding.ASCII.GetBytes(string.IsNullOrWhiteSpace(track.Name) ? "Track" : track.Name));

        var cursorTick = 0;
        foreach (var e in timeline.Events.Where(x => x.TrackIndex == trackIndex))
        {
            var tick = map.TickOf(e.TimeMs);
            var delta = Math.Max(0, tick - cursorTick);
            cursorTick = Math.Max(cursorTick, tick);
            WriteVarLen(events, delta);
            events.Add((byte)(e.Status & 0xFF));
            events.Add((byte)(e.Data1 & 0x7F));
            if ((e.Status & 0xF0) != 0xC0 && (e.Status & 0xF0) != 0xD0) events.Add((byte)(e.Data2 & 0x7F));
        }
        // every track ends with the song, so all chunks are the same length
        return BuildChunk(events, Math.Max(0, map.EndTick - cursorTick));
    }

    /// <summary>
    /// Absolute ms to absolute MIDI ticks, using the timeline's own bar/tempo map. Each performed bar has a clock (<see cref="BarClock"/>):
    /// the time playback reaches each slot, through the bar's tempo, mid-bar tempo changes and ramps, and fermata holds. The tempo events
    /// written to the file are derived from the same clock, so reading the file back gives playback's time at every bar start and note.
    /// </summary>
    internal sealed class TickMap
    {
        private readonly BarClock[] _clocks;
        private readonly double _endMs;
        private readonly int _endTick;
        private int _lastMpq = 500_000;

        /// <summary>Tempo events (tick, microseconds per quarter) that reproduce the timeline's time at every bar start, ramp slot and hold.</summary>
        public List<(int Tick, int Mpq)> TempoChanges { get; } = new();

        /// <summary>The tick at the end of the last performed bar.</summary>
        public int EndTick => _endTick;

        public TickMap(ScoreTimeline timeline, SongProject project)
        {
            _clocks = new BarClock[timeline.Bars.Count];
            var running = 0;
            for (var i = 0; i < _clocks.Length; i++)
            {
                var bar = timeline.Bars[i];
                _clocks[i] = new BarClock(bar, MusicTime.BarOf(project, bar.Bar), running);
                running += bar.Slots * Division / MusicTime.SlotsPerQuarter;
            }
            _endTick = running;
            _endMs = timeline.Bars.Count > 0 ? timeline.Bars[^1].EndMs : 0;
            BuildTempoMap();
        }

        public int TickOfBar(int index) => index < 0 || index >= _clocks.Length ? 0 : _clocks[index].StartTick;

        public int TickOf(double ms)
        {
            if (_clocks.Length == 0) return 0;
            var lo = 0;
            var hi = _clocks.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (_clocks[mid].Bar.StartMs <= ms + 1e-6) lo = mid; else hi = mid - 1;
            }
            // After the last bar (a release or let-ring tail): carry on at the tempo the file ends with.
            if (lo == _clocks.Length - 1 && ms > _endMs) return _endTick + (int)Math.Round((ms - _endMs) * 1000.0 / _lastMpq * Division);
            return _clocks[lo].TickAt(ms);
        }

        private void BuildTempoMap()
        {
            double fileMs = 0;
            var lastMpq = -1;
            foreach (var clock in _clocks)
            {
                var points = clock.Breakpoints();
                for (var k = 0; k + 1 < points.Count; k++)
                {
                    var last = k + 2 == points.Count;
                    var from = clock.StartTick + (int)Math.Round(points[k] * Division / MusicTime.SlotsPerQuarter);
                    var to = last ? clock.EndTick : clock.StartTick + (int)Math.Round(points[k + 1] * Division / MusicTime.SlotsPerQuarter);
                    if (to <= from) continue;
                    var target = last ? clock.Bar.EndMs : clock.MsAt(points[k + 1]);
                    var quarters = (to - from) / (double)Division;
                    // Exact microseconds per quarter for this stretch, correcting the rounding left by earlier stretches so nothing accumulates.
                    var mpq = Math.Clamp((int)Math.Round((target - fileMs) * 1000.0 / quarters), 1, 0xFFFFFF);
                    if (mpq != lastMpq) { TempoChanges.Add((from, mpq)); lastMpq = mpq; }
                    fileMs += quarters * mpq / 1000.0;
                }
            }
            if (lastMpq > 0) _lastMpq = lastMpq;
        }
    }

    /// <summary>The time a performed bar's slots are reached: the bar's own tempo map (mid-bar changes, ramps) then its fermata holds.</summary>
    internal sealed class BarClock
    {
        public ScoreBar Bar { get; }
        public int StartTick { get; }
        public int EndTick => StartTick + Bar.Slots * Division / MusicTime.SlotsPerQuarter;
        private readonly MeasureModel? _measure;

        public BarClock(ScoreBar bar, MeasureModel? measure, int startTick) { Bar = bar; _measure = measure; StartTick = startTick; }

        private bool Varying => _measure?.MidBarTempos is { Count: > 0 };

        public double MsAt(double slot) => Bar.StartMs + FermataSpan.Warp(Bar.Fermatas, MusicTime.OffsetMs(_measure, slot, Bar.Tempo));

        public int TickAt(double ms) => StartTick + (int)Math.Round(SlotAt(ms) * Division / MusicTime.SlotsPerQuarter);

        /// <summary>Inverse of <see cref="MsAt"/> (a slot position, not clamped to the bar so a tail after the last slot keeps counting).</summary>
        public double SlotAt(double ms)
        {
            var relative = FermataSpan.Unwarp(Bar.Fermatas, ms - Bar.StartMs);
            if (!Varying) return Math.Max(0, relative / MusicTime.SlotsToMsAt(1, Bar.Tempo));
            double low = 0, high = Bar.Slots;
            for (var step = 0; step < 40; step++)
            {
                var middle = (low + high) / 2;
                if (MusicTime.OffsetMs(_measure, middle, Bar.Tempo) < relative) low = middle; else high = middle;
            }
            return (low + high) / 2;
        }

        /// <summary>Slot positions where the file needs a tempo event: bar start, every slot inside a ramp, tempo steps and hold edges, bar end.</summary>
        public List<double> Breakpoints()
        {
            var set = new SortedSet<double> { 0, Bar.Slots };
            void Add(double slot) { if (slot > 1e-6 && slot < Bar.Slots - 1e-6) set.Add(Math.Round(slot, 6)); }
            if (Bar.Fermatas is { } holds)
                foreach (var hold in holds) { Add(hold.Slot); Add(hold.Slot + hold.LengthSlots); }
            if (_measure?.MidBarTempos is { Count: > 0 } points)
                for (var i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    Add(point.Slot);
                    if (point.RampSlots <= 0) continue;
                    var next = i + 1 < points.Count ? points[i + 1].Slot : Bar.Slots;
                    var rampEnd = Math.Min(Math.Min(point.Slot + point.RampSlots, next), Bar.Slots);
                    for (var slot = Math.Floor(point.Slot) + 1; slot < rampEnd - 1e-6; slot += 1) Add(slot);
                    Add(rampEnd);
                }
            return set.ToList();
        }
    }

    // ---------- chunk helpers ----------

    private static byte[] BuildChunk(List<byte> events, int endDelta = 0)
    {
        WriteVarLen(events, endDelta); events.Add(0xFF); events.Add(0x2F); events.Add(0x00);
        var output = new List<byte>();
        output.AddRange(Encoding.ASCII.GetBytes("MTrk"));
        var length = events.Count;
        output.Add((byte)((length >> 24) & 0xFF)); output.Add((byte)((length >> 16) & 0xFF));
        output.Add((byte)((length >> 8) & 0xFF)); output.Add((byte)(length & 0xFF));
        output.AddRange(events);
        return output.ToArray();
    }

    private static void WriteVarLen(List<byte> b, int v)
    {
        v = Math.Max(0, v);
        var tmp = new List<byte> { (byte)(v & 0x7F) };
        v >>= 7;
        while (v > 0) { tmp.Insert(0, (byte)((v & 0x7F) | 0x80)); v >>= 7; }
        for (var i = 0; i < tmp.Count - 1; i++) tmp[i] |= 0x80;
        b.AddRange(tmp);
    }

    private static void WriteMeta(List<byte> b, int delta, byte type, byte[] data)
    {
        WriteVarLen(b, delta); b.Add(0xFF); b.Add(type); WriteVarLen(b, data.Length); b.AddRange(data);
    }

    private static void WriteTempo(List<byte> b, int delta, int bpm)
        => WriteTempoMpq(b, delta, (int)Math.Round(60_000_000.0 / Math.Clamp(bpm, 20, 400)));

    private static void WriteTempoMpq(List<byte> b, int delta, int mpq)
    {
        mpq = Math.Clamp(mpq, 1, 0xFFFFFF);
        WriteVarLen(b, delta); b.Add(0xFF); b.Add(0x51); b.Add(0x03);
        b.Add((byte)((mpq >> 16) & 0xFF)); b.Add((byte)((mpq >> 8) & 0xFF)); b.Add((byte)(mpq & 0xFF));
    }

    private static void WriteTimeSig(List<byte> b, int delta, int num, int denom)
    {
        var e = 2; var d = Math.Clamp(denom, 1, 64);
        while (d > 2) { d /= 2; e++; }
        WriteVarLen(b, delta); b.Add(0xFF); b.Add(0x58); b.Add(0x04);
        b.Add((byte)Math.Clamp(num, 1, 32)); b.Add((byte)e); b.Add(24); b.Add(8);
    }

    private static void WriteBe32(Stream s, int v) { s.WriteByte((byte)((v >> 24) & 0xFF)); s.WriteByte((byte)((v >> 16) & 0xFF)); s.WriteByte((byte)((v >> 8) & 0xFF)); s.WriteByte((byte)(v & 0xFF)); }
    private static void WriteBe16(Stream s, ushort v) { s.WriteByte((byte)((v >> 8) & 0xFF)); s.WriteByte((byte)(v & 0xFF)); }
}
