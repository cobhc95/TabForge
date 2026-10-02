using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Interaction regression scenarios: real <see cref="MainWindow"/> instances driven through the entry points the interface uses (window key routing,
/// command ids, the window's own handlers), each asserting the song, the undo and dirty state, the tab marks, the playhead and the glitch counters.
/// Scenarios that expose a defect from the architecture traces report it through <see cref="IxKnown"/> (group "interactions-known": reported, never required).
/// </summary>
public static partial class SelfTest
{
    /// <summary>The error and information messages the windows raised during the scenarios (a message box would block a headless run; they are captured instead).</summary>
    private static readonly List<string> IxMessages = new();

    private static void TestInteractions() => RunInWindowFixture((a, context) =>
    {
        var previousMessages = DialogHost.MessageCapture;
        DialogHost.MessageCapture = (caption, text) => { IxMessages.Add($"{caption}: {text}"); IxTrace($"message {caption}: {text}"); };
        (IxKnownFailing, IxKnownPassing) = (0, 0);
        IxKnownIds.Clear();
        try { RunInteractionCases(context); }
        finally { DialogHost.MessageCapture = previousMessages; IxMessages.Clear(); }
        Log.Add($"  info  interactions-known: {IxKnownFailing} known defects still failing ({string.Join(", ", IxKnownIds)}), {IxKnownPassing} now passing; --require interactions-known makes the failing ones hard failures");
    });

    private static void RunInteractionCases(LifetimeContext context)
    {
        RunInteractionCase("I-1a editing during playback", () => IxEditDuringPlaybackCase(context));
        RunInteractionCase("I-2 switching documents during a save", () => IxSwitchDuringSaveCase(context));
        RunInteractionCase("I-3 undo, save, reopen identity", () => IxUndoSaveReopenCase(context));
        RunInteractionCase("I-4 tab transfer during playback", () => IxTabTransferCase(context));
        RunInteractionCase("I-5 plug-in change during playback", () => IxPluginChangeCase(context));
        RunInteractionCase("I-6 section move with undo and redo across a save", () => IxSectionMoveCase(context));
        RunInteractionCase("I-7 a tab switch keeps per-tab view state", () => IxViewStateCase(context));
        RunInteractionCase("I-8 a tab switch while recording", () => IxRecordingTabSwitchCase(context));
    }

    private static void RunInteractionCase(string name, Action body)
    {
        IxTrace($"case {name}");
        try { body(); }
        catch (Exception ex) { Check($"interactions: {name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message} at {string.Join(" <- ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()))}"); }
    }

    // ---------- I-1a: editing during playback ----------

    /// <summary>How often the song's compiled timeline had a note (track, bar, pitch) and how often the scheduler sent its note-on.</summary>
    private static (int InTimeline, int Heard) IxHeard(PlaybackEngine engine, int track, int bar, int midi)
    {
        var onsets = engine.Timeline?.Notes.Where(n => n.TrackIndex == track && n.Bar == bar && n.Midi == midi).Select(n => n.OnsetMs).ToArray() ?? Array.Empty<double>();
        var log = engine.DispatchLog;
        return (onsets.Length, onsets.Sum(onset => log.Count(r => r.IsNoteOn && r.TrackIndex == track && r.Data1 == midi && Math.Abs(r.StreamMs - onset) < 2)));
    }

    /// <summary>Dispatches of the same message at the same stream time (a note sent twice) and times that ran backwards (a rewind or restart), from the scheduler's log.</summary>
    private static (int Duplicates, int Rewinds) IxReplays(PlaybackEngine engine)
    {
        var log = engine.DispatchLog.Where(r => r.IsNoteOn).ToList();
        var duplicates = log.GroupBy(r => (r.TrackIndex, r.Data1, Math.Round(r.StreamMs / 2))).Count(g => g.Count() > 1);
        var rewinds = 0; var max = double.NegativeInfinity;
        foreach (var r in log) { if (r.StreamMs < max - 5) rewinds++; max = Math.Max(max, r.StreamMs); }
        return (duplicates, rewinds);
    }

    private static void IxEditDuringPlaybackCase(LifetimeContext context)
    {
        // With "advance after entering a note" on (the default) the entry moves the cursor on, and a cursor move while playing repositions playback.
        IxEditDuringPlayback(context, advance: true);
        // With it off, an edit ahead of the playhead leaves playback alone.
        IxEditDuringPlayback(context, advance: false);
        IxEditIsHeardAfterRestart(context);
    }

    /// <summary>
    /// I-1b: a note typed ahead of the playhead is spliced into the running timeline at the next bar boundary and heard once on this pass,
    /// without a seek and without any note sent twice; playing again from before it sounds it exactly once.
    /// </summary>
    private static void IxEditIsHeardAfterRestart(LifetimeContext context)
    {
        var w = NewLifetimeWindow();
        var song = IxDemoSong();
        var session = IxOpen(w, song, out _);
        var editor = IxEditor(w);
        var previousAdvance = editor.AutoAdvanceAfterEntry;
        try
        {
            editor.AutoAdvanceAfterEntry = false;
            LtCall(w, "ApplySpeed", 2.0);
            var engine = session.Playback.Engine;
            engine.StartDiagnostics();
            var pitch = song.Tracks[0].PitchOf(1, 5);
            IxCursor(w, 0, 0, 0, 1);
            IxCommand(w, "Transport.PlayFromStart");
            IxPumpUntil(() => IxPosition(session) >= 2.0, 15000);
            IxCursor(w, 0, 5, 0, 1);
            IxKey(w, Key.D5);
            Check("interactions: I-1a pin: the note was typed while the song played", session.Project.Tracks[0].Measures[5].Cells[0].Notes.Count == 1 && engine.IsPlaying);
            // Paste takes a different edit path than typing; it is heard from the next bar too (a later beat of the same bar; the paste question is answered up front: a dialog would block a headless run).
            var settings = (AppSettings)w.GetType().GetProperty("_settings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.GetValue(w)!;
            var previousAnswer = settings.Editing.PasteBeatsOntoNotes;
            settings.Editing.PasteBeatsOntoNotes = nameof(BeatsOntoNotesAnswer.Replace);
            var pasteBar = 5;
            var barCells = session.Project.Tracks[0].Measures[pasteBar].Cells;
            var pasteCell = Enumerable.Range(1, Math.Max(0, barCells.Count - 1)).FirstOrDefault(i => barCells.Skip(i).All(c => c.Notes.Count == 0));
            Check("interactions: I-1b: bar 6 ends in empty beats to paste into", pasteCell > 0, $"cell {pasteCell}");
            IxCursor(w, 0, 5, 0, 1);
            var copied = IxCommand(w, "Edit.Copy");
            IxCursor(w, 0, pasteBar, Math.Max(1, pasteCell), 1);
            var pasted = IxCommand(w, "Edit.Paste");
            settings.Editing.PasteBeatsOntoNotes = previousAnswer;
            Check("interactions: I-1b: the note was pasted into that beat while the song played", pasteCell > 0 && session.Project.Tracks[0].Measures[pasteBar].Cells[pasteCell].Notes.Count == 1, $"copy {copied}, paste {pasted}, status \"{LtField<System.Windows.Controls.TextBlock>(w, "StatusText")?.Text}\"");
            IxPumpUntil(() => IxPosition(session) >= Math.Max(6.9, pasteBar + 1.9) || !engine.IsPlaying, 15000);
            var pastedOnsets = engine.Timeline?.Notes.Where(n => n.TrackIndex == 0 && n.Bar == pasteBar && n.Cell == pasteCell && n.Midi == pitch).Select(n => n.OnsetMs).ToArray() ?? Array.Empty<double>();
            var pastedHeard = pastedOnsets.Sum(onset => engine.DispatchLog.Count(r => r.IsNoteOn && r.TrackIndex == 0 && r.Data1 == pitch && Math.Abs(r.StreamMs - onset) < 2));
            Check("interactions: I-1b: a note pasted ahead of the playhead during playback is heard on that pass", pastedHeard >= 1 && pastedHeard <= pastedOnsets.Length, $"in timeline {pastedOnsets.Length}x, heard {pastedHeard}x, playhead {IxPosition(session):0.00}");
            var onThisPass = IxHeard(engine, 0, 5, pitch);
            Check("interactions: I-1b: a note typed ahead of the playhead during playback joins the timeline at the next bar and is heard on that pass", onThisPass.Heard >= 1 && onThisPass.Heard <= onThisPass.InTimeline && engine.Playhead().Bar >= 5, $"in timeline {onThisPass.InTimeline}x, heard {onThisPass.Heard}x, playhead {IxPosition(session):0.00}");
            var (replayed, rewinds) = IxReplays(engine);
            Check("interactions: I-1b: the live splice sent no note twice and played no bar again", replayed == 0 && rewinds == 0, $"duplicates {replayed}, rewinds {rewinds}");
            IxCommand(w, "Transport.Stop");
            engine.StartDiagnostics();
            IxCursor(w, 0, 4, 0, 1);
            IxCommand(w, "Transport.PlayPause");
            var restarted = IxPumpUntil(() => IxPosition(session) < 5.5 && engine.IsPlaying, 30000);   // the new run has started (the old position was past bar 6); a loaded machine may take a while
            if (!restarted) Log.Add($"  info  I-1a pin: the new run had not started after 30 s (position {IxPosition(session):0.00}, playing {engine.IsPlaying})");
            IxPumpUntil(() => restarted && (IxHeard(engine, 0, 5, pitch).Heard >= 1 || IxPosition(session) >= 7.5 || !engine.IsPlaying), 30000);   // only the new run counts: the old position is already past bar 6
            IxPump(300);   // a second send of the same note would show now
            var afterRestart = IxHeard(engine, 0, 5, pitch);
            Check("interactions: I-1b: playing again from before the notes sounds each exactly once (the typed and the pasted one)", afterRestart.Heard == 2, $"in timeline {afterRestart.InTimeline}x, heard {afterRestart.Heard}x");
        }
        finally
        {
            editor.AutoAdvanceAfterEntry = previousAdvance;
            IxCommand(w, "Transport.Stop");
            IxRelease(session);
            foreach (var document in w.OpenDocuments) document.MarkClean();
            w.Close();
            SettleLifetimeDispatcher();
        }
    }

    private static void IxEditDuringPlayback(LifetimeContext context, bool advance)
    {
        var label = advance ? "I-1a (advance on)" : "I-1a (advance off)";
        var w = NewLifetimeWindow();
        var song = IxDemoSong();
        var session = IxOpen(w, song, out var output);
        var editor = IxEditor(w);
        var previousAdvance = editor.AutoAdvanceAfterEntry;
        try
        {
            editor.AutoAdvanceAfterEntry = advance;
            LtCall(w, "ApplySpeed", 2.0);
            var engine = session.Playback.Engine;
            engine.StartDiagnostics();
            var track = song.Tracks[0];
            var pitch = track.PitchOf(1, 5);
            Check($"interactions: {label}: fret 5 on string 2 is the string's tuning plus 5", pitch == track.StringTunings[1] + 5, $"pitch {pitch}, tuning {track.StringTunings[1]}");
            IxCursor(w, 0, 0, 0, 1);
            if (!advance)
            {
                var baseline = IxRenderIdentity(w, 5);
                Check($"interactions: {label}: before the scenario the editor draws what a new editor draws (the comparison itself is sound)", baseline.Identical, baseline.Detail);
            }
            IxCommand(w, "Transport.PlayFromStart");
            var trace = new IxPlayheadTrace(session);
            Check($"interactions: {label}: playback started from bar 1", engine.IsPlaying);
            IxPumpUntil(() => IxPosition(session) >= 2.0, 15000, trace.Sample);
            var glitches = IxGlitches.Take(context, session);
            var contentBefore = IxContentHash(song);
            Check($"interactions: {label}: bar 6 beat 1 is empty before the edit", track.Measures[5].Cells[0].Notes.Count == 0);

            // 1. a fret typed at bar 6 beat 1, ahead of the playhead.
            var (undo, revision) = (session.Undo.UndoCount, song.TimelineRevision);
            var positionAtEdit = IxPosition(session);
            IxCursor(w, 0, 5, 0, 1);
            IxKey(w, Key.D5);
            var typed = track.Measures[5].Cells[0].Notes.Count == 1 && track.Measures[5].Cells[0].Notes[0] is { StringIndex: 1, Fret: 5 } n && n.MidiValue == pitch;
            Check($"interactions: {label}: the typed fret is one note on string 2 with the right pitch", typed);
            Check($"interactions: {label}: typing a fret adds one undo entry, makes the song dirty and invalidates the timeline once",
                session.Undo.UndoCount == undo + 1 && session.IsDirty && song.TimelineRevision == revision + 1, $"undo +{session.Undo.UndoCount - undo}, dirty {session.IsDirty}, revision +{song.TimelineRevision - revision}");
            var jump = IxPosition(session) - positionAtEdit;
            if (advance)
            {
                IxKnown("A-advance-seek", "typing a note ahead of the playhead leaves playback where it is (no jump to the cursor)", jump < 1.0 && engine.IsPlaying, $"playhead {positionAtEdit:0.00} -> {IxPosition(session):0.00}, playing {engine.IsPlaying}");
                IxPump(200, trace.Sample);
                Log.Add($"  info  {label}: after the entry the playhead is at {IxPosition(session):0.00} (was {positionAtEdit:0.00}); {trace}");
                engine.Stop();
                return;
            }
            trace.Sample();

            // 2. palm mute on the beat under the playhead.
            var playing = engine.Playhead();
            var bar = playing.Bar;
            var cell = track.Measures[bar].Cells.FindIndex(c => c.Notes.Count > 0 && !c.Notes.Any(x => x.Techniques.Contains("PalmMute")));
            Check($"interactions: {label}: the bar under the playhead has a beat to mute", cell >= 0, $"bar {bar + 1}");
            IxCursor(w, 0, bar, Math.Max(0, cell), 1);
            (undo, revision) = (session.Undo.UndoCount, song.TimelineRevision);
            IxCommand(w, "Note.PalmMute");
            Check($"interactions: {label}: palm mute under the playhead adds one undo entry, keeps the song dirty and invalidates the timeline once",
                session.Undo.UndoCount == undo + 1 && session.IsDirty && song.TimelineRevision == revision + 1 && track.Measures[bar].Cells[Math.Max(0, cell)].Notes.Any(x => x.Techniques.Contains("PalmMute")),
                $"undo +{session.Undo.UndoCount - undo}, dirty {session.IsDirty}, revision +{song.TimelineRevision - revision}");
            IxPump(150, trace.Sample);

            // 3. one undo takes the palm mute back; the typed note stays.
            var projectBeforeUndo = session.Project;
            (undo, revision) = (session.Undo.UndoCount, projectBeforeUndo.TimelineRevision);
            IxCommand(w, "Edit.Undo");
            // An undo may restore into a new song object, whose revision counter starts over: then it must have been marked once, otherwise it advances by one.
            var revisionOk = ReferenceEquals(projectBeforeUndo, session.Project) ? session.Project.TimelineRevision == revision + 1 : session.Project.TimelineRevision == 1;
            Check($"interactions: {label}: undo removes one entry, keeps the song dirty and invalidates the timeline once",
                session.Undo.UndoCount == undo - 1 && session.IsDirty && revisionOk, $"undo {session.Undo.UndoCount - undo}, dirty {session.IsDirty}, same object {ReferenceEquals(projectBeforeUndo, session.Project)}, revision {revision} -> {session.Project.TimelineRevision}");
            var after = session.Project;
            Check($"interactions: {label}: after the undo the typed note is still there and the palm mute is gone",
                after.Tracks[0].Measures[5].Cells[0].Notes.Count == 1 && !after.Tracks[0].Measures[bar].Cells.Any(c => c.Notes.Any(x => x.Techniques.Contains("PalmMute"))));

            // 4. playing on to bar 8.
            IxPumpUntil(() => IxPosition(session) >= 7.5 || !engine.IsPlaying, 15000, trace.Sample);
            Check($"interactions: {label}: playback ran on to bar 8 without stopping", engine.IsPlaying && IxPosition(session) >= 7.5, $"playing {engine.IsPlaying}, at {IxPosition(session):0.00}");
            Check($"interactions: {label}: the playhead only moved forward, without jumps or stalls, across the edits and the undo", trace.Backward == 0 && trace.Jumps == 0 && trace.Stalls == 0 && trace.Samples >= 15, trace.ToString());
            var (replayed, rewinds) = IxReplays(engine);
            Check($"interactions: {label}: no note of an already played bar was sent again", replayed == 0 && rewinds == 0, $"duplicates {replayed}, rewinds {rewinds}");
            var heard = IxHeard(engine, 0, 5, pitch);
            Log.Add($"  info  {label}: the bar-6 note is in the running timeline {heard.InTimeline}x and was sent {heard.Heard}x (playhead {IxPosition(session):0.00})");
            Log.Add($"  info  {label}: timeline notes of that pitch on track 1: {string.Join("; ", engine.Timeline!.Notes.Where(n => n.TrackIndex == 0 && n.Midi == pitch).Select(n => $"bar {n.Bar} cell {n.Cell} onset {n.OnsetMs:0}"))}");
            Log.Add($"  info  {label}: sent: {string.Join("; ", engine.DispatchLog.Where(r => r.IsNoteOn && r.TrackIndex == 0 && r.Data1 == pitch).Select(r => $"stream {r.StreamMs:0}"))}");
            Check($"interactions: {label}: the glitch counters are unchanged across the scenario", IxGlitches.Take(context, session) == glitches, $"{IxGlitches.Take(context, session)} vs {glitches}");

            // 5. the song as saved is the song as edited: stop, and the editor draws what a new editor draws for it.
            IxCommand(w, "Transport.Stop");
            Check($"interactions: {label}: the song after the scenario differs from the song at the start (the typed note is in it)", IxContentHash(session.Project) != contentBefore);
            var render = IxRenderIdentity(w, 5);
            Check($"interactions: {label}: the editor draws the same as a new editor for the edited song (no stale layout)", render.Identical, render.Detail);
        }
        finally
        {
            editor.AutoAdvanceAfterEntry = previousAdvance;
            IxCommand(w, "Transport.Stop");
            IxRelease(session);
            foreach (var document in w.OpenDocuments) document.MarkClean();
            w.Close();
            SettleLifetimeDispatcher();
        }
    }
}
