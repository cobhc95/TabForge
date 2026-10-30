using System;
using System.Collections.Generic;
using System.IO;
using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the bar-codec refusal of crafted bytes: a truncated varint, double, string or list is an InvalidDataException,
// never an out-of-range throw, a hang or a huge pre-allocation.
// Does not own: the bar field order (BarCodec) or the writers (BarWriter).
public sealed class BarReaderBoundsTests
{
    private static byte[] EncodeVarU(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80) { bytes.Add((byte)(value | 0x80)); value >>= 7; }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static ulong ReadVarU(byte[] data) { var r = new BarReader(data); return r.VarU(); }

    private static double ReadDbl(byte[] data) { var r = new BarReader(data); return r.Dbl(); }

    private static string? ReadStr(byte[] data) { var r = new BarReader(data); return r.Str(); }

    private static int ReadCount(byte[] data) { var r = new BarReader(data); return r.Count(); }

    [Fact]
    public void EmptyInputIsRefused() =>
        Assert.Throws<InvalidDataException>(() => ReadVarU(Array.Empty<byte>()));

    [Fact]
    public void ATruncatedVarintIsRefused() =>
        Assert.Throws<InvalidDataException>(() => ReadVarU(new byte[] { 0x80 }));

    [Fact]
    public void AnOverlongVarintIsRefused()
    {
        var eleven = new byte[11];
        Array.Fill(eleven, (byte)0x80);
        Assert.Throws<InvalidDataException>(() => ReadVarU(eleven));
    }

    [Fact]
    public void ACountThatDoesNotFitAnIntIsRefused() =>
        Assert.Throws<InvalidDataException>(() => ReadCount(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }));

    [Fact]
    public void ATruncatedDoubleIsRefused() =>
        Assert.Throws<InvalidDataException>(() => ReadDbl(new byte[] { 0x01, 0x00, 0x00, 0x00 }));

    [Fact]
    public void AnOverClaimedStringIsRefused()
    {
        var data = EncodeVarU(500_001);   // claims 500,000 chars, holds none
        Assert.Throws<InvalidDataException>(() => ReadStr(data));
    }

    [Fact]
    public void ATruncatedBarIsRefused() =>
        Assert.Throws<InvalidDataException>(() => BarCodec.Read(new byte[] { 0x01 }));

    [Fact]
    public void AnOverClaimedCellListIsRefusedWithoutPreallocatingIt()
    {
        // flags=1 then the twelve header fields as zeros: reaches the cell list with only the count left.
        var data = new List<byte> { 0x01 };
        for (var i = 0; i < 12; i++) data.Add(0x00);
        data.AddRange(EncodeVarU(1_000_001));   // claims 1,000,000 cells, holds none
        Assert.Throws<InvalidDataException>(() => BarCodec.Read(data.ToArray()));
    }

    [Fact]
    public void ValidBarsStillRoundTrip()
    {
        var writer = new BarWriter();
        BarCodec.Write(writer, null);
        Assert.Null(BarCodec.Read(writer.Written));

        writer = new BarWriter();
        var measure = new MeasureModel { SectionName = "Verse" };
        BarCodec.Write(writer, measure);
        var back = BarCodec.Read(writer.Written);
        Assert.NotNull(back);
        Assert.Equal("Verse", back!.SectionName);
        Assert.Equal(measure.Cells.Count, back.Cells.Count);
    }
}
