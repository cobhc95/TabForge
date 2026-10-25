using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Plugins;

namespace TabForge;

// Owns: guards that PresetFolderStore never replaces a preset unasked, the numbered-sibling ceiling and the atomic save claim.
// Does not own: the preset UIs (TestPresetManagePanel) or the save flow bytes (TestSaveFlowCharacterization).
// Tests: TestPresetFolderStoreNoOverwrite, TestNumberedSiblingCeiling, TestSaveClaimAtomic.
public static partial class SelfTest
{
    private static void TestPresetFolderStoreNoOverwrite()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-presetnoover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var store = new PresetFolderStore(folder, ".tfpreset");
            File.WriteAllText(store.PathOf("A"), "a");
            File.WriteAllText(store.PathOf("B"), "b");
            Check("rename onto an existing preset throws", Throws<IOException>(() => store.Rename("A", "B")));
            Check("duplicate onto an existing preset throws", Throws<IOException>(() => store.Duplicate("A", "B")));
            Check("the other preset keeps its content", File.ReadAllText(store.PathOf("B")) == "b" && File.Exists(store.PathOf("A")));
            store.Rename("A", "a");
            Check("a case-only rename is allowed", store.Names().Contains("a"));
            store.Rename("a", "B", replace: true);
            Check("a confirmed replace replaces", File.ReadAllText(store.PathOf("B")) == "a" && store.Names().Count == 1);
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void TestNumberedSiblingCeiling()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-sibling-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            File.WriteAllText(path, "");
            Check("the first free number is 2", Path.GetFileName(DocumentSaveFlow.NumberedSibling(path)) == "song (2).tforge");
            for (var i = 2; i <= 1000; i++) File.WriteAllText(Path.Combine(folder, $"song ({i}).tforge"), "");
            Check("past 1000 numbered copies it throws", Throws<IOException>(() => DocumentSaveFlow.NumberedSibling(path)));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void TestSaveClaimAtomic()
    {
        var controller = new DocumentController();
        var wins = 0;
        using var start = new ManualResetEventSlim();
        var claims = Enumerable.Range(0, 16).Select(_ => Task.Run(() => { start.Wait(); if (controller.TryBeginSave() is not null) Interlocked.Increment(ref wins); })).ToArray();
        start.Set();
        Task.WaitAll(claims);
        Check("exactly one of 16 racing callers claims the save", wins == 1 && controller.IsSaving);
    }
}
