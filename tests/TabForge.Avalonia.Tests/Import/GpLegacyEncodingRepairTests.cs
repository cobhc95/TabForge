using System;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
namespace TabForge.Avalonia.Tests;

// Owns: opening a Guitar Pro 3-5 file saved in the Latin-1 code page: the track names and the song fields read back with
//   their real characters (Bjorn Pettersson, Nedergard), never the replacement character.
// Does not own: the byte decoding (GpLegacyTextTests) or the export (TestLegacyGpExport).
// Tests: GuitarProImporter (with GpLegacyTextRepair).
// The files are built in the test: a song is exported to .gp3/.gp4/.gp5 bytes, the UTF-8 strings are transcoded to
// Latin-1 the way the older programs write them, and the import reads them back.
public class GpLegacyEncodingRepairTests
{
    public GpLegacyEncodingRepairTests() => AlphaTabWarmUp.EnsureReady();

    [Theory]
    [InlineData(LegacyGpFormat.Gp5, ".gp5")]
    [InlineData(LegacyGpFormat.Gp4, ".gp4")]
    [InlineData(LegacyGpFormat.Gp3, ".gp3")]
    public void Latin1TrackNameAndTitleImportBackWithTheirRealCharacters(LegacyGpFormat format, string extension)
    {
        var song = DemoSongFactory.Create();
        song.Title = "Björns låt Yq";
        song.Tracks[0].Name = "Nedergård Xq";

        var file = LegacyGpExport.ToBytes(song, format);
        file = Latin1TrackName(file, song.Tracks[0].Name);
        file = Latin1InfoString(file, song.Title);

        var imported = GuitarProImporter.ImportBytes(file, "latin1" + extension, new ImportContext());

        Assert.Equal("Nedergård Xq", imported.Tracks[0].Name);
        Assert.Equal("Björns låt Yq", imported.Title);
        Assert.DoesNotContain("\uFFFD", imported.Tracks[0].Name);
        Assert.DoesNotContain("\uFFFD", imported.Title);
    }

    [Theory]
    [InlineData(LegacyGpFormat.Gp5, ".gp5")]
    [InlineData(LegacyGpFormat.Gp4, ".gp4")]
    [InlineData(LegacyGpFormat.Gp3, ".gp3")]
    public void Utf8FileStillImportsUnchanged(LegacyGpFormat format, string extension)
    {
        var song = DemoSongFactory.Create();
        song.Title = "Björns låt Yq";
        song.Tracks[0].Name = "Nedergård Xq";

        var imported = GuitarProImporter.ImportBytes(LegacyGpExport.ToBytes(song, format), "utf8" + extension, new ImportContext());

        Assert.Equal("Nedergård Xq", imported.Tracks[0].Name);
        Assert.Equal("Björns låt Yq", imported.Title);
    }

    private const string TrooperPath = "C:/tfrel-tmp/trooper/trooper.gp5";

    [Fact]
    public void TrooperGp5TrackNamesHoldNoReplacementCharacter()
    {
        if (!File.Exists(TrooperPath)) return;
        var imported = GuitarProImporter.Import(TrooperPath, new ImportContext());
        Assert.NotEmpty(imported.Tracks);
        foreach (var track in imported.Tracks)
            Assert.DoesNotContain("\uFFFD", track.Name ?? "");
    }

    /// <summary>Transcodes the UTF-8 bytes of a track name to Latin-1 in place (the length byte and the zero padding follow).</summary>
    private static byte[] Latin1TrackName(byte[] file, string name) => Latin1Field(file, name, trackName: true);

    /// <summary>Transcodes the UTF-8 bytes of an info string to Latin-1 (the length prefixes follow, the file shrinks).</summary>
    private static byte[] Latin1InfoString(byte[] file, string text) => Latin1Field(file, text, trackName: false);

    private static byte[] Latin1Field(byte[] file, string text, bool trackName)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var latin = text.Select(c => (byte)c).ToArray();
        var at = IndexOf(file, utf8);
        Assert.True(at > 5, "The exported file should hold the UTF-8 bytes of '" + text + "'.");
        Assert.Equal(-1, IndexOf(file.Skip(at + 1).ToArray(), utf8));
        if (trackName)
        {
            Assert.Equal(utf8.Length, file[at - 1]);
            var width = 40;
            var result = (byte[])file.Clone();
            result[at - 1] = (byte)latin.Length;
            latin.CopyTo(result, at);
            Array.Clear(result, at + latin.Length, width - latin.Length);
            return result;
        }
        Assert.Equal(utf8.Length, file[at - 1]);
        Assert.Equal(utf8.Length + 1, BitConverter.ToInt32(file, at - 5));
        var shrunk = new byte[file.Length - (utf8.Length - latin.Length)];
        Array.Copy(file, 0, shrunk, 0, at - 5);
        BitConverter.GetBytes(latin.Length + 1).CopyTo(shrunk, at - 5);
        shrunk[at - 1] = (byte)latin.Length;
        latin.CopyTo(shrunk, at);
        Array.Copy(file, at + utf8.Length, shrunk, at + latin.Length, file.Length - at - utf8.Length);
        return shrunk;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { found = false; break; }
            if (found) return i;
        }
        return -1;
    }
}
