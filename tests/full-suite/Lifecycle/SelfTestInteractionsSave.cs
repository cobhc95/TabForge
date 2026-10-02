using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Plugins;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Interaction scenarios about saving: I-2 (switching documents during a save), I-3 (undo, save, reopen identity).</summary>
public static partial class SelfTest
{
    private static ObservableCollection<TabItemModel> IxTabItems(MainWindow window) =>
        LtField<ObservableCollection<TabItemModel>>(LtField<BrowserTabBar>(window, "Tabs")!, "_items")!;

    private static TabItemModel IxTabOf(MainWindow window, DocumentSession session) => IxTabItems(window).First(item => ReferenceEquals(item.Session, session));

    private static DocumentController IxController(MainWindow window) => LtField<DocumentController>(window, "_documentController")!;

    /// <summary>A guitar song whose first track plays through a plug-in: saving it asks the engine for that plug-in's state, which is where a save can be held.</summary>
    private static SongProject IxPluginSong(string name)
    {
        var song = DoSong(2, 8);
        var track = song.Tracks[0];
        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new PluginSlot { Name = name, Path = $@"C:\NoSuch\{name}.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
        return song;
    }

    /// <summary>Answers every plug-in state request the engine client waits for (the engine is a fake here): "no state", which leaves the song as it is.</summary>
    private static int IxAnswerStateRequests(int plugins)
    {
        var client = AudioEngineClient.Instance;
        var pending = LtField<System.Collections.IDictionary>(client, "_stateRequests")!.Values.Cast<AudioEngineClient.StateRequest>().ToArray();
        foreach (var request in pending)
            for (var index = 0; index < plugins; index++)
            {
                var stream = new MemoryStream();
                Frames.Write(stream, (byte)EngineEvent.PluginState, w => { w.Write(request.Slot); w.Write(request.Id); w.Write(index); w.Write(plugins); w.Write((byte)PluginStateStatus.NoState); w.WriteString(""); });
                stream.Position = 0;
                client.OnPluginState(Frames.Read(stream)!.Value.Reader);
            }
        return pending.Length;
    }

    /// <summary>Answers the two questions a .gp save of a song with audio data asks: how to save it (a clean .gp plus its audio-data file) and what a clean .gp leaves out (continue).</summary>
    private static Func<Window, bool?> IxSaveAnswers(LifetimeContext context, List<string> asked)
    {
        return dialog =>
        {
            if (dialog.Title == "Save song with audio settings")
            {
                asked.Add("audio data");
                var button = Logical<Button>(dialog).First(b => b.Content is StackPanel panel && panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text.StartsWith("Clean .gp file", StringComparison.Ordinal) == true);
                try { button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); } catch (InvalidOperationException) { }   // the dialog was never shown: the choice is stored before DialogResult throws
                return true;
            }
            if (dialog.Title == "Save as .gp file")
            {
                asked.Add("preflight");
                var button = Logical<Button>(dialog).First(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == GpExportPreflightDialog.CompatibleId);
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return true;
            }
            context.Captured.Add(dialog);
            return false;
        };
    }

    // ---------- I-2: switching documents during a save ----------

    private static void IxSwitchDuringSaveCase(LifetimeContext context)
    {
        IxSwitchDuringSave(context, ".tforge");
        IxSwitchDuringSave(context, ".gp");
    }

    private static void IxSwitchDuringSave(LifetimeContext context, string extension)
    {
        var label = $"I-2 ({extension})";
        var folder = DoScratchFolder();
        var previousCapture = DialogHost.Capture;
        var asked = new List<string>();
        var w = NewLifetimeWindow();
        DocumentSession? a = null, b = null;
        try
        {
            DialogHost.Capture = IxSaveAnswers(context, asked);
            a = IxOpen(w, IxPluginSong("Gated"), out _);
            b = IxOpen(w, DoSong(2, 8), out _);
            a.Path = Path.Combine(folder, "A" + extension); a.IsNew = false; a.MarkClean();
            b.Path = Path.Combine(folder, "B" + extension); b.IsNew = false; b.MarkClean();
            var failures = context.Failures.Count;

            // A: one edit, then Save.
            IxActivate(w, a);
            IxCursor(w, 0, 1, 0, 1);
            IxKey(w, Key.D3);
            var startHash = IxContentHash(a.Project);
            Check($"interactions: {label}: A is dirty with one undo entry after its edit", a.IsDirty && a.Undo.UndoCount == 1 && IxTabOf(w, a).DirtyVisibility == Visibility.Visible,
                $"dirty {a.IsDirty}, undo {a.Undo.UndoCount}, tab mark {IxTabOf(w, a).DirtyVisibility}");
            IxTrace("I-3 save start");
            IxCommand(w, "File.Save");
            IxTrace("I-3 save returned");
            var controller = IxController(w);
            var waiting = (LtField<System.Collections.IDictionary>(AudioEngineClient.Instance, "_stateRequests")!).Count;
            Check($"interactions: {label}: the save is held waiting for A's plug-in state (the writer is gated)", controller.IsSaving && waiting >= 1 && !File.Exists(a.Path), $"saving {controller.IsSaving}, requests {waiting}, file {File.Exists(a.Path)}");
            if (extension == ".gp") Check($"interactions: {label}: the save asked how to save a song with audio data, then what a clean .gp leaves out", asked.SequenceEqual(new[] { "audio data", "preflight" }) || asked.SequenceEqual(new[] { "audio data" }), string.Join(",", asked));

            // While it is held: B is displayed, a key typed in B reaches nothing, and A's tab cannot be closed.
            IxActivate(w, b);
            var undoB = b.Undo.UndoCount;
            IxCursor(w, 0, 1, 0, 1);
            IxKey(w, Key.D7);
            var swallowed = b.Undo.UndoCount == undoB && !b.IsDirty;
            Check($"interactions: {label}: while a save runs the window takes no keyboard input, so a note typed in B is swallowed (the gate is window-wide)", swallowed, $"B undo {b.Undo.UndoCount}, dirty {b.IsDirty}");
            var acceptedWhileSaving = b.Undo.UndoCount - undoB;
            LtCall(w, "CloseDocument", IxIndexOf(w, a));
            SettleLifetimeDispatcher();
            Check($"interactions: {label}: a close requested for A's tab during its save is refused: the tab stays open and unsaved until the save is done", w.OpenDocuments.Contains(a) && a.IsDirty && controller.IsSaving,
                $"open {w.OpenDocuments.Contains(a)}, dirty {a.IsDirty}, saving {controller.IsSaving}");
            Check($"interactions: {label}: B is displayed, and nothing was written for either song while the writer is held", ReferenceEquals(LtField<DocumentManager>(w, "_documents")!.Active, b) && !File.Exists(a.Path) && !File.Exists(b.Path));

            // Release the writer.
            IxAnswerStateRequests(1);
            var done = IxPumpUntil(() => !controller.IsSaving, 10000);
            SettleLifetimeDispatcher();
            Check($"interactions: {label}: releasing the writer finishes the save", done && !controller.IsSaving);
            Check($"interactions: {label}: A is written, clean, and its tab shows no unsaved mark", File.Exists(a.Path) && !a.HasUnsavedChanges && IxTabOf(w, a).DirtyVisibility == Visibility.Collapsed, $"exists {File.Exists(a.Path)}, unsaved {a.HasUnsavedChanges}");
            Check($"interactions: {label}: the song in memory is the song the save started with (the typed note in B did not touch A)", IxContentHash(a.Project) == startHash);
            if (extension == ".tforge")
                Check($"interactions: {label}: A's file holds exactly A's song as it was when the save started", IxContentHash(ProjectService.Load(a.Path!, InputLimits.MaxTforgeFileBytes)) == startHash);
            if (extension == ".gp")
            {
                var pair = AudioDataFile.PathFor(a.Path!);
                Check($"interactions: {label}: the companion audio-data file is written for A, and nothing is written for B", File.Exists(pair) && !File.Exists(b.Path) && !File.Exists(AudioDataFile.PathFor(b.Path!)), $"companion {File.Exists(pair)}, B {File.Exists(b.Path)}");
            }
            else Check($"interactions: {label}: B's file was not written", !File.Exists(b.Path));

            // Afterwards the gate is gone: B takes its edit, and A's tab closes now that it is saved.
            IxCursor(w, 0, 2, 0, 1);
            IxKey(w, Key.D7);
            Check($"interactions: {label}: after the save B takes input again: one undo entry per accepted edit, dirty, its tab marked", b.Undo.UndoCount == acceptedWhileSaving + 1 && b.IsDirty && IxTabOf(w, b).DirtyVisibility == Visibility.Visible, $"B undo {b.Undo.UndoCount}, dirty {b.IsDirty}");
            Check($"interactions: {label}: B's file is still not written", !File.Exists(b.Path));
            LtCall(w, "CloseDocument", IxIndexOf(w, a));
            SettleLifetimeDispatcher();
            Check($"interactions: {label}: A's tab closes once its save is done, B is displayed and keeps its edit", !w.OpenDocuments.Contains(a) && w.OpenDocuments.Contains(b) && ReferenceEquals(LtField<DocumentManager>(w, "_documents")!.Active, b) && b.IsDirty);
            Check($"interactions: {label}: the tab shows the document's file name and the window title the song's title with the unsaved mark", IxTabOf(w, b).Title == b.DisplayName && w.Title == $"TabForge - {b.Project.Title} *", $"tab '{IxTabOf(w, b).Title}', window '{w.Title}'");
            Check($"interactions: {label}: no unhandled exception", context.Failures.Count == failures, string.Join(" | ", context.Failures.Skip(failures).Take(2)));
        }
        finally
        {
            DialogHost.Capture = previousCapture;
            foreach (var dialog in context.Captured.ToArray()) { try { dialog.Close(); } catch (InvalidOperationException) { } }
            context.Captured.Clear();
            foreach (var document in w.OpenDocuments.ToList()) document.MarkClean();
            if (a is not null) IxRelease(a);
            if (b is not null) IxRelease(b);
            w.Close();
            SettleLifetimeDispatcher();
            DoCleanFolder(folder);
        }
    }

    // ---------- I-3: undo, save, reopen identity ----------

    /// <summary>
    /// What a clean .gp of <paramref name="song"/> loses that the export question does not list (the loss-coverage rules, applied to one song): empty when every measured
    /// difference is a listed feature, native-only audio data (which Export asks about) or a recorded exemption.
    /// </summary>
    private static List<string> IxUnexplainedGpLosses(SongProject song, string folder, string name)
    {
        var lost = LcMeasureLosses(song, folder, name);
        var report = GpExportPreflight.Analyze(song);
        var features = report.Losses.Select(l => l.Feature).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var group in lost.GroupBy(d => d.Category))
        {
            var rule = LcRuleFor(group.Key);
            var real = rule?.When is null ? group.ToList() : group.Where(rule.When).ToList();
            if (real.Count == 0) continue;
            if (rule is null) missing.Add($"{group.Key} (not classified: {real[0].Where} '{real[0].Expected}' -> '{real[0].Actual}')");
            else if (rule.Kind == LcKind.Feature && !features.Contains(rule.Text)) missing.Add($"{group.Key} (the preflight does not list '{rule.Text}')");
            else if (rule.Kind == LcKind.NativeAudio && !report.HasNativeOnlyAudioData) missing.Add($"{group.Key} (not reported as native-only audio data)");
            else if (rule.Kind == LcKind.Kept) missing.Add($"{group.Key} ({rule.Text} is written exactly, but differs)");
        }
        Log.Add($"  info  clean .gp of '{name}': {lost.Count} measured differences in {lost.Select(d => d.Category).Distinct().Count()} categories, {report.Losses.Count} listed features, {missing.Count} unexplained");
        return missing;
    }

    /// <summary>Six edits through the window's entry points: a note, a technique, a section move, a new track, a mixer volume drag, a tempo change.</summary>
    private static void IxSixEdits(MainWindow w, DocumentSession session)
    {
        var steps = session.Undo.UndoCount;
        IxCursor(w, 0, 1, 0, 1);
        IxKey(w, Key.D4);                                                       // 1: a note (bar 2, string 2)
        IxTrace("I-3 edit 1 done");
        IxCommand(w, "Note.PalmMute");                                          // 2: a technique on that note
        IxTrace("I-3 edit 2 done");
        LtCall(w, "MoveSection", 1, 3);                                         // 3: a section move (the drag's entry point)
        IxTrace("I-3 edit 3 done");
        IxCommand(w, "Track.Add");                                              // 4: a new track
        IxTrace("I-3 edit 4 done");
        var host = (IMixerHost)w;
        host.BeginMixerEdit();                                                  // 5: one mixer volume drag = one transaction
        for (var volume = 95; volume >= 70; volume -= 5) { session.Project.Tracks[0].Volume = volume; host.MixerChanged(false); }
        SettleLifetimeDispatcher();
        LtField<System.Windows.Controls.TextBox>(w, "TempoBox")!.Text = "150";   // 6: a tempo change typed in the tempo box
        LtCall(w, "ApplyTempo");
        IxTrace("I-3 edit 6 done");
        SettleLifetimeDispatcher();
        Check("interactions: I-3: the six edits are six undo steps", session.Undo.UndoCount - steps == 6, $"+{session.Undo.UndoCount - steps}");
    }

    // ---------- I-6: a section move with undo and redo across a save ----------

    /// <summary>What one bar holds across all tracks: signature, repeats and every beat's duration and notes.</summary>
    private static string IxBarContent(SongProject song, int bar) =>
        string.Join("|", song.Tracks.Select(t => bar < 0 || bar >= t.Measures.Count ? "-" : $"{t.Measures[bar].TimeSigNum}/{t.Measures[bar].TimeSigDenom}{(t.Measures[bar].RepeatStart ? "[" : "")}{(t.Measures[bar].RepeatEnd ? "]" : "")}:"
            + string.Join(",", t.Measures[bar].Cells.Select(c => $"{c.DurationDenominator}{(c.IsRest ? "r" : "")}{string.Join("+", c.Notes.Select(n => $"{n.StringIndex}.{n.Fret}"))}"))));

    private static string IxSectionOrder(SongProject song) => string.Join(",", SectionLayout.Sorted(song).Select(m => $"{m.Title}@{m.MeasureIndex}"));

    private static void IxSectionMoveCase(LifetimeContext context)
    {
        var folder = DoScratchFolder();
        var w = NewLifetimeWindow();
        DocumentSession? original = null, reopened = null;
        try
        {
            var song = IxDemoSong();
            var clip = new AudioClip { File = "", Name = "midi take", StartSec = 10, SourceLengthSec = 2, FileLengthSec = 2, Notes = new List<ClipNote> { new(0, 0.5, 60, 100) } };
            song.Tracks[1].AudioClips.Add(clip);
            song.IsDirty = false;
            original = IxOpen(w, song, out _);
            original.Path = Path.Combine(folder, "I6.tforge"); original.IsNew = false; original.MarkClean();
            var engine = original.Playback.Engine;
            LtCall(w, "ApplySpeed", 2.0);
            IxActivate(w, original);

            // Playing inside section 2 (the verse, bars 9-16), then the verse is moved before the intro.
            IxCursor(w, 0, 8, 0, 1);
            IxCommand(w, "Transport.PlayPause");
            void Tick() { LtCall(w, "ApplyPendingPlayhead"); }
            IxPumpUntil(() => engine.IsPlaying && original.Playback.PlayheadBar >= 9, 15000, Tick);
            var barBefore = original.Playback.PlayheadBar;
            var contentUnderPlayhead = IxBarContent(original.Project, barBefore);
            var before = IxContentHash(original.Project);
            var orderBefore = IxSectionOrder(original.Project);
            var (undo, revision) = (original.Undo.UndoCount, original.Project.TimelineRevision);
            var beforeKey = original.Undo.Snapshot(original.Project).Fingerprint;
            Check("interactions: I-6: the song plays inside section 2", engine.IsPlaying && barBefore is >= 9 and < 16, $"playhead bar {barBefore}");
            Check("interactions: I-6: the audio clip starts at its time in seconds", clip.StartSec == 10);

            original.SkipRanges.Add((11, 12));   // "skip this area" on two bars of the verse
            engine.SetSkipRanges(original.SkipRanges);
            LtCall(w, "MoveSection", 1, 0);
            var project = original.Project;
            Check("interactions: I-6: a skipped area follows its section when the section moves", original.SkipRanges.Count == 1 && original.SkipRanges[0] == (3, 4),
                $"skip ranges after the move: {string.Join(",", original.SkipRanges.Select(r => $"{r.Start}-{r.End}"))} (the verse's bars 11-12 are now 3-4)");
            // The mapping rule: a deleted bar leaves its range, a range whose bars end up apart becomes one range per run, bars that stay together stay one range.
            var mapped = Controllers.ArrangementController.RemapSkipRanges(new[] { (1, 4), (6, 6) }, new[] { 0, 5, -1, 3, 4, 8, 9 });
            Check("interactions: I-6: skipped ranges follow a bar mapping (a gone bar leaves, a split range becomes two, a run stays one)",
                mapped.SequenceEqual(new[] { (3, 5), (9, 9) }), string.Join(",", mapped.Select(r => $"{r.Start}-{r.End}")));
            Check("interactions: I-6: the move is one undo entry, dirty, one timeline invalidation", original.Undo.UndoCount == undo + 1 && original.IsDirty && project.TimelineRevision == revision + 1 && IxSectionOrder(project) != orderBefore,
                $"undo +{original.Undo.UndoCount - undo}, dirty {original.IsDirty}, revision +{project.TimelineRevision - revision}, order {IxSectionOrder(project)}");
            var remap = original.Playback.PlaybackBarRemap;
            var rememberedBefore = original.Playback.PlaybackBarMappingsBySnapshot.TryGetValue(beforeKey, out var savedMapping) ? savedMapping : null;
            Check("interactions: I-6: the bar mapping of the state before the move is remembered (the identity), and the live mapping now moves the verse's bars up by 8",
                rememberedBefore is not null && Enumerable.Range(0, 16).All(i => rememberedBefore[i] == i) && remap is not null && Enumerable.Range(8, 8).All(i => remap[i] == i - 8) && Enumerable.Range(0, 8).All(i => remap[i] == i + 8),
                $"remembered {(rememberedBefore is null ? "none" : string.Join(",", rememberedBefore.Take(16)))}; live {(remap is null ? "none" : string.Join(",", remap.Take(16)))}");
            Tick();
            Check("interactions: I-6: after the move the playhead is on the same musical content (the verse is now bars 1-8, so its bar number dropped by 8)",
                engine.IsPlaying && original.Playback.PlayheadBar >= barBefore - 8 && original.Playback.PlayheadBar <= barBefore - 8 + 1 && IxBarContent(project, barBefore - 8) == contentUnderPlayhead,
                $"playhead {barBefore} -> {original.Playback.PlayheadBar}, playing {engine.IsPlaying}");
            IxPump(300, Tick);
            Check("interactions: I-6: playback runs on after the move", engine.IsPlaying);
            Check("interactions: I-6: the audio clip keeps its time in seconds (it does not follow its section)", clip.StartSec == 10 && original.Project.Tracks[1].AudioClips[0].StartSec == 10);

            // Stop (the editor can be compared to a new one only without a playhead), then each step's render.
            IxCommand(w, "Transport.Stop");
            var afterMove = IxRenderIdentity(w, 3);
            Check("interactions: I-6: the editor draws the moved song like a new editor", afterMove.Identical, afterMove.Detail);

            // Save: the moved song is the saved song.
            var movedHash = IxContentHash(original.Project);
            IxCommand(w, "File.Save");
            Check("interactions: I-6: the save leaves the moved song clean, no unsaved mark", IxPumpUntil(() => !IxController(w).IsSaving, 10000) && !original.IsDirty && !original.HasUnsavedChanges && IxTabOf(w, original).DirtyVisibility == Visibility.Collapsed);

            // Undo: the song before the move, unsaved; redo: the saved song again, clean.
            IxCommand(w, "Edit.Undo");
            Check("interactions: I-6: undo restores the song as it was before the move and makes it unsaved", IxContentHash(original.Project) == before && IxSectionOrder(original.Project) == orderBefore && original.IsDirty && original.HasUnsavedChanges, $"order {IxSectionOrder(original.Project)}");
            Check("interactions: I-6: the audio clip is still at its time after the undo", original.Project.Tracks[1].AudioClips[0].StartSec == 10);
            Check("interactions: I-6: undo gives the skipped area back on the verse's bars", original.SkipRanges.Count == 1 && original.SkipRanges[0] == (11, 12), string.Join(",", original.SkipRanges.Select(r => $"{r.Start}-{r.End}")));
            var afterUndo = IxRenderIdentity(w, 3);
            Check("interactions: I-6: the editor draws the restored song like a new editor", afterUndo.Identical, afterUndo.Detail);
            IxCommand(w, "Edit.Redo");
            Check("interactions: I-6: redo is the saved moved song again: clean, no mark", IxContentHash(original.Project) == movedHash && !original.IsDirty && !original.HasUnsavedChanges && IxTabOf(w, original).DirtyVisibility == Visibility.Collapsed, $"dirty {original.IsDirty}");
            Check("interactions: I-6: redo moves the skipped area with the verse again", original.SkipRanges.Count == 1 && original.SkipRanges[0] == (3, 4), string.Join(",", original.SkipRanges.Select(r => $"{r.Start}-{r.End}")));
            var afterRedo = IxRenderIdentity(w, 3);
            Check("interactions: I-6: the editor draws the redone song like a new editor", afterRedo.Identical, afterRedo.Detail);

            // The file holds the moved song.
            var copy = Path.Combine(folder, "I6-reopen.tforge");
            File.Copy(original.Path!, copy);
            var tabs = w.OpenDocuments.Count;
            LtCall(w, "OpenScore", copy, false, false, null, false);
            SettleLifetimeDispatcher();
            reopened = w.OpenDocuments.Count == tabs + 1 ? w.OpenDocuments[^1] : null;
            Check("interactions: I-6: the reopened file is the moved song", reopened is not null && IxContentHash(reopened.Project) == movedHash && IxSectionOrder(reopened.Project) == IxSectionOrder(original.Project) && !reopened.IsDirty,
                reopened is null ? "no tab" : $"order {IxSectionOrder(reopened.Project)}");
        }
        finally
        {
            foreach (var document in w.OpenDocuments.ToList()) document.MarkClean();
            if (original is not null) { original.Playback.Engine.Stop(); IxRelease(original); }
            if (reopened is not null) IxRelease(reopened);
            w.Close();
            SettleLifetimeDispatcher();
            DoCleanFolder(folder);
        }
    }

    private static void IxUndoSaveReopenCase(LifetimeContext context)
    {
        IxUndoSaveReopen(context, ".tforge");
        IxUndoSaveReopen(context, ".gp");
    }

    private static void IxUndoSaveReopen(LifetimeContext context, string extension)
    {
        var label = $"I-3 ({extension})";
        var folder = DoScratchFolder();
        var w = NewLifetimeWindow();
        DocumentSession? original = null, reopened = null;
        try
        {
            // The demo song's bars are stress rhythms a .gp file cannot hold as they are, so the .gp run uses the dense song of the round-trip tests (it has three sections).
            var baseSong = extension == ".gp" ? RtDenseSong() : IxDemoSong();
            baseSong.IsDirty = false;
            original = IxOpen(w, baseSong, out _);
            original.Path = Path.Combine(folder, "I3" + extension); original.IsNew = false; original.MarkClean();
            IxSixEdits(w, original);
            IxCommand(w, "Edit.Undo"); IxCommand(w, "Edit.Undo");
            Check($"interactions: {label}: after two undos four edits are left", original.Undo.UndoCount == 4 && original.IsDirty, $"undo {original.Undo.UndoCount}, dirty {original.IsDirty}");
            var savedHash = IxContentHash(original.Project);
            var savedMidi = IxMidiHash(original.Project);
            IxTrace("I-3 save start");
            IxCommand(w, "File.Save");
            IxTrace("I-3 save returned");
            Check($"interactions: {label}: the save wrote the file and left the song clean", IxPumpUntil(() => !IxController(w).IsSaving, 10000) && File.Exists(original.Path) && !original.HasUnsavedChanges && !original.IsDirty, $"exists {File.Exists(original.Path)}, unsaved {original.HasUnsavedChanges}, messages: {string.Join(" | ", IxMessages)}");
            Check($"interactions: {label}: saving changed nothing in the song", IxContentHash(original.Project) == savedHash && original.Undo.UndoCount == 4);

            // Reopen: the file is opened through the window's open path as another tab (a copy at another path, so the open tab is not just activated).
            var copy = Path.Combine(folder, "I3-reopen" + extension);
            File.Copy(original.Path!, copy);
            var tabs = w.OpenDocuments.Count;
            IxTrace("I-3 reopen start");
            LtCall(w, "OpenScore", copy, false, false, null, false);
            IxTrace("I-3 reopen returned");
            SettleLifetimeDispatcher();
            reopened = w.OpenDocuments.Count == tabs + 1 ? w.OpenDocuments[^1] : null;
            // A .tforge reopens as the saved file; a .gp opens as an import with no native path yet (the next save asks where to write).
            Check($"interactions: {label}: the file reopens as a new tab ({(extension == ".gp" ? "an import with no save path" : "with its own path")})", reopened is not null && (extension == ".gp" ? reopened.Path is null : reopened.Path == copy));
            if (reopened is null) return;
            Check($"interactions: {label}: the reopened tab is not dirty and shows no unsaved mark", !reopened.IsDirty && !reopened.HasUnsavedChanges && IxTabOf(w, reopened).DirtyVisibility == Visibility.Collapsed);
            var sameState = original.Undo.Snapshot(original.Project).State.ContentEquals(reopened.Undo.Snapshot(reopened.Project).State);
            Check($"interactions: {label}: the reopened song has the original's content (ContentEquals and content hash)", sameState && IxContentHash(reopened.Project) == savedHash, $"state {sameState}, hash {IxContentHash(reopened.Project)} vs {savedHash}");
            Check($"interactions: {label}: the compiled MIDI events of the reopened song equal the original's", IxMidiHash(reopened.Project) == savedMidi);

            // Render identity: both tabs show the same editor drawing.
            IxTrace("I-3 identity");
            IxActivate(w, original);
            IxCursor(w, 0, 1, 0, 1);
            var drawnOriginal = IxEditorHashes(w, 1);
            IxActivate(w, reopened);
            IxCursor(w, 0, 1, 0, 1);   // the same track in both tabs: each tab keeps its own selected track
            var drawnReopened = IxEditorHashes(w, 1);
            var common = drawnOriginal.Keys.Intersect(drawnReopened.Keys).ToArray();
            Check($"interactions: {label}: the reopened tab draws what the original tab draws", common.Length > 0 && common.All(k => drawnOriginal[k] == drawnReopened[k]), $"systems compared {common.Length}, differing [{string.Join(",", common.Where(k => drawnOriginal[k] != drawnReopened[k]))}]");

            // Back in the original: one undo makes it unsaved, the redo makes it the saved content again.
            IxTrace("I-3 identity");
            IxActivate(w, original);
            IxCommand(w, "Edit.Undo");
            Check($"interactions: {label}: undoing one more step makes the saved song unsaved (dirty mark on its tab)", original.IsDirty && original.HasUnsavedChanges && IxTabOf(w, original).DirtyVisibility == Visibility.Visible, $"dirty {original.IsDirty}, unsaved {original.HasUnsavedChanges}");
            IxCommand(w, "Edit.Redo");
            Check($"interactions: {label}: redoing it is the saved content again: clean, no mark", !original.IsDirty && !original.HasUnsavedChanges && IxTabOf(w, original).DirtyVisibility == Visibility.Collapsed && IxContentHash(original.Project) == savedHash, $"dirty {original.IsDirty}, unsaved {original.HasUnsavedChanges}");

            if (extension == ".gp")
            {
                // The compatible export (no embedded project): what comes back differs only by what the preflight listed.
                IxTrace("I-3 losses start");
                var unexplained = IxUnexplainedGpLosses(original.Project, folder, "i3");
                IxTrace("I-3 losses done");
                Check($"interactions: {label}: a clean .gp of the song, reopened, differs from it only by what the export question lists", unexplained.Count == 0, string.Join(" | ", unexplained.Take(6)));
            }
        }
        finally
        {
            foreach (var document in w.OpenDocuments.ToList()) document.MarkClean();
            if (original is not null) IxRelease(original);
            if (reopened is not null) IxRelease(reopened);
            w.Close();
            SettleLifetimeDispatcher();
            DoCleanFolder(folder);
        }
    }
}
