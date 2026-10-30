using TabForge.Models;

namespace TabForge.Services;

// Owns: repairing the Latin-1 text of a Guitar Pro 3-5 import from the file's raw bytes (track names, titles, artist, album,
//   lyricist, composer, copyright, tab author, instructions).
// Does not own: the score reader (TabForge.AlphaTab), the byte decoding (GpLegacyText) or the song model.
// Tests: TabForge.Avalonia.Tests.GpLegacyEncodingRepairTests, TabForge.Avalonia.Tests.GpLegacyWalkTests.
// Long on purpose: one version-walk per legacy format, each validated step by step.
//
// The bundled score reader decodes every 3-5 string as UTF-8, so a file saved in the Latin-1 code page arrives with U+FFFD
// wherever a high byte stood (Bj\uFFFDrn Pettersson). The original byte is lost in that string, so the repair re-reads the
// raw bytes the layouts below mirror (the same layouts LegacyGpWriter writes) and decodes them with GpLegacyText. Only a
// value that holds U+FFFD is ever overwritten, and only when the raw bytes decode cleanly; anything unexpected in the walk
// (a count, a flag, a landing that does not validate) aborts the whole repair and the import keeps the reader's strings.
// Beat-level strings (chord names, beat text, per-beat lyrics) and the file-level lyric/notice joins are decoded inside the
// bundled reader and cannot be recovered from their strings afterwards, so they are out of reach here; every string byte this
// walk passes still goes through GpLegacyText. Instrument names never come from the file (they are derived from the General
// MIDI program), so there is nothing to repair in them.
internal static class GpLegacyTextRepair
{
    private sealed record Field(string Text, byte[] Raw);

    /// <summary>Repairs the Latin-1 header strings and track names of <paramref name="project"/> from <paramref name="raw"/> (a gp3/gp4/gp5 file). True when any value changed.</summary>
    public static bool Apply(SongProject project, byte[] raw)
    {
        try
        {
            if (!TryReadFile(raw, out var header, out var trackNames)) return false;
            if (trackNames.Count != project.Tracks.Count) return false;
            var changed = false;
            changed |= ApplyField(v => project.Title = v, project.Title, header.Title);
            changed |= ApplyField(v => project.Subtitle = v, project.Subtitle, header.Subtitle);
            changed |= ApplyField(v => project.Artist = v, project.Artist, header.Artist);
            changed |= ApplyField(v => project.Album = v, project.Album, header.Album);
            if (header.Words is { Text.Length: > 0 }) changed |= ApplyField(v => project.LyricsAuthor = v, project.LyricsAuthor, header.Words);
            if (header.Music is { Text.Length: > 0 }) changed |= ApplyField(v => project.MusicAuthor = v, project.MusicAuthor, header.Music);
            changed |= ApplyField(v => project.Copyright = v, project.Copyright, header.Copyright);
            changed |= ApplyField(v => project.TabAuthor = v, project.TabAuthor, header.Tab);
            changed |= ApplyField(v => project.Instructions = v, project.Instructions, header.Instructions);
            for (var i = 0; i < trackNames.Count; i++)
            {
                var track = project.Tracks[i];
                var name = GpLegacyText.OrRepaired(track.Name, trackNames[i].Raw);
                if (name.Length > 0 && name != track.Name && !GpLegacyText.ContainsReplacement(name))
                {
                    track.Name = GuitarProTrackConverter.TrackName(name);
                    changed = true;
                }
            }
            return changed;
        }
        catch (ArgumentException) { return false; }   // Not logged: a failed walk keeps the reader's strings; the import itself already succeeded.
        catch (IndexOutOfRangeException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool ApplyField(Action<string> set, string? current, Field? field)
    {
        if (field is null || !GpLegacyText.ContainsReplacement(current) || field.Raw.Length == 0) return false;
        var fixed_ = GpLegacyText.OrRepaired(current, field.Raw);
        if (fixed_.Length == 0 || fixed_ == current || GpLegacyText.ContainsReplacement(fixed_)) return false;
        set(fixed_);
        return true;
    }

    private sealed record Header(Field Title, Field Subtitle, Field Artist, Field Album, Field Words, Field? Music, Field Copyright, Field Tab, Field Instructions);

    private static bool TryReadFile(byte[] data, out Header header, out List<Field> trackNames)
    {
        header = null!;
        trackNames = new List<Field>();
        if (data.Length < 64 || data[0] is 0 or > 30) return false;
        var version = EncodingAscii(data, 1, data[0]);
        if (!version.StartsWith("FICHIER GUITAR PRO v", StringComparison.Ordinal)) return false;
        var (major, minor) = VersionOf(version);
        return major switch
        {
            3 => TryReadV3(data, out header, out trackNames),
            4 => TryReadV4(data, 8, out header, out trackNames) || TryReadV4(data, 9, out header, out trackNames),
            5 => TryReadV5(data, minor >= 10, out header, out trackNames) || TryReadV5(data, minor < 10, out header, out trackNames),
            _ => false,
        };
    }

    private static (int Major, int Minor) VersionOf(string version)
    {
        var v = version.StartsWith("FICHIER GUITAR PRO v", StringComparison.Ordinal) ? version["FICHIER GUITAR PRO v".Length..] : "";
        var dot = v.IndexOf('.');
        if (dot < 0 || !int.TryParse(v[..dot], out var major)) return (0, 0);
        return int.TryParse(new string(v[(dot + 1)..].TakeWhile(char.IsAsciiDigit).ToArray()), out var minor) ? (major, minor) : (major, 0);
    }

    private static string EncodingAscii(byte[] data, int offset, int count) => System.Text.Encoding.ASCII.GetString(data, offset, Math.Min(count, data.Length - offset));

    private static bool TryReadV3(byte[] data, out Header header, out List<Field> trackNames)
    {
        header = null!; trackNames = new List<Field>();
        var pos = 31;
        if (!TryReadInfo(data, ref pos, 8, out var info)) return false;
        if (!TrySkipNotices(data, ref pos)) return false;
        if (!TrySkipByte(data, ref pos)) return false;                       // the file-wide swing flag
        if (!TryReadTempo(data, ref pos, showByte: false)) return false;
        if (!TrySkipInt(data, ref pos)) return false;                       // the key
        return FinishTracks(data, ref pos, v5: false, rse: false, info, music: null, out header, out trackNames);
    }

    private static bool TryReadV4(byte[] data, int infoCount, out Header header, out List<Field> trackNames)
    {
        header = null!; trackNames = new List<Field>();
        var pos = 31;
        if (!TryReadInfo(data, ref pos, infoCount, out var info)) return false;
        if (!TrySkipNotices(data, ref pos)) return false;
        if (!TrySkipByte(data, ref pos)) return false;                       // the file-wide swing flag
        if (!TryReadLyrics(data, ref pos)) return false;
        if (!TryReadTempo(data, ref pos, showByte: false)) return false;
        if (!TrySkipInt(data, ref pos)) return false;                       // the key
        if (!TrySkipByte(data, ref pos)) return false;                       // the key's extra byte
        var music = infoCount == 9 ? info[5] : null;
        return FinishTracks(data, ref pos, v5: false, rse: false, info, music, out header, out trackNames);
    }

    private static bool TryReadV5(byte[] data, bool rse, out Header header, out List<Field> trackNames)
    {
        header = null!; trackNames = new List<Field>();
        var pos = 31;
        if (!TryReadInfo(data, ref pos, 9, out var info)) return false;
        if (!TrySkipNotices(data, ref pos)) return false;
        if (!TryReadLyrics(data, ref pos)) return false;
        if (rse && !TrySkipRse(data, ref pos)) return false;
        if (!TryReadPageSetup(data, ref pos)) return false;
        if (!TryReadTempo(data, ref pos, showByte: rse)) return false;
        if (!TrySkipByte(data, ref pos)) return false;                       // the key
        if (!TrySkipInt(data, ref pos)) return false;                        // the octave
        return FinishTracks(data, ref pos, v5: true, rse, info, info[5], out header, out trackNames);
    }

    private static bool FinishTracks(byte[] data, ref int pos, bool v5, bool rse, List<Field> info, Field? music,
        out Header header, out List<Field> trackNames)
    {
        header = null!; trackNames = new List<Field>();
        if (!TrySkipChannels(data, ref pos)) return false;
        if (v5 && !TrySkipShorts(data, ref pos, 19)) return false;
        if (v5 && !TrySkipInt(data, ref pos)) return false;                   // master reverb
        if (!TryReadCounts(data, ref pos, out var bars, out var tracks)) return false;
        if (!TrySkipMasterBars(data, ref pos, bars, v5)) return false;
        if (!TryReadTrackNames(data, ref pos, tracks, v5, rse, trackNames)) return false;
        var words = info[4];
        var offset = music is null ? 5 : 6;
        header = new Header(info[0], info[1], info[2], info[3], words, music, info[offset], info[offset + 1], info[offset + 2]);
        return true;
    }

    private static bool TryReadInfo(byte[] data, ref int pos, int count, out List<Field> info)
    {
        info = new List<Field>();
        for (var i = 0; i < count; i++)
        {
            var start = pos;
            if (!GpLegacyText.TryReadIntByteString(data, ref pos, out var text)) return false;
            // The raw text bytes only, not the int-plus-byte length envelope around them: decoding the envelope would read its lengths as control characters.
            info.Add(new Field(text, data[(start + 5)..pos]));
        }
        return true;
    }

    private static bool TrySkipNotices(byte[] data, ref int pos)
    {
        if (!TryReadInt(data, ref pos, out var count) || count is < 0 or > 1000) return false;
        for (var i = 0; i < count; i++)
            if (!GpLegacyText.TrySkipIntByteString(data, ref pos)) return false;
        return true;
    }

    private static bool TryReadLyrics(byte[] data, ref int pos)
    {
        if (!TryReadInt(data, ref pos, out var track) || track is < 0 or > 1000) return false;
        for (var i = 0; i < 5; i++)
        {
            if (!TryReadInt(data, ref pos, out _) || !GpLegacyText.TryReadIntString(data, ref pos, out _)) return false;
        }
        return true;
    }

    private static bool TrySkipRse(byte[] data, ref int pos)
    {
        var start = pos;
        if (!TryReadInt(data, ref pos, out var volume) || volume is < 0 or > 127) { pos = start; return false; }
        if (!TryReadInt(data, ref pos, out var reverb) || reverb is < 0 or > 127) { pos = start; return false; }
        for (var i = 0; i < 11; i++)
            if (!TrySkipByte(data, ref pos, 0)) { pos = start; return false; }
        return true;
    }

    private static bool TryReadPageSetup(byte[] data, ref int pos)
    {
        for (var i = 0; i < 7; i++)
            if (!TryReadInt(data, ref pos, out _)) return false;
        if (!TrySkipShort(data, ref pos)) return false;
        for (var i = 0; i < 11; i++)
            if (!GpLegacyText.TryReadIntByteString(data, ref pos, out _)) return false;
        return true;
    }

    private static bool TryReadTempo(byte[] data, ref int pos, bool showByte)
    {
        // The tempo text is the last page-setup string (already read); here comes only the tempo and, in a 5.10 file, whether it shows.
        if (!TryReadInt(data, ref pos, out var tempo) || tempo is < -1 or > 1000) return false;
        return !showByte || TrySkipByte(data, ref pos);
    }

    private static bool TrySkipChannels(byte[] data, ref int pos)
    {
        for (var i = 0; i < 64; i++)
        {
            if (!TryReadInt(data, ref pos, out var program) || program is < -1 or > 127) return false;
            for (var b = 0; b < 6; b++)
                if (!TrySkipByte(data, ref pos, 16)) return false;
            if (!TrySkipByte(data, ref pos)) return false;
            if (!TrySkipByte(data, ref pos)) return false;
        }
        return true;
    }

    private static bool TryReadCounts(byte[] data, ref int pos, out int bars, out int tracks)
    {
        bars = 0; tracks = 0;
        if (!TryReadInt(data, ref pos, out bars) || bars is < 1 or > 20000) return false;
        return TryReadInt(data, ref pos, out tracks) && tracks is >= 1 and <= 100;
    }

    private static bool TrySkipMasterBars(byte[] data, ref int pos, int bars, bool v5)
    {
        for (var b = 0; b < bars; b++)
        {
            if (!TryReadByte(data, ref pos, out var flags)) return false;
            if ((flags & 0x01) != 0 && !TrySkipByte(data, ref pos)) return false;
            if ((flags & 0x02) != 0 && !TrySkipByte(data, ref pos)) return false;
            if ((flags & 0x08) != 0 && !TrySkipByte(data, ref pos)) return false;   // the repeat-close count
            if (!v5 && (flags & 0x10) != 0 && !TrySkipByte(data, ref pos)) return false;   // the alternate ending
            if ((flags & 0x20) != 0)
            {
                if (!GpLegacyText.TryReadIntByteString(data, ref pos, out _)) return false;   // the section marker
                for (var i = 0; i < 4; i++)
                    if (!TrySkipByte(data, ref pos)) return false;
            }
            if ((flags & 0x40) != 0)
            {
                if (!TrySkipByte(data, ref pos)) return false;             // the key
                if (!TrySkipByte(data, ref pos)) return false;             // major or minor
            }
            if (!v5) continue;
            if ((flags & 0x03) != 0 && !TrySkipBytes(data, ref pos, 4)) return false;   // how eighth notes beam
            if (!TrySkipByte(data, ref pos)) return false;                 // the alternate endings
            if (!TrySkipByte(data, ref pos)) return false;                 // the swing feel
            if (!TrySkipByte(data, ref pos)) return false;
        }
        return true;
    }

    private static bool TryReadTrackNames(byte[] data, ref int pos, int tracks, bool v5, bool rse, List<Field> names)
    {
        for (var t = 0; t < tracks; t++)
        {
            if (!TrySkipByte(data, ref pos)) return false;                 // the track flags
            var start = pos;
            if (!GpLegacyText.TryReadByteStringPadded(data, ref pos, 40, out var name)) return false;
            // The raw text bytes only (the length byte plus the name, without its zero padding): decoding the padding would read it as control characters.
            names.Add(new Field(GuitarProTrackConverter.TrackName(name), data[(start + 1)..(start + 1 + data[start])]));
            if (!TryReadInt(data, ref pos, out var strings) || strings is < 1 or > 12) return false;
            for (var i = 0; i < 7; i++)
                if (!TryReadInt(data, ref pos, out var tuning) || tuning is < -1 or > 127) return false;
            for (var i = 0; i < 5; i++)
                if (!TryReadInt(data, ref pos, out _)) return false;      // port, channel, effect, frets, capo
            if (!TrySkipBytes(data, ref pos, 4)) return false;             // the colour
            if (!v5) continue;
            if (!TrySkipShort(data, ref pos)) return false;
            if (!TrySkipBytes(data, ref pos, 2 + 1 + 12 + 12 + 16)) return false;
            if (rse)
            {
                if (!TrySkipBytes(data, ref pos, 4)) return false;
                if (!GpLegacyText.TryReadIntByteString(data, ref pos, out _)) return false;
                if (!GpLegacyText.TryReadIntByteString(data, ref pos, out _)) return false;
            }
        }
        return true;
    }

    private static bool TryReadInt(byte[] data, ref int pos, out int value)
    {
        value = 0;
        if (pos < 0 || pos + 4 > data.Length) return false;
        value = BitConverter.ToInt32(data, pos);
        pos += 4;
        return true;
    }

    private static bool TrySkipInt(byte[] data, ref int pos) => TryReadInt(data, ref pos, out _);

    private static bool TryReadByte(byte[] data, ref int pos, out int value)
    {
        value = 0;
        if (pos < 0 || pos + 1 > data.Length) return false;
        value = data[pos++];
        return true;
    }

    private static bool TrySkipByte(byte[] data, ref int pos) => TryReadByte(data, ref pos, out _);

    private static bool TrySkipByte(byte[] data, ref int pos, int maximum)
    {
        if (!TryReadByte(data, ref pos, out var value) || value > maximum) return false;
        return true;
    }

    private static bool TrySkipBytes(byte[] data, ref int pos, int count)
    {
        if (count < 0 || pos < 0 || pos + count > data.Length) return false;
        pos += count;
        return true;
    }

    private static bool TrySkipShort(byte[] data, ref int pos) => TrySkipBytes(data, ref pos, 2);

    private static bool TrySkipShorts(byte[] data, ref int pos, int count) => TrySkipBytes(data, ref pos, count * 2);
}
