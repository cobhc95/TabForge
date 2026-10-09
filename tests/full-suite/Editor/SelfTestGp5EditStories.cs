using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Views;
using TabForge.Views.EffectEditors;

namespace TabForge;

// Owns: workflow stories of the GP5 harness's marks / ties / cut / entry-on-rests clusters (work/gp5diff REPORT.md 6-10): L and Ctrl+L
//     on an empty beat or a rest write a tied note, Cut takes beats and whole bars out, accent / staccato on an empty
//     spot do nothing (dot and triplet there are the next note's, see TestGp5EntryStories), duration keys and "." change a rest R wrote, a chord's grace note belongs to
//     the cursor string's note.
// Does not own: the commands (Views/Score/ScoreEditCommands*.cs, EditCommands.CutClear, OrnamentEdits), the input helpers (WorkflowKit.cs).
// Tests: TestGp5EditStories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5EditStories()
    {
        var w = SmNewWindow();
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("G1 L on the new song writes a tied fret 0 (m07)", GsTieOnNewSong),
                ("G2 L on a note with nothing before it ties it as fret 0", GsTieOnFirstNote),
                ("G3 Ctrl+L on an inserted rest ties the previous note (s004605)", GsTieBeatOnRest),
                ("G4 cut beats closes the bar up (m10)", GsCutBeats),
                ("G5 cut a whole bar removes it (m21)", GsCutBar),
                ("G6 accent / staccato on an empty spot are not kept (m13)", GsMarksOnEmpty),
                ("G7 Ctrl+Z takes back a dot / triplet pressed on an empty spot (m14, m27)", GsDotTripletOnEmpty),
                ("G8 longer and dot change a rest R wrote (m09, m19)", GsDurationOnRest),
                ("G9 a chord's grace note goes to the cursor string (m18)", GsGraceOnChord),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { SmCloseWindow(w); }
    }

    private static string GsStatus(Wf s) => LtField<TextBlock>(s.Window, "StatusText")!.Text;

    private static void GsTieOnNewSong(MainWindow w)
    {
        var s = WfOpen(w, "G1 tie new song");
        s.Begin("L on the empty first beat");
        s.Key(Key.L);
        s.Expect("a tied fret 0 quarter on string 1", s.NoteAt(0, 0, 0) is { Fret: 0, Tied: true } && s.Cell(0, 0).DurationDenominator == 4, "0[tie]/4", s.Dump(0));
    }

    private static void GsTieOnFirstNote(MainWindow w)
    {
        var s = WfOpen(w, "G2 tie first note");
        s.Begin("12, L");
        s.Fret(12); s.Key(Key.L);
        s.Expect("the note becomes a tied fret 0", s.NoteAt(0, 0, 0) is { Fret: 0, Tied: true } && s.Cell(0, 0).Notes.Count == 1, "0[tie]/4", s.Dump(0));
    }

    private static void GsTieBeatOnRest(MainWindow w)
    {
        var s = WfOpen(w, "G3 tie beat on rest");
        s.Begin("15, Right, 5, Right, Insert, Ctrl+L");
        s.Fret(15); s.Key(Key.Right); s.Fret(5); s.Key(Key.Right); s.Key(Key.Insert); s.Tool("gp:tie_beat");
        s.Expect("the inserted beat is a tied 5", s.NoteAt(0, 8, 0) is { Fret: 5, Tied: true }, "15 5 5[tie]", s.Dump(0));
    }

    private static void GsCutBeats(MainWindow w)
    {
        var s = WfOpen(w, "G4 cut beats");
        s.Begin("5, Right, 7, Right, 9, Home, Shift+Right, Ctrl+X");
        s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Fret(9);
        s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.X);
        s.Expect("the 9 moves to the start, rests only after it", s.NoteAt(0, 0, 0)?.Fret == 9 && s.Beats(0).Skip(1).All(c => s.Cell(0, c).Notes.Count == 0) && s.State(0).Complete,
            "9 (later beats close up)", s.Dump(0));
    }

    private static void GsCutBar(MainWindow w)
    {
        var s = WfOpen(w, "G5 cut bar", bars: 1);
        s.Begin("Ctrl+Insert, Home, Shift+Right, Ctrl+X");
        s.Key(Key.Insert, ModifierKeys.Control); s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.X);
        s.Expect("one bar is left", s.Track.Measures.Count == 1, "the bar is gone", $"{s.Track.Measures.Count} bars, {s.Dump(0)}, sel {s.Ed.SelectionCellRange}, {GsStatus(s)}");
    }

    private static void GsMarksOnEmpty(MainWindow w)
    {
        var s = WfOpen(w, "G6 marks on empty");
        s.Begin("accent, staccato on the empty first beat, then 5");
        s.Ed.Effects.CycleAccent(); s.Ed.Effects.ToggleStaccato();
        var said = GsStatus(s);
        s.Fret(5);
        var c = s.Cell(0, 0);
        s.Expect("a plain 5", c.Notes.Count == 1 && c.Accent == 0 && !c.Staccato, "5 (no marks)", s.Dump(0));
        s.Expect("the status says there is no note", said.Contains("No note here"), "nothing happens", said);
    }

    private static void GsDotTripletOnEmpty(MainWindow w)
    {
        var s = WfOpen(w, "G7 dot triplet on empty");
        s.Begin("., Ctrl+Z, 1, then triplet, Ctrl+Z, 2 on beat 2");
        s.Key(Key.OemPeriod); s.Ctrl(Key.Z); s.Fret(1);
        s.Expect("a plain quarter 1", s.Cell(0, 0).Dots == 0 && s.Cell(0, 0).DurationDenominator == 4, "1/4", s.Dump(0));
        s.Click(0, 4, 0); s.Ed.Effects.ToggleTriplet(); s.Ctrl(Key.Z); s.Fret(2);
        s.Expect("a plain quarter 2", s.Cell(0, 4).Notes.Count == 1 && !s.Cell(0, 4).IsTriplet && s.Cell(0, 4).TupletNumerator == 0, "2/4", s.Dump(0));
    }

    private static void GsDurationOnRest(MainWindow w)
    {
        var s = WfOpen(w, "G8 duration on rest");
        s.Begin("R, numpad - (longer)");
        s.Key(Key.R); s.Key(Key.Subtract);
        s.Expect("a half rest", s.Cell(0, 0).IsRest && s.Cell(0, 0).DurationDenominator == 2, "r/2", s.Dump(0));
        var t = WfOpen(w, "G8b dot on rest");
        t.Begin("5, Right, R, .");
        t.Fret(5); t.Key(Key.Right); t.Key(Key.R); t.Key(Key.OemPeriod);
        t.Expect("a dotted quarter rest after the 5", t.Cell(0, 4).IsRest && t.Cell(0, 4).Dots == 1 && t.Cell(0, 4).DurationDenominator == 4 && t.State(0).Complete, "5 r/4.", t.Dump(0));
    }

    private static void GsGraceOnChord(MainWindow w)
    {
        var s = WfOpen(w, "G9 grace on chord");
        s.Begin("5, Down, 3, G (OK)");
        s.Fret(5); s.Key(Key.Down); s.Fret(3);
        new EffectEditorFlow(new EeHost(s.Ed.Effects)).OpenOrnament(EffectEditorKind.Grace, _ => EditorAnswer.Ok);
        var grace = s.Cell(0, 0).Notes.Where(n => n.IsGraceNote).ToList();
        s.Expect("one grace note, on string 2", grace.Count == 1 && grace[0].StringIndex == 1, "the grace belongs to the 3 on string 2",
            string.Join(",", s.Cell(0, 0).Notes.Select(n => $"{n.Fret}@{n.StringIndex}{(n.IsGraceNote ? "g" : "")}")));
    }
}
