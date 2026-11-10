using System;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Features.TuxGuitar;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: TuxGuitar file (.tg) open and save: loading the example song, saving and reloading, cross-checking with the native format, malformed input, unicode titles.
// Does not own: the native format converter and the file dialogs.
public sealed class TuxGuitarTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-tux-{Guid.NewGuid():N}");

    public TuxGuitarTests()
    {
        Directory.CreateDirectory(_folder);
        AlphaTabWarmUp.EnsureReady();
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static string SamplePath()
    {
        foreach (var candidate in Candidates())
            if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException("The example TuxGuitar file was not found.");
    }

    private static string NativeSamplePath()
    {
        foreach (var candidate in NativeCandidates())
            if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException("The example native-format file was not found.");
    }

    private static System.Collections.Generic.IEnumerable<string> Candidates()
    {
        yield return @"C:\Users\COBHC\Downloads\TabForge\samples\Ashen Meridian.tg";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "samples", "Ashen Meridian.tg");
            yield return Path.Combine(dir.FullName, "..", "..", "samples", "Ashen Meridian.tg");
        }
    }

    private static System.Collections.Generic.IEnumerable<string> NativeCandidates()
    {
        yield return @"C:\Users\COBHC\Downloads\TabForge\samples\Ashen Meridian.gp";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "samples", "Ashen Meridian.gp");
            yield return Path.Combine(dir.FullName, "..", "..", "samples", "Ashen Meridian.gp");
        }
    }

    private static int CountNotes(TrackModel track)
    {
        var count = 0;
        foreach (var m in track.Measures)
            foreach (var c in m.Cells.Concat(m.Voice2Cells))
                count += c.Notes.Count;
        return count;
    }

    private static int CountNotesAll(SongProject song)
    {
        var count = 0;
        foreach (var t in song.Tracks)
            foreach (var m in t.Measures)
                foreach (var c in m.Cells.Concat(m.Voice2Cells))
                    count += c.Notes.Count;
        return count;
    }

    [Fact]
    public void LoadExampleSong()
    {
        var song = TuxGuitarImporter.Import(SamplePath());
        Assert.Equal("Ashen Meridian", song.Title);
        Assert.Equal("TabForge Demo", song.Artist);
        Assert.Equal("Examples", song.Album);
        Assert.Equal(10, song.Tracks.Count);
        Assert.Equal(150, song.Tempo);
        Assert.Equal("Rhythm Gtr L", song.Tracks[0].Name);
        Assert.Equal(new[] { 64, 59, 55, 50, 45, 40, 33 }, song.Tracks[0].StringTunings);
        Assert.Equal(144, song.Tracks[0].Measures.Count);
        Assert.Equal(5, song.Tracks.Single(t => t.Name == "Clean Gtr").Capo);
        Assert.Contains(song.Markers, m => m.Title == "Intro: Embers");
        Assert.Contains(song.Markers, m => m.Title == "Verse 1");
        Assert.True(CountNotesAll(song) > 3000);
    }

    [Fact]
    public void RoundTripThroughFile()
    {
        var original = TuxGuitarImporter.Import(SamplePath());
        var path = Path.Combine(_folder, "roundtrip.tg");
        TuxGuitarExporter.Save(original, path);
        var reloaded = TuxGuitarImporter.Import(path);
        Assert.Equal(original.Title, reloaded.Title);
        Assert.Equal(original.Artist, reloaded.Artist);
        Assert.Equal(original.Album, reloaded.Album);
        Assert.Equal(original.Tracks.Count, reloaded.Tracks.Count);
        Assert.Equal(original.Tempo, reloaded.Tempo);
        Assert.Equal(original.Markers.Count, reloaded.Markers.Count);
        for (var i = 0; i < original.Tracks.Count; i++)
        {
            var a = original.Tracks[i];
            var b = reloaded.Tracks[i];
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.MidiProgram, b.MidiProgram);
            Assert.Equal(a.Capo, b.Capo);
            Assert.Equal(a.StringTunings, b.StringTunings);
            Assert.Equal(a.Measures.Count, b.Measures.Count);
            Assert.Equal(CountNotes(a), CountNotes(b));
        }
    }

    [Fact]
    public void CrossCheckWithNativeFormatFile()
    {
        // The two example files hold the same song: title, track list, metronome, metre, tunings, capo marks and section list must agree.
        var tux = TuxGuitarImporter.Import(SamplePath());
        var nativePath = NativeSamplePath();
        var native = GuitarProImporter.Import(nativePath);
        Assert.Equal(native.Title, tux.Title);
        Assert.Equal(native.Tracks.Count, tux.Tracks.Count);
        Assert.Equal(native.Tempo, tux.Tempo);
        Assert.Equal(native.TimeSignatureNumerator, tux.TimeSignatureNumerator);
        Assert.Equal(native.TimeSignatureDenominator, tux.TimeSignatureDenominator);
        Assert.Equal(native.Markers.Count, tux.Markers.Count);
        for (var i = 0; i < tux.Tracks.Count; i++)
        {
            Assert.Equal(native.Tracks[i].Name, tux.Tracks[i].Name);
            // Pad and Piano sit on pitch-only tunings; one file may call them Guitar, the other Keys. Both are the same strings.
            if (native.Tracks[i].Kind == TrackKind.Drums || native.Tracks[i].Kind == TrackKind.Bass || tux.Tracks[i].Kind == TrackKind.Drums || tux.Tracks[i].Kind == TrackKind.Bass)
                Assert.Equal(native.Tracks[i].Kind, tux.Tracks[i].Kind);
            else
                Assert.True(native.Tracks[i].Kind is TrackKind.Guitar or TrackKind.Keys);
            Assert.Equal(native.Tracks[i].Measures.Count, tux.Tracks[i].Measures.Count);
            Assert.Equal(native.Tracks[i].StringTunings, tux.Tracks[i].StringTunings);
            Assert.Equal(native.Tracks[i].Capo, tux.Tracks[i].Capo);
        }
        // Pitched tracks hold nearly the same number of notes (the drum line of the example is rests in the TuxGuitar file, hits in the native file, so it is left out of the count).
        var tuxPitched = tux.Tracks.Where(t => t.Kind != TrackKind.Drums).Sum(CountNotes);
        var nativePitched = native.Tracks.Where(t => t.Kind != TrackKind.Drums).Sum(CountNotes);
        Assert.True(Math.Abs(tuxPitched - nativePitched) <= Math.Max(60, nativePitched / 10),
            $"pitched note counts differ: tux={tuxPitched}, native={nativePitched}");
        for (var i = 0; i < tux.Tracks.Count; i++)
        {
            if (tux.Tracks[i].Kind == TrackKind.Drums) continue;
            var a = CountNotes(tux.Tracks[i]);
            var b = CountNotes(native.Tracks[i]);
            Assert.True(Math.Abs(a - b) <= Math.Max(100, b / 10),
                $"track {tux.Tracks[i].Name}: tux={a}, native={b}");
        }
    }

    [Fact]
    public void CrossCheckThroughNativeProjectFile()
    {
        var tux = TuxGuitarImporter.Import(SamplePath());
        var path = Path.Combine(_folder, "native-project.tforge");
        ProjectService.Save(path, tux);
        var back = ProjectService.Load(path);
        Assert.Equal(tux.Title, back.Title);
        Assert.Equal(tux.Tracks.Count, back.Tracks.Count);
        Assert.Equal(tux.Tempo, back.Tempo);
        Assert.Equal(tux.Tracks[0].Measures.Count, back.Tracks[0].Measures.Count);
        Assert.Equal(CountNotesAll(tux), CountNotesAll(back));
    }

    [Fact]
    public void MalformedAndTruncatedFilesFailGracefully()
    {
        var raw = File.ReadAllBytes(SamplePath());
        var truncated = raw.Take(raw.Length / 2).ToArray();
        var truncatedPath = Path.Combine(_folder, "truncated.tg");
        File.WriteAllBytes(truncatedPath, truncated);
        Assert.ThrowsAny<Exception>(() => TuxGuitarImporter.Import(truncatedPath));

        var garbagePath = Path.Combine(_folder, "garbage.tg");
        File.WriteAllBytes(garbagePath, Encoding.UTF8.GetBytes("this is not a zip archive at all, plain text garbage"));
        Assert.ThrowsAny<Exception>(() => TuxGuitarImporter.Import(garbagePath));

        var emptyPath = Path.Combine(_folder, "empty.tg");
        File.WriteAllBytes(emptyPath, Array.Empty<byte>());
        Assert.ThrowsAny<Exception>(() => TuxGuitarImporter.Import(emptyPath));
    }

    [Fact]
    public void UnicodeTitleSurvivesRoundTrip()
    {
        var song = new SongProject { Title = "Café 🎸 — 日本語", Artist = "Über 🎵", Tempo = 120 };
        var track = new TrackModel { Name = "Lead 🎸", Kind = TrackKind.Guitar, StringTunings = new() { 64, 59, 55, 50, 45, 40 }, Measures = new System.Collections.Generic.List<MeasureModel> { new MeasureModel { Number = 1 } } };
        var cell = track.Measures[0].Cells[0];
        cell.DurationDenominator = 4;
        var note = new TabNote { StringIndex = 1, Fret = 3 };
        note.MidiValue = track.PitchOf(1, 3);
        cell.Notes.Add(note);
        song.Tracks.Add(track);
        var path = Path.Combine(_folder, "unicode.tg");
        TuxGuitarExporter.Save(song, path);
        var back = TuxGuitarImporter.Import(path);
        Assert.Equal("Café 🎸 — 日本語", back.Title);
        Assert.Equal("Über 🎵", back.Artist);
        Assert.Equal("Lead 🎸", back.Tracks[0].Name);
    }
}
