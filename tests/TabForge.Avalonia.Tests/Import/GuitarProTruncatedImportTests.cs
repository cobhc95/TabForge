using System;
using System.IO;
using System.Linq;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: opening a Guitar Pro 3-5 file that ends inside its bars (cut-off download): the bars before the cut are kept and the open notice says so.
// Does not own: the other damaged-file checks (ImportPlausibility) or the worker process.
// Env: TABFORGE_SAMPLES_DIR = the sample folder; without it the file tests return at once (the files are not in the repo).
public class GuitarProTruncatedImportTests
{
    public GuitarProTruncatedImportTests() => AlphaTabWarmUp.EnsureReady();

    private static string? Sample(string relative)
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        var path = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, relative);
        return path is not null && File.Exists(path) ? path : null;
    }

    // Kept bars are the bars every track holds completely; declared is TuxGuitar's bar count for the same file.
    [Theory]
    [InlineData("metallica/metallica/death_magnetic.gp5", 702, 1936)]
    [InlineData("metallica/metallica/for_whom_the_bell_tolls_3.gp3", 84, 169)]
    [InlineData("metallica/metallica/the_strugle_within_2.gp3", 50, 239)]
    [InlineData("metallica/metallica/to_live_is_to_die_6.gp4", 215, 258)]
    public void A_cut_off_sample_opens_with_the_bars_before_the_cut(string relative, int kept, int declared)
    {
        if (Sample(relative) is not { } path) return;
        var context = new ImportContext();
        var song = GuitarProImporter.Import(path, context);
        Assert.All(song.Tracks, t => Assert.Equal(kept, t.Measures.Count));
        Assert.Contains($"of {declared:N0}", context.DamageNotice);
        Assert.Contains("cut off", context.DamageNotice);
    }

    [Fact]
    public void A_complete_file_cut_in_the_middle_keeps_its_first_bars()
    {
        if (Sample("children_of_bodom/aces_high.gp4") is not { } path) return;
        var whole = File.ReadAllBytes(path);
        var full = GuitarProImporter.ImportBytes(whole, path, new ImportContext());
        var context = new ImportContext();
        var cut = GuitarProImporter.ImportBytes(whole[..(whole.Length * 6 / 10)], path, context);
        var bars = cut.Tracks.Max(t => t.Measures.Count);
        Assert.InRange(bars, 1, full.Tracks.Max(t => t.Measures.Count) - 1);
        Assert.Contains("cut off", context.DamageNotice);
        Assert.Equal(full.Tracks[0].Measures[0].Cells.Sum(c => c.Notes.Count), cut.Tracks[0].Measures[0].Cells.Sum(c => c.Notes.Count));
    }

    [Fact]
    public void A_file_cut_off_inside_its_header_is_still_refused()
    {
        var header = new byte[40];
        header[0] = 0x18;
        System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v4.00").CopyTo(header, 1);
        Assert.Throws<InvalidDataException>(() => GuitarProImporter.ImportBytes(header, "cut.gp4", new ImportContext()));
    }
}
