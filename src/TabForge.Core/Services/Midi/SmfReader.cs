using System.IO;
using System.Text;
using TabForge.Playback;

namespace TabForge.Services;

// Owns: reading a Standard MIDI File (format 0 or 1) into notes, tempo, time and key signatures, and refusing anything malformed.
// Does not own: turning those into a song (MidiToSong).
// Tests: MidiImportTests.
/// <summary>One note of a MIDI file; ticks count from the start of the file. <paramref name="Marker"/> is the TF1 technique payload found at its tick.</summary>
internal readonly record struct SmfNote(long Start, long End, int Channel, int Pitch, int Velocity, int Program, string? Marker = null);

/// <summary>One controller point of a MIDI file: a control change (<see cref="Kind"/> 0xB0) or the pitch bend (0xE0, value -8192..8191).</summary>
internal readonly record struct SmfController(long Tick, int Channel, int Kind, int Controller, int Value);

/// <summary>One track chunk: its name, its notes in start order, its controller points and its TF1 technique markers.</summary>
internal sealed class SmfTrack
{
    public string Name { get; set; } = "";
    public List<SmfNote> Notes { get; } = new();
    public List<SmfController> Controllers { get; } = new();
    public List<(long Tick, int Channel, int Pitch, string Payload)> Markers { get; } = new();
    /// <summary>Program changes in file order (tick, channel, program): the track's first change anywhere, not only before the first note.</summary>
    public List<(long Tick, int Channel, int Program)> ProgramChanges { get; } = new();
    /// <summary>Marker (0x06) and cue-point (0x07) texts that are not TF1 technique markers: song-section candidates.</summary>
    public List<(long Tick, string Text)> SectionEvents { get; } = new();
}

/// <summary>A parsed Standard MIDI File.</summary>
internal sealed class SmfFile
{
    public int Format { get; set; }
    public int Division { get; set; }
    public List<SmfTrack> Tracks { get; } = new();
    /// <summary>(tick, microseconds per quarter), in tick order.</summary>
    public List<(long Tick, int Mpq)> Tempos { get; } = new();
    public List<(long Tick, int Numerator, int Denominator)> TimeSignatures { get; } = new();
    public List<(long Tick, int Fifths, bool Minor)> Keys { get; } = new();
}

internal static class SmfReader
{
    private const string Invalid = "This score file is invalid, truncated, or unsupported.";
    private const long MaxTick = 1L << 40;

    /// <summary>Parses the file; throws <see cref="InvalidDataException"/> when it is not a well-formed format 0/1 file with ticks-per-quarter timing. Notes are limited by <see cref="InputLimits"/>.</summary>
    public static SmfFile Read(byte[] data, ImportContext? context = null)
    {
        if (data.Length < 14 || !IsTag(data, 0, "MThd")) throw new InvalidDataException(Invalid);
        var headerLength = (int)Math.Min(ReadU32(data, 4), int.MaxValue);
        if (headerLength < 6 || headerLength > data.Length - 8) throw new InvalidDataException(Invalid);
        var file = new SmfFile { Format = ReadU16(data, 8), Division = ReadU16(data, 12) };
        var declared = ReadU16(data, 10);
        if (file.Format > 1 || file.Division == 0 || (file.Division & 0x8000) != 0) throw new InvalidDataException(Invalid);   // format 2 and SMPTE timing are not scores
        if (declared == 0 || declared > InputLimits.MaxTracks || (file.Format == 0 && declared != 1)) throw new InvalidDataException(Invalid);
        var pos = 8 + headerLength;
        long notes = 0;
        while (file.Tracks.Count < declared)
        {
            context?.Check();
            if (pos + 8 > data.Length) throw new InvalidDataException(Invalid);
            var length = ReadU32(data, pos + 4);
            if (length > (uint)(data.Length - pos - 8)) throw new InvalidDataException(Invalid);
            var isTrack = IsTag(data, pos, "MTrk");
            var start = pos + 8;
            pos = start + (int)length;
            if (!isTrack) continue;   // an unknown chunk is skipped
            file.Tracks.Add(ReadTrack(data, start, pos, file, context, ref notes));
        }
        // Stable sort: events on one tick keep file order (track by track), so the last one read is the one that applies.
        var tempos = file.Tempos.OrderBy(t => t.Tick).ToList(); file.Tempos.Clear(); file.Tempos.AddRange(tempos);
        var signatures = file.TimeSignatures.OrderBy(t => t.Tick).ToList(); file.TimeSignatures.Clear(); file.TimeSignatures.AddRange(signatures);
        var keys = file.Keys.OrderBy(t => t.Tick).ToList(); file.Keys.Clear(); file.Keys.AddRange(keys);
        return file;
    }

    private static SmfTrack ReadTrack(byte[] d, int pos, int end, SmfFile file, ImportContext? context, ref long noteCount)
    {
        var track = new SmfTrack();
        long tick = 0;
        var running = 0;
        var program = new int[16];
        var open = new Dictionary<(int Channel, int Pitch), Queue<(long Start, int Velocity, int Program)>>();
        var ended = false;
        var events = 0;
        while (pos < end && !ended)
        {
            if ((++events & 0x3FF) == 0) context?.Check();   // a huge track chunk still honours cancel, time and memory limits
            tick += ReadVlq(d, ref pos, end);
            if (tick > MaxTick) throw new InvalidDataException(Invalid);
            if (pos >= end) throw new InvalidDataException(Invalid);
            int status = d[pos];
            if (status < 0x80)
            {
                if (running == 0) throw new InvalidDataException(Invalid);
                status = running;
            }
            else pos++;
            if (status == 0xFF)
            {
                running = 0;
                if (ReadMeta(d, ref pos, end, tick, track, file)) ended = true;
            }
            else if (status is 0xF0 or 0xF7)
            {
                running = 0;
                var length = ReadVlq(d, ref pos, end);
                if (length > end - pos) throw new InvalidDataException(Invalid);
                pos += (int)length;
            }
            else if (status >= 0xF0) throw new InvalidDataException(Invalid);
            else
            {
                running = status;
                ReadChannelEvent(d, ref pos, end, status, tick, track, program, open, ref noteCount);
            }
        }
        // A note still sounding at the end of the track stops there.
        foreach (var ((channel, pitch), queue) in open)
            foreach (var (noteStart, velocity, noteProgram) in queue)
                track.Notes.Add(new SmfNote(noteStart, Math.Max(tick, noteStart), channel, pitch, velocity, noteProgram));
        AttachMarkers(track);
        track.Notes.Sort((x, y) => x.Start != y.Start ? x.Start.CompareTo(y.Start) : x.Pitch.CompareTo(y.Pitch));
        track.Controllers.Sort((x, y) => x.Tick != y.Tick ? x.Tick.CompareTo(y.Tick) : x.Channel.CompareTo(y.Channel));
        return track;
    }

    /// <summary>Puts each TF1 marker on the note it describes (same start tick, channel and pitch; a pitch mismatch still matches by tick when unique).</summary>
    private static void AttachMarkers(SmfTrack track)
    {
        if (track.Markers.Count == 0) return;
        var byKey = new Dictionary<(long Tick, int Channel, int Pitch), string>();
        var byTick = new Dictionary<long, string>();
        foreach (var marker in track.Markers)
        {
            byKey.TryAdd((marker.Tick, marker.Channel, marker.Pitch), marker.Payload);
            byTick.TryAdd(marker.Tick, marker.Payload);
        }
        var tickCounts = new Dictionary<long, int>();
        foreach (var note in track.Notes) tickCounts[note.Start] = tickCounts.GetValueOrDefault(note.Start) + 1;
        for (var i = 0; i < track.Notes.Count; i++)
        {
            var note = track.Notes[i];
            if (byKey.TryGetValue((note.Start, note.Channel, note.Pitch), out var payload) ||
                (tickCounts[note.Start] == 1 && byTick.TryGetValue(note.Start, out payload)))
                track.Notes[i] = note with { Marker = payload };
        }
    }

    /// <summary>A meta event (the status byte is already read): track name, tempo, time signature or key; true for the end of the track.</summary>
    private static bool ReadMeta(byte[] d, ref int pos, int end, long tick, SmfTrack track, SmfFile file)
    {
        if (pos >= end) throw new InvalidDataException(Invalid);
        var type = d[pos++];
        var length = ReadVlq(d, ref pos, end);
        if (length > end - pos) throw new InvalidDataException(Invalid);
        var body = d.AsSpan(pos, (int)length);
        pos += (int)length;
        switch (type)
        {
            case 0x2F: return true;
            case 0x03 when track.Name.Length == 0:
                var name = Encoding.Latin1.GetString(body).Trim((char)0, (char)32);
                track.Name = name.Length > InputLimits.MaxTitleLength ? name[..InputLimits.MaxTitleLength] : name;
                break;
            case 0x51 when body.Length == 3:
                var mpq = body[0] << 16 | body[1] << 8 | body[2];
                if (mpq > 0) { RequireRoom(file.Tempos.Count); file.Tempos.Add((tick, mpq)); }
                break;
            case 0x58 when body.Length >= 2 && body[1] <= 6:
                RequireRoom(file.TimeSignatures.Count);
                if (body[0] > InputLimits.MaxTimeSignatureNumerator) throw new InvalidDataException(Invalid);   // refused here, not after bars are built from it
                file.TimeSignatures.Add((tick, Math.Max(1, (int)body[0]), 1 << body[1]));
                break;
            case 0x59 when body.Length == 2:
                RequireRoom(file.Keys.Count);
                file.Keys.Add((tick, (sbyte)body[0], body[1] != 0));
                break;
            case 0x01 or 0x06 or 0x07 when body.Length is > 0 and <= TechniqueMarker.MaxPayload + 24:
                // A "Keep techniques for TabForge" marker: version, channel, pitch, payload. Other 0x06/0x07 texts are song sections.
                var text = Encoding.Latin1.GetString(body);
                if (TechniqueMarker.PayloadOf(text) is { } payload)
                {
                    var parts = text.Split(':', 4);
                    if (int.TryParse(parts[1], out var channel) && int.TryParse(parts[2], out var pitch)
                        && channel is >= 0 and <= 15 && pitch is >= 0 and <= 127
                        && track.Markers.Count < InputLimits.MaxMidiMarkers)
                        track.Markers.Add((tick, channel, pitch, payload));
                    break;
                }
                if (type == 0x01 || TechniqueMarker.IsMarker(text)) break;   // legacy TF1 markers never become sections
                var sectionText = Encoding.UTF8.GetString(body).Trim();
                if (sectionText.Length == 0) break;
                if (track.SectionEvents.Count >= InputLimits.MaxMidiMetaEvents) break;
                track.SectionEvents.Add((tick, sectionText.Length > InputLimits.MaxTitleLength ? sectionText[..InputLimits.MaxTitleLength] : sectionText));
                break;
            case 0x06 or 0x07 when body.Length is > TechniqueMarker.MaxPayload + 24 and <= InputLimits.MaxTitleLength * 4 + 8:
                // An overlong (multi-byte) section text the marker case above did not take.
                var longSection = Encoding.UTF8.GetString(body).Trim();
                if (longSection.Length == 0 || TechniqueMarker.IsMarker(longSection)) break;
                if (track.SectionEvents.Count >= InputLimits.MaxMidiMetaEvents) break;
                track.SectionEvents.Add((tick, longSection.Length > InputLimits.MaxTitleLength ? longSection[..InputLimits.MaxTitleLength] : longSection));
                break;
            case 0x7F when body.Length is >= 5 and <= TechniqueMarker.MaxPayload + 28:
                // A "Keep techniques for TabForge" sequencer-specific event: 0x7D then the same TF1 text (other programs ignore it).
                if (body[0] != 0x7D || body[1] != (byte)'T' || body[2] != (byte)'F' || body[3] != (byte)'1') break;
                var seqText = Encoding.Latin1.GetString(body.Slice(1));
                if (TechniqueMarker.PayloadOf(seqText) is not { } seqPayload) break;
                var seqParts = seqText.Split(':', 4);
                if (!int.TryParse(seqParts[1], out var seqChannel) || !int.TryParse(seqParts[2], out var seqPitch)) break;
                if (seqChannel is < 0 or > 15 || seqPitch is < 0 or > 127) break;
                if (track.Markers.Count >= InputLimits.MaxMidiMarkers) break;
                track.Markers.Add((tick, seqChannel, seqPitch, seqPayload));
                break;
        }
        return false;
    }

    /// <summary>A channel message (the status byte is already read): a program change, or a note on / off paired into a note of the track.</summary>
    private static void ReadChannelEvent(byte[] d, ref int pos, int end, int status, long tick, SmfTrack track, int[] program, Dictionary<(int Channel, int Pitch), Queue<(long Start, int Velocity, int Program)>> open, ref long noteCount)
    {
        var kind = status & 0xF0;
        var channel = status & 0x0F;
        var need = kind is 0xC0 or 0xD0 ? 1 : 2;
        if (pos + need > end) throw new InvalidDataException(Invalid);
        int a = d[pos], b = need == 2 ? d[pos + 1] : 0;
        pos += need;
        if ((a | b) >= 0x80) throw new InvalidDataException(Invalid);
        if (kind == 0xB0 && track.Controllers.Count < InputLimits.MaxMidiControllerPoints)
            track.Controllers.Add(new SmfController(tick, channel, 0xB0, a, b));
        else if (kind == 0xE0 && track.Controllers.Count < InputLimits.MaxMidiControllerPoints)
            track.Controllers.Add(new SmfController(tick, channel, 0xE0, 0, ((b << 7) | a) - 8192));
        if (kind == 0xC0)
        {
            program[channel] = a;
            if (track.ProgramChanges.Count < InputLimits.MaxMidiMetaEvents) track.ProgramChanges.Add((tick, channel, a));
        }
        else if (kind == 0x90 && b > 0)
        {
            if (++noteCount > InputLimits.MaxTotalNotes) throw new InvalidDataException("The score file contains too many notes.");
            if (!open.TryGetValue((channel, a), out var queue)) open[(channel, a)] = queue = new();
            queue.Enqueue((tick, b, program[channel]));
        }
        else if (kind is 0x80 or 0x90 && open.TryGetValue((channel, a), out var held) && held.Count > 0)
        {
            var (noteStart, velocity, noteProgram) = held.Dequeue();
            track.Notes.Add(new SmfNote(noteStart, Math.Max(tick, noteStart), channel, a, velocity, noteProgram));
        }
    }

    private static void RequireRoom(int count)
    {
        if (count >= InputLimits.MaxMidiMetaEvents) throw new InvalidDataException(Invalid);
    }

    private static long ReadVlq(byte[] d, ref int pos, int end)
    {
        long value = 0;
        for (var i = 0; i < 4; i++)
        {
            if (pos >= end) throw new InvalidDataException(Invalid);
            var b = d[pos++];
            value = value << 7 | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) return value;
        }
        throw new InvalidDataException(Invalid);
    }

    private static bool IsTag(byte[] d, int at, string tag) => d[at] == tag[0] && d[at + 1] == tag[1] && d[at + 2] == tag[2] && d[at + 3] == tag[3];
    private static uint ReadU32(byte[] d, int at) => (uint)(d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3]);
    private static int ReadU16(byte[] d, int at) => d[at] << 8 | d[at + 1];
}
