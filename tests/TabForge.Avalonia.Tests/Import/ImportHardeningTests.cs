using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the resource limits of the MusicXML container (.mxl) and the MIDI reader: crafted zips and MIDI files are refused fast with the standard message.
// Does not own: the Guitar Pro zip checks (GuitarProPreParse) or the conversions.
public sealed class ImportHardeningTests
{
    private const string Standard = "This score file is invalid, truncated, or unsupported.";
    private const string Score = "<?xml version=\"1.0\"?><score-partwise version=\"3.1\"><part-list/></score-partwise>";

    private static byte[] Zip(Action<ZipArchive> fill)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) fill(zip);
        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string name, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var s = zip.CreateEntry(name, level).Open();
        s.Write(data);
    }

    private static void Assert_Refused(byte[] mxl)
    {
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidDataException>(() => MusicXmlInput.Prepare(mxl));
        Assert.Equal(Standard, ex.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "refused slowly: " + watch.Elapsed);
    }

    [Theory]
    [InlineData("../score.xml")]
    [InlineData("a/../../score.xml")]
    [InlineData("/score.xml")]
    [InlineData("C:\\score.xml")]
    public void MxlRootFileWithAnEscapingNameIsRefused(string fullPath)
    {
        var container = "<container><rootfiles><rootfile full-path=\"" + fullPath + "\"/></rootfiles></container>";
        Assert_Refused(Zip(zip =>
        {
            Add(zip, "META-INF/container.xml", Encoding.UTF8.GetBytes(container));
            Add(zip, "score.xml", Encoding.UTF8.GetBytes(Score));
        }));
    }

    [Theory]
    [InlineData("score.xml", true)]
    [InlineData("META-INF/container.xml", true)]
    [InlineData("a\\b.xml", true)]
    [InlineData("..\\x", false)]
    [InlineData("a/../x", false)]
    [InlineData("/x", false)]
    [InlineData("C:x", false)]
    [InlineData("", false)]
    public void ArchiveEntryNamesMustBeRelativeAndStayInside(string name, bool safe) =>
        Assert.Equal(safe, FilePathPolicy.IsSafeArchiveEntryName(name));

    [Fact]
    public void MxlWithSpoofedEntryLengthIsRefused()
    {
        var bomb = new byte[200 * 1024 * 1024];
        var file = Zip(z => Add(z, "score.xml", bomb));
        // The directory and local header claim 100 bytes; the entry really inflates to 200 MiB.
        for (var i = 0; i + 30 < file.Length; i++)
        {
            var sig = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(i, 4));
            if (sig == 0x04034b50) BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(i + 22, 4), 100);
            else if (sig == 0x02014b50) BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(i + 24, 4), 100);
        }
        Assert_Refused(file);
    }

    [Fact]
    public void MxlWithTooManyEntriesIsRefused()
    {
        Assert_Refused(Zip(z =>
        {
            Add(z, "score.xml", Encoding.UTF8.GetBytes(Score));
            for (var i = 0; i < GuitarProPreParse.MaxZipEntries + 10; i++) z.CreateEntry($"x{i}.txt");
        }));
    }

    [Fact]
    public void MxlCompressionBombIsRefused()
    {
        var head = Encoding.UTF8.GetBytes(Score[..^"</score-partwise>".Length]);
        var padding = Enumerable.Repeat((byte)' ', 40 * 1024 * 1024).ToArray();
        Assert_Refused(Zip(z => Add(z, "score.xml", head.Concat(padding).ToArray())));
    }

    [Fact]
    public void MxlThatIsNotAPkSignatureZipIsNotTreatedAsOne()
    {
        // "PK" followed by anything else is not a container: it goes to the XML check and is refused there.
        Assert_Refused(Encoding.ASCII.GetBytes("PK-not a zip"));
    }

    [Fact]
    public void ValidMxlStillOpens()
    {
        var file = Zip(z =>
        {
            Add(z, "META-INF/container.xml", Encoding.UTF8.GetBytes("<container><rootfiles><rootfile full-path=\"s.xml\"/></rootfiles></container>"));
            Add(z, "s.xml", Encoding.UTF8.GetBytes(Score));
        });
        Assert.Equal(Score, Encoding.UTF8.GetString(MusicXmlInput.Prepare(file)));
    }

    private static byte[] Mid(byte[] track, int tracks = 1) =>
        Encoding.ASCII.GetBytes("MThd").Concat(new byte[] { 0, 0, 0, 6, 0, 1, 0, (byte)tracks, 1, 0xE0 })
            .Concat(Encoding.ASCII.GetBytes("MTrk")).Concat(BitConverter.GetBytes(track.Length).Reverse()).Concat(track).ToArray();

    private static byte[] Repeat(byte[] unit, int count, byte[]? tail = null)
    {
        var list = new List<byte>(unit.Length * count + 4);
        for (var i = 0; i < count; i++) list.AddRange(unit);
        list.AddRange(tail ?? new byte[] { 0, 0xFF, 0x2F, 0 });
        return list.ToArray();
    }

    private static void AssertMidiRefused(byte[] file, ImportContext? context = null)
    {
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidDataException>(() => SmfReader.Read(file, context));
        Assert.Equal(Standard, ex.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "refused slowly: " + watch.Elapsed);
    }

    [Fact]
    public void TenThousandTempoEventsAreRefused() =>
        AssertMidiRefused(Mid(Repeat(new byte[] { 1, 0xFF, 0x51, 3, 7, 0xA1, 0x20 }, 10_000)));

    [Fact]
    public void TenThousandTimeSignaturesAreRefused() =>
        AssertMidiRefused(Mid(Repeat(new byte[] { 1, 0xFF, 0x58, 4, 4, 2, 24, 8 }, 10_000)));

    [Fact]
    public void ImpossibleTimeSignatureIsRefusedWhileReading() =>
        AssertMidiRefused(Mid(new byte[] { 0, 0xFF, 0x58, 4, 255, 2, 24, 8, 0, 0xFF, 0x2F, 0 }));

    [Fact]
    public void HugeTrackHonoursTheMemoryBudgetInsideTheEventLoop()
    {
        var file = Mid(Repeat(new byte[] { 0, 0x90, 60, 90 }, 400_000));   // note-ons that never end: each one stays queued
        var guard = new ImportGuard(CancellationToken.None, memoryBudgetBytes: 1024 * 1024);
        var ex = Assert.Throws<InvalidDataException>(() => SmfReader.Read(file, new ImportContext(guard)));
        Assert.Contains("too much memory", ex.Message);
    }

    [Fact]
    public void LongTrackNameIsTruncatedToTheTitleLimit()
    {
        var name = new string('A', 1000);
        var body = new List<byte> { 0, 0xFF, 0x03, 0x87, 0x68 };   // delta 0, track name, length 1000 as a variable-length quantity
        body.AddRange(Encoding.ASCII.GetBytes(name));
        body.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
        var file = SmfReader.Read(Mid(body.ToArray()));
        Assert.Equal(InputLimits.MaxTitleLength, file.Tracks[0].Name.Length);
    }
}
