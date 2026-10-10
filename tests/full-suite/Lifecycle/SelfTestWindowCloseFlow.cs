using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Documents;

namespace TabForge;

// Owns: the headless pins of WindowCloseFlow: the save-or-close questions, Cancel, the answers kept for the close that follows, the deferred close,
//     the save input gate and degraded-mode overwrite confirmation.
// Does not own: the real window's Closing handler (the window-lifetime tests) or the save itself (DocumentSaveFlow, TestSaveTransactions).
// Tests: TestWindowCloseFlow.
public static partial class SelfTest
{
    /// <summary>
    /// A window with no dialogs: the answers are scripted, posted work is queued for the test to run, and a close request is recorded (the test
    /// plays the window's part by running it).
    /// </summary>
    private sealed class FakeCloseHost : IWindowCloseHost
    {
        public readonly List<DocumentSession> Open = new();
        public DocumentSession? Shown;
        public bool ConfirmOnClose = true;
        public string Status = "";
        public Cursor? CursorValue;
        public readonly List<string> Asked = new();
        public readonly Queue<DiscardAnswer> Script = new();
        public readonly Queue<Action> Posted = new();
        public readonly List<Action> Closes = new();
        public Func<DocumentSession, bool> SaveSucceeds = _ => true;
        public int Saves;

        IReadOnlyList<DocumentSession> IWindowCloseHost.Documents => Open;
        DocumentSession IWindowCloseHost.Shown => Shown!;
        bool IWindowCloseHost.IsSaving => false;   // the save-in-progress path runs through the document operation counter (see CloseDuringSaveWaitsForTheSave)
        bool IWindowCloseHost.ConfirmOnClose => ConfirmOnClose;
        bool IWindowCloseHost.IsClosed => false;
        DiscardAnswer IWindowCloseHost.AskSaveChanges(string message)
        {
            Asked.Add(message);
            return Script.Dequeue();
        }
        Task<bool> IWindowCloseHost.SaveDocumentAsync(DocumentSession doc)
        {
            Saves++;
            var ok = SaveSucceeds(doc);
            if (ok) doc.MarkClean();
            return Task.FromResult(ok);
        }
        void IWindowCloseHost.PostClose(Action closed) => Closes.Add(closed);
        void IWindowCloseHost.Post(Action work) => Posted.Enqueue(work);
        Cursor? IWindowCloseHost.Cursor { get => CursorValue; set => CursorValue = value; }
        void IWindowCloseHost.SetStatus(string text) => Status = text;

        public void RunPosted()
        {
            while (Posted.Count > 0) Posted.Dequeue()();
        }
    }

    private static DocumentSession CleanSong(int seed, string title)
    {
        var doc = DirtyDocument(1, 4, seed, title);
        doc.MarkClean();
        return doc;
    }

    private static void TestWindowCloseFlow()
    {
        CleanWindowClosesWithoutAsking();
        CancelLeavesEverythingAsItWas();
        SaveAnswerSavesThenCloses();
        FailedSaveKeepsWindowAndAsksAgain();
        DiscardAnswerIsNotAskedAgain();
        CloseDuringSaveWaitsForTheSave();
        RequestCloseIsOnePostAtATime();
        ConfirmOnCloseOffSkipsQuestions();
        SaveInputGateNestsAndRestoresCursor();
        DegradedModeConfirmsOverwriteOnce();
    }

    private static void CleanWindowClosesWithoutAsking()
    {
        var host = new FakeCloseHost();
        var song = CleanSong(31, "Clean");
        host.Open.Add(song);
        host.Shown = song;
        var flow = new WindowCloseFlow(host);
        var closed = flow.ConfirmWindowClose();
        Check("window close: a window whose songs are all saved closes without a question and without a save",
            closed && host.Asked.Count == 0 && host.Posted.Count == 0 && host.Saves == 0);
    }

    private static void CancelLeavesEverythingAsItWas()
    {
        var host = new FakeCloseHost();
        var first = DirtyDocument(1, 4, 32, "First");
        var second = DirtyDocument(1, 4, 33, "Second");
        host.Open.AddRange(new[] { first, second });
        host.Shown = first;
        host.Script.Enqueue(DiscardAnswer.SaveFirst);
        host.Script.Enqueue(DiscardAnswer.Keep);   // Cancel on the second question
        var flow = new WindowCloseFlow(host);
        var closed = flow.ConfirmWindowClose();
        host.RunPosted();
        Check("window close: Cancel on any question keeps the window open, saves nothing and closes nothing; every song is still unsaved",
            !closed && host.Posted.Count == 0 && host.Saves == 0 && host.Closes.Count == 0 && first.HasUnsavedChanges && second.HasUnsavedChanges
            && host.Asked.Count == 2 && host.Asked[1] == $"Save changes to {second.DisplayName}?",
            $"asked {host.Asked.Count}, saves {host.Saves}, closes {host.Closes.Count}");
    }

    private static void SaveAnswerSavesThenCloses()
    {
        var host = new FakeCloseHost();
        var song = DirtyDocument(1, 4, 34, "Save me");
        host.Open.Add(song);
        host.Shown = song;
        host.Script.Enqueue(DiscardAnswer.SaveFirst);
        var flow = new WindowCloseFlow(host);
        var closed = flow.ConfirmWindowClose();
        var savedBeforeRun = host.Saves;
        host.RunPosted();   // the save runs after the close was cancelled; the window then closes
        var secondClose = flow.ConfirmWindowClose();   // the Closing that follows: the song is clean now, no second question
        Check("window close: Save on the lone shown song asks the short question, saves it, then closes without asking again",
            !closed && host.Asked.Count == 1 && host.Asked[0] == "Save changes to the current project?" && savedBeforeRun == 0 && host.Saves == 1
            && !song.HasUnsavedChanges && host.Closes.Count == 1 && secondClose && host.Asked.Count == 1,
            $"asked {host.Asked.Count}, saves {host.Saves}, closes {host.Closes.Count}");
    }

    private static void FailedSaveKeepsWindowAndAsksAgain()
    {
        var host = new FakeCloseHost();
        var discard = DirtyDocument(1, 4, 35, "Discard");
        var failing = DirtyDocument(1, 4, 36, "Failing");
        host.Open.AddRange(new[] { discard, failing });
        host.Shown = discard;
        host.SaveSucceeds = d => d != failing;
        host.Script.Enqueue(DiscardAnswer.Close);
        host.Script.Enqueue(DiscardAnswer.SaveFirst);
        var flow = new WindowCloseFlow(host);
        flow.ConfirmWindowClose();
        host.RunPosted();
        var stillOpen = host.Closes.Count == 0 && failing.HasUnsavedChanges;
        host.Script.Enqueue(DiscardAnswer.Close);
        host.Script.Enqueue(DiscardAnswer.SaveFirst);
        flow.ConfirmWindowClose();   // the answers given before are not kept: both songs are asked again
        Check("window close: a save that does not complete keeps the window open, and the next close asks every song again",
            stillOpen && host.Asked.Count == 4 && host.Asked[2] == $"Save changes to {discard.DisplayName}?" && host.Asked[3] == $"Save changes to {failing.DisplayName}?"
            && host.Closes.Count == 0,
            $"asked {host.Asked.Count}, closes {host.Closes.Count}");
    }

    private static void DiscardAnswerIsNotAskedAgain()
    {
        var host = new FakeCloseHost();
        var discard = DirtyDocument(1, 4, 37, "Not saved");
        var save = DirtyDocument(1, 4, 38, "Saved");
        host.Open.AddRange(new[] { discard, save });
        host.Shown = discard;
        host.Script.Enqueue(DiscardAnswer.Close);
        host.Script.Enqueue(DiscardAnswer.SaveFirst);
        var flow = new WindowCloseFlow(host);
        flow.ConfirmWindowClose();
        host.RunPosted();
        var secondClose = flow.ConfirmWindowClose();   // the close after the saves: the song answered "don't save" is not asked again
        Check("window close: a song answered \"don't save\" is not asked about again by the close that follows the other saves",
            host.Asked.Count == 2 && secondClose && host.Closes.Count == 1 && discard.HasUnsavedChanges && !save.HasUnsavedChanges,
            $"asked {host.Asked.Count}, closes {host.Closes.Count}");
    }

    private static void CloseDuringSaveWaitsForTheSave()
    {
        var host = new FakeCloseHost();
        var song = DirtyDocument(1, 4, 39, "Busy");
        host.Open.Add(song);
        host.Shown = song;
        var flow = new WindowCloseFlow(host);
        flow.BeginDocumentOperation();
        var closedDuringSave = flow.ConfirmWindowClose();
        var statusWhileSaving = host.Status;
        var askedDuringSave = host.Asked.Count;
        flow.EndDocumentOperation();   // the save ends: the close that was held back is requested now
        Check("window close: a close requested during a save is refused, asks nothing, and happens once the save has ended",
            !closedDuringSave && statusWhileSaving == "Closing when the save has finished…" && askedDuringSave == 0 && host.Closes.Count == 1,
            $"closes {host.Closes.Count}, status \"{statusWhileSaving}\"");
    }

    private static void RequestCloseIsOnePostAtATime()
    {
        var host = new FakeCloseHost();
        var flow = new WindowCloseFlow(host);
        flow.RequestClose();
        flow.RequestClose();
        var oneWhilePending = host.Closes.Count;
        host.Closes[0]();   // the window ran the posted close
        flow.RequestClose();
        Check("window close: while a close is pending another request posts nothing; once it has run, the next request posts again",
            oneWhilePending == 1 && host.Closes.Count == 2, $"pending {oneWhilePending}, total {host.Closes.Count}");
    }

    private static void ConfirmOnCloseOffSkipsQuestions()
    {
        var host = new FakeCloseHost { ConfirmOnClose = false };
        var song = DirtyDocument(1, 4, 40, "Probe");
        host.Open.Add(song);
        host.Shown = song;
        var closed = new WindowCloseFlow(host).ConfirmWindowClose();
        Check("window close: with the unsaved-changes questions off (probes, scripted runs) a dirty window closes without asking",
            closed && host.Asked.Count == 0 && host.Saves == 0);
    }

    private static void SaveInputGateNestsAndRestoresCursor()
    {
        var host = new FakeCloseHost { CursorValue = Cursors.Hand };
        var flow = new WindowCloseFlow(host);
        flow.BeginSaveInputGate();
        var busy = host.CursorValue == Cursors.AppStarting && flow.InputGated;
        flow.BeginSaveInputGate();
        flow.EndSaveInputGate();
        var stillGated = flow.InputGated && host.CursorValue == Cursors.AppStarting;
        flow.EndSaveInputGate();
        var released = !flow.InputGated && ReferenceEquals(host.CursorValue, Cursors.Hand);
        flow.EndSaveInputGate();   // an extra end is harmless
        Check("window close: the save input gate nests, shows the busy cursor while held, and restores the cursor when the last holder ends",
            busy && stillGated && released && !flow.InputGated, $"busy {busy}, stillGated {stillGated}, released {released}");
    }

    private static void DegradedModeConfirmsOverwriteOnce()
    {
        var flow = new WindowCloseFlow(new FakeCloseHost());
        var normal = !flow.Degraded && !flow.MustConfirmOverwrite("a.tforge") && flow.DegradedTitleSuffix == "";
        flow.MarkDegraded();
        var asks = flow.MustConfirmOverwrite("a.tforge");
        flow.ConfirmedWhileDegraded("A.TFORGE");   // the same file, another case: confirmed once for the path
        var confirmedOnce = !flow.MustConfirmOverwrite("a.tforge") && flow.MustConfirmOverwrite("b.tforge");
        Check("window close: after an error an overwrite asks until the person confirms that file; the title says a restart is recommended",
            normal && asks && confirmedOnce && flow.DegradedTitleSuffix.Contains("restart recommended", StringComparison.Ordinal),
            $"normal {normal}, asks {asks}, confirmedOnce {confirmedOnce}");
    }
}
