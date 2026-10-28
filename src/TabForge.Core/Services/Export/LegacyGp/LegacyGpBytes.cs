using System.IO;
using System.Text;

namespace TabForge.Services;

// Owns: the little-endian primitives and the string layouts of the Guitar Pro 3-5 binary format.
// Does not own: what is written with them (LegacyGpWriter).
// Tests: TestLegacyGpExport.
/// <summary>A growing buffer with the field types of the 3-5 binary format: bytes, 32-bit ints, shorts, doubles and its length-prefixed strings.</summary>
internal sealed class LegacyGpBytes
{
    private readonly MemoryStream _stream = new();

    public byte[] ToArray() => _stream.ToArray();

    public void Byte(int value) => _stream.WriteByte(unchecked((byte)value));
    public void Bytes(byte[] data) => _stream.Write(data, 0, data.Length);
    public void Zeros(int count) { for (var i = 0; i < count; i++) _stream.WriteByte(0); }
    public void Bool(bool value) => Byte(value ? 1 : 0);
    public void Short(int value) { Byte(value); Byte(value >> 8); }
    public void Int(int value) { Byte(value); Byte(value >> 8); Byte(value >> 16); Byte(value >> 24); }
    public void Double(double value) => Bytes(BitConverter.GetBytes(value));

    private static byte[] Encode(string? text, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? "");
        if (bytes.Length <= maxBytes) return bytes;
        var cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;   // never cut inside a multi-byte character
        return bytes[..cut];
    }

    /// <summary>An int that holds the text length plus one, a byte that holds the length, then the text (info lines, markers, beat text).</summary>
    public void IntByteString(string? text)
    {
        var data = Encode(text, 255);
        Int(data.Length + 1); Byte(data.Length); Bytes(data);
    }

    /// <summary>An int length, then the text (the lyric lines).</summary>
    public void IntString(string? text)
    {
        var data = Encode(text, 1 << 16);
        Int(data.Length); Bytes(data);
    }

    /// <summary>A length byte, then the text padded with zeros to a fixed width (the version line, track and chord names).</summary>
    public void ByteStringPadded(string? text, int width)
    {
        var data = Encode(text, width);
        Byte(data.Length); Bytes(data); Zeros(width - data.Length);
    }
}
