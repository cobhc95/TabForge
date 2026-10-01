using System.IO;
using TabForge.Audio;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>One note read from a Standard MIDI File: ticks from the file start, channel 0-15.</summary>
public readonly record struct MidiFileNote(long OnTick, long OffTick, int Channel, int Pitch, int Velocity);

/// <summary>
/// A Standard MIDI File reduced to what a MIDI clip needs: every note of every track (merged), the file length and how
/// its ticks map to time. <see cref="Ppq"/> &gt; 0: ticks are musical (quarter notes), placed on the song's own tempo map;
/// otherwise (SMPTE division) ticks are absolute time, <see cref="TicksPerSecond"/>.
/// </summary>
public sealed class MidiFileData
{
    public int Ppq { get; init; }
    public double TicksPerSecond { get; init; }
    public long LengthTicks { get; init; }
    public List<MidiFileNote> Notes { get; init; } = new();

    public bool Musical => Ppq > 0;
    /// <summary>Length in quarter notes (musical files), rounded up to a whole beat so a groove fills its bars.</summary>
    public double LengthQuarters => Musical ? Math.Max(1, Math.Ceiling(LengthTicks / (double)Ppq - 1e-6)) : 0;
    /// <summary>Length in seconds (SMPTE files only).</summary>
    public double LengthSeconds => Musical ? 0 : LengthTicks / Math.Max(1, TicksPerSecond);
    public bool HasDrumChannel => Notes.Any(n => n.Channel == 9);

    /// <summary>
    /// The notes a track takes: a drum track takes the drum channel (10) when the file has one, a melodic track the other
    /// channels when there are any; a file with only one kind gives all its notes either way (many drum plug-ins write
    /// their grooves on channel 1).
    /// </summary>
    public IEnumerable<MidiFileNote> NotesFor(bool drumTrack)
    {
        var drums = Notes.Any(n => n.Channel == 9);
        var other = Notes.Any(n => n.Channel != 9);
        if (!(drums && other)) return Notes;
        return drumTrack ? Notes.Where(n => n.Channel == 9) : Notes.Where(n => n.Channel != 9);
    }
}

/// <summary>Reads .mid / .midi files (format 0, 1 and 2) for MIDI clips; bounded, never trusts the file's lengths.</summary>
public static class MidiFileImport
{
    public static readonly string[] Extensions = { ".mid", ".midi" };
    public const long MaxFileBytes = 16L * 1024 * 1024;
    public const int MaxNotes = 200_000;

    public static bool IsMidiFile(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static MidiFileData Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The MIDI file is missing", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("The MIDI file is too large");
        return Parse(File.ReadAllBytes(path));
    }

    public static MidiFileData Parse(byte[] bytes)
    {
        var pos = 0;
        string Chunk() { Need(bytes, pos, 8); var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4); pos += 4; return id; }
        uint U32() { Need(bytes, pos, 4); var v = (uint)(bytes[pos] << 24 | bytes[pos + 1] << 16 | bytes[pos + 2] << 8 | bytes[pos + 3]); pos += 4; return v; }
        int U16() { Need(bytes, pos, 2); var v = bytes[pos] << 8 | bytes[pos + 1]; pos += 2; return v; }

        // A RIFF-wrapped MIDI file (RMID): skip to the "data" chunk.
        if (bytes.Length >= 20 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F')
        {
            var idx = IndexOf(bytes, "MThd"u8);
            if (idx < 0) throw new InvalidDataException("Not a MIDI file");
            pos = idx;
        }
        if (Chunk() != "MThd") throw new InvalidDataException("Not a MIDI file");
        var headerLength = (int)Math.Min(U32(), 64);
        var headerStart = pos;
        U16();   // format: 0, 1 or 2; every track is merged either way
        var trackCount = U16();
        var division = U16();
        pos = headerStart + Math.Max(6, headerLength);
        int ppq; double ticksPerSecond = 0;
        if ((division & 0x8000) != 0)
        {
            var fps = -(sbyte)(division >> 8);
            var perFrame = division & 0xFF;
            if (fps <= 0 || perFrame <= 0) throw new InvalidDataException("The MIDI file's time division is not valid");
            ppq = 0;
            ticksPerSecond = (fps == 29 ? 29.97 : fps) * perFrame;
        }
        else
        {
            ppq = division;
            if (ppq <= 0) throw new InvalidDataException("The MIDI file's time division is not valid");
        }

        var notes = new List<MidiFileNote>();
        long length = 0;
        for (var t = 0; t < trackCount && pos + 8 <= bytes.Length; t++)
        {
            var id = Chunk();
            var size = U32();
            var end = (int)Math.Min(bytes.Length, (long)pos + size);
            if (id != "MTrk") { pos = end; t--; if (pos >= bytes.Length) break; continue; }   // unknown chunk: skip it
            length = Math.Max(length, ReadTrack(bytes, pos, end, notes));
            pos = end;
        }
        if (notes.Count == 0) throw new InvalidDataException("The MIDI file has no notes");
        notes.Sort((a, b) => a.OnTick != b.OnTick ? a.OnTick.CompareTo(b.OnTick) : a.Pitch.CompareTo(b.Pitch));
        length = Math.Max(length, notes.Max(n => n.OffTick));
        return new MidiFileData { Ppq = ppq, TicksPerSecond = ticksPerSecond, LengthTicks = length, Notes = notes };
    }

    /// <summary>One track's notes (note-on velocity 0 is a note-off); returns the track's last tick (its end-of-track).</summary>
    private static long ReadTrack(byte[] b, int pos, int end, List<MidiFileNote> notes)
    {
        long tick = 0;
        var status = 0;
        var open = new Dictionary<int, Queue<(long Tick, int Velocity)>>();
        while (pos < end)
        {
            tick += ReadVlq(b, ref pos, end);
            if (pos >= end) break;
            int first = b[pos];
            if (first >= 0x80) { pos++; if (first < 0xF0) status = first; }
            else if (status == 0) throw new InvalidDataException("The MIDI file has a data byte without a status");
            else first = status;   // running status: this byte is data
            if (first == 0xFF)
            {
                if (pos >= end) break;
                var type = b[pos++];
                var len = ReadVlq(b, ref pos, end);
                pos = (int)Math.Min(end, pos + len);
                if (type == 0x2F) break;   // end of track
                continue;
            }
            if (first is 0xF0 or 0xF7)
            {
                var len = ReadVlq(b, ref pos, end);
                pos = (int)Math.Min(end, pos + len);
                continue;
            }
            var kind = first & 0xF0;
            var channel = first & 0x0F;
            var dataBytes = kind is 0xC0 or 0xD0 ? 1 : 2;
            if (pos + dataBytes > end) break;
            int d1 = b[pos] & 0x7F, d2 = dataBytes == 2 ? b[pos + 1] & 0x7F : 0;
            pos += dataBytes;
            var key = channel << 8 | d1;
            if (kind == 0x90 && d2 > 0)
            {
                if (!open.TryGetValue(key, out var q)) open[key] = q = new Queue<(long, int)>();
                q.Enqueue((tick, d2));
            }
            else if (kind == 0x80 || (kind == 0x90 && d2 == 0))
            {
                if (open.TryGetValue(key, out var q) && q.Count > 0) Add(notes, q.Dequeue(), tick, channel, d1);
            }
        }
        // Notes never released end with the track.
        foreach (var (key, q) in open)
            while (q.Count > 0) Add(notes, q.Dequeue(), tick, key >> 8, key & 0xFF);
        return tick;
    }

    private static void Add(List<MidiFileNote> notes, (long Tick, int Velocity) on, long off, int channel, int pitch)
    {
        if (notes.Count >= MaxNotes) throw new InvalidDataException("The MIDI file has too many notes");
        notes.Add(new MidiFileNote(on.Tick, Math.Max(on.Tick + 1, off), channel, pitch, Math.Clamp(on.Velocity, 1, 127)));
    }

    private static long ReadVlq(byte[] b, ref int pos, int end)
    {
        long value = 0;
        for (var i = 0; i < 4; i++)
        {
            if (pos >= end) return value;
            var c = b[pos++];
            value = value << 7 | (uint)(c & 0x7F);
            if ((c & 0x80) == 0) return value;
        }
        throw new InvalidDataException("The MIDI file has a malformed length");
    }

    private static void Need(byte[] b, int pos, int count)
    {
        if (pos < 0 || pos + count > b.Length) throw new InvalidDataException("The MIDI file is cut short");
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);

    /// <summary>
    /// The clip's notes in seconds from the clip start, placed at <paramref name="clipStartSec"/> on the song's tempo map
    /// (musical files: a beat of the file is a beat of the song, so a groove lands on the bar grid at the song's tempo).
    /// Returns the clip's source length in seconds as well.
    /// </summary>
    public static (List<ClipNote> Notes, double LengthSec) ToClipNotes(MidiFileData file, double clipStartSec, SongQuarterMap time, bool drumTrack)
    {
        var result = new List<ClipNote>();
        if (file.Musical)
        {
            var q0 = time.QuarterAt(clipStartSec);
            double Sec(long tick) => time.SecAt(q0 + tick / (double)file.Ppq) - clipStartSec;
            foreach (var n in file.NotesFor(drumTrack))
            {
                var on = Sec(n.OnTick);
                result.Add(new ClipNote(on, Math.Max(0.01, Sec(n.OffTick) - on), n.Pitch, n.Velocity));
            }
            return (result, Math.Max(0.05, time.SecAt(q0 + file.LengthQuarters) - clipStartSec));
        }
        foreach (var n in file.NotesFor(drumTrack))
        {
            var on = n.OnTick / file.TicksPerSecond;
            result.Add(new ClipNote(on, Math.Max(0.01, (n.OffTick - n.OnTick) / file.TicksPerSecond), n.Pitch, n.Velocity));
        }
        return (result, Math.Max(0.05, file.LengthSeconds));
    }
}

/// <summary>
/// Song seconds &lt;-&gt; quarter notes on the song as performed (repeats included), from the same bar map the plug-in transport
/// uses (<see cref="SongClock.TransportBars"/>): each bar at its own tempo and time signature, the last bar's tempo after the end.
/// </summary>
public sealed class SongQuarterMap
{
    private readonly double[] _sec, _q, _tempo;

    public SongQuarterMap(IReadOnlyList<TabForge.Audio.Contracts.TransportBar> bars, double fallbackTempo)
    {
        var n = Math.Max(1, bars.Count);
        _sec = new double[n]; _q = new double[n]; _tempo = new double[n];
        if (bars.Count == 0) { _tempo[0] = Math.Clamp(fallbackTempo, 1, 2000); return; }
        for (var i = 0; i < bars.Count; i++)
        {
            _sec[i] = bars[i].StartSec; _q[i] = bars[i].StartPpq; _tempo[i] = Math.Clamp(bars[i].Tempo, 1, 2000);
        }
    }

    public static SongQuarterMap For(SongProject project) => new(SongClock.TransportBars(project), project.Tempo);

    public double QuarterAt(double sec)
    {
        var i = Last(_sec, sec);
        return _q[i] + (sec - _sec[i]) * _tempo[i] / 60.0;
    }

    public double SecAt(double quarter)
    {
        var i = Last(_q, quarter);
        return _sec[i] + (quarter - _q[i]) * 60.0 / _tempo[i];
    }

    /// <summary>One bar's worth of time at a song time (a placeholder length for a file not measured yet).</summary>
    public double BarSecAt(double sec) => SecAt(QuarterAt(sec) + 4) - sec;

    private static int Last(double[] starts, double value)
    {
        int lo = 0, hi = starts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (starts[mid] <= value) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}
