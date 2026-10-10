using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge;

// Owns: the headless pins of DocumentTabsController: closing one, other and right-hand tabs (the question, Cancel, Save, the busy-save refusal),
//     the last tab closing the window, Save-then-close, duplicate and reorder.
// Does not own: the real window's tab strip (TestTabUi, TestEssentialCloseTabUnsaved) or the save itself (DocumentSaveFlow).
// Tests: TestDocumentTabsController.
public static partial class SelfTest
{
    private sealed class FakeTabsHost : IDocumentTabsHost
    {
        public readonly DocumentManager Manager = new();
        public readonly TabSettings Settings = new();
        public bool Saving;
        public string Status = "";
        public int WindowCloses, Refreshes, Captures, Operations, OpenOperations;
        public readonly List<string> Asked = new();
        public readonly List<DocumentSession> Activated = new();
        public readonly Queue<DiscardAnswer> Script = new();
        public bool SaveSucceeds = true;

        DocumentManager IDocumentTabsHost.Documents => Manager;
        TabSettings IDocumentTabsHost.TabSettings => Settings;
        bool IDocumentTabsHost.IsSaving => Saving;
        void IDocumentTabsHost.CaptureDocumentState() => Captures++;
        void IDocumentTabsHost.RefreshTabs() => Refreshes++;
        void IDocumentTabsHost.Activate(DocumentSession session, bool focusTabSelection, bool applyPlaybackSwitchPolicy) { Activated.Add(session); Manager.Activate(session); }
        DiscardAnswer IDocumentTabsHost.AskDiscard(DocumentSession doc)
        {
            if (!doc.HasUnsavedChanges) return DiscardAnswer.Close;
            Asked.Add(doc.DisplayName);
            return Script.Dequeue();
        }
        Task<bool> IDocumentTabsHost.SaveDocumentAsync(DocumentSession doc)
        {
            if (SaveSucceeds) doc.MarkClean();
            return Task.FromResult(SaveSucceeds);
        }
        void IDocumentTabsHost.BeginDocumentOperation() { Operations++; OpenOperations++; }
        void IDocumentTabsHost.EndDocumentOperation() => OpenOperations--;
        void IDocumentTabsHost.CloseWindow() => WindowCloses++;
        void IDocumentTabsHost.SetStatus(string text) => Status = text;
    }

    private static void TestDocumentTabsController()
    {
        var host = new FakeTabsHost();
        var controller = new DocumentTabsController(host);
        var clean = CleanSong(61, "Clean");
        var dirtyA = DirtyDocument(1, 4, 62, "DirtyA");
        var dirtyB = DirtyDocument(1, 4, 63, "DirtyB");
        host.Manager.Add(clean);
        host.Manager.Add(dirtyA);
        host.Manager.Add(dirtyB);

        host.Script.Enqueue(DiscardAnswer.Keep);
        controller.CloseAsync(2).GetAwaiter().GetResult();
        Check("tabs: Cancel on the save question keeps the tab (asked once, nothing closed)", host.Manager.Documents.Count == 3 && host.Asked.Count == 1 && host.Captures == 1);

        host.Saving = true;
        controller.CloseAsync(2).GetAwaiter().GetResult();
        Check("tabs: a tab with unsaved changes does not close while a save runs, and nothing is asked", host.Manager.Documents.Count == 3 && host.Asked.Count == 1 && host.Status.StartsWith("Saving", StringComparison.Ordinal));
        host.Saving = false;

        host.Script.Enqueue(DiscardAnswer.SaveFirst);
        controller.CloseAsync(2).GetAwaiter().GetResult();
        Check("tabs: Save saves the tab and then closes it; the neighbour is shown", host.Manager.Documents.Count == 2 && !host.Manager.Documents.Contains(dirtyB) && ReferenceEquals(host.Manager.Active, host.Activated[^1]));

        host.SaveSucceeds = false;
        host.Script.Enqueue(DiscardAnswer.SaveFirst);
        controller.CloseAsync(1).GetAwaiter().GetResult();
        Check("tabs: a failed save keeps the tab open with its changes", host.Manager.Documents.Contains(dirtyA) && dirtyA.HasUnsavedChanges);
        host.SaveSucceeds = true;

        var extra = DirtyDocument(1, 4, 64, "DirtyC");
        host.Manager.Add(extra);
        host.Script.Enqueue(DiscardAnswer.Close);
        host.Script.Enqueue(DiscardAnswer.Close);
        host.Asked.Clear();
        controller.CloseOthersAsync(host.Manager.IndexOf(clean)).GetAwaiter().GetResult();
        Check("tabs: Close others asks last tab first, closes the rest and keeps the chosen tab shown",
            host.Manager.Documents.Count == 1 && ReferenceEquals(host.Manager.Documents[0], clean) && host.Asked.SequenceEqual(new[] { "DirtyC", "DirtyA" }) && host.OpenOperations == 0 && host.Operations == 1,
            string.Join(",", host.Asked));

        controller.Duplicate(0);
        Check("tabs: Duplicate opens a copy and shows it", host.Manager.Documents.Count == 2 && ReferenceEquals(host.Manager.Active, host.Manager.Documents[1]));
        var refreshes = host.Refreshes;
        controller.Move(0, 1);
        controller.Move(1, 1);
        Check("tabs: Move refreshes the strip only when the order changed", host.Refreshes == refreshes + 1);

        controller.CloseToTheRightAsync(0).GetAwaiter().GetResult();
        Check("tabs: Close to the right leaves the tabs up to the chosen one", host.Manager.Documents.Count == 1 && host.OpenOperations == 0);

        host.Script.Enqueue(DiscardAnswer.Close);   // the copy may carry the unsaved flag
        host.Settings.LastTabClosed = LastTabActions.CloseWindow;
        controller.CloseAsync(0).GetAwaiter().GetResult();
        Check("tabs: closing the last tab with 'close window' detaches it and closes the window", host.WindowCloses == 1 && host.Manager.Documents.Count == 0);

        var keep = CleanSong(65, "Keep");
        var saved = DirtyDocument(1, 4, 66, "Saved");
        host.Manager.Add(keep);
        host.Manager.Add(saved);
        controller.SaveThenCloseAsync(saved).GetAwaiter().GetResult();
        Check("tabs: Save-then-close closes the saved tab once it is clean", host.Manager.Documents.Count == 1 && !host.Manager.Documents.Contains(saved));
        controller.SaveThenCloseAsync(keep).GetAwaiter().GetResult();
        Check("tabs: Save-then-close never closes the only tab", host.Manager.Documents.Count == 1);
    }
}

public static partial class SelfTest
{
    private sealed class FakeExportHost : IScoreExportHost
    {
        public SongProject Project { get; } = DirtyDocument(1, 4, 71, "Export me").Project;
        public string? Path;
        public string Status = "", Error = "";
        public string? SuggestedName;
        public string? PickSavePath(string title, string filter, string defaultExt, string suggestedName) { SuggestedName = suggestedName; return Path; }
        public string? PromptTemplateName(string suggested) => null;
        public int WritePdf(string path) => 2;
        public void ShowError(string title, string message) => Error = title;
        public void SetStatus(string text) => Status = text;
    }

    private static void TestScoreExportController()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-export-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var host = new FakeExportHost { Path = System.IO.Path.Combine(dir, "song.mid") };
            var controller = new ScoreExportController(host);
            controller.ExportMidi();
            Check("export: MIDI writes the chosen file and reports its name", System.IO.File.Exists(host.Path) && host.Status == "Exported MIDI song.mid" && host.SuggestedName == "Export me", host.Status);
            host.Path = System.IO.Path.Combine(dir, "song.txt");
            controller.ExportAscii();
            Check("export: ASCII writes the chosen file", System.IO.File.Exists(host.Path) && host.Status == "Exported ASCII song.txt");
            controller.ExportPdf();
            Check("export: PDF reports the page count (plural)", host.Status == "Exported PDF song.txt (2 pages)", host.Status);
            host.Status = "";
            host.Path = null;
            controller.ExportMidi();
            Check("export: cancelling the dialog does nothing", host.Status == "" && host.Error == "");
            host.Path = System.IO.Path.Combine(dir, "missing", "deeper", "song.xml");
            controller.ExportMusicXml();
            Check("export: a failed write is shown as an error and sets no status", host.Error == "MusicXML export failed" && host.Status == "", host.Error + "/" + host.Status);
            controller.SaveAsTemplate();
            Check("export: cancelling the template name stores nothing", host.Status.Length == 0 || !host.Status.StartsWith("Saved template", StringComparison.Ordinal));
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }
}
