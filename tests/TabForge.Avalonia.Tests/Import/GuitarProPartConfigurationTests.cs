using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the pre-parse check of a .gp's PartConfiguration (alphaTab loops over its counts without a bound): a damaged count is refused fast with
//   the controlled error, the PartConfiguration TabForge writes still opens.
// Does not own: the other zip limits (GuitarProPreParse) or the conversion.
public sealed class GuitarProPartConfigurationTests
{
    private static byte[] WithPartConfiguration(byte[] gp, byte[] part)
    {
        var ms = new MemoryStream();
        ms.Write(gp);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("Content/PartConfiguration")!;
            entry.Delete();
            using var stream = zip.CreateEntry("Content/PartConfiguration").Open();
            stream.Write(part);
        }
        return ms.ToArray();
    }

    [Fact]
    public void A_view_group_count_far_past_the_entry_is_refused_fast_with_the_controlled_error()
    {
        var gp = GuitarProExporter.ToBytes(DemoSongFactory.Create(), embedProject: false);
        // One score view, then 0x7FFFFFFF track view groups in a 10-byte entry: alphaTab read it for billions of steps (fuzz case seed 20260930 #62).
        var damaged = WithPartConfiguration(gp, new byte[] { 0, 0, 0, 1, 0, 0x7F, 0xFF, 0xFF, 0xFF, 3 });
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidDataException>(() => GuitarProImporter.ImportBytes(damaged, "fuzz.gp"));
        Assert.Contains("PartConfiguration", ex.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
    }

    [Fact]
    public void Counts_are_checked_as_alphaTab_reads_them()
    {
        GuitarProPreParse.ValidatePartConfiguration(new byte[] { 0, 0, 0, 1, 0, 0, 0, 0, 2, 3, 3 });   // what TabForge writes for two tracks
        GuitarProPreParse.ValidatePartConfiguration(new byte[] { 0, 0, 0, 1, 0, 0, 0 });              // a count cut off by the end reads as -1: no loop
        GuitarProPreParse.ValidatePartConfiguration(Array.Empty<byte>());
        Assert.Throws<InvalidDataException>(() => GuitarProPreParse.ValidatePartConfiguration(new byte[] { 0, 0x04, 0x60, 0x03, 0 }));   // views past the entry
        Assert.Throws<InvalidDataException>(() => GuitarProPreParse.ValidatePartConfiguration(new byte[] { 0, 0, 0, 1, 0, 0, 0, 0, 3, 3, 3 }));   // 3 groups, 2 bytes
    }

    [Fact]
    public void The_PartConfiguration_TabForge_writes_still_opens()
    {
        var song = GuitarProImporter.ImportBytes(GuitarProExporter.ToBytes(DemoSongFactory.Create(), embedProject: false), "demo.gp");
        Assert.NotEmpty(song.Tracks);
    }
}
