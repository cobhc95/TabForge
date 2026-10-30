using System;
using System.Text;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: decoding the single-byte text of Guitar Pro 3-5 files: valid UTF-8 stays UTF-8, anything else reads as
//   Windows-1252/Latin-1, and an invalid sequence never yields U+FFFD.
// Does not own: the file walk (GpLegacyTextRepair) or the song model.
// Tests: GpLegacyText.
public class GpLegacyTextTests
{
    [Fact]
    public void Latin1BytesDecodeToTheRightChars()
    {
        Assert.Equal("Björn", GpLegacyText.Decode(new byte[] { 0x42, 0x6A, 0xF6, 0x72, 0x6E }));
    }

    [Fact]
    public void ValidUtf8StillDecodesRight()
    {
        Assert.Equal("Björn", GpLegacyText.Decode(Encoding.UTF8.GetBytes("Björn")));
        Assert.Equal("Nedergård", GpLegacyText.Decode(Encoding.UTF8.GetBytes("Nedergård")));
    }

    [Fact]
    public void InvalidSequenceNeverYieldsTheReplacementCharacter()
    {
        var text = GpLegacyText.Decode(new byte[] { 0x4E, 0x65, 0x64, 0x65, 0x72, 0x67, 0xE5, 0x72, 0x64 });
        Assert.Equal("Nedergård", text);
        Assert.DoesNotContain("\uFFFD", text);
        Assert.DoesNotContain("\uFFFD", GpLegacyText.Decode(new byte[] { 0xF6 }));
        Assert.DoesNotContain("\uFFFD", GpLegacyText.Decode(new byte[] { 0x80, 0x81, 0x9D, 0xFF }));
    }

    [Fact]
    public void Windows1252PunctuationDecodesToItself()
    {
        Assert.Equal("a\u201Cb\u201D", GpLegacyText.Decode(new byte[] { 0x61, 0x93, 0x62, 0x94 }));
        Assert.Equal("\u20AC", GpLegacyText.Decode(new byte[] { 0x80 }));
    }

    [Fact]
    public void EmptyInputDecodesToEmpty()
    {
        Assert.Equal("", GpLegacyText.Decode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Gp5TrackNameFieldWithLatin1ByteDecodes()
    {
        var field = TrackNameField("Nederg" + (char)0xE5 + "rd");
        var pos = 0;
        Assert.True(GpLegacyText.TryReadByteStringPadded(field, ref pos, 40, out var name));
        Assert.Equal("Nedergård", name);
        Assert.Equal(41, pos);
    }

    [Fact]
    public void InfoStringEnvelopeDecodes()
    {
        var field = InfoField("Björn");
        var pos = 0;
        Assert.True(GpLegacyText.TryReadIntByteString(field, ref pos, out var text));
        Assert.Equal("Björn", text);
        Assert.False(GpLegacyText.TryReadIntByteString(field, ref pos, out _));
    }

    [Fact]
    public void RepairReplacesOnlyTheReplacementCharacter()
    {
        var raw = new byte[] { 0x42, 0x6A, 0xF6, 0x72, 0x6E };
        Assert.Equal("Björn", GpLegacyText.OrRepaired("Bj\uFFFDrn", raw));
        Assert.Equal("Clean", GpLegacyText.OrRepaired("Clean", raw));
        Assert.Equal("", GpLegacyText.OrRepaired(null, raw));
    }

    /// <summary>A GP5 track-name field (a length byte, then the name padded with zeros to 40) holding Latin-1 bytes, as the file stores it.</summary>
    private static byte[] TrackNameField(string name)
    {
        var bytes = new byte[1 + 40];
        var latin = new byte[name.Length];
        for (var i = 0; i < name.Length; i++) latin[i] = (byte)name[i];
        bytes[0] = (byte)latin.Length;
        latin.CopyTo(bytes, 1);
        return bytes;
    }

    /// <summary>A GP info string (an int holding the length plus one, a byte holding the length, then the bytes).</summary>
    private static byte[] InfoField(string name)
    {
        var latin = new byte[name.Length];
        for (var i = 0; i < name.Length; i++) latin[i] = (byte)name[i];
        var field = new byte[4 + 1 + latin.Length];
        BitConverter.GetBytes(latin.Length + 1).CopyTo(field, 0);
        field[4] = (byte)latin.Length;
        latin.CopyTo(field, 5);
        return field;
    }
}
