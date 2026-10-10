using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The document and editing operations run for an explicit document without
/// a window, and every entry point of one logical edit (keyboard, menu, context menu, timeline gesture) gives the same model, one undo step, one dirty
/// change and one timeline invalidation.
/// </summary>
public static partial class SelfTest
{
    private static void TestDocumentOperations()
    {
        RunDoCase("explicit-document edits without a window", DoWindowlessEditsCase);
        RunDoCase("the playback bar remap is document state", DoBarRemapCase);
        RunDoCase("note rules without a control", DoNoteRulesCase);
        RunDoCase("every editor command is one logical edit", DoEditorCommandSweepCase);
        RunDoCase("save and export sequences without a window", DoSaveFlowCase);
        RunDoCase("save hardening without a window", DoSaveHardeningCase);
        RunDoCase("close sequences without a window", DoCloseFlowCase);
        RunDoCase("where an opened song goes, without a window", DoPlacementCase);
        RunDoCase("a background tab follows its document", DoTabBarFollowsDocumentCase);
        RunDoCase("write notation from a clip that fits nothing", DoWriteNotationCase);
        RunInWindowFixture((a, context) =>
        {
            var previousConfirm = context.Store.Settings.Editing.ConfirmDeleteBar;
            context.Store.Settings.Editing.ConfirmDeleteBar = false;   // the bar commands ask first; the test answers by not asking
            var previousCapture = DialogHost.Capture;
            DialogHost.Capture = dialog => { if (dialog is ThemedConfirmDialog confirm) confirm.AnswerForTest(MessageBoxResult.Yes); return true; };   // Delete track asks first; the test says yes
            try
            {
                RunDoCase("keyboard and menu give the same result", () => DoKeyboardMenuParityCase(a));
                RunDoCase("paste through keyboard and menu", () => DoPasteParityCase(a));
                RunDoCase("sites moved onto the shared edit", () => DoMigratedSitesCase(a));
                RunDoCase("undoing a retune restores the tuning offset", () => DoRetuneUndoCase(a));
                RunDoCase("context menu and timeline entry points match the shared edit", () => DoSectionEntryPointsCase(a));
                RunDoCase("a track edit invalidates the timeline once", () => DoTrackInvalidationCase(a));
                RunDoCase("saving a background tab while another tab is displayed", () => DoSaveWhileOtherTabDisplayedCase(a));
                RunDoCase("opening into a tab chosen earlier while another tab is displayed", () => DoOpenWhileOtherTabDisplayedCase(a));
                RunDoCase("closing with several unsaved songs and a save that fails", () => DoCloseWithFailingSaveCase(context));
            }
            finally { context.Store.Settings.Editing.ConfirmDeleteBar = previousConfirm; DialogHost.Capture = previousCapture; }
        });
    }

    private static void RunDoCase(string name, Action body)
    {
        try { body(); }
        catch (Exception ex) { Check($"document operations: {name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message} at {string.Join(" <- ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()))}"); }
    }

    // ---------- fixtures ----------

    /// <summary>A small song with a known shape: <paramref name="tracks"/> guitar tracks of <paramref name="bars"/> bars, sections "A" at bar 0 and "B" at bar 4, a note in bar 0 of the first track.</summary>
    private static SongProject DoSong(int tracks = 2, int bars = 8)
    {
        var project = TemplateFactory.Blank();
        var controller = new TrackController();
        project.Tracks.Clear();
        for (var i = 0; i < tracks; i++)
        {
            var track = controller.CreateTrack(project, TrackKind.Guitar);
            track.Name = $"T{i + 1}";
            track.Measures = TemplateFactory.Measures(bars);
            track.ColorHex = new[] { "#F61A16", "#ED2224", "#F4E014", "#2248E8", "#35B954", "#D850C6" }[i % 6];   // the recorded flow hashes use these colours
            project.Tracks.Add(track);
        }
        project.Markers.Clear();
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "A", ColorHex = "#2E74B5" });
        project.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "B", ColorHex = "#35B954" });
        var first = project.Tracks[0];
        var cell = first.Measures[0].Cells[0];
        cell.DurationDenominator = 4;
        cell.Notes.Add(new TabNote { StringIndex = 2, Fret = 5, MidiValue = first.PitchOf(2, 5) });
        project.IsDirty = false;
        return project;
    }

    /// <summary>The song's content hash with the random track ids replaced by their position (two songs built or edited separately compare equal when their content does).</summary>
    private static string DoHash(DocumentSession document)
    {
        var copy = ProjectService.Restore(ProjectService.Snapshot(document.Project));
        for (var i = 0; i < copy.Tracks.Count; i++) copy.Tracks[i].Id = new Guid(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return Convert.ToHexString(ProjectService.ContentHash(copy));
    }

    private sealed record DoOutcome(string Hash, int UndoSteps, bool Dirty, int Revisions, string Selection)
    {
        public bool SameAs(DoOutcome other) => Hash == other.Hash && UndoSteps == other.UndoSteps && Dirty == other.Dirty && Revisions == other.Revisions && Selection == other.Selection;
        public override string ToString() => $"hash {Hash[..8]}, undo steps {UndoSteps}, dirty {Dirty}, timeline invalidations {Revisions}, selection {Selection}";
    }

    // ---------- 1: no window ----------

    private static void DoWindowlessEditsCase()
    {
        var windowsBefore = Application.Current.Windows.OfType<MainWindow>().Count();
        var arrangement = new ArrangementController();
        var tracks = new TrackController();

        void Case(string name, Func<DocumentSession, bool> act, Func<SongProject>? build = null)
        {
            var document = DocumentSession.FromProject((build ?? (() => DoSong()))(), null);
            document.MarkClean();
            var before = DoHash(document);
            var revision = document.Project.TimelineRevision;
            var changed = act(document);
            var undoSteps = document.Undo.UndoCount;
            var after = DoHash(document);
            Check($"document operations (no window): {name} changes the song once: one undo step, dirty, one timeline invalidation",
                changed && undoSteps == 1 && document.Project.IsDirty && document.Project.TimelineRevision == revision + 1 && after != before,
                $"changed {changed}, undo steps {undoSteps}, dirty {document.Project.IsDirty}, revisions +{document.Project.TimelineRevision - revision}");
            var undone = DocumentEdits.Undo(document) is not null && DoHash(document) == before;
            var redone = DocumentEdits.Redo(document) is not null && DoHash(document) == after;
            Check($"document operations (no window): {name} undoes to the original song and redoes to the edited one", undone && redone);
        }

        void NoOp(string name, Func<DocumentSession, bool> act, Func<SongProject>? build = null)
        {
            var document = DocumentSession.FromProject((build ?? (() => DoSong()))(), null);
            document.MarkClean();
            var before = DoHash(document);
            var revision = document.Project.TimelineRevision;
            var changed = act(document);
            Check($"document operations (no window): {name} changes nothing: no undo step, not dirty, no invalidation",
                !changed && document.Undo.UndoCount == 0 && !document.Project.IsDirty && document.Project.TimelineRevision == revision && DoHash(document) == before,
                $"changed {changed}, undo steps {document.Undo.UndoCount}, dirty {document.Project.IsDirty}");
        }

        Case("insert bar", d => arrangement.InsertBar(d, 2, 1, moveMarkers: true).Changed);
        Case("delete bar", d => arrangement.DeleteBar(d, 2, -1, allTracks: true, moveMarkers: true).Changed);
        Case("duplicate bars", d => arrangement.DuplicateBars(d, 1, 2).Changed);
        Case("delete bars", d => arrangement.DeleteBars(d, 1, 2).Changed);
        Case("move bars", d => arrangement.MoveBars(d, 0, 1, 6).Changed);
        Case("section reorder", d => arrangement.MoveSection(d, 0, 2).Changed);
        Case("section duplicate", d =>
        {
            var marker = d.Project.Markers[0];
            var snapshot = arrangement.CaptureSectionSnapshot(d.Project, marker)!;
            arrangement.TryGetSectionBounds(d.Project, marker, out _, out var end);
            return arrangement.InsertSection(d, end, snapshot).Changed;
        });
        Case("section delete", d => arrangement.DeleteSection(d, d.Project.Markers[1]).Changed);
        Case("track add", d => tracks.AddTrack(d, tracks.CreateTrack(d.Project, TrackKind.Bass)).Changed);
        Case("track delete", d => tracks.DeleteTrack(d, 1).Changed);
        Case("track move", d => tracks.MoveTrack(d, 0, 1).Changed, () => DoSong(3));
        Case("repeat bars (generic edit)", d => DocumentEdits.Run(d, p => arrangement.RepeatRange(p, 0, 1, 2) > 0).Changed);

        NoOp("deleting the last track", d => tracks.DeleteTrack(d, 0).Changed, () => DoSong(1));
        NoOp("moving a track onto itself", d => tracks.MoveTrack(d, 1, 1).Changed);
        NoOp("moving a section to its own place", d => arrangement.MoveSection(d, 1, 1).Changed);
        NoOp("deleting a bar that does not exist", d => arrangement.DeleteBar(d, -1, -1, allTracks: true, moveMarkers: false).Changed);
        NoOp("inserting an empty section snapshot", d => arrangement.InsertSection(d, 0, new SectionClipboardSnapshot(new(), null)).Changed);

        // A drag takes its undo state when it starts; the edit then stores that state, not one taken after the model had changed.
        var dragged = DocumentSession.FromProject(DoSong(3), null);
        dragged.MarkClean();
        var snapshot = dragged.Undo.Snapshot(dragged.Project);
        var original = DoHash(dragged);
        var drop = tracks.MoveTrack(dragged, 0, 2, snapshot);
        Check("document operations (no window): a drag edit stores the state taken when the drag started, once", drop.Changed && dragged.Undo.UndoCount == 1 && DocumentEdits.Undo(dragged) is not null && DoHash(dragged) == original);

        var failing = DocumentSession.FromProject(DoSong(), null);
        failing.MarkClean();
        var revisionBefore = failing.Project.TimelineRevision;
        try { DocumentEdits.Run(failing, p => throw new InvalidOperationException("boom")); } catch (InvalidOperationException) { }
        Check("document operations (no window): an edit that throws leaves no undo step and no dirty flag", failing.Undo.UndoCount == 0 && !failing.Project.IsDirty && failing.Project.TimelineRevision == revisionBefore);

        // A model step that marks the timeline itself (the bar grid does) inside an edit: the edit still publishes one new revision, at its end.
        var selfMarking = DocumentSession.FromProject(DoSong(), null);
        selfMarking.MarkClean();
        var revisionAtStart = selfMarking.Project.TimelineRevision;
        var midEdit = -1;
        DocumentEdits.Run(selfMarking, p =>
        {
            p.Tracks[0].Measures[0].Cells.Clear();
            p.MarkTimelineChanged();
            p.MarkTimelineChanged();
            midEdit = p.TimelineRevision;
            return true;
        });
        Check("document operations (no window): marks made inside an edit are one invalidation, published when the edit ends",
            midEdit == revisionAtStart && selfMarking.Project.TimelineRevision == revisionAtStart + 1, $"mid-edit +{midEdit - revisionAtStart}, after +{selfMarking.Project.TimelineRevision - revisionAtStart}");
        Check("document operations (no window): none of this constructed a main window", Application.Current.Windows.OfType<MainWindow>().Count() == windowsBefore);
    }

    // ---------- 2: keyboard vs menu on a real window ----------

    private static DocumentSession DoOpen(MainWindow window, SongProject song)
    {
        var document = DocumentSession.FromProject(song, null);
        document.MarkClean();
        LtField<DocumentManager>(window, "_documents")!.Add(document);
        LtCall(window, "ActivateDocument", document, false, false, false, false);
        SettleLifetimeDispatcher();
        return document;
    }

    private static IEnumerable<MenuItem> DoMenuItems(ItemsControl root)
    {
        foreach (var item in root.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in DoMenuItems(item)) yield return child;
        }
    }

    private static void DoClickMenu(MainWindow window, string commandId)
    {
        var menu = LtField<Menu>(window, "MainMenu")!;
        var item = DoMenuItems(menu).First(m => MenuHotkey.GetId(m) == commandId);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        SettleLifetimeDispatcher();
    }

    private static DoOutcome DoMeasure(MainWindow window, DocumentSession document, Action act)
    {
        var editor = LtField<TabEditorControl>(window, "Editor")!;
        var revision = document.Project.TimelineRevision;
        var steps = document.Undo.UndoCount;
        act();
        SettleLifetimeDispatcher();
        return new DoOutcome(DoHash(document), document.Undo.UndoCount - steps, document.Project.IsDirty, document.Project.TimelineRevision - revision,
            $"{editor.SelectedTrackIndex}/{editor.SelectedMeasure}/{editor.SelectedCell}/{editor.SelectedString}");
    }

    private static void DoKeyboardMenuParityCase(MainWindow window)
    {
        var editor = LtField<TabEditorControl>(window, "Editor")!;
        var grid = LtField<DataGrid>(window, "TrackMixerGrid")!;

        void Parity(string name, string commandId, Func<SongProject> build, Action position)
        {
            var keyboardDoc = DoOpen(window, build());
            position();
            var original = DoHash(keyboardDoc);
            var keyboard = DoMeasure(window, keyboardDoc, () => Check($"document operations: {name}: the command {commandId} is handled", (bool)LtCall(window, "RunHotkey", commandId)!));
            LtCall(window, "Undo_Click", window, new RoutedEventArgs());
            var undone = DoHash(keyboardDoc) == original && keyboardDoc.Undo.UndoCount == 0;

            var menuDoc = DoOpen(window, build());
            position();
            var menu = DoMeasure(window, menuDoc, () => DoClickMenu(window, commandId));

            Check($"document operations: {name}: keyboard and menu give the same model, undo steps, dirty state, timeline invalidations and selection", keyboard.SameAs(menu),
                $"keyboard [{keyboard}] / menu [{menu}]");
            Check($"document operations: {name}: one logical edit is one undo step, one dirty change and one timeline invalidation", keyboard is { UndoSteps: 1, Dirty: true, Revisions: 1 } && keyboard.Hash != original, keyboard.ToString());
            Check($"document operations: {name}: one undo restores the song exactly", undone);
        }

        void OnBar(int bar) => editor.SetPosition(bar, 0, 2, false);
        void OnTrack(int index) { grid.SelectedIndex = index; editor.SelectedTrackIndex = index; }

        Parity("insert bar", "Bar.Insert", () => DoSong(), () => OnBar(2));
        Parity("delete bar", "Bar.Delete", () => DoSong(), () => OnBar(2));
        Parity("add track", "Track.Add", () => DoSong(), () => OnTrack(0));
        Parity("delete track", "Track.Delete", () => DoSong(3), () => OnTrack(1));
        Parity("move track up", "Track.MoveUp", () => DoSong(3), () => OnTrack(2));
        Parity("palm mute (effect change)", "Note.PalmMute", () => DoSong(), () => { OnTrack(0); editor.SetPosition(0, 0, 2, false); });
        Parity("tie (note change)", "Note.Tie", () => DoSong(), () => { OnTrack(0); editor.SetPosition(0, 0, 2, false); });
        Parity("longer note value (duration change)", "Note.Longer", () => DoSong(), () => { OnTrack(0); editor.SetPosition(0, 0, 2, false); });
        Parity("dot (duration change)", "Note.Dot", () => DoSong(), () => { OnTrack(0); editor.SetPosition(0, 0, 2, false); });

        // Note creation: typing a fret on an empty beat (the fretboard click is the other entry point; both end in the editor's one edit).
        var created = DoOpen(window, DoSong());
        OnTrack(0); editor.SetPosition(1, 0, 3, false);
        var before = DoHash(created);
        var creation = DoMeasure(window, created, () => editor.TryHandleKey(System.Windows.Input.Key.D7, System.Windows.Input.ModifierKeys.None));
        LtCall(window, "Undo_Click", window, new RoutedEventArgs());
        Check("document operations: note creation: typing a fret is one undo step, one dirty change and one timeline invalidation, and one undo removes the note",
            creation is { UndoSteps: 1, Dirty: true, Revisions: 1 } && creation.Hash != before && DoHash(created) == before && created.Undo.UndoCount == 0, creation.ToString());
    }

    /// <summary>Sites moved onto DocumentEdits in stage 2: each logical edit is one undo step and one dirty change; timing-neutral ones leave the timeline alone, the others invalidate it once.</summary>
    private static void DoMigratedSitesCase(MainWindow window)
    {
        var editor = LtField<TabEditorControl>(window, "Editor")!;
        var grid = LtField<DataGrid>(window, "TrackMixerGrid")!;
        void Site(string name, Action<DocumentSession> act, int revisions, Func<SongProject>? build = null)
        {
            var document = DoOpen(window, (build ?? (() => DoSong(3)))());
            grid.SelectedIndex = 0; editor.SelectedTrackIndex = 0; editor.SetPosition(2, 0, 2, false);
            var before = DoHash(document);
            var outcome = DoMeasure(window, document, () => act(document));
            var ok = outcome is { UndoSteps: 1, Dirty: true } && outcome.Revisions == revisions && outcome.Hash != before;
            LtCall(window, "Undo_Click", window, new RoutedEventArgs());
            Check($"document operations: {name}: one undo step, dirty, {revisions} timeline invalidation(s), and one undo restores the song", ok && DoHash(document) == before && document.Undo.UndoCount == 0, $"{outcome}; undone {DoHash(document) == before}");
        }

        Site("palette: dim inactive voice", _ => LtCall(LtTools(window), "ToggleInactiveVoiceGray"), 1);
        Site("palette: cycle triplet feel", _ => LtCall(LtTools(window), "CycleTripletFeel"), 1);
        Site("palette: line break", _ => LtCall(LtTools(window), "ToggleLineBreak", true), 1);
        Site("palette: simile", _ => LtCall(LtTools(window), "ToggleSimile", 1), 1);
        Site("Bar menu: clef", _ => LtCall(window, "Clef_Click", window, new RoutedEventArgs()), 1);
        Site("Bar menu: double bar", _ => LtCall(window, "DoubleBar_Click", window, new RoutedEventArgs()), 1);
        Site("Bar menu: triplet feel", _ => LtCall(window, "TripletFeel_Click", window, new RoutedEventArgs()), 1);
        Site("Bar menu: simile (one bar)", _ => LtCall(window, "Simile1_Click", window, new RoutedEventArgs()), 1);
        Site("bus effects on / off", d => window.MixerHost.ToggleBus(Models.MixerGroups.GroupOf(d.Project, d.Project.Tracks[0])), 0);
        Site("track FX power switch", d => window.MixerHost.ToggleTrackChain(d.Project.Tracks[0]), 0);
        Site("track input monitor (clip lanes)", d => DocumentEdits.Run(d, p => { p.Tracks[0].MonitorInput = !p.Tracks[0].MonitorInput; return true; }, invalidatesTimeline: false), 0);
        Site("clip edit (mute a clip)", d =>
        {
            var clip = d.Project.Tracks[0].AudioClips[0];
            LtField<ClipEditController>(window, "_clips")!.Edit(d, () => clip.Muted = !clip.Muted);
        }, 0, () =>
        {
            var song = DoSong(3);
            song.Tracks[0].AudioClips.Add(new AudioClip { File = @"C:\songs\a\x.wav", Name = "x", SourceLengthSec = 1, FileLengthSec = 1 });
            return song;
        });
    }

    /// <summary>Paste: keyboard and menu through the shared clipboard (an in-process one here: the test must not touch the Windows clipboard).</summary>
    private static void DoPasteParityCase(MainWindow window)
    {
        var shared = typeof(ClipboardService).GetField("_shared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previous = shared.GetValue(null);
        var clipboard = new ClipboardService(null);
        shared.SetValue(null, clipboard);
        try
        {
            var editor = LtField<TabEditorControl>(window, "Editor")!;
            var source = DoSong();
            foreach (var kind in new[] { "bars", "beats" })
            {
                var clip = kind == "bars" ? ClipboardService.CaptureBars(source, new[] { 0 }, 0, 1) : ClipboardService.CaptureBeats(source, 0, 0, 0, 0, 0, 0);
                clipboard.Copy(clip);
                SongProject Build() => DoSong();
                void Position() { editor.SelectedTrackIndex = 0; editor.SetPosition(3, 0, 2, false); }

                var keyboardDoc = DoOpen(window, Build()); Position();
                var original = DoHash(keyboardDoc);
                var keyboard = DoMeasure(window, keyboardDoc, () => LtCall(window, "RunHotkey", "Edit.Paste"));
                LtCall(window, "Undo_Click", window, new RoutedEventArgs());
                var undone = DoHash(keyboardDoc) == original && keyboardDoc.Undo.UndoCount == 0;
                var menuDoc = DoOpen(window, Build()); Position();
                var menu = DoMeasure(window, menuDoc, () => DoClickMenu(window, "Edit.Paste"));

                Check($"document operations: paste ({kind}): keyboard and menu give the same model, undo steps, dirty state, timeline invalidations and selection", keyboard.SameAs(menu), $"keyboard [{keyboard}] / menu [{menu}]");
                Check($"document operations: paste ({kind}): one logical edit is one undo step, one dirty change and one timeline invalidation", keyboard is { UndoSteps: 1, Dirty: true, Revisions: 1 } && keyboard.Hash != original, keyboard.ToString());
                Check($"document operations: paste ({kind}): one undo restores the song exactly", undone);
            }
        }
        finally { shared.SetValue(null, previous); }
    }

    /// <summary>
    /// Every editor command that a key or a menu item can run (the note, duration, effect and repeat commands): on a selected note, a command that
    /// changes the song is exactly one undo step, one refresh and one timeline invalidation; one that changes nothing leaves no undo step behind.
    /// </summary>
    private static void DoEditorCommandSweepCase()
    {
        var ids = HotkeyCatalog.All.Select(a => a.Id).Distinct().ToList();
        var handled = 0;
        var offenders = new List<string>();
        foreach (var id in ids)
        {
            var document = DocumentSession.FromProject(DoSong(), null);
            document.MarkClean();
            var editor = new TabEditorControl { Project = document.Project, SelectedTrackIndex = 0 };
            editor.SetPosition(0, 0, 2, false);
            var steps = 0; var edits = 0;
            editor.EditStarting += (_, _) => { document.Undo.Capture(document.Project); steps++; };
            editor.Edited += (_, _) => edits++;
            var before = DoHash(document);
            var revision = document.Project.TimelineRevision;
            bool ran;
            try { ran = editor.Effects.TryRunNoteCommand(id); }
            catch (Exception ex) { offenders.Add($"{id}: threw {ex.GetType().Name}"); continue; }
            if (!ran) continue;
            handled++;
            var changed = DoHash(document) != before;
            var invalidations = document.Project.TimelineRevision - revision;
            var undoSteps = document.Undo.UndoCount;
            if (changed && !(steps == 1 && undoSteps == 1 && edits == 1 && invalidations == 1 && document.Project.IsDirty))
                offenders.Add($"{id}: changed the song with {steps} capture(s), {undoSteps} undo step(s), {edits} refresh(es), {invalidations} invalidation(s)");
            if (!changed && undoSteps != 0)
                offenders.Add($"{id}: changed nothing but left {undoSteps} undo step(s)");
        }
        Check("document operations: the editor's note commands are found (the sweep ran them)", handled >= 25, $"{handled} of {ids.Count} catalogue commands are editor commands");
        Check("document operations: every editor command that changes the song is one undo step, one refresh and one timeline invalidation, and one that changes nothing leaves no undo step",
            offenders.Count == 0, string.Join("; ", offenders.Take(8)));
    }

    /// <summary>The playback bar remap is document state: it follows edits, undo and a new timeline with no window.</summary>
    private static void DoBarRemapCase()
    {
        var idle = DocumentSession.FromProject(DoSong(), null);
        Check("document operations (no window): a section move on a document that is not playing changes no playback state", !idle.Playback.ApplySectionMove(new[] { 1, 0, 2, 3 }) && idle.Playback.PlaybackBarRemap is null && idle.Playback.PlayheadBar == -1);

        var doc = DocumentSession.FromProject(DoSong(), null);
        var playback = doc.Playback;
        playback.IsPlayingVisual = true;
        playback.PlayheadBar = 0;
        var swap = new[] { 1, 0, 2, 3, 4, 5, 6, 7 };
        Check("document operations (no window): a section move while playing composes the remap and moves the playhead bar with its bar",
            playback.ApplySectionMove(swap) && playback.PlaybackBarRemap!.SequenceEqual(SectionReorderService.ComposeBarRemap(Enumerable.Range(0, 8).ToArray(), swap)) && playback.PlayheadBar == 1);

        var before = doc.Undo.Snapshot(doc.Project);
        playback.PlaybackBarRemap = new[] { 3, 2, 1, 0, 4, 5, 6, 7 };
        playback.RememberBarMapping(before);
        playback.PlaybackBarRemap = swap;
        playback.PlayheadBar = 1;
        var restored = playback.RestoreBarMapping(before, 8, engineBar: 2);
        Check("document operations (no window): undo while playing brings back the remap saved with that state and moves the playhead to the engine's bar through it",
            restored is { PlayheadMoved: true } && playback.PlaybackBarRemap!.Take(4).SequenceEqual(new[] { 3, 2, 1, 0 }) && playback.PlayheadBar == 1 && restored.PriorLiveBar == 1, restored?.ToString());

        Check("document operations (no window): inserted bars compose into the remap and the playhead bar follows",
            playback.ApplyStructureEdit(new[] { 0, 2, 3, 4, 5, 6, 7, 8 }, 9) && playback.PlaybackBarRemap is { Length: > 0 });

        var again = playback.RebaseBarMappings(9, () => doc.Undo.Snapshot(doc.Project));
        Check("document operations (no window): a new timeline resets the live remap to the identity and re-bases the saved ones", again && playback.PlaybackBarRemap!.SequenceEqual(Enumerable.Range(0, 9)));
        playback.IsPlayingVisual = false;
        Check("document operations (no window): when playback has stopped re-basing clears the remap and the saved mappings", !playback.RebaseBarMappings(9, () => doc.Undo.Snapshot(doc.Project)) && playback.PlaybackBarRemap is null && playback.PlaybackBarMappingsBySnapshot.Count == 0);
    }

    /// <summary>The note rules the score editor used to hold inline, run on plain models: no control, no window.</summary>
    private static void DoNoteRulesCase()
    {
        TrackModel Guitar(out TabCell cell)
        {
            var track = DoSong().Tracks[0];
            cell = track.Measures[0].Cells[0];
            cell.Notes.Clear();
            return track;
        }
        TabNote Note(TrackModel track, TabCell cell, int stringIndex, int fret)
        {
            var note = new TabNote { StringIndex = stringIndex, Fret = fret, MidiValue = track.PitchOf(stringIndex, fret) };
            cell.Notes.Add(note);
            return note;
        }
        IEnumerable<(TabCell, IReadOnlyList<TabNote>)> Beat(TabCell cell, params TabNote[] movers) { yield return (cell, movers); }

        var track = Guitar(out var cell);
        var note = Note(track, cell, 2, 5);
        var pitch = note.MidiValue;
        var plan = EditCommands.PlanStringMove(track, Beat(cell, note), 1);
        Check("document operations (no control): a string move plans the same pitch on the lower string and changes nothing until it is applied",
            plan.Refusal is null && plan.Moves.Count == 1 && plan.Moves[0].Target == 3 && track.PitchOf(3, plan.Moves[0].Fret) == pitch && note.StringIndex == 2 && note.Fret == 5);
        EditCommands.ApplyStringMove(track, plan);
        Check("document operations (no control): applying the plan moves the note and keeps its sounding pitch", note.StringIndex == 3 && track.PitchOf(3, note.Fret) == pitch && note.MidiValue == pitch);

        var top = Note(track, cell, 0, 3);
        Check("document operations (no control): a note already on the highest string is refused with the reason, whole move cancelled",
            EditCommands.PlanStringMove(track, Beat(cell, top), -1) is { Moves.Count: 0, Refusal: "No change: the note is already on the highest string" });
        Note(track, cell, 4, 0);   // a note on the string the other one would move to
        var low = cell.Notes.First(n => n.StringIndex == 3);
        Check("document operations (no control): a string already holding a note in that beat refuses the move", EditCommands.PlanStringMove(track, Beat(cell, low), 1) is { Refusal: "No change: the lower string already has a note in that beat" });
        Check("document operations (no control): nothing to move says so", EditCommands.PlanStringMove(track, Beat(cell), 1) is { Refusal: "No note to move: put the cursor on a note or select some beats" });
        var drums = new TrackController().CreateTrack(DoSong(), TrackKind.Drums);
        Check("document operations (no control): a drum track has no strings to move between", EditCommands.PlanStringMove(drums, Beat(cell, note), 1) is { Refusal: "This track has no strings to move notes between" });

        var steps = Guitar(out var stepCell);
        var stepNote = Note(steps, stepCell, 2, 5);
        Check("document operations (no control): a pitch step up is one fret on the same string", EditCommands.PlanPitchShift(steps, stepCell, stepNote, 1) is (2, 6));
        stepNote.Fret = 0; stepNote.MidiValue = steps.PitchOf(2, 0);
        var down = EditCommands.PlanPitchShift(steps, stepCell, stepNote, -1);
        Check("document operations (no control): below the open string the note stays (GP5)", down is null, down.ToString());
        stepNote.StringIndex = 5; stepNote.Fret = 0;
        Check("document operations (no control): the lowest playable pitch cannot go lower", EditCommands.PlanPitchShift(steps, stepCell, stepNote, -1) is null);
        EditCommands.ApplyPitchShift(steps, stepNote, 5, 3);
        Check("document operations (no control): applying a pitch step sets string, fret and the sounding pitch", stepNote.Fret == 3 && stepNote.MidiValue == steps.PitchOf(5, 3));

        var a = new TabNote(); var b = new TabNote(); b.Techniques.Add("Vibrato");
        EditCommands.ToggleTechnique(new[] { a, b }, "Vibrato");
        var allOn = a.Techniques.Contains("Vibrato") && b.Techniques.Contains("Vibrato");
        EditCommands.ToggleTechnique(new[] { a, b }, "Vibrato");
        Check("document operations (no control): an effect over a selection is on when any note lacks it, off when all have it", allOn && !a.Techniques.Contains("Vibrato") && !b.Techniques.Contains("Vibrato"));
    }

    // ---------- 3: the other entry points ----------

    private static void DoSectionEntryPointsCase(MainWindow window)
    {
        var arrangement = new ArrangementController();
        var editor = LtField<TabEditorControl>(window, "Editor")!;

        // Context menu "Duplicate section" (the timeline's section menu) against the shared edit run on a plain document.
        var viaWindow = DoOpen(window, DoSong());
        var windowOutcome = DoMeasure(window, viaWindow, () => LtField<SectionEditFlow>(window, "_sections")!.DuplicateSection(viaWindow, viaWindow.Project.Markers[0]));
        var plain = DocumentSession.FromProject(DoSong(), null);
        plain.MarkClean();
        var revision = plain.Project.TimelineRevision;
        arrangement.TryGetSectionBounds(plain.Project, plain.Project.Markers[0], out _, out var end);
        arrangement.InsertSection(plain, end, arrangement.CaptureSectionSnapshot(plain.Project, plain.Project.Markers[0])!);
        Check("document operations: the section context menu's duplicate gives the shared edit's model, one undo step and one timeline invalidation",
            windowOutcome.Hash == DoHash(plain) && windowOutcome is { UndoSteps: 1, Dirty: true, Revisions: 1 } && plain.Project.TimelineRevision == revision + 1, windowOutcome.ToString());

        // The timeline's section drag / reorder.
        var dragWindow = DoOpen(window, DoSong());
        var dragOutcome = DoMeasure(window, dragWindow, () => LtCall(window, "MoveSection", 0, 2));
        var dragPlain = DocumentSession.FromProject(DoSong(), null);
        arrangement.MoveSection(dragPlain, 0, 2);
        Check("document operations: the timeline's section reorder gives the shared edit's model, one undo step and one timeline invalidation",
            dragOutcome.Hash == DoHash(dragPlain) && dragOutcome is { UndoSteps: 1, Dirty: true, Revisions: 1 }, dragOutcome.ToString());

        // Bar duplicate: the Bar menu item against the shared edit.
        var barWindow = DoOpen(window, DoSong());
        editor.SetPosition(1, 0, 2, false);
        var barOutcome = DoMeasure(window, barWindow, () => LtCall(window, "DuplicateBar_Click", window, new RoutedEventArgs()));
        var barPlain = DocumentSession.FromProject(DoSong(), null);
        arrangement.DuplicateBars(barPlain, 1, 1);
        Check("document operations: the Bar menu's duplicate gives the shared edit's model, one undo step and one timeline invalidation",
            barOutcome.Hash == DoHash(barPlain) && barOutcome is { UndoSteps: 1, Dirty: true, Revisions: 1 }, barOutcome.ToString());

        // An undo through the window restores through the same service.
        LtCall(window, "Undo_Click", window, new RoutedEventArgs());
        Check("document operations: the window's Undo is the shared undo (content restored, clean again)", DoHash(barWindow) == DoHash(DocumentSession.FromProject(DoSong(), null)) && !barWindow.Project.IsDirty);
    }

    private static void DoTrackInvalidationCase(MainWindow window)
    {
        var document = DoOpen(window, DoSong(3));
        var revision = document.Project.TimelineRevision;
        LtCall(window, "AddGuitar_Click", window, new RoutedEventArgs());
        Check("document operations: adding a track invalidates the playback timeline (it did not before the shared edit)", document.Project.TimelineRevision == revision + 1, $"+{document.Project.TimelineRevision - revision}");
        revision = document.Project.TimelineRevision;
        LtCall(window, "MoveTrackTo", 0, 2);
        Check("document operations: moving a track invalidates the playback timeline once", document.Project.TimelineRevision == revision + 1, $"+{document.Project.TimelineRevision - revision}");
        Check("document operations: those two edits are two undo steps", document.Undo.UndoCount == 2, document.Undo.UndoCount.ToString());
    }

    // ---------- 4: save, close and open sequences for an explicit document, no window ----------

    private sealed class DoAsk : ISaveInteractions
    {
        public int AudioQuestions, PreflightQuestions;
        public AudioDataSaveChoice? Audio = AudioDataSaveChoice.GpWithEmbeddedProject;
        public GpExportChoice Preflight = GpExportChoice.ExportCompatible;
        public Action? OnQuestion;
        public AudioDataSaveChoice? AskAudioDataChoice(string fileName) { AudioQuestions++; OnQuestion?.Invoke(); return Audio; }
        public GpExportChoice AskGpPreflight(GpPreflightReport report, GpExportKind kind, string fileName) { PreflightQuestions++; return Preflight; }
        public ReplaceFileChoice Replace = ReplaceFileChoice.Replace;
        public readonly List<string> ReplaceQuestions = new();
        public ReplaceFileChoice AskReplaceFullCopy(string fileName) { ReplaceQuestions.Add(fileName); return Replace; }
    }

    private static readonly Func<DocumentSession, Task<TabForge.Audio.StateCollection>> DoNoStates = _ => Task.FromResult(new TabForge.Audio.StateCollection());

    private static string DoScratchFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-doc-ops-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DoCleanFolder(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void DoSaveFlowCase()
    {
        var folder = DoScratchFolder();
        try
        {
            var controller = new DocumentController();
            var flow = new DocumentSaveFlow(controller);
            var ask = new DoAsk();
            var windowsBefore = Application.Current.Windows.OfType<MainWindow>().Count();

            var document = DocumentSession.FromProject(DoSong(), null);
            document.Project.Lyrics = "stale";
            document.Project.IsDirty = true;
            var path = Path.Combine(folder, "song.tforge");
            var saved = flow.SaveAsync(document, path, "lyrics at the start of the command", ask, DoNoStates).GetAwaiter().GetResult();
            Check("document operations (no window): a save writes the file, names it as the document's path and leaves the document clean",
                saved is { Saved: true, Cancelled: false } && File.Exists(path) && document.Path == path && !document.HasUnsavedChanges && !document.IsNew && saved.Message == "Saved song.tforge" && ask.AudioQuestions == 0 && ask.PreflightQuestions == 0, saved.ToString());
            Check("document operations (no window): the lyrics written are the ones the command started with, not whatever the document held", ProjectService.Load(path, InputLimits.MaxTforgeFileBytes).Lyrics == "lyrics at the start of the command");
            Check("document operations (no window): the document's media scope followed the save (its path is now the approval scope)", document.Media.SavedPath is not null && controller.IsSaving == false);

            // An injected failure (the file cannot be replaced): the exception reaches the caller, the previous file is byte-identical, the document stays unsaved and the next save works.
            var before = File.ReadAllBytes(path);
            document.Project.Title = "changed after the first save";
            document.Project.IsDirty = true;
            File.SetAttributes(path, FileAttributes.ReadOnly);
            Exception? failure = null;
            try { flow.SaveAsync(document, path, document.Project.Lyrics, ask, DoNoStates).GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; }
            File.SetAttributes(path, FileAttributes.Normal);
            Check("document operations (no window): an injected save failure throws to the caller, leaves the file as it was and the document unsaved",
                failure is not null && File.ReadAllBytes(path).SequenceEqual(before) && document.HasUnsavedChanges && !controller.IsSaving && !Directory.EnumerateFiles(folder, "*.tmp*").Any(), failure?.GetType().Name ?? "no exception");
            var retry = flow.SaveAsync(document, path, document.Project.Lyrics, ask, DoNoStates).GetAwaiter().GetResult();
            Check("document operations (no window): after the failure the same document saves normally (nothing is stuck in 'saving')", retry.Saved && !document.HasUnsavedChanges);

            // A second save while the first waits for plug-in states is refused at once; the first still completes.
            var release = new TaskCompletionSource<TabForge.Audio.StateCollection>();
            var slow = DocumentSession.FromProject(DoSong(), null);
            slow.Project.IsDirty = true;
            var first = flow.SaveAsync(slow, Path.Combine(folder, "slow.tforge"), "", ask, _ => release.Task);
            var second = flow.SaveAsync(slow, Path.Combine(folder, "other.tforge"), "", ask, DoNoStates).GetAwaiter().GetResult();
            release.SetResult(new TabForge.Audio.StateCollection());
            var firstDone = first.GetAwaiter().GetResult();
            Check("document operations (no window): a save started while another waits for plug-in states is refused; the first completes", second is { Saved: false, Message: "Already saving…" } && firstDone.Saved && !File.Exists(Path.Combine(folder, "other.tforge")));

            // Songs with audio data saved as .gp ask how, once per song; Cancel writes nothing and is not remembered.
            var audioSong = DocumentSession.FromProject(DoSong(), null);
            audioSong.Project.Tracks[0].MixerGroup = "Lead";
            audioSong.Project.IsDirty = true;
            var gpPath = Path.Combine(folder, "audio.gp");
            ask.Audio = null;
            var cancelled = flow.SaveAsync(audioSong, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
            Check("document operations (no window): cancelling the audio-data question writes nothing and leaves the song unsaved", cancelled is { Cancelled: true, Saved: false } && !File.Exists(gpPath) && audioSong.HasUnsavedChanges && ask.AudioQuestions == 1);
            ask.Audio = AudioDataSaveChoice.TForgeFile;
            var asTforge = flow.SaveAsync(audioSong, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
            var again = flow.SaveAsync(audioSong, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
            Check("document operations (no window): the answer is remembered for the song (asked twice in all: the cancel and the answer), and the .tforge choice writes the .tforge file",
                asTforge.Saved && File.Exists(Path.Combine(folder, "audio.tforge")) && !File.Exists(gpPath) && ask.AudioQuestions == 2 && again.Message == "Saved audio.tforge", $"{asTforge}; questions {ask.AudioQuestions}");
            Check("document operations (no window): saving did not construct a main window", Application.Current.Windows.OfType<MainWindow>().Count() == windowsBefore);
        }
        finally { DoCleanFolder(folder); }
    }

    /// <summary>The .tforge a .gp save of a song with audio data writes beside the .gp asks before replacing a file; the full copy and the .gp are written as one pair; writing a project does not change its unsaved state.</summary>
    private static void DoSaveHardeningCase()
    {
        var folder = DoScratchFolder();
        try
        {
            // ProjectService.Save writes and returns the hash; whether the song is clean is the saver's decision.
            var plain = DoSong();
            plain.IsDirty = true;
            var hash = ProjectService.Save(Path.Combine(folder, "plain.tforge"), plain);
            Check("document operations (no window): writing a .tforge does not mark the song clean (the caller decides)", plain.IsDirty && hash.SequenceEqual(ProjectService.ContentHash(plain)) && File.Exists(Path.Combine(folder, "plain.tforge")));

            // The side .tforge of a .gp save: Replace / Keep both / Cancel when the file exists, no question when it does not.
            DocumentSession AudioSong() { var d = DocumentSession.FromProject(DoSong(), null); d.Project.Tracks[0].MixerGroup = "Lead"; d.Project.IsDirty = true; return d; }
            var flowController = new DocumentController();
            var flow = new DocumentSaveFlow(flowController);
            var ask = new DoAsk { Audio = AudioDataSaveChoice.TForgeFile };
            var savingDuringQuestions = new List<bool>();
            ask.OnQuestion = () => savingDuringQuestions.Add(flowController.IsSaving);
            var gpPath = Path.Combine(folder, "audio.gp");
            var sideTforge = Path.Combine(folder, "audio.tforge");
            var fresh = AudioSong();
            var written = flow.SaveAsync(fresh, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
            Check("document operations (no window): the side .tforge is written without a question when no such file exists", written.Saved && File.Exists(sideTforge) && ask.ReplaceQuestions.Count == 0);
            Check("document operations (no window): the save is already claimed while its first question is open (an import cannot replace the tab meanwhile)", savingDuringQuestions.SequenceEqual(new[] { true }) && !flowController.IsSaving, string.Join(",", savingDuringQuestions));
            var heldSong = AudioSong();
            using (var held = flowController.TryBeginSave())
            {
                var refused = flow.SaveAsync(heldSong, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
                var passed = flow.SaveAsync(heldSong, gpPath, "", new DoAsk { Audio = AudioDataSaveChoice.TForgeFile }, DoNoStates, null, held).GetAwaiter().GetResult();
                Check("document operations (no window): while a save is claimed another is refused, and the holder's own save runs under its claim and does not release it",
                    refused is { Saved: false, Message: "Already saving…" } && passed.Saved && flowController.IsSaving);
                string? busy = null; var bodyRan = false;
                var claimed = flow.RunClaimedAsync(_ => { bodyRan = true; return Task.FromResult(true); }, text => busy = text).GetAwaiter().GetResult();
                Check("document operations (no window): a save or export asked for while another is claimed says \"Already saving…\" and runs nothing",
                    !claimed && !bodyRan && busy == SaveFlowText.AlreadySaving, busy ?? "(nothing said)");
            }
            Check("document operations (no window): disposing the claim releases the save", !flowController.IsSaving);

            var sentinel = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(sideTforge, sentinel);
            ask.Replace = ReplaceFileChoice.Cancel;
            var cancelledSong = AudioSong();
            var cancelled = flow.SaveAsync(cancelledSong, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
            Check("document operations (no window): cancelling the replace question writes nothing and leaves the song unsaved",
                cancelled is { Cancelled: true, Saved: false } && File.ReadAllBytes(sideTforge).SequenceEqual(sentinel) && !File.Exists(gpPath) && cancelledSong.HasUnsavedChanges && ask.ReplaceQuestions.SequenceEqual(new[] { "audio.tforge" }), cancelled.ToString());

            ask.Replace = ReplaceFileChoice.KeepBoth;
            var both = AudioSong();
            var kept = flow.SaveAsync(both, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
            var numbered = Path.Combine(folder, "audio (2).tforge");
            Check("document operations (no window): Keep both leaves the existing file as it is and saves under a numbered name, which becomes the song's file",
                kept.Saved && File.ReadAllBytes(sideTforge).SequenceEqual(sentinel) && File.Exists(numbered) && both.Path == numbered && kept.Message == "Saved audio (2).tforge", $"{kept}; path {both.Path}");

            ask.Replace = ReplaceFileChoice.Replace;
            var replacing = AudioSong();
            var replaced = flow.SaveAsync(replacing, gpPath, "", ask, DoNoStates).GetAwaiter().GetResult();
            Check("document operations (no window): Replace overwrites the existing file with the song's full copy", replaced.Saved && replacing.Path == sideTforge && ProjectService.Load(sideTforge, InputLimits.MaxTforgeFileBytes).Tracks[0].MixerGroup == "Lead");

            // The full copy and the clean .gp are one pair: written together, nothing left over, and a failure between the two commits leaves the old state.
            var controller = new DocumentController();
            var pairGp = Path.Combine(folder, "pair.gp");
            var pairNative = Path.Combine(folder, "pair (full copy).tforge");
            var song = DocumentSession.FromProject(DoSong(), null);
            song.Project.IsDirty = true;
            GuitarProExporter.Save(DoSong(), pairGp, embedProject: false);
            var oldGp = File.ReadAllBytes(pairGp);
            FilePathPolicy.FaultInjection = s => { if (s == "committed:pair.gp") throw new IOException("injected between the two commits"); };
            Exception? failure = null;
            try { controller.ExportCleanGuitarPro(song, pairGp, GpExportKind.Save, GpExportChoice.KeepNativeCopy, ""); } catch (Exception ex) { failure = ex; }
            finally { FilePathPolicy.FaultInjection = null; }
            Check("document operations (no window): a failure between the .gp and its full copy leaves the old .gp, no full copy and an unsaved song",
                failure is not null && File.ReadAllBytes(pairGp).SequenceEqual(oldGp) && !File.Exists(pairNative) && song.HasUnsavedChanges && !File.Exists(FilePathPolicy.PairMarkerPathFor(pairGp))
                && !Directory.EnumerateFiles(folder, "*.tmp*").Any(), failure?.Message ?? "no exception");
            var result = controller.ExportCleanGuitarPro(song, pairGp, GpExportKind.Save, GpExportChoice.KeepNativeCopy, "");
            Check("document operations (no window): the pair is written together: the full copy and the .gp exist, the song follows the full copy and no recovery marker is left",
                result.NativeCopyPath == pairNative && File.Exists(pairNative) && File.Exists(pairGp) && !File.ReadAllBytes(pairGp).SequenceEqual(oldGp) && song.Path == pairNative && !song.HasUnsavedChanges
                && !File.Exists(FilePathPolicy.PairMarkerPathFor(pairGp)), $"{result}; path {song.Path}");

            // An export (never a save) writes the same pair and leaves the song's own state alone.
            var exported = DocumentSession.FromProject(DoSong(), null);
            exported.Project.IsDirty = true;
            var exportGp = Path.Combine(folder, "exported.gp");
            var export = controller.ExportCleanGuitarPro(exported, exportGp, GpExportKind.Export, GpExportChoice.KeepNativeCopy, "");
            Check("document operations (no window): an export with a full copy writes both files and leaves the song unsaved with no file", export.NativeCopyPath is not null && File.Exists(export.NativeCopyPath) && File.Exists(exportGp) && exported.HasUnsavedChanges && exported.Path is null);
        }
        finally { DoCleanFolder(folder); }
    }

    private static void DoCloseFlowCase()
    {
        DocumentSession Dirty(string title) { var d = DocumentSession.FromProject(DoSong(), null); d.Project.Title = title; d.Project.IsDirty = true; return d; }
        var one = Dirty("one"); var two = Dirty("two"); var three = Dirty("three");
        var clean = DocumentSession.FromProject(DoSong(), null); clean.MarkClean();
        var all = new[] { one, clean, two, three };
        var asked = new List<string>();

        var plan = DocumentCloseFlow.Plan(all, new HashSet<DocumentSession>(), (d, count) => { asked.Add($"{d.Project.Title}/{count}"); return DiscardAnswer.SaveFirst; });
        Check("document operations (no window): a close asks once per unsaved document, in tab order, never about a clean one, and tells each how many are being asked",
            !plan.Cancel && string.Join(",", asked) == "one/3,two/3,three/3" && plan.Save.Count == 3 && plan.Discard.Count == 0);

        asked.Clear();
        plan = DocumentCloseFlow.Plan(all, new HashSet<DocumentSession>(), (d, _) => { asked.Add(d.Project.Title); return d == two ? null : DiscardAnswer.SaveFirst; });
        Check("document operations (no window): Cancel at the second document stops asking and plans nothing at all (nothing saved, nothing discarded)", plan.Cancel && plan.Save.Count == 0 && plan.Discard.Count == 0 && asked.Count == 2);

        asked.Clear();
        plan = DocumentCloseFlow.Plan(all, new HashSet<DocumentSession> { two }, (d, count) => { asked.Add($"{d.Project.Title}/{count}"); return d == one ? DiscardAnswer.Close : DiscardAnswer.SaveFirst; });
        Check("document operations (no window): a document already answered 'don't save' is not asked again; discard and save lists are kept apart",
            string.Join(",", asked) == "one/2,three/2" && plan.Discard.SequenceEqual(new[] { one }) && plan.Save.SequenceEqual(new[] { three }));

        // The save sequence stops at the first failure: later documents are not attempted and the caller keeps the window open.
        var attempted = new List<string>();
        var ok = DocumentCloseFlow.SaveAllAsync(new[] { one, two, three }, d => { attempted.Add(d.Project.Title); if (d == two) return Task.FromResult(false); d.MarkClean(); return Task.FromResult(true); }).GetAwaiter().GetResult();
        Check("document operations (no window): a failed save stops the close sequence at that document (the first is saved, the third not attempted)", !ok && string.Join(",", attempted) == "one,two" && !one.HasUnsavedChanges && three.HasUnsavedChanges);
        var stillDirty = DocumentCloseFlow.SaveAllAsync(new[] { three }, _ => Task.FromResult(true)).GetAwaiter().GetResult();
        Check("document operations (no window): a save that reports success but leaves the document unsaved counts as a failure", !stillDirty);
        Check("document operations (no window): an empty plan saves nothing and succeeds", DocumentCloseFlow.SaveAllAsync(Array.Empty<DocumentSession>(), _ => Task.FromResult(false)).GetAwaiter().GetResult());
    }

    private static void DoPlacementCase()
    {
        DocumentSession New(string title) { var d = DocumentSession.FromProject(DoSong(), null); d.Project.Title = title; d.MarkClean(); return d; }
        static DiscardAnswer Close(DocumentSession _) => DiscardAnswer.Close;

        // A new tab opens beside the others.
        var documents = new DocumentManager();
        var first = documents.Active;   // the blank tab every window starts with
        var added = DocumentPlacement.Place(documents, New("added"), null, false, false, Close);
        Check("document operations (no window): opening without a target adds a tab and keeps the others", added.Kind == PlacementKind.Added && documents.Documents.Count == 2 && documents.Documents[0] == first);

        // The replace target was chosen when the open started: the tab that is displayed now is not the one replaced.
        var a = documents.Documents[0]; var b = documents.Documents[1];
        documents.Activate(b);
        var replaced = DocumentPlacement.Place(documents, New("replacement"), a, false, false, Close);
        Check("document operations (no window): a replace open applies to the tab chosen when it started, not to the tab displayed when it completes",
            replaced.Kind == PlacementKind.Replaced && replaced.Target == a && documents.Documents[0] == replaced.Document && documents.Documents[1] == b && documents.Documents.Count == 2);
        Check("document operations (no window): the replaced tab's media context is closed (queued work for it ends)", a.Media.IsClosed && !b.Media.IsClosed);

        // A target that left this window (moved to another window meanwhile) is not replaced from here: the song opens beside the tabs.
        var moved = documents.Detach(1)!;
        var beside = DocumentPlacement.Place(documents, New("beside"), moved, false, false, Close);
        Check("document operations (no window): a target that left the window is never replaced; the song opens as a new tab and the tab that moved is untouched",
            beside.Kind == PlacementKind.Added && documents.Documents.Count == 2 && !moved.Media.IsClosed);

        // Unsaved changes in the target: Keep opens nothing; Save-first opens beside; a running save opens beside without asking.
        var dirtyTarget = documents.Documents[1];
        dirtyTarget.Project.IsDirty = true;
        var asks = 0;
        var kept = DocumentPlacement.Place(documents, New("kept"), dirtyTarget, false, false, _ => { asks++; return DiscardAnswer.Keep; });
        Check("document operations (no window): answering Keep opens nothing and changes nothing", kept.Kind == PlacementKind.Kept && kept.Document is null && documents.Documents.Count == 2 && asks == 1 && documents.Documents.Contains(dirtyTarget));
        var saveFirst = DocumentPlacement.Place(documents, New("save-first"), dirtyTarget, false, false, _ => DiscardAnswer.SaveFirst);
        Check("document operations (no window): answering Save opens the song right after the target (which stays until its save succeeded)",
            saveFirst.Kind == PlacementKind.OpenedBesideToSave && saveFirst.Target == dirtyTarget && documents.IndexOf(saveFirst.Document!) == documents.IndexOf(dirtyTarget) + 1 && !dirtyTarget.Media.IsClosed);
        asks = 0;
        var whileSaving = DocumentPlacement.Place(documents, New("while-saving"), dirtyTarget, false, true, _ => { asks++; return DiscardAnswer.Close; });
        Check("document operations (no window): while a save runs the target is not replaced and nobody is asked", whileSaving.Kind == PlacementKind.Added && asks == 0);

        // Replace everything (a launch with a file): the old tabs are released.
        var count = documents.Documents.Count;
        var old = documents.Documents.ToArray();
        var order = new List<string>();
        var all = DocumentPlacement.Place(documents, New("everything"), null, true, false, Close, beforeReplaceAll: _ => order.Add($"hook with {documents.Documents.Count} tabs open"));
        Check("document operations (no window): replacing everything runs its hook while the old tabs are still open, leaves one tab and releases the old ones",
            all.Kind == PlacementKind.ReplacedAll && documents.Documents.Count == 1 && order.Single() == $"hook with {count} tabs open" && old.All(d => d.Media.IsClosed));
    }

    // ---------- 5: the same sequences on a real window with another tab displayed ----------

    private static bool DoWait(Task<bool> task)
    {
        var end = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < end)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
        if (!task.IsCompleted) throw new TimeoutException("the save did not complete");
        return task.Result;
    }

    private static void DoSaveWhileOtherTabDisplayedCase(MainWindow window)
    {
        var folder = DoScratchFolder();
        try
        {
            var shown = DoOpen(window, DoSong());
            shown.Project.Title = "displayed";
            shown.Project.IsDirty = true;
            var other = DoOpen(window, DoSong());
            other.Project.Title = "background";
            other.Path = Path.Combine(folder, "background.tforge");
            other.IsNew = false;
            other.Project.IsDirty = true;
            var documents = LtField<DocumentManager>(window, "_documents")!;
            documents.Activate(shown);
            LtCall(window, "ActivateDocument", shown, false, false, false, false);
            SettleLifetimeDispatcher();

            var activeChanges = 0;
            void OnActive(object? s, EventArgs e) => activeChanges++;
            documents.ActiveChanged += OnActive;
            bool saved;
            try { saved = DoWait((Task<bool>)LtCall(window, "SaveDocumentAsync", other)!); }
            finally { documents.ActiveChanged -= OnActive; }

            Check("document operations: saving a background tab writes that tab's file and leaves it clean", saved && File.Exists(other.Path) && !other.HasUnsavedChanges && ProjectService.Load(other.Path!, InputLimits.MaxTforgeFileBytes).Title == "background");
            Check("document operations: the displayed tab was never switched to save the other one (no active-tab change, no borrowed 'current document')", activeChanges == 0 && documents.Active == shown, $"{activeChanges} change(s)");
            Check("document operations: the displayed tab is untouched by the other tab's save (still unsaved, still unnamed)", shown.HasUnsavedChanges && shown.Path is null && shown.IsNew);
            Check("document operations: the window is not left in a saving state", !LtField<DocumentController>(window, "_documentController")!.IsSaving);
        }
        finally { DoCleanFolder(folder); }
    }

    private static bool DoPumpUntil(Func<bool> done, int seconds = 30)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < end)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
        return done();
    }

    /// <summary>Closing a window with three unsaved songs; the second one's file cannot be written. Nothing is lost, the window stays, the next close asks again.</summary>
    private static void DoCloseWithFailingSaveCase(LifetimeContext context)
    {
        var folder = DoScratchFolder();
        var previousCapture = DialogHost.Capture;
        var previousMessages = DialogHost.MessageCapture;
        var answers = new Queue<MessageBoxResult>();
        var asked = new List<string>();
        var errors = new List<string>();
        DialogHost.Capture = dialog =>
        {
            if (dialog is ThemedConfirmDialog confirm && answers.Count > 0) { asked.Add(confirm.Title ?? ""); confirm.AnswerForTest(answers.Dequeue()); return true; }
            context.Captured.Add(dialog); return false;
        };
        DialogHost.MessageCapture = (caption, _) => errors.Add(caption);
        try
        {
            var w = NewLifetimeWindow();
            LtCall(w, "NewTab"); LtCall(w, "NewTab");
            var docs = w.OpenDocuments.ToList();
            for (var i = 0; i < docs.Count; i++)
            {
                docs[i].Project.Title = $"Song {i}";
                docs[i].Path = Path.Combine(folder, $"s{i}.tforge");
                docs[i].IsNew = false;
                docs[i].Project.IsDirty = true;
            }
            File.WriteAllText(docs[1].Path!, "old content");
            File.SetAttributes(docs[1].Path!, FileAttributes.ReadOnly);   // the injected failure: this file cannot be replaced
            LtCall(w, "ActivateTabAt", 0);
            foreach (var r in new[] { MessageBoxResult.Yes, MessageBoxResult.Yes, MessageBoxResult.Yes }) answers.Enqueue(r);
            w.Close();
            DoPumpUntil(() => errors.Count > 0);
            SettleLifetimeDispatcher();
            var discard = LtField<System.Collections.IEnumerable>(LtField<object>(w, "_closeFlow")!, "_discardOnClose")!.Cast<object>().Count();
            Check("document operations: closing with three unsaved songs asks about each, saves them in order and stops at the one that fails: the window stays open",
                asked.Count == 3 && errors.SequenceEqual(new[] { "Save failed" }) && w.IsVisible && docs.Count == 3, $"asked {asked.Count}, errors [{string.Join(",", errors)}], visible {w.IsVisible}");
            Check("document operations: the song saved before the failure is on disk and clean; the failing one keeps its file and its unsaved changes; the one after it was not attempted",
                File.Exists(docs[0].Path!) && !docs[0].HasUnsavedChanges && File.ReadAllText(docs[1].Path!) == "old content" && docs[1].HasUnsavedChanges && docs[2].HasUnsavedChanges && !File.Exists(docs[2].Path!));
            Check("document operations: after a failed save nothing is remembered as discarded (every answer is asked again at the next close) and the window is not left saving",
                discard == 0 && !LtField<DocumentController>(w, "_documentController")!.IsSaving);

            File.SetAttributes(docs[1].Path!, FileAttributes.Normal);
            asked.Clear(); errors.Clear();
            foreach (var r in new[] { MessageBoxResult.Yes, MessageBoxResult.Yes }) answers.Enqueue(r);
            w.Close();
            var closed = DoPumpUntil(() => !w.IsVisible);
            SettleLifetimeDispatcher();
            Check("document operations: the next close asks only about the two songs still unsaved, saves both and closes the window",
                closed && asked.Count == 2 && errors.Count == 0 && ProjectService.Load(docs[1].Path!, InputLimits.MaxTforgeFileBytes).Title == "Song 1" && ProjectService.Load(docs[2].Path!, InputLimits.MaxTforgeFileBytes).Title == "Song 2",
                $"closed {closed}, asked {asked.Count}, errors {errors.Count}");
        }
        finally
        {
            DialogHost.Capture = previousCapture;
            DialogHost.MessageCapture = previousMessages;
            DoCleanFolder(folder);
        }
    }

    private static void DoOpenWhileOtherTabDisplayedCase(MainWindow window)
    {
        var documents = LtField<DocumentManager>(window, "_documents")!;
        var a = DoOpen(window, DoSong()); a.Project.Title = "A"; a.MarkClean();
        var b = DoOpen(window, DoSong()); b.Project.Title = "B"; b.MarkClean();
        var count = documents.Documents.Count;
        documents.Activate(b);
        LtCall(window, "ActivateDocument", b, false, false, false, false);
        SettleLifetimeDispatcher();
        var incoming = DoSong(); incoming.Title = "incoming";

        // A background import that was started for tab A completes while tab B is displayed: A is replaced, B is not.
        var loaded = (bool)LtCall(window, "LoadProject", incoming, null, true, a, false, null)!;
        SettleLifetimeDispatcher();
        Check("document operations: an open started for tab A replaces A even though tab B is displayed when it completes", loaded && documents.Documents.Count == count && !documents.Documents.Contains(a) && documents.Documents.Contains(b) && b.Project.Title == "B"
            && documents.Documents.Any(d => d.Project.Title == "incoming"), string.Join(",", documents.Documents.Select(d => d.Project.Title)));

        // The target left this window while the song was read: nothing is replaced, the song opens beside.
        var c = DoOpen(window, DoSong()); c.Project.Title = "C"; c.MarkClean();
        var detached = documents.Detach(documents.IndexOf(c))!;
        var before = documents.Documents.ToArray();
        var second = DoSong(); second.Title = "second";
        LtCall(window, "LoadProject", second, null, true, detached, false, null);
        SettleLifetimeDispatcher();
        Check("document operations: an open whose target moved to another window opens as a new tab and replaces nothing here", documents.Documents.Count == before.Length + 1 && before.All(documents.Documents.Contains) && !detached.Media.IsClosed);
        detached.DisposePlayback();

        // Replacing a background tab switches away from the displayed one: the playback-on-tab-switch preference applies (here: stop the previous tab).
        var tabSettings = LtField<TabSettings>(window, "_tabSettings")!;
        var preference = tabSettings.PlaybackOnTabSwitch;
        var playing = StartTabPlaybackSession();
        try
        {
            var target = DoOpen(window, DoSong()); target.Project.Title = "target"; target.MarkClean();
            documents.Add(playing);
            LtCall(window, "ActivateDocument", playing, false, false, false, false);
            SettleLifetimeDispatcher();
            tabSettings.PlaybackOnTabSwitch = TabPlaybackActions.StopPrevious;
            var wasPlaying = playing.Playback.Engine.IsPlaying;
            var third = DoSong(); third.Title = "third";
            LtCall(window, "LoadProject", third, null, true, target, false, null);
            SettleLifetimeDispatcher();
            Check("document operations: an open that replaces a background tab applies the tab-switch playback preference to the tab that was displayed",
                wasPlaying && !playing.Playback.Engine.IsPlaying && documents.Documents.Contains(playing) && !documents.Documents.Contains(target), $"was playing {wasPlaying}, still playing {playing.Playback.Engine.IsPlaying}");
        }
        finally
        {
            tabSettings.PlaybackOnTabSwitch = preference;
            playing.Playback.Engine.Stop();
        }
    }
}
