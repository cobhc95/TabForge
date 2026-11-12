using System;
using System.Collections.Generic;
using System.IO;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Owns: the pins for the free-sibling-name finders (DocumentSaveFlow.NumberedSibling, GpExportPreflight.UniqueSibling) and for what an undo and a redo restore, and in which order.
// Does not own: the save flow bytes (TestPairSaveEveryStage), the undo stack rules (UndoRulesTests) or the 1000-copy ceiling (TestNumberedSiblingCeiling).
// Tests: TestSiblingNameFinder, TestUndoRedoRestoreOrder.
public static partial class SelfTest
{
    private static void TestSiblingNameFinder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-sibpin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            Eq("numbered: a free path is returned as it is", path, DocumentSaveFlow.NumberedSibling(path));
            File.WriteAllText(path, "");
            Eq("numbered: a taken path gets (2)", "song (2).tforge", Path.GetFileName(DocumentSaveFlow.NumberedSibling(path)));
            File.WriteAllText(Path.Combine(folder, "SONG (2).TFORGE"), "");
            Eq("numbered: a taken name differing only in case counts as taken", "song (3).tforge", Path.GetFileName(DocumentSaveFlow.NumberedSibling(path)));
            Directory.CreateDirectory(Path.Combine(folder, "song (3).tforge"));
            Eq("numbered: a folder of that name counts as taken", "song (4).tforge", Path.GetFileName(DocumentSaveFlow.NumberedSibling(path)));
            Check("numbered: the ceiling is the caller's", Throws<IOException>(() => DocumentSaveFlow.NumberedSibling(path, 3)));
            Eq("numbered: a ceiling just above the number found still returns it", "song (4).tforge", Path.GetFileName(DocumentSaveFlow.NumberedSibling(path, 4)));

            var native = Path.Combine(folder, "set.tforge");
            Eq("unique: the first name is the label", "set (full copy).tforge", Path.GetFileName(GpExportPreflight.UniqueSibling(native, "full copy")));
            File.WriteAllText(Path.Combine(folder, "set (full copy).tforge"), "");
            Eq("unique: the second try is label 2", "set (full copy 2).tforge", Path.GetFileName(GpExportPreflight.UniqueSibling(native, "full copy")));
            File.WriteAllText(Path.Combine(folder, "set (full copy 2).tforge"), "");
            Eq("unique: then label 3", "set (full copy 3).tforge", Path.GetFileName(GpExportPreflight.UniqueSibling(native, "full copy")));
            Eq("unique: another label starts again", "set (compatible).tforge", Path.GetFileName(GpExportPreflight.UniqueSibling(native, "compatible")));
            var missing = Path.Combine(folder, "no-such-folder", "x.gp");
            Eq("unique: a folder that does not exist is no obstacle", Path.Combine(folder, "no-such-folder", "x (compatible).gp"), GpExportPreflight.UniqueSibling(missing, "compatible"));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void TestUndoRedoRestoreOrder()
    {
        var song = TemplateFactory.Blank();
        song.Tempo = 120;
        var doc = new SongDocument(() => null) { Project = song };
        doc.MarkClean();
        var restored = new List<string>();
        doc.Restored += () => restored.Add($"tempo={doc.Project.Tempo} shift={doc.TuningShift[0]} skips={doc.SkipRanges.Count} dirty={doc.Project.IsDirty}");

        Check("nothing to undo returns null", DocumentEdits.Undo(doc) is null);
        Check("nothing to redo returns null", DocumentEdits.Redo(doc) is null);
        Eq("no restore is announced when nothing was undone or redone", 0, restored.Count);

        DocumentEdits.Run(doc, p => { p.Tempo = 130; doc.TuningShift[0] = 2; doc.SkipRanges.Add((1, 2)); return true; }, label: "Pin edit");
        Check("the edit marks the song dirty", doc.Project.IsDirty);

        Check("undo returns the restored state", DocumentEdits.Undo(doc) is not null);
        Eq("undo: the song comes back", 120, doc.Project.Tempo);
        Eq("undo: the tuning shift comes back", 0, doc.TuningShift[0]);
        Eq("undo: the skipped areas come back", 0, doc.SkipRanges.Count);
        Check("undo back to the saved state clears the dirty mark", !doc.Project.IsDirty);
        Eq("undo: one restore announced, after everything is restored", "tempo=120 shift=0 skips=0 dirty=False", string.Join("|", restored));

        Check("redo returns the restored state", DocumentEdits.Redo(doc) is not null);
        Eq("redo: the song is edited again", 130, doc.Project.Tempo);
        Eq("redo: the tuning shift is back", 2, doc.TuningShift[0]);
        Eq("redo: the skipped areas are back", 1, doc.SkipRanges.Count);
        Check("redo leaves the song dirty", doc.Project.IsDirty);
        Eq("redo: a second restore announced after everything is restored", "tempo=120 shift=0 skips=0 dirty=False|tempo=130 shift=2 skips=1 dirty=True", string.Join("|", restored));

        DocumentEdits.Undo(doc);
        DocumentEdits.Redo(doc);
        Eq("a second undo and redo give the same values", "tempo=130 shift=2 skips=1 dirty=True", restored[^1]);
        Eq("every undo and redo announced exactly once", 4, restored.Count);
        Check("redo has nothing left", DocumentEdits.Redo(doc) is null && restored.Count == 4);
    }
}
