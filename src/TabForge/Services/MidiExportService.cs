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
        var events = new List<byte>();
        WriteMeta(events, 0, 0x03, Encoding.ASCII.GetBytes("TabForge " + project.Title));
        WriteTimeSig(events, 0, project.TimeSignatureNumerator, project.TimeSignatureDenominator);

        var lastTempo = int.MinValue;
        // Seed from the meta written above so bar 1 does not duplicate the same time signature.
        var lastNum = project.TimeSignatureNumerator;
        var lastDen = project.TimeSignatureDenominator;
        var cursorTick = 0;
        for (var i = 0; i < timeline.Bars.Count; i++)
        {
            var bar = timeline.Bars[i];
            var tick = map.TickOfBar(i);
            var measure = MusicTime.BarOf(project, bar.Bar);
            var num = measure?.TimeSigNum ?? project.TimeSignatureNumerator;
            var den = measure?.TimeSigDenom ?? project.TimeSignatureDenominator;
            if (num != lastNum || den != lastDen)
            {
                WriteTimeSig(events, tick - cursorTick, num, den);
                cursorTick = tick;
                lastNum = num; lastDen = den;
            }
            if (bar.Tempo != lastTempo)
            {
                WriteTempo(events, tick - cursorTick, bar.Tempo);
                cursorTick = tick;
                lastTempo = bar.Tempo;
            }
            // Tempo changes and ramps inside the bar (tempo ramp): one tempo event per sixteenth slot whose
            // whole-BPM tempo differs, so the file's tempo track follows the same map the notes were timed with.
            if (measure?.MidBarTempos is { Count: > 0 })
                for (var slot = 1; slot < bar.Slots; slot++)
                {
                    var tempo = MusicTime.TempoAtSlot(measure, slot, bar.Tempo);
                    if (tempo == lastTempo) continue;
                    var slotTick = tick + (int)Math.Round(slot * (double)Division / MusicTime.SlotsPerQuarter);
                    WriteTempo(events, slotTick - cursorTick, tempo);
                    cursorTick = slotTick;
                    lastTempo = tempo;
                }
        }
        return BuildChunk(events);
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
            cursorTick = tick;
            WriteVarLen(events, delta);
            events.Add((byte)(e.Status & 0xFF));
            events.Add((byte)(e.Data1 & 0x7F));
            if ((e.Status & 0xF0) != 0xC0 && (e.Status & 0xF0) != 0xD0) events.Add((byte)(e.Data2 & 0x7F));
        }
        return BuildChunk(events);
    }

    /// <summary>Absolute ms to absolute MIDI ticks, using the timeline's own bar/tempo map.</summary>
    internal sealed class TickMap
    {
        private readonly List<ScoreBar> _bars;
        private readonly double[] _startTick;

        private readonly SongProject _project;

        public TickMap(ScoreTimeline timeline, SongProject project)
        {
            _project = project;
            _bars = timeline.Bars;
            _startTick = new double[_bars.Count];
            var running = 0.0;
            for (var i = 0; i < _bars.Count; i++)
            {
                _startTick[i] = running;
                running += _bars[i].Slots * (double)Division / MusicTime.SlotsPerQuarter;
            }
        }

        public int TickOfBar(int index)
        {
            if (index < 0 || index >= _startTick.Length) return 0;
            return (int)Math.Round(_startTick[index]);
        }

        public int TickOf(double ms)
        {
            if (_bars.Count == 0) return 0;
            var lo = 0;
            var hi = _bars.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (_bars[mid].StartMs <= ms + 1e-6) lo = mid; else hi = mid - 1;
            }
            var bar = _bars[lo];
            var barTicks = bar.Slots * (double)Division / MusicTime.SlotsPerQuarter;
            // A bar with tempo changes is not linear in time: find the slot whose tempo-map time is this moment.
            if (MusicTime.BarOf(_project, bar.Bar) is { MidBarTempos.Count: > 0 } varying && bar.Slots > 0)
            {
                double low = 0, high = bar.Slots;
                var relative = ms - bar.StartMs;
                for (var step = 0; step < 40; step++)
                {
                    var middle = (low + high) / 2;
                    if (MusicTime.OffsetMs(varying, middle, bar.Tempo) < relative) low = middle; else high = middle;
                }
                return (int)Math.Round(_startTick[lo] + (low + high) / 2 * Division / MusicTime.SlotsPerQuarter);
            }
            var length = Math.Max(1.0, bar.EndMs - bar.StartMs);
            var fraction = Math.Clamp((ms - bar.StartMs) / length, 0, 1);
            return (int)Math.Round(_startTick[lo] + fraction * barTicks);
        }
    }

    // ---------- chunk helpers ----------

    private static byte[] BuildChunk(List<byte> events)
    {
        WriteVarLen(events, 0); events.Add(0xFF); events.Add(0x2F); events.Add(0x00);
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
    {
        bpm = Math.Clamp(bpm, 20, 400);
        var mpq = (int)Math.Round(60_000_000.0 / bpm);
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
