using System.Linq;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: checks of GP5's per-note tie model: L ties only the cursor string's note (a copy of that string's previous fret at the
// writing duration), L again removes it, strings tie independently, a string with nothing before it does nothing, a fret typed onto
// a tied note drops the tie, the TAB hides a tied note's fret even under the cursor, and a tie over a barline is one arc.
// Does not own: the command (Views/Score/ScoreEditCommands.Ties.cs), arcs (StaffNotationArcs, StaffNotationDrawing).
// Tests: TestTiePerString.
public static partial class SelfTest
{
    private static void TestTiePerString()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmBlankSong(4));
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            ed.SelectedTrackIndex = 0;
            string Dump(TabCell c) => $"{c.Notes.Count} notes [{string.Join(",", c.Notes.Select(n => $"s{n.StringIndex}f{n.Fret}{(n.Tied ? "t" : "")}"))}] /{c.DurationDenominator}{(c.IsTied ? " beat-tied" : "")}{(c.IsRest ? " rest" : "")}";
            ed.Effects.SetDuration(4);
            foreach (var (s, f) in new[] { (3, 5), (4, 5), (5, 3) }) { ed.SetPosition(0, 0, s); ed.Effects.EnterFret(f, autoAdvance: false); }
            var beat = SmCell(doc, 0, 4);

            ed.SetPosition(0, 4, 5); ed.Effects.SetDuration(4); ed.Effects.ToggleTie();
            Check("L after a chord ties only the cursor string, at the writing duration",
                beat.Notes.Count == 1 && beat.Notes[0] is { StringIndex: 5, Fret: 3, Tied: true } && !beat.IsTied && beat.DurationDenominator == 4, Dump(beat));
            ed.Effects.ToggleTie();
            Check("L again removes the tied note", beat.Notes.Count == 0 && !beat.IsTied, Dump(beat));

            ed.Effects.ToggleTie(); ed.SetPosition(0, 4, 4); ed.Effects.ToggleTie();
            Check("another string ties on its own", beat.Notes.Count == 2 && beat.Notes.All(n => n.Tied) && beat.Notes.Any(n => n is { StringIndex: 4, Fret: 5 }), Dump(beat));
            ed.SetPosition(0, 4, 0); ed.Effects.ToggleTie();
            Check("L on a string with nothing before it does nothing", beat.Notes.Count == 2 && beat.Notes.All(n => n.StringIndex != 0), Dump(beat));

            int Digits() { var items = new List<LayoutAudit.Item>(); foreach (var (_, d) in ed.AuditSystemDrawings()) LayoutAudit.Walk(d, Matrix.Identity, items); return items.Count(i => i.Kind == LayoutAudit.Kind.Text && i.Label.Length > 0 && i.Label.All(char.IsDigit)); }
            ed.SetPosition(0, 4, 4);
            var hidden = Digits();
            ed.Effects.TieSelectedNote();   // untie string 4 here: its fret now shows
            Check("the TAB hides a tied note's fret, even on the cursor beat", Digits() == hidden + 1 && beat.Notes.First(n => n.StringIndex == 4) is { Tied: false }, $"digits {hidden} -> {Digits()}, {Dump(beat)}");

            ed.SetPosition(0, 4, 5); ed.Effects.EnterFret(7, autoAdvance: false);
            Check("a fret typed onto a tied note is a new note: the tie goes", beat.Notes.First(n => n.StringIndex == 5) is { Fret: 7, Tied: false }, Dump(beat));

            ed.SetPosition(2, 0, 1); ed.Effects.SetDuration(1); ed.Effects.EnterFret(9, autoAdvance: false);
            ed.SetPosition(3, 0, 1); ed.Effects.SetDuration(4); ed.Effects.ToggleTie();
            var next = SmCell(doc, 3, 0);
            Check("L over the barline ties that string's note at the writing duration (not the empty bar's length)",
                next.Notes.Count == 1 && next.Notes[0] is { StringIndex: 1, Fret: 9, Tied: true } && next.DurationDenominator == 4, Dump(next));
        }
        finally { SmCloseWindow(w); }

        // A tie over the barline: the outgoing half ends at the barline and the incoming half starts there, so they join.
        MeasureModel Bar() => new() { Clef = "G2", Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList() };
        TabCell NoteCell(bool tied) => new() { DurationDenominator = 4, Notes = new List<TabNote> { new() { StringIndex = 0, MidiValue = 64, Tied = tied } } };
        var first = Bar(); first.Cells[12] = NoteCell(false);
        var second = Bar(); second.Cells[0] = NoteCell(true);
        var track = new TrackModel { Kind = TrackKind.Other, Measures = new List<MeasureModel> { first, second } };
        var renderer = new StaffNotationRenderer();
        var slots = MusicTime.BarSlots(4, 4);
        var outLayout = renderer.CreateLayout(track, first, 0, slots, 100, 40, 10, 4, 4, 0, 1.0);
        var barline = 100 + 10 * slots;
        var inLayout = renderer.CreateLayout(track, second, 1, slots, barline, 40, 10, 4, 4, 0, 1.0);
        var outHalf = outLayout.Ties.Cast<StaffNotationTie?>().FirstOrDefault(t => t!.Value.IsStub && !t.Value.TowardLeft);
        var inHalf = inLayout.Ties.Cast<StaffNotationTie?>().FirstOrDefault(t => t!.Value.IsStub && t.Value.TowardLeft);
        Check("a tie over the barline is one arc: both halves meet at the barline on the same side",
            outHalf is { } o && inHalf is { } i && Math.Abs(o.X2 - barline) < 0.5 && Math.Abs(i.X2 - barline) < 0.5
            && o.Above == i.Above && Math.Abs(o.Y1 - i.Y1) < 0.5,
            $"out {outHalf}, in {inHalf}, barline {barline}");
    }
}
