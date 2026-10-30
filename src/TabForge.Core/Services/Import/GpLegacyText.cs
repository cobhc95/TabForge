using System.Text;

namespace TabForge.Services;

// Owns: decoding the single-byte text of Guitar Pro 3-5 files (track names, titles, artist, album, lyrics, chord names,
//   text events, instrument names) and the binary string envelopes that carry them.
// Does not own: the score reader itself (TabForge.AlphaTab, reached through AlphaTabBoundary) or the song model (TabForge.Models).
// Tests: TabForge.Avalonia.Tests.GpLegacyTextTests, TabForge.Avalonia.Tests.GpLegacyEncodingRepairTests.
// Long on purpose: one place for the legacy text rules of every gp3/gp4/gp5 byte reader.
//
// Guitar Pro 3-5 strings are single-byte in the file's code page (usually Windows-1252/Latin-1); some files carry UTF-8.
// The bundled score reader decodes them as UTF-8, so a Latin-1 byte that is not valid UTF-8 arrives as U+FFFD
// (Bj\uFFFDrn Pettersson for Bjorn Pettersson). Decode strictly: try UTF-8 with throwOnInvalid; on failure fall back to
// Windows-1252 (as a switch, so no CodePages provider is needed; the five bytes 1252 leaves undefined read as Latin-1).
// The fallback never yields U+FFFD for Latin-1 text.
internal static class GpLegacyText
{
    private static Encoding StrictUtf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Windows-1252 for the high bytes (the five bytes 1252 leaves undefined read as Latin-1); the rest of Latin-1 maps to itself.</summary>
    private static char HighByte(byte b) => b switch
    {
        0x80 => '\u20AC', 0x82 => '\u201A', 0x83 => '\u0192', 0x84 => '\u201E', 0x85 => '\u2026',
        0x86 => '\u2020', 0x87 => '\u2021', 0x88 => '\u02C6', 0x89 => '\u2030', 0x8A => '\u0160',
        0x8B => '\u2039', 0x8C => '\u0152', 0x8E => '\u017D', 0x91 => '\u2018', 0x92 => '\u2019',
        0x93 => '\u201C', 0x94 => '\u201D', 0x95 => '\u2022', 0x96 => '\u2013', 0x97 => '\u2014',
        0x98 => '\u02DC', 0x99 => '\u2122', 0x9A => '\u0161', 0x9B => '\u203A', 0x9C => '\u0153',
        0x9E => '\u017E', 0x9F => '\u0178',
        _ => (char)b,
    };

    /// <summary>Decodes legacy bytes: valid UTF-8 stays UTF-8, anything else reads as Windows-1252/Latin-1. Never returns U+FFFD for Latin-1 text.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return "";
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Fallback(bytes); }
        catch (ArgumentException) { return Fallback(bytes); }
    }

    private static string Fallback(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return "";
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : HighByte(bytes[i]);
        return new string(chars);
    }

    /// <summary>True when <paramref name="value"/> holds the replacement character left by a UTF-8 read of Latin-1 bytes.</summary>
    public static bool ContainsReplacement(string? value) => value is not null && value.Contains('\uFFFD');

    /// <summary>Reads the int-prefixed legacy string at <paramref name="pos"/> (an int holding the text length plus one, a byte holding the length, then the text): the info lines, markers and beat text.</summary>
    public static bool TryReadIntByteString(byte[] data, ref int pos, out string value)
    {
        value = "";
        if (pos < 0 || pos + 5 > data.Length) return false;
        var total = BitConverter.ToInt32(data, pos);
        var length = data[pos + 4];
        if (total != length + 1 || length > 255 || pos + 5 + length > data.Length) return false;
        value = Decode(data.AsSpan(pos + 5, length));
        pos += 5 + length;
        return true;
    }

    /// <summary>Skips the int-prefixed legacy string at <paramref name="pos"/> (validated like <see cref="TryReadIntByteString"/>); false when it is not one.</summary>
    public static bool TrySkipIntByteString(byte[] data, ref int pos)
    {
        if (pos < 0 || pos + 5 > data.Length) return false;
        var total = BitConverter.ToInt32(data, pos);
        var length = data[pos + 4];
        if (total != length + 1 || length > 255 || pos + 5 + length > data.Length) return false;
        pos += 5 + length;
        return true;
    }

    /// <summary>Reads the int-length legacy string at <paramref name="pos"/> (an int length, then the text): the lyric lines.</summary>
    public static bool TryReadIntString(byte[] data, ref int pos, out string value)
    {
        value = "";
        if (pos < 0 || pos + 4 > data.Length) return false;
        var length = BitConverter.ToInt32(data, pos);
        if (length < 0 || length > 1 << 16 || pos + 4 + length > data.Length) return false;
        value = Decode(data.AsSpan(pos + 4, length));
        pos += 4 + length;
        return true;
    }

    /// <summary>Reads the fixed-width legacy string at <paramref name="pos"/> (a length byte, then the text padded with zeros to <paramref name="width"/>): the version line, track and chord names.</summary>
    public static bool TryReadByteStringPadded(byte[] data, ref int pos, int width, out string value)
    {
        value = "";
        if (pos < 0 || width <= 0 || width > 256 || pos + 1 + width > data.Length) return false;
        var length = data[pos];
        if (length > width) return false;
        value = Decode(data.AsSpan(pos + 1, length));
        pos += 1 + width;
        return true;
    }

    /// <summary>Repairs <paramref name="current"/> from <paramref name="raw"/> when the current text holds a replacement character and the raw bytes decode cleanly; else the current text.</summary>
    public static string OrRepaired(string? current, ReadOnlySpan<byte> raw)
    {
        if (!ContainsReplacement(current) || raw.IsEmpty) return current ?? "";
        var fixed_ = Decode(raw);
        return ContainsReplacement(fixed_) ? current! : fixed_;
    }
}
