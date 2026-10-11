using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: pasting beats keeps them in their bar: notes that do not fit leave the bar overfull (red), with no tie and nothing moved into the next bar; a bar-range
//     clipboard still fills consecutive bars; a paste aimed at a selection overwrites the selected beats only, the whole bar, or inserts; undo and redo restore exactly.
// Does not own: the placement (BarLocalPaste, EditCommands.PasteBeats), the input helpers (WorkflowKit.cs).
// Tests: TestPasteIntoBar (--areas workflow).
public static partial class SelfTest
{
    private sealed class PbAsker : IPasteQuestionAsker
    {
        public BeatsOntoNotesAnswer Beats = BeatsOntoNotesAnswer.Replace;
        public List<PasteQuestion> Asked = new();
        public PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions) { Asked = questions.ToList(); return new PasteAnswers(BeatsOntoNotes: Beats); }
    }

    private static void TestPasteIntoBar()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;
        try
        {
            // Three bars of eighths: bar 1 is 0 0 0 2 0 0 0 2, bars 2 and 3 count down from 9 and 8.
            Wf Fresh(string name)
            {
                var s = GnOpenNew(w, name);
                s.Tool("duration:eighth");
                var rows = new[] { new[] { 0, 0, 0, 2, 0, 0, 0, 2 }, new[] { 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 8, 7, 6, 5, 4, 3, 2, 1 } };
                for (var b = 0; b < 3; b++) { s.Click(b, 0, 0); foreach (var f in rows[b]) { s.Fret(f); s.Key(Key.Right); } }
                return s;
            }
            string Hash(Wf s) => Convert.ToHexString(ProjectService.ContentHash(s.Song));
            string Frets(Wf s, int bar) => string.Join(" ", s.Beats(bar).Select(i => s.Cell(bar, i)).Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].Fret));
            bool Red(Wf s, int bar) => MusicTime.AnalyzeBar(s.Song, bar, s.Track).Error;
            bool AnyTie(Wf s) => s.Track.Measures.SelectMany(m => m.Cells).Any(c => c.Notes.Any(n => n.Tied));
            PasteOutcome Paste(Wf s, PbAsker asker)
            {
                var clip = ClipboardService.Shared.TryGetClip(out _)!;
                var outcome = EditCommands.RunPaste(s.Doc, clip, s.Ed.PasteTarget, new EditingSettings(), asker);
                if (outcome.Changed) { s.Ed.NotifyEdited(); if (outcome.CursorBar >= 0) s.Ed.SelectForEdit(outcome.CursorBar, outcome.CursorCell, s.Ed.SelectedString); }
                SmSettle();
                return outcome;
            }
            void UndoRedo(Wf s, string before, string after)
            {
                s.Ctrl(Key.Z);
                s.Expect("Undo restores the song exactly", Hash(s) == before, "undo", $"{Hash(s)[..8]} vs {before[..8]}");
                s.Ctrl(Key.Y);
                s.Expect("Redo gives the pasted song back exactly", Hash(s) == after, "redo", $"{Hash(s)[..8]} vs {after[..8]}");
            }

            // 1. Four notes that do not fit the bar: they stay in it, the bar is red, no tie, the bar after is untouched.
            var s1 = Fresh("few notes overflow");
            s1.Click(0, 0, 0); s1.Repeat(Key.Right, 3, ModifierKeys.Shift); s1.Ctrl(Key.C);
            s1.Click(1, 12, 0);
            var bars = s1.Track.Measures.Count; var bar3 = Frets(s1, 2); var h = Hash(s1);
            s1.Begin("copy 4 notes, paste at bar 2 beat 7");
            var o1 = Paste(s1, new PbAsker());
            s1.Expect("the notes stay in bar 2, which is red (overfull)", o1.Changed && Red(s1, 1) && s1.Track.Measures.Count == bars && Frets(s1, 1) == "9 8 7 6 5 4 0 0 0 2 3 2", "red bar, no split", $"{o1.Status}; bar 2 {Frets(s1, 1)}");
            s1.Expect("no tie was added and bar 3 is untouched", !AnyTie(s1) && Frets(s1, 2) == bar3, "no tie, next bar kept", s1.Dump(1));
            s1.Expect("the status says the bar is over full", o1.Status.Contains("over full", StringComparison.Ordinal), "status", o1.Status);
            UndoRedo(s1, h, Hash(s1));

            // 2. A bar-range clipboard still fills consecutive bars: two bars pasted onto the last bar and the one added after it, no red bar, no tie.
            var s2 = Fresh("two bars pasted");
            ClipboardService.Shared.Copy(ClipboardService.CaptureBars(s2.Song, new[] { 0 }, 0, 1));   // the bar-range clipboard of a timeline bar selection
            var kind = ClipboardService.Shared.TryGetClip(out _)?.Kind;
            s2.Click(2, 0, 0);
            s2.Begin("copy bars 1-2, paste at bar 3");
            var o2 = Paste(s2, new PbAsker());
            s2.Expect("the clipboard holds bars", kind == ScoreClipKind.Bars, "bar range", $"{kind}");
            s2.Expect("bars 3 and 4 are the copies, none red, no tie", Frets(s2, 2) == Frets(s2, 0) && Frets(s2, 3) == Frets(s2, 1) && !Red(s2, 2) && !Red(s2, 3) && !AnyTie(s2), "clean bars", $"{o2.Status}; {Frets(s2, 2)} | {Frets(s2, 3)}");

            // 3. A paste aimed at a selection: 8 beats copied, 4 beats selected in bar 2.
            foreach (var (answer, expected, red, note) in new (BeatsOntoNotesAnswer, string, bool, string)[]
            {
                (BeatsOntoNotesAnswer.Replace, "0 0 0 2 0 0 0 2 5 4 3 2", true, "selection only: the rest of the bar stays after the longer clip"),
                (BeatsOntoNotesAnswer.ReplaceBar, "0 0 0 2 0 0 0 2", false, "whole bar replaced"),
                (BeatsOntoNotesAnswer.Insert, "0 0 0 2 0 0 0 2 9 8 7 6 5 4 3 2", true, "inserted before the selection"),
            })
            {
                var s3 = Fresh("selection " + answer);
                s3.Click(0, 0, 0); s3.Repeat(Key.Right, 7, ModifierKeys.Shift); s3.Ctrl(Key.C);
                s3.Click(1, 0, 0); s3.Ed.ShiftClickExtend(1, 6, 0); SmSettle();
                var before = Hash(s3);
                s3.Begin($"paste onto 4 selected beats of bar 2: {note}");
                var asker = new PbAsker { Beats = answer };
                var o3 = Paste(s3, asker);
                s3.Expect("the prompt was asked", asker.Asked.Contains(PasteQuestion.BeatsOntoNotes), "overwrite question", string.Join(",", asker.Asked));
                s3.Expect("bar 2 holds the expected beats", Frets(s3, 1) == expected && Red(s3, 1) == red && !AnyTie(s3), note, $"{Frets(s3, 1)} red {Red(s3, 1)}; {o3.Status}");
                s3.Expect("bar 3 is untouched, the cursor is in bar 2 and nothing is selected", Frets(s3, 2) == "8 7 6 5 4 3 2 1" && s3.Ed.SelectedMeasure == 1 && !s3.Ed.HasSelection, "cursor in the selection's bar", s3.Cursor);
                if (red) s3.Expect("the status says the bar is over full", o3.Status.Contains("over full", StringComparison.Ordinal), "status", o3.Status);
                UndoRedo(s3, before, Hash(s3));
            }

            // 4. No selection: the cursor beat is the start and the clip overwrites the span it covers, as before.
            var s4 = Fresh("no selection");
            s4.Click(0, 0, 0); s4.Repeat(Key.Right, 7, ModifierKeys.Shift); s4.Ctrl(Key.C);
            s4.Click(1, 0, 0);
            s4.Begin("paste with the cursor on bar 2 beat 1");
            var asker4 = new PbAsker();
            Paste(s4, asker4);
            s4.Expect("the beats go in before the cursor beat as before (the bar is red), nothing was asked about a selection", Frets(s4, 1) == "0 0 0 2 0 0 0 2 9 8 7 6 5 4 3 2" && Red(s4, 1) && !asker4.Asked.Contains(PasteQuestion.BeatsOntoNotes), "paste at the cursor", Frets(s4, 1));

            // 5. A selection over two bars: the selected beats of both go, the clip lands at the start.
            var s5 = Fresh("selection over two bars");
            s5.Click(0, 0, 0); s5.Repeat(Key.Right, 1, ModifierKeys.Shift); s5.Ctrl(Key.C);   // two beats: 0 0
            s5.Click(1, 12, 0); s5.Ed.ShiftClickExtend(2, 2, 0); SmSettle();
            s5.Begin("paste 2 beats onto bar 2 beat 7 .. bar 3 beat 2");
            Paste(s5, new PbAsker());
            s5.Expect("bar 2 lost its last two beats, bar 3 its first two, the clip is in bar 2", Frets(s5, 1) == "9 8 7 6 5 4 0 0" && Frets(s5, 2) == "6 5 4 3 2 1", "selected beats of both bars replaced", $"{Frets(s5, 1)} | {Frets(s5, 2)}");
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }
}
