using System.Linq;
using System.Windows.Input;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the "workflow" area: little stories of a guitarist writing and editing a part, driven through the window's key routing,
//     tool-palette handlers and the editor's click path, checked after each step against what the user sees (model, red bars,
//     drawn items, cursor in view) and hears (scheduled note-ons), with the GP5 behaviour as the reference in each check.
// Does not own: product fixes (a failing check is a bug report), the input helpers (WorkflowKit.cs).
// Tests: TestWorkflow (--areas workflow).
public static partial class SelfTest
{
    private static void TestWorkflow()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;   // a paste question takes its defaults; any other dialog is cancelled
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("W1 eighth riff, change one duration, undo, redo", WfRiffUndo),
                ("W2 chord, delete one note", WfChordDelete),
                ("W3 dot, un-dot, triplet, un-triplet", WfDotsAndTriplets),
                ("W4 chord tied into the next beat, then untied", WfChordTie),
                ("W5 full bar, delete a beat, insert a rest", WfBarFill),
                ("W6 select one chord, copy, paste twice, delete a paste", WfCopyPasteChord),
                ("W7 accent, palm mute, vibrato on and off", WfMarkToggles),
                ("W8 Right at the song end keeps adding bars", WfRightAtEnd),
                ("W9 typing and techniques on an empty string", WfEmptyString),
                ("W10 track switch mid-edit keeps the cursor in view", WfTrackSwitch),
                ("W11 hammer-on and slide phrase", WfLegatoPhrase),
                ("W12 cut and paste across bars, undo back to empty", WfCutPasteUndoAll),
                ("W13 tie a single note across the barline", WfTieAcrossBar),
                ("W14 Backspace and Delete on a chord note", WfBackspaceChord),
                ("W15 retyping a fret never goes past fret 24", WfRetypeFret),
                ("W16 plain navigation and typing end a selection; Backspace and paste act on the cursor", WfStaleSelection),
                ("W17 two-digit fret on the fifth beat of an overfull bar, Ctrl+Right", WfOverfullBar),
                ("W18 Up and Down wrap within the beat", WfStringWrap),
                ("W19 delete a bar while a selection is up, then draw", WfDeleteBarWithSelection),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    // ---------- W1 ----------
    // Type a bar of eighths (5 7 5 7 ...), lengthen the third note with "-", undo, redo.
    private static void WfRiffUndo(MainWindow w)
    {
        var s = WfOpen(w, "W1 riff"); s.ToString_(2);
        s.Begin("type eight eighths");
        s.Tool("duration:eighth");
        for (var i = 0; i < 8; i++) { s.Fret(i % 2 == 0 ? 5 : 7); if (i < 7) s.Key(Key.Right); }
        s.Expect("bar 1 holds eight eighths and is not red", s.Beats(0).Count == 8 && s.Beats(0).All(c => s.Cell(0, c).DurationDenominator == 8) && !s.State(0).Marked,
            "a full bar of eighths is black", s.Dump(0));
        s.Begin("go back to the third note and press - (longer)");
        s.Key(Key.Home); s.Repeat(Key.Right, 2);
        var third = s.Ed.SelectedCell;
        s.Expect("Home + Right Right lands on the third note (slot 4)", third == 4, "arrows step beat by beat", s.Cursor);
        s.Key(Key.OemMinus);
        s.Expect("the third note became a quarter, no note lost", s.Cell(0, third).DurationDenominator == 4 && s.Bar(0).Cells.Sum(c => c.Notes.Count) == 8,
            "only that beat changes length; the bar turns red as overfull", s.Dump(0));
        s.Expect("the overfull bar is marked red", s.State(0).Marked, "an overfull bar is drawn red", s.Dump(0));
        s.Begin("Ctrl+Z");
        s.Ctrl(Key.Z);
        s.Expect("undo restores the eighth and the clean bar", s.Cell(0, third).DurationDenominator == 8 && !s.State(0).Marked, "undo restores exactly", s.Dump(0));
        s.Begin("Ctrl+Y");
        s.Ctrl(Key.Y);
        s.Expect("redo brings the quarter back", s.Cell(0, third).DurationDenominator == 4, "redo repeats the edit", s.Dump(0));
        s.Expect("the cursor bar is in view", s.CursorInView(out var v), "the cursor never leaves the screen", v);
    }

    // ---------- W2 ----------
    // An open-ish chord on strings 1-3, Delete on the middle note.
    private static void WfChordDelete(MainWindow w)
    {
        var s = WfOpen(w, "W2 chord delete");
        s.Begin("type a chord 3/3/0 on strings 1..3");
        s.Fret(3); s.Key(Key.Down); s.Fret(3); s.Key(Key.Down); s.Fret(0);
        s.Expect("one beat with three notes", s.Cell(0, 0).Notes.Count == 3, "digits on other strings stack a chord", s.Dump(0));
        s.Begin("Up to string 2, Delete");
        s.Key(Key.Up); s.Key(Key.Delete);
        s.Expect("only the string-2 note is gone", s.Cell(0, 0).Notes.Count == 2 && s.NoteAt(0, 0, 1) is null && s.NoteAt(0, 0, 0) is not null && s.NoteAt(0, 0, 2) is not null,
            "Delete removes the note under the cursor only", s.Dump(0));
        s.Expect("the beat keeps its place and length", s.Beats(0).FirstOrDefault() == 0 && s.Cell(0, 0).DurationDenominator == 4, "the beat is unchanged", s.Dump(0));
        s.Begin("Ctrl+Z");
        s.Ctrl(Key.Z);
        s.Expect("undo brings the note back", s.Cell(0, 0).Notes.Count == 3, "undo restores the chord", s.Dump(0));
    }

    // ---------- W3 ----------
    private static void WfDotsAndTriplets(MainWindow w)
    {
        var s = WfOpen(w, "W3 dots and triplets");
        s.Begin("type a quarter, press . (dot)");
        s.Fret(5); s.Key(Key.OemPeriod);
        s.Expect("the note is dotted", s.Cell(0, 0).Dots == 1, "'.' toggles the dot", s.Dump(0));
        s.Begin("press . again");
        s.Key(Key.OemPeriod);
        s.Expect("the dot is removed", s.Cell(0, 0).Dots == 0, "'.' again removes it", s.Dump(0));
        s.Begin("click the dotted tool twice");
        s.Tool("duration:dotted");
        var on = s.Cell(0, 0).Dots == 1;
        s.Tool("duration:dotted");
        s.Expect("the palette dot button toggles on then off", on && s.Cell(0, 0).Dots == 0, "the toolbar dot is a toggle", s.Dump(0));
        s.Begin("press / (triplet) twice");
        s.Key(Key.OemQuestion);
        var trip = s.Cell(0, 0).IsTriplet || s.Cell(0, 0).Tuplet.Numerator == 3;
        s.Key(Key.OemQuestion);
        s.Expect("triplet on, then off", trip && !s.Cell(0, 0).IsTriplet && s.Cell(0, 0).Tuplet.Numerator == 0, "the triplet key is a toggle", s.Dump(0));
        s.Expect("the note is still a plain quarter 5", s.Cell(0, 0).DurationDenominator == 4 && s.NoteAt(0, 0, 0)?.Fret == 5, "toggles leave the note", s.Dump(0));
    }

    // ---------- W4 ----------
    // Beat 1: chord 5/5 on strings 2-3. Beat 2: L on string 2 (tie). Beats 3-4: 7 7. Then L again removes the tied note (GP5).
    private static void WfChordTie(MainWindow w)
    {
        var s = WfOpen(w, "W4 chord tie");
        s.ToString_(1);
        s.Begin("chord 5/5 on strings 2-3");
        s.Fret(5); s.Key(Key.Down); s.Fret(5); s.Key(Key.Up);
        var drawnBefore = s.Drawn(0);
        s.Begin("Right, L (tie) on string 2");
        s.Key(Key.Right); var beat2 = s.Ed.SelectedCell; s.Key(Key.L);
        var tied = s.NoteAt(0, beat2, 1);
        s.Expect("beat 2 has a tied 5 on string 2", tied is { Fret: 5 } && (tied.Tied || s.Cell(0, beat2).IsTied), "L on the next beat writes a tied copy of the note", s.Dump(0));
        s.Begin("fill beats 3-4 with 7 7");
        s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Fret(7);
        s.Expect("the bar is complete and not red", !s.State(0).Marked && s.State(0).Complete, "four quarters fill 4/4", s.Dump(0));
        var drawn = s.Drawn(0);
        // The tie arc replaces the quarter-rest glyph (also a curve), so the curve count holds while the layout gains the arc.
        s.Expect("the tie is drawn (an arc in the bar)", drawn[LayoutAudit.Kind.Curve] >= drawnBefore[LayoutAudit.Kind.Curve] && s.Ed.Layout.CachedStaffLayout(0, 0)?.Ties.Any(t => !t.IsStub) == true, "a tie arc joins the two notes",
            $"curves {drawnBefore[LayoutAudit.Kind.Curve]} -> {drawn[LayoutAudit.Kind.Curve]}");
        s.Expect("the tied beat's notehead is drawn", drawn[LayoutAudit.Kind.Head] >= drawnBefore[LayoutAudit.Kind.Head] + 3, "every beat has its notehead",
            $"heads {drawnBefore[LayoutAudit.Kind.Head]} -> {drawn[LayoutAudit.Kind.Head]}");
        s.Expect("playback sustains: one attack of the string-2 5", s.Onsets(0, 1, 5) == 1, "a tied note is not struck again", $"onsets {s.Onsets(0, 1, 5)}");
        s.Begin("back to beat 2, L again (removes the tied note)");
        s.Click(0, beat2, 1); s.Key(Key.L);
        s.Expect("the tied note is gone", s.NoteAt(0, beat2, 1) is null && !s.Cell(0, beat2).IsTied, "L again removes the tied note (ties are per note)", s.Dump(0));
        s.Expect("playback attacks once", s.Onsets(0, 1, 5) == 1, "only the chord strikes the 5", $"onsets {s.Onsets(0, 1, 5)}");
    }

    // ---------- W5 ----------
    private static void WfBarFill(MainWindow w)
    {
        var s = WfOpen(w, "W5 bar fill");
        s.Begin("type four quarters 0 2 3 5");
        foreach (var f in new[] { 0, 2, 3 }) { s.Fret(f); s.Key(Key.Right); }
        s.Fret(5);
        s.Expect("the bar is complete, not red", s.State(0).Complete && !s.State(0).Marked, "a full bar is black", s.Dump(0));
        s.Begin("on beat 3, Delete (clears the note)");
        s.Key(Key.Left);
        s.Key(Key.Delete);
        s.Expect("beat 3 is a rest and the bar still not red", s.Cell(0, 8).Notes.Count == 0 && s.Beats(0).Contains(8) && !s.State(0).Marked,
            "deleting a note leaves a rest; the bar stays full", s.Dump(0));
        s.Begin("Delete again (removes the empty beat)");
        s.Key(Key.Delete);
        // The editor refills a bar with rests after a delete (Preferences: fill bars with rests); either way red must match the fill.
        s.Expect("the 5 moved left and red matches the fill", s.NoteAt(0, 8, 0)?.Fret == 5 && s.State(0).Marked == !s.State(0).Complete,
            "the beat closes up; a short bar is drawn red, a refilled one is not", s.Dump(0));
        s.Begin("Insert on beat 1 (insert beat)");
        s.Click(0, 0, 0); s.Key(Key.Insert);
        s.Expect("the notes keep their order and red matches the fill", s.Bar(0).Cells.SelectMany(c => c.Notes).Select(n => n.Fret).SequenceEqual(new[] { 0, 2, 5 }) && s.State(0).Marked == !s.State(0).Complete,
            "insert beat pushes the bar right", s.Dump(0));
    }

    // ---------- W6 ----------
    private static void WfCopyPasteChord(MainWindow w)
    {
        var s = WfOpen(w, "W6 copy chord");
        s.Begin("chord 2/3/2 on strings 1..3");
        s.Fret(2); s.Key(Key.Down); s.Fret(3); s.Key(Key.Down); s.Fret(2);
        s.Begin("select just that chord (click, then extend inside the same beat)");
        s.Click(0, 0, 1);
        var extended = s.Ed.ShiftClickExtend(0, 0, 1); SmSettle();
        s.Expect("one beat is selected", extended && s.Ed.HasSelection, "a drag inside one beat selects that beat", s.Cursor);
        s.Begin("Ctrl+C, paste on bar 3 and bar 4");
        s.Ctrl(Key.C);
        s.Click(2, 0, 0); s.Ctrl(Key.V);
        s.Click(3, 0, 0); s.Ctrl(Key.V);
        s.Expect("both bars start with the 3-note chord", s.Cell(2, 0).Notes.Count == 3 && s.Cell(3, 0).Notes.Count == 3, "paste puts the beat at the cursor", s.Dump(2) + " | " + s.Dump(3));
        s.Expect("the paste adds no bars and leaves bar 3 black", s.Track.Measures.Count == 4 && !s.State(2).Marked && !s.State(3).Marked, "pasting one beat keeps the bar valid", s.Dump(2));
        s.Begin("select the second paste and Delete");
        s.Click(3, 0, 0); s.Ed.ShiftClickExtend(3, 0, 2); SmSettle();
        s.Key(Key.Delete);
        s.Expect("bar 4's chord is gone, bar 3's stays", s.Cell(3, 0).Notes.Count == 0 && s.Cell(2, 0).Notes.Count == 3, "Delete on a selection clears it only", s.Dump(2) + " | " + s.Dump(3));
    }

    // ---------- W7 ----------
    private static void WfMarkToggles(MainWindow w)
    {
        var s = WfOpen(w, "W7 marks");
        s.Fret(5);
        TabNote N() => s.NoteAt(0, 0, 0)!;
        s.Begin("accent key twice");
        s.Key(Key.Oem1); var a = s.Cell(0, 0).Accent; s.Key(Key.Oem1);
        s.Expect("accent on, then off", a == 1 && s.Cell(0, 0).Accent == 0, "pressing again removes it", $"{a} -> {s.Cell(0, 0).Accent}");
        s.Begin("accent tool twice");
        s.Tool("effect:accent"); a = s.Cell(0, 0).Accent; s.Tool("effect:accent");
        s.Expect("accent tool on, then off", a == 1 && s.Cell(0, 0).Accent == 0, "toolbar buttons toggle", $"{a} -> {s.Cell(0, 0).Accent}");
        s.Begin("P (palm mute) twice");
        s.Key(Key.P); var pm = N().Techniques.Contains(TechniqueNames.PalmMute); s.Key(Key.P);
        s.Expect("palm mute on, then off", pm && !N().Techniques.Contains(TechniqueNames.PalmMute), "pressing again removes it", string.Join(",", N().Techniques));
        s.Begin("V (vibrato) twice");
        s.Key(Key.V); var vib = N().Techniques.Contains(TechniqueNames.Vibrato); s.Key(Key.V);
        s.Expect("vibrato on, then off", vib && !N().Techniques.Contains(TechniqueNames.Vibrato), "pressing again removes it", string.Join(",", N().Techniques));
        s.Begin("palm mute tool twice");
        s.Tool("effect:palm_mute"); pm = N().Techniques.Contains(TechniqueNames.PalmMute); s.Tool("effect:palm_mute");
        s.Expect("palm mute tool on, then off", pm && !N().Techniques.Contains(TechniqueNames.PalmMute), "toolbar buttons toggle", string.Join(",", N().Techniques));
        s.Expect("the note itself is untouched", N().Fret == 5 && s.Cell(0, 0).Notes.Count == 1, "marks never change the fret", s.Dump(0));
    }

    // ---------- W8 ----------
    private static void WfRightAtEnd(MainWindow w)
    {
        var s = WfOpen(w, "W8 right at end", bars: 2);
        s.Begin("Ctrl+End, type a whole bar of quarters");
        s.Ctrl(Key.End);
        s.Expect("Ctrl+End goes to the last bar", s.Ed.SelectedMeasure == 1, "Ctrl+End jumps to the last bar", s.Cursor);
        for (var i = 0; i < 4; i++) { s.Fret(3); s.Key(Key.Right); }
        s.Expect("Right after the last beat adds bar 3 and moves into it", s.Track.Measures.Count == 3 && s.Ed.SelectedMeasure == 2, "Right at the end appends a bar", $"{s.Track.Measures.Count} bars, {s.Cursor}");
        s.Begin("keep typing and pressing Right for two more bars");
        for (var i = 0; i < 8; i++) { s.Fret(5); s.Key(Key.Right); }
        s.Expect("bars 3 and 4 are full and bar 5 exists", s.Track.Measures.Count == 5 && s.Beats(2).Count == 4 && s.Beats(3).Count == 4 && !s.State(2).Marked && !s.State(3).Marked,
            "typing flows on bar after bar", $"{s.Track.Measures.Count} bars; {s.Dump(2)} | {(s.Track.Measures.Count > 3 ? s.Dump(3) : "")}");
        s.Expect("every track got the new bars", s.Song.Tracks.All(t => t.Measures.Count == s.Track.Measures.Count), "bars are song-wide", "");
        s.Begin("Right on an empty last bar");
        var bars = s.Track.Measures.Count;
        s.Key(Key.Right);
        s.Expect("Right on an empty last beat does not add empty bars forever (moves or adds one)", s.Track.Measures.Count <= bars + 1, "an empty bar is added only once you have written into the last one", $"{bars} -> {s.Track.Measures.Count}");
        s.Expect("the cursor bar is in view", s.CursorInView(out var v), "the score follows the cursor", v);
    }

    // ---------- W9 ----------
    private static void WfEmptyString(MainWindow w)
    {
        var s = WfOpen(w, "W9 empty string");
        s.Fret(5);
        s.Begin("Down to an empty string 4, H (hammer-on)");
        s.ToString_(3); s.Key(Key.H);
        s.Expect("no phantom note is created on string 4", s.NoteAt(0, 0, 3) is null && s.Cell(0, 0).Notes.Count == 1, "an effect on an empty string adds no note", s.Dump(0));
        s.Begin("type 7 on string 4");
        s.Fret(7);
        s.Expect("the beat becomes a two-note chord, same length", s.Cell(0, 0).Notes.Count == 2 && s.NoteAt(0, 0, 3)?.Fret == 7 && s.Cell(0, 0).DurationDenominator == 4, "a digit on another string adds to the chord", s.Dump(0));
        s.Begin("Right to an empty beat, P on an empty string");
        s.Key(Key.Right); var cell = s.Ed.SelectedCell; s.Key(Key.P);
        s.Expect("no note and no red bar appear", (cell >= s.Bar(0).Cells.Count || s.Cell(0, cell).Notes.Count == 0) && !s.State(0).Error, "an effect on an empty beat changes nothing", s.Dump(0));
        s.Begin("Delete on the empty string of the chord");
        s.Click(0, 0, 5); s.Key(Key.Delete);
        s.Expect("Delete on an empty string of a chord leaves the chord", s.Cell(0, 0).Notes.Count == 2, "Delete with no note under the cursor removes nothing in the beat", s.Dump(0));
    }

    // ---------- W10 ----------
    private static void WfTrackSwitch(MainWindow w)
    {
        var s = WfOpen(w, "W10 track switch", bars: 40, tracks: 2);
        s.Begin("Ctrl+Right to bar 26, type");
        s.Repeat(Key.Right, 25, ModifierKeys.Control);
        s.Fret(9);
        s.Expect("the cursor is on bar 26 with the note", s.Ed.SelectedMeasure == 25 && s.NoteAt(25, 0, 0)?.Fret == 9, "Ctrl+Right steps bars", s.Cursor);
        s.Expect("bar 26 is in view", s.CursorInView(out var v), "the score follows the cursor", v);
        s.Begin("switch to track 2 and back");
        var grid = SmField<System.Windows.Controls.DataGrid>(w, "TrackMixerGrid")!;
        grid.SelectedIndex = 1; SmSettle();
        s.Expect("track 2's view shows the same bar", s.Ed.SelectedTrackIndex == 1 && s.Ed.SelectedMeasure == 25 && s.CursorInView(out v), "a track switch keeps the bar on screen", $"{s.Cursor}; {v}");
        grid.SelectedIndex = 0; SmSettle();
        s.Expect("back on track 1 the cursor bar is still in view", s.Ed.SelectedTrackIndex == 0 && s.Ed.SelectedMeasure == 25 && s.CursorInView(out v), "no scroll jump", $"{s.Cursor}; {v}");
        s.Begin("keep typing after the switch");
        s.Key(Key.Right); s.Fret(10);
        s.Expect("the new note lands on track 1 bar 26", s.Song.Tracks[0].Measures[25].Cells.Count(c => c.Notes.Any(n => n.Fret == 10)) == 1 && s.Song.Tracks[1].Measures[25].Cells.All(c => c.Notes.Count == 0),
            "typing goes to the selected track", s.Dump(25));
    }

    // ---------- W11 ----------
    private static void WfLegatoPhrase(MainWindow w)
    {
        var s = WfOpen(w, "W11 legato"); s.ToString_(2);
        s.Tool("duration:eighth");
        var before = s.Drawn(0);
        s.Begin("5 H, 7, 9 S, 7");
        s.Fret(5); s.Key(Key.H); s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Fret(9); s.Key(Key.S); s.Key(Key.Right); s.Fret(7);
        s.Expect("hammer-on on the 5 and slide on the 9", s.NoteAt(0, 0, 2)?.Techniques.Contains(TechniqueNames.Hopo) == true && s.NoteAt(0, 4, 2)?.Techniques.Contains(TechniqueNames.LegatoSlide) == true,
            "H and S mark the note under the cursor", s.Dump(0));
        var after = s.Drawn(0);
        s.Expect("the legato arc and slide line are drawn", after[LayoutAudit.Kind.Curve] > before[LayoutAudit.Kind.Curve] && after[LayoutAudit.Kind.Line] > before[LayoutAudit.Kind.Line],
            "a slur over H and a slide line are visible", $"curves {before[LayoutAudit.Kind.Curve]}->{after[LayoutAudit.Kind.Curve]}, lines {before[LayoutAudit.Kind.Line]}->{after[LayoutAudit.Kind.Line]}");
        s.Expect("red matches the fill", s.State(0).Marked == !s.State(0).Complete, "an incomplete bar is red, a rest-filled one is not", s.Dump(0));
        s.Begin("H again removes the hammer-on");
        s.Click(0, 0, 2); s.Key(Key.H);
        s.Expect("hammer-on removed", s.NoteAt(0, 0, 2)?.Techniques.Contains(TechniqueNames.Hopo) == false, "pressing again removes it", s.Dump(0));
    }

    // ---------- W12 ----------
    private static void WfCutPasteUndoAll(MainWindow w)
    {
        var s = WfOpen(w, "W12 cut paste undo");
        s.Begin("write bar 1 (4 quarters) and bar 2 (2 halves)");
        for (var i = 0; i < 4; i++) { s.Fret(i + 1); s.Key(Key.Right); }
        s.Tool("duration:half"); s.Fret(8); s.Key(Key.Right); s.Fret(10);
        s.Expect("both bars full", !s.State(0).Marked && !s.State(1).Marked && s.Beats(1).Count == 2, "", s.Dump(0) + " | " + s.Dump(1));
        s.Begin("select bar 1 as a bar, Ctrl+X");   // Shift+Right over a full bar's beats cuts the beats and keeps the bar (quiet GP5 b07, TestGp5Batch5Stories K2)
        s.Click(0, 0, 0); s.Ed.SelectMeasureRange(0, 0); s.Settle();
        s.Ctrl(Key.X);
        s.Expect("bar 1 is gone: the halves 8 10 move up", s.Track.Measures.Count == 3 && s.NoteAt(0, 0, 0)?.Fret == 8, "a cut of the whole bar takes the bar out", s.Dump(0));
        s.Expect("bar 1 is not red after the cut", !s.State(0).Marked, "the bars after move up whole", s.Dump(0));
        s.Begin("paste on bar 3");
        s.Click(2, 0, 0); s.Ctrl(Key.V);
        s.Expect("bar 3 has the four notes 1 2 3 4", s.Beats(2).Count == 4 && s.Beats(2).Select(c => s.Cell(2, c).Notes.FirstOrDefault()?.Fret ?? -1).SequenceEqual(new[] { 1, 2, 3, 4 }), "paste lands the cut beats", s.Dump(2));
        s.Begin("Ctrl+Z until nothing is left to undo");
        s.Ed.SelectForEdit(0, 0, 0); s.Settle();
        for (var i = 0; i < 60 && s.Doc.Undo.CanUndo; i++) s.Ctrl(Key.Z);
        s.Expect("the song is empty again", s.Song.Tracks.All(t => t.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0))) && s.Track.Measures.Count == 4,
            "undo walks back to the empty song", string.Join(" | ", Enumerable.Range(0, s.Track.Measures.Count).Select(s.Dump)));
        s.Expect("no bar is red", Enumerable.Range(0, s.Track.Measures.Count).All(b => !s.State(b).Marked), "an empty song has no red bars", "");
    }

    // ---------- W13 ----------
    private static void WfTieAcrossBar(MainWindow w)
    {
        var s = WfOpen(w, "W13 tie across bar");
        s.Begin("whole note 12 in bar 1, Right, L in bar 2");
        s.Tool("duration:whole"); s.Fret(12); s.Key(Key.Right);
        s.Expect("Right moves to bar 2", s.Ed.SelectedMeasure == 1, "", s.Cursor);
        s.Key(Key.L);
        var n = s.NoteAt(1, s.Ed.SelectedCell, 0);
        s.Expect("bar 2 starts with a tied 12", n is { Fret: 12 } && (n.Tied || s.Cell(1, s.Ed.SelectedCell).IsTied), "L ties across the barline", s.Dump(1));
        s.Expect("one attack across both bars", s.Onsets(0, 0, 12) + s.Onsets(1, 0, 12) == 1, "the tied note sustains", $"bar1 {s.Onsets(0, 0, 12)}, bar2 {s.Onsets(1, 0, 12)}");
        s.Expect("bar 1 is not red", !s.State(0).Marked, "a whole note fills 4/4", s.Dump(0));
    }

    // ---------- W14 ----------
    private static void WfBackspaceChord(MainWindow w)
    {
        var s = WfOpen(w, "W14 backspace chord");
        s.Fret(1); s.Key(Key.Down); s.Fret(1); s.Key(Key.Down); s.Fret(2);
        s.Begin("Backspace on string 2");
        s.Key(Key.Up); s.Key(Key.Back);
        s.Expect("only that note goes", s.Cell(0, 0).Notes.Count == 2 && s.NoteAt(0, 0, 1) is null, "Backspace erases the note under the cursor", s.Dump(0));
        s.Begin("Delete the remaining two, one by one");
        s.Click(0, 0, 0); s.Key(Key.Delete);
        s.Expect("one note left", s.Cell(0, 0).Notes.Count == 1, "Delete removes one note", s.Dump(0));
        s.Click(0, 0, 2); s.Key(Key.Delete);
        s.Expect("the beat is now a rest in place", s.Cell(0, 0).Notes.Count == 0, "the last note leaves a rest", s.Dump(0));
    }

    // ---------- W15 ----------
    // The monkey's commonest failure: a second digit on the same note (a correction) made frets above the neck.
    private static void WfRetypeFret(MainWindow w)
    {
        var s = WfOpen(w, "W15 retype fret");
        foreach (var (first, second) in new[] { (5, 5), (3, 7), (2, 9), (1, 2) })
        {
            s.Begin($"type {first} then {second} on the same note");
            s.Fret(first); s.Fret(second);
            var fret = s.NoteAt(0, 0, 0)?.Fret ?? -1;
            s.Expect("the fret stays on the neck (0..24)", fret is >= 0 and <= 24, "two digits above the last fret keep only the second digit", $"fret {fret}");
        }
    }
}
