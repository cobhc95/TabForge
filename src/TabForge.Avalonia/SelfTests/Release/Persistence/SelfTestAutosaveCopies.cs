using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TabForge.Autosave;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;

namespace TabForge;

// Owns: which songs autosave copies: a clean song gets no copy, a song saved by hand loses its own copy at the next pass, and an unchanged song
//     is not written again.
// Does not own: the copy writer (RecoveryCopyWriter), the failure notice or the timer (the window's autosave controller).
// Tests: TestAutosaveCopies.
public static partial class SelfTest
{
    private static void TestAutosaveCopies()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-autosave-copies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            CleanSongGetsNoCopy(Path.Combine(scratch, "clean"));
            SongSavedByHandLosesItsCopy(Path.Combine(scratch, "saved"));
            UnchangedSongIsNotWrittenAgain(Path.Combine(scratch, "unchanged"));
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void CleanSongGetsNoCopy(string folder)
    {
        Directory.CreateDirectory(folder);
        var registry = new AutosaveRegistry();
        var dirty = DirtyDocument(2, 10, 21, "Dirty");
        var clean = DirtyDocument(2, 10, 22, "Clean");
        clean.Project.IsDirty = false;   // saved (or never changed)
        var runner = new AutosaveRunner(registry, RecoveryCopyWriter.Write);
        var result = RunAutosavePass(runner, new[] { dirty, clean }, folder);
        var files = Directory.GetFiles(folder, "autosave-*.tforge");
        Check("autosave copies: in one pass a dirty song gets its recovery copy and a clean song gets no file and no copy",
            result is { Dirty: 1, Written: 1, Failed: 0 } && AutosavePathOf(registry, dirty) is { } copy && files.Length == 1 && files[0] == copy
            && AutosavePathOf(registry, clean) is null,
            $"dirty {result?.Dirty}, written {result?.Written}, files {files.Length}");
        registry.Retire(new[] { dirty, clean });
    }

    private static void SongSavedByHandLosesItsCopy(string folder)
    {
        Directory.CreateDirectory(folder);
        var registry = new AutosaveRegistry();
        var doc = DirtyDocument(1, 6, 23, "Saved by hand");
        var runner = new AutosaveRunner(registry, RecoveryCopyWriter.Write);
        RunAutosavePass(runner, new[] { doc }, folder);
        var copy = AutosavePathOf(registry, doc);
        var hadCopy = copy is not null && File.Exists(copy);
        doc.Project.IsDirty = false;   // saved by hand
        var result = RunAutosavePass(runner, new[] { doc }, folder);
        Check("autosave copies: a song saved by hand loses its own recovery copy at the next pass; nothing is written for it",
            hadCopy && result is { Dirty: 0, Written: 0, Failed: 0 } && copy is not null && !File.Exists(copy) && AutosavePathOf(registry, doc) is null
            && Directory.GetFiles(folder, "autosave-*.tforge").Length == 0,
            $"had copy {hadCopy}, dirty {result?.Dirty}, written {result?.Written}");
        registry.Retire(new[] { doc });
    }

    private static void UnchangedSongIsNotWrittenAgain(string folder)
    {
        Directory.CreateDirectory(folder);
        var registry = new AutosaveRegistry();
        var doc = DirtyDocument(1, 6, 24, "Unchanged");
        var writes = 0;
        var runner = new AutosaveRunner(registry, (project, path) => { writes++; return RecoveryCopyWriter.Write(project, path); });
        try
        {
            var first = RunAutosavePass(runner, new[] { doc }, folder);
            var afterFirst = writes;
            var second = RunAutosavePass(runner, new[] { doc }, folder);   // no edit in between
            var copy = AutosavePathOf(registry, doc);
            Check("autosave copies: a song unchanged since its last copy is reported unchanged and its copy is not written again",
                first is { Written: 1 } && afterFirst == 1 && second is { Dirty: 1, Unchanged: 1, Written: 0 } && writes == 1
                && copy is not null && File.Exists(copy),
                $"writes {writes}, second unchanged {second?.Unchanged}");
        }
        finally { registry.Retire(new[] { doc }); }
    }

    private static AutosavePassResult? RunAutosavePass(AutosaveRunner runner, IReadOnlyList<SongDocument> songs, string folder) =>
        Task.Run(() => runner.RunPassAsync(songs, folder)).GetAwaiter().GetResult();

    private static SongDocument DirtyDocument(int tracks, int bars, int seed, string title)
    {
        var project = RichSong(tracks, bars, seed);
        project.Title = title;
        project.IsDirty = true;
        return new SongDocument(() => null) { Project = project };
    }

    private static string? AutosavePathOf(AutosaveRegistry registry, SongDocument song) => registry.For(song).Path;

    /// <summary>A generated multi-track song shaped like a real one (≈8 notes per bar, techniques, bends, voice 2, mix and tempo points).</summary>
    private static SongProject RichSong(int tracks, int bars, int seed)
    {
        var rng = new Random(seed);
        var song = new SongProject { Title = "Undo bench", Tempo = 140 };
        for (var t = 0; t < tracks; t++)
        {
            var track = new TrackModel { Name = $"Track {t + 1}", Measures = TemplateFactory.Measures(bars), Volume = 90 + t };
            for (var b = 0; b < bars; b++)
            {
                var measure = track.Measures[b];
                for (var c = 0; c < 16; c += 2)
                {
                    var cell = measure.Cells[c];
                    cell.DurationDenominator = 16;
                    var fret = rng.Next(0, 22);
                    var note = new TabNote { StringIndex = rng.Next(6), Fret = fret, MidiValue = 40 + fret, Velocity = 80 + rng.Next(40) };
                    if (rng.Next(4) == 0) note.Techniques.Add("PalmMute");
                    if (rng.Next(9) == 0) note.Techniques.Add("Vibrato");
                    if (rng.Next(12) == 0)
                    {
                        note.Techniques.Add("Bend");
                        note.BendPoints.Add(new BendPointModel { Offset = 0, Value = 0 });
                        note.BendPoints.Add(new BendPointModel { Offset = 30, Value = 4 });
                    }
                    cell.Notes.Add(note);
                }
                if (b % 9 == 4) measure.CellsForVoice(1, create: true)[0].Notes.Add(new TabNote { StringIndex = 5, Fret = 3, MidiValue = 43 });
                if (b % 50 == 0) measure.Cells[0].Mix = new MixChange { Volume = 12, Pan = -2 };
                if (b % 40 == 3) measure.MidBarTempos = new List<TempoPoint> { new(8, 150) };
                if (b % 16 == 0) measure.Cells[0].Lyrics = $"line {b}";
            }
            song.Tracks.Add(track);
        }
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        song.Markers.Add(new MarkerModel { MeasureIndex = Math.Min(8, bars - 1), Title = "Verse" });
        return song;
    }
}
