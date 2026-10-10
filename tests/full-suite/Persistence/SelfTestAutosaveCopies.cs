using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// Owns: which songs autosave copies: a clean song gets no copy, a song saved by hand loses its own copy at the next pass, and an unchanged song
//     is not written again.
// Does not own: the copy writer and naming (TestAutosaveRecovery), the failure notice (TestAutosaveRecovery) or the timer (AutosaveController).
// Tests: TestAutosaveCopies.
public static partial class SelfTest
{
    private static void TestAutosaveCopies()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-autosave-copies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        var realWriter = AutosaveRunner.WriteCopy;
        AutosaveRegistry.ResetForTests();
        try
        {
            CleanSongGetsNoCopy(Path.Combine(scratch, "clean"));
            SongSavedByHandLosesItsCopy(Path.Combine(scratch, "saved"));
            UnchangedSongIsNotWrittenAgain(Path.Combine(scratch, "unchanged"), realWriter);
        }
        finally
        {
            AutosaveRunner.WriteCopy = realWriter;
            AutosaveRegistry.ResetForTests();
            try { Directory.Delete(scratch, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void CleanSongGetsNoCopy(string folder)
    {
        Directory.CreateDirectory(folder);
        var dirty = DirtyDocument(2, 10, 21, "Dirty");
        var clean = DirtyDocument(2, 10, 22, "Clean");
        clean.Project.IsDirty = false;   // saved (or never changed)
        var runner = new AutosaveRunner();
        var result = RunAutosavePass(runner, new[] { dirty, clean }, folder);
        var files = Directory.GetFiles(folder, "autosave-*.tforge");
        Check("autosave copies: in one pass a dirty song gets its recovery copy and a clean song gets no file and no copy",
            result is { Dirty: 1, Written: 1, Failed: 0 } && AutosavePathOf(dirty) is { } copy && files.Length == 1 && files[0] == copy
            && AutosavePathOf(clean) is null,
            $"dirty {result?.Dirty}, written {result?.Written}, files {files.Length}");
        AutosaveRegistry.Retire(new[] { dirty, clean });
    }

    private static void SongSavedByHandLosesItsCopy(string folder)
    {
        Directory.CreateDirectory(folder);
        var doc = DirtyDocument(1, 6, 23, "Saved by hand");
        var runner = new AutosaveRunner();
        RunAutosavePass(runner, new[] { doc }, folder);
        var copy = AutosavePathOf(doc);
        var hadCopy = copy is not null && File.Exists(copy);
        doc.Project.IsDirty = false;   // saved by hand
        var result = RunAutosavePass(runner, new[] { doc }, folder);
        Check("autosave copies: a song saved by hand loses its own recovery copy at the next pass; nothing is written for it",
            hadCopy && result is { Dirty: 0, Written: 0, Failed: 0 } && copy is not null && !File.Exists(copy) && AutosavePathOf(doc) is null
            && Directory.GetFiles(folder, "autosave-*.tforge").Length == 0,
            $"had copy {hadCopy}, dirty {result?.Dirty}, written {result?.Written}");
        AutosaveRegistry.Retire(new[] { doc });
    }

    private static void UnchangedSongIsNotWrittenAgain(string folder, Func<SongProject, string, string?> realWriter)
    {
        Directory.CreateDirectory(folder);
        var doc = DirtyDocument(1, 6, 24, "Unchanged");
        var writes = 0;
        AutosaveRunner.WriteCopy = (project, path) => { writes++; return realWriter(project, path); };
        try
        {
            var runner = new AutosaveRunner();
            var first = RunAutosavePass(runner, new[] { doc }, folder);
            var afterFirst = writes;
            var second = RunAutosavePass(runner, new[] { doc }, folder);   // no edit in between
            var copy = AutosavePathOf(doc);
            Check("autosave copies: a song unchanged since its last copy is reported unchanged and its copy is not written again",
                first is { Written: 1 } && afterFirst == 1 && second is { Dirty: 1, Unchanged: 1, Written: 0 } && writes == 1
                && copy is not null && File.Exists(copy),
                $"writes {writes}, second unchanged {second?.Unchanged}");
        }
        finally
        {
            AutosaveRunner.WriteCopy = realWriter;
            AutosaveRegistry.Retire(new[] { doc });
        }
    }
}
