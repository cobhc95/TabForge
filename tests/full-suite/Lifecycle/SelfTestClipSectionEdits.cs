using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The clip commands (keyboard, menu, drag, drop) and the section, bar and area flows of the timeline, run through real windows: each logical edit is
/// one undo step and one dirty change, undo restores the song, a clip copied in one window pastes in another, and a drop lands in the song it was
/// made on. The section flows also pin their exact outcome (model hash, selection, status text) so a move of the code cannot change them.
/// </summary>
public static partial class SelfTest
{
    private static void TestClipAndSectionEdits() => RunInWindowFixture((a, context) =>
    {
        var settings = context.Store.Settings;
        var (confirmBar, confirmSection) = (settings.Editing.ConfirmDeleteBar, settings.General.ConfirmDeleteSection);
        settings.Editing.ConfirmDeleteBar = false;   // the delete commands ask first; the test answers by not asking
        settings.General.ConfirmDeleteSection = false;
        var shared = typeof(ClipboardService).GetField("_shared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previous = shared.GetValue(null);
        shared.SetValue(null, new ClipboardService(null));   // an in-memory score clipboard: the test must not touch the Windows clipboard
        try
        {
            RunCseCase("each clip operation", () => CseClipOperationsCase(a));
            RunCseCase("clip clipboard across windows", () => CseCrossWindowPasteCase(a));
            RunCseCase("a drop lands in the displayed song only", () => CseDropTargetCase(a));
            RunCseCase("section, bar and area flows", CseSectionFlowsInOwnWindowCase);
        }
        finally
        {
            shared.SetValue(null, previous);
            (settings.Editing.ConfirmDeleteBar, settings.General.ConfirmDeleteSection) = (confirmBar, confirmSection);
        }
    });

    private static void RunCseCase(string name, Action body)
    {
        try { body(); }
        catch (Exception ex) { Check($"clip and section edits: {name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message} at {string.Join(" <- ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()))}"); }
    }

    private static AudioClip CseClip(string name, double start, double length = 1, string file = @"C:\songs\a\x.wav") =>
        new() { File = file, Name = name, StartSec = start, SourceLengthSec = length, FileLengthSec = length };

    private static SongProject CseClipSong()
    {
        var song = DoSong(3);
        song.Tracks[0].AudioClips.Add(CseClip("x", 0));
        return song;
    }

    private static ClipEditController CseClips(MainWindow window) => LtField<ClipEditController>(window, "_clips")!;

    private static string CseStatus(MainWindow window) => LtField<TextBlock>(window, "StatusText")!.Text;

    private static void CseUndo(MainWindow window) => LtCall(window, "Undo_Click", window, new RoutedEventArgs());

    // ---------- clip operations ----------

    private static void CseClipOperationsCase(MainWindow window)
    {
        var arrangement = LtField<ArrangementPanel>(window, "Arrangement")!;

        void Operation(string name, Func<DocumentSession, bool> act, Action<DocumentSession>? prepare = null)
        {
            var doc = DoOpen(window, CseClipSong());
            arrangement.SelectedClip = doc.Project.Tracks[0].AudioClips[0];
            prepare?.Invoke(doc);
            var original = DoHash(doc);
            var handled = false;
            var outcome = DoMeasure(window, doc, () => handled = act(doc));
            var changed = DoHash(doc) != original;
            CseUndo(window);
            Check($"clip edits: {name}: handled, one undo step, one dirty change, and one undo restores the song",
                handled && changed && outcome is { UndoSteps: 1, Dirty: true } && DoHash(doc) == original && doc.Undo.UndoCount == 0, $"{outcome}; handled {handled}, changed {changed}");
        }

        bool Hotkey(DocumentSession d, string id) => CseClips(window).RunHotkey(d, id);
        Operation("delete", d => Hotkey(d, "Clip.Delete"));
        Operation("nudge right", d => Hotkey(d, "Clip.NudgeRight"));
        Operation("nudge left (fine)", d => Hotkey(d, "Clip.NudgeLeftFine"), d => d.Project.Tracks[0].AudioClips[0].StartSec = 0.5);
        Operation("lane down", d => Hotkey(d, "Clip.LaneDown"));
        Operation("mute", d => Hotkey(d, "Clip.Mute"));
        Operation("duplicate", d => Hotkey(d, "Clip.Duplicate"));
        Operation("cut", d => Hotkey(d, "Clip.Cut"));
        Operation("paste", d => Hotkey(d, "Clip.Paste"), d => Check("clip edits: copy is handled and edits nothing", Hotkey(d, "Clip.Copy")));
        Operation("move to another track", d =>
        {
            var plan = MediaDrop.PlanMove(d.Project, d.Project.Tracks[0].AudioClips[0], d.Project.Tracks[0].Kind, 1, 0, 2.0, SongQuarterMap.For(d.Project));
            CseClips(window).MoveTo(d, d.Project.Tracks[0].AudioClips[0], 0, plan, false);
            return true;
        });
        Operation("copy to a new track below the last", d =>
        {
            var plan = MediaDrop.PlanMove(d.Project, d.Project.Tracks[0].AudioClips[0], d.Project.Tracks[0].Kind, d.Project.Tracks.Count, 0, 2.0, SongQuarterMap.For(d.Project), copy: true);
            CseClips(window).MoveTo(d, d.Project.Tracks[0].AudioClips[0], 0, plan, true);
            return d.Project.Tracks.Count == 4;
        });
        Operation("audio file dropped on a track", d =>
        {
            CseClips(window).ApplyMediaDrop(d, CseDropPlan(d, 1, 2.0));
            return d.Project.Tracks[1].AudioClips.Count == 1;
        });
        Operation("audio file dropped below the last track", d =>
        {
            CseClips(window).ApplyMediaDrop(d, CseDropPlan(d, d.Project.Tracks.Count, 2.0));
            return d.Project.Tracks.Count == 4 && d.Project.Tracks[3].AudioClips.Count == 1;
        });

        // A clip longer than the song grows the song in the same undo step.
        var longDoc = DoOpen(window, DoSong(2, 8));
        longDoc.Project.Tracks[0].AudioClips.Add(CseClip("long", 0, 1));
        arrangement.SelectedClip = longDoc.Project.Tracks[0].AudioClips[0];
        var barsBefore = longDoc.Project.Tracks[0].Measures.Count;
        var originalLong = DoHash(longDoc);
        var grown = DoMeasure(window, longDoc, () => { CseClips(window).Edit(longDoc, () => longDoc.Project.Tracks[0].AudioClips[0].StartSec = 20); });
        var barsAfter = longDoc.Project.Tracks[0].Measures.Count;
        CseUndo(window);
        Check("clip edits: a clip moved past the last bar adds bars in the same undo step, and one undo removes them again",
            barsAfter > barsBefore && grown is { UndoSteps: 1, Dirty: true } && DoHash(longDoc) == originalLong && longDoc.Project.Tracks[0].Measures.Count == barsBefore,
            $"{grown}; bars {barsBefore} -> {barsAfter} -> {longDoc.Project.Tracks[0].Measures.Count}");
    }

    private static MediaDropPlan CseDropPlan(DocumentSession doc, int trackIndex, double seconds)
    {
        var item = new DropItem { Path = @"C:\songs\a\dropped.wav", Name = "dropped", Kind = DropItemKind.Audio, Seconds = seconds };
        return MediaDrop.Plan(doc.Project, new[] { item }, trackIndex, 0, 0, SongQuarterMap.For(doc.Project));
    }

    private static void CseCrossWindowPasteCase(MainWindow first)
    {
        var second = NewLifetimeWindow();
        try
        {
            var arrangementA = LtField<ArrangementPanel>(first, "Arrangement")!;
            var arrangementB = LtField<ArrangementPanel>(second, "Arrangement")!;
            var docA = DoOpen(first, CseClipSong());
            docA.Project.Tracks[0].AudioClips[0].Name = "copied-in-a";
            arrangementA.SelectedClip = docA.Project.Tracks[0].AudioClips[0];
            var docB = DoOpen(second, CseClipSong());
            arrangementB.SelectedClip = docB.Project.Tracks[0].AudioClips[0];
            var hashA = DoHash(docA);
            var copied = CseClips(first).RunHotkey(docA, "Clip.Copy");
            var pasted = DoMeasure(second, docB, () => CseClips(second).RunHotkey(docB, "Clip.Paste"));
            var clips = docB.Project.Tracks[0].AudioClips;
            Check("clip edits: a clip copied in one window pastes into the song of another window as one undo step",
                copied && clips.Count == 2 && clips.Any(c => c.Name == "copied-in-a") && pasted is { UndoSteps: 1, Dirty: true }
                && DoHash(docA) == hashA && docA.Undo.UndoCount == 0, $"{pasted}; clips in B {string.Join(",", clips.Select(c => c.Name))}");
        }
        finally { second.Close(); SettleLifetimeDispatcher(); }
    }

    private static void CseDropTargetCase(MainWindow window)
    {
        var background = DoOpen(window, DoSong(3));
        var shown = DoOpen(window, DoSong(3));
        var backgroundHash = DoHash(background);
        var outcome = DoMeasure(window, shown, () => CseClips(window).ApplyMediaDrop(shown, CseDropPlan(shown, 0, 1.5)));
        Check("clip edits: a drop is applied to the song shown, one undo step, and the other tab is untouched",
            outcome is { UndoSteps: 1, Dirty: true } && shown.Project.Tracks[0].AudioClips.Count == 1
            && DoHash(background) == backgroundHash && background.Undo.UndoCount == 0 && !background.Project.IsDirty, outcome.ToString());

        // A drop that completes after its tab closed (or for a tab that is not shown) is ignored: nothing lands in the tab that is shown.
        var closed = DoOpen(window, DoSong(3));
        var survivor = DoOpen(window, DoSong(3));
        var survivorHash = DoHash(survivor);
        var documents = LtField<DocumentManager>(window, "_documents")!;
        documents.Detach(documents.Documents.ToList().IndexOf(closed));
        var closedHash = DoHash(closed);
        CseClips(window).ApplyMediaDrop(closed, CseDropPlan(closed, 0, 1.5));
        Check("clip edits: a drop completing after its tab closed changes neither the closed song nor the tab that is shown",
            DoHash(survivor) == survivorHash && survivor.Undo.UndoCount == 0 && !survivor.Project.IsDirty && DoHash(closed) == closedHash && closed.Undo.UndoCount == 0 && closed.Project.Tracks[0].AudioClips.Count == 0);
        AutosaveRegistry.Retire(new[] { closed });   // closing the tab for good: its autosave state goes with it
    }

    // ---------- section, bar and area flows ----------

    /// <summary>The exact outcome of each flow (content hash, undo steps, selection, status text).</summary>
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["copy section"] = "hash 9F0ED3DE, undo steps 0, dirty False, timeline invalidations 0, selection 0/2/0/2; status 'Copied section 'A' (clipboard busy: paste works inside TabForge only)'",
        ["copy bar (this track)"] = "hash 9F0ED3DE, undo steps 0, dirty False, timeline invalidations 0, selection 0/2/0/2; status 'Copied bar 2 from T1 (clipboard busy: paste works inside TabForge only)'",
        ["copy bar (all tracks)"] = "hash 9F0ED3DE, undo steps 0, dirty False, timeline invalidations 0, selection 0/2/0/2; status 'Copied bar 2 from all tracks (clipboard busy: paste works inside TabForge only)'",
        ["duplicate section"] = "hash 26A0B7A0, undo steps 1, dirty True, timeline invalidations 1, selection 0/4/0/2; status 'Duplicated section 'A''",
        ["paste section after"] = "hash A49A3A8A, undo steps 1, dirty True, timeline invalidations 1, selection 0/8/0/2; status 'Pasted section 'A''",
        ["paste section at a bar"] = "hash D949D80C, undo steps 1, dirty True, timeline invalidations 1, selection 0/2/0/2; status 'Pasted section 'B''",
        ["cut section"] = "hash 19E07459, undo steps 1, dirty True, timeline invalidations 1, selection 0/2/0/2; status 'Cut section 'B''",
        ["delete section"] = "hash 1FD24D98, undo steps 1, dirty True, timeline invalidations 1, selection 0/0/0/2; status 'Deleted section 'A' and its bars from every track (Undo restores them)'",
        ["insert bar before"] = "hash 7EF15505, undo steps 1, dirty True, timeline invalidations 1, selection 0/2/0/2; status 'Added bar 3'",
        ["insert bar after the last"] = "hash 93274727, undo steps 1, dirty True, timeline invalidations 1, selection 0/8/0/2; status 'Added bar 9'",
        ["delete bar (all tracks)"] = "hash 5AC0B9F5, undo steps 1, dirty True, timeline invalidations 1, selection 0/2/0/2; status 'Deleted bar 3'",
        ["delete bar (this track)"] = "hash 37F50CA5, undo steps 1, dirty True, timeline invalidations 1, selection 0/2/0/2; status 'Deleted bar 3'",
        ["paste bar (this track)"] = "hash 3A439B5C, undo steps 1, dirty True, timeline invalidations 1, selection 0/2/0/2; status 'Pasted 1 bar at bar 4'",
        ["paste bar (all tracks)"] = "hash 3A439B5C, undo steps 1, dirty True, timeline invalidations 1, selection 0/2/0/2; status 'Pasted 1 bar at bar 4'",
        ["section move"] = "hash 4F875DF7, undo steps 1, dirty True, timeline invalidations 1, selection 0/6/0/2; status 'Section moved'",
        ["copy area"] = "hash 9F0ED3DE, undo steps 0, dirty False, timeline invalidations 0, selection 0/1/0/2; status 'Copied bars 2-3 (clipboard busy: paste works inside TabForge only)'",
        ["delete area"] = "hash A67E1EB3, undo steps 1, dirty True, timeline invalidations 1, selection 0/1/0/2; status 'Deleted bars 2-3'",
        ["paste area"] = "hash DB9EEF48, undo steps 1, dirty True, timeline invalidations 1, selection 0/5/0/2; status 'Loop range set to bars 6-7 (press Loop (F9) to play in loops)'",
        ["move area"] = "hash 229B372F, undo steps 1, dirty True, timeline invalidations 1, selection 0/4/0/2; status 'Moved bars 2-3 to bar 5'",
    };

    private static void CseSectionFlowsInOwnWindowCase()
    {
        var window = NewLifetimeWindow();
        try { CseSectionFlowsCase(window); }
        finally { window.Close(); SettleLifetimeDispatcher(); }
    }

    private static void CseSectionFlowsCase(MainWindow window)
    {
        var editor = LtField<TabEditorControl>(window, "Editor")!;
        var clipboard = ClipboardService.Shared;

        void Flow(string name, Action<DocumentSession> act, Action<DocumentSession>? prepare = null, int steps = 1)
        {
            var doc = DoOpen(window, DoSong(3));
            editor.SelectedTrackIndex = 0;
            editor.SetPosition(2, 0, 2, false);
            prepare?.Invoke(doc);
            SettleLifetimeDispatcher();
            var original = DoHash(doc);
            var outcome = DoMeasure(window, doc, () => act(doc));
            var text = $"{outcome}; status '{CseStatus(window)}'";
            var restored = true;
            if (outcome.UndoSteps == 1) { CseUndo(window); restored = DoHash(doc) == original && doc.Undo.UndoCount == 0; }
            var ok = outcome.UndoSteps == steps && (steps == 0 || outcome.Dirty) && restored && text == Expected.GetValueOrDefault(name);
            Log.Add($"  info  section flow [{name}] {text}");
            Check($"clip and section edits: {name}: {(steps == 1 ? "one undo step, one dirty change, undo restores" : "no document change")}, exact outcome",
                ok, $"{text} (expected {Expected.GetValueOrDefault(name)})");
        }

        var sections = LtField<SectionEditFlow>(window, "_sections")!;
        var track0 = new Func<DocumentSession, TrackModel>(d => d.Project.Tracks[0]);
        void CopySection(DocumentSession d, int marker = 0) => sections.CopySection(d, d.Project.Markers[marker]);
        void SelectArea(DocumentSession d) => LtCall(window, "ApplyLoopRange", 1, 2, 0, -1);

        Flow("copy section", d => CopySection(d), steps: 0);
        Flow("copy bar (this track)", d => sections.CopyBar(d, 1, track0(d), false), steps: 0);
        Flow("copy bar (all tracks)", d => sections.CopyBar(d, 1, track0(d), true), steps: 0);
        Flow("duplicate section", d => sections.DuplicateSection(d, d.Project.Markers[0]));
        Flow("paste section after", d => sections.PasteSectionAfter(d, d.Project.Markers[1]), d => CopySection(d, 0));
        Flow("paste section at a bar", d => sections.PasteSectionAt(d, 2), d => CopySection(d, 1));
        Flow("cut section", d => sections.CutSection(d, d.Project.Markers[1]));
        Flow("delete section", d => sections.DeleteSection(d, d.Project.Markers[0], confirm: false));
        Flow("insert bar before", d => sections.InsertBar(d, 2));
        Flow("insert bar after the last", d => sections.InsertBar(d, 8));
        Flow("delete bar (all tracks)", d => sections.DeleteBar(d, 2, track0(d), true));
        Flow("delete bar (this track)", d => sections.DeleteBar(d, 2, track0(d), false));
        Flow("paste bar (this track)", d => sections.PasteBar(d, 3, track0(d), false), d => sections.CopyBar(d, 0, track0(d), false));
        Flow("paste bar (all tracks)", d => sections.PasteBar(d, 3, track0(d), true), d => sections.CopyBar(d, 0, track0(d), true));
        Flow("section move", d => LtCall(window, "MoveSection", 0, 2));
        Flow("copy area", d => sections.CopyArea(d, 1, 2), SelectArea, steps: 0);
        Flow("delete area", d => sections.DeleteArea(d, 1, 2, "Deleted"), SelectArea);
        Flow("paste area", d => sections.PasteAreaAt(d, 5), d =>
        {
            SelectArea(d);
            sections.CopyArea(d, 1, 2);
        });
        Flow("move area", d => sections.MoveArea(d, 1, 2, 6), SelectArea);
        Check("clip and section edits: the section flows copied through the shared clipboard", clipboard.Current is not null);
    }
}
