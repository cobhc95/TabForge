using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>With the rest fill on: Delete on a rest-only range merges that span, + / - on rests keep the bar exactly full with the fewest rests, the selection follows, "when deleting notes, leave" a/b, notes survive, one undo step.</summary>
    private static void TestRestMerge()
    {
        static TabCell Rest(int denominator) => new() { DurationDenominator = denominator, IsRest = true };
        static TabCell Note(int denominator, int fret) => new() { DurationDenominator = denominator, Notes = { new TabNote { StringIndex = 1, Fret = fret, MidiValue = 50 + fret } } };
        (Views.TabEditorControl Editor, SongProject Project, List<TabCell> Bar, Func<int> Steps) Make(bool mergeOnDelete = false)
        {
            var project = Presets.TemplateFactory.Create("Rock Band");
            var e = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, FillBarsWithRests = true, MergeRestsOnDelete = mergeOnDelete, AutoAdvanceAfterEntry = false };
            var steps = 0;
            e.EditStarting += (_, _) => steps++;
            var bar = project.Tracks[0].Measures[0].Cells;
            for (var i = 0; i < 16; i++) bar[i] = new TabCell();
            for (var i = 0; i < 8; i++) bar[i * 2] = Rest(8);
            return (e, project, bar, () => steps);
        }
        static int Rests(List<TabCell> bar) => bar.Count(c => c.IsRest);
        static (int, int, int, int) Sel(Views.TabEditorControl e) => ((Views.Score.IScoreEditContext)e).SelectionRange();

        // Owner example: five of eight eighth rests selected, Delete: half + eighth, the other three unchanged.
        var (ed, song, bar, steps) = Make();
        ed.SelectRange(0, 0, 0, 8);
        ed.DeleteBeat();
        Check("rest merge: Delete on 5 eighth rests gives a half rest and an eighth rest, the other 3 stay eighth rests, one undo step",
            bar[0].IsRest && bar[0].DurationDenominator == 2 && bar[0].Dots == 0 && bar[8].IsRest && bar[8].DurationDenominator == 8 &&
            new[] { 10, 12, 14 }.All(i => bar[i].IsRest && bar[i].DurationDenominator == 8) && Rests(bar) == 5 && !MusicTime.AnalyzeBar(song, 0).Marked && steps() == 1);
        var (_, _, _, endCell) = Sel(ed);
        Check("rest merge: the selection covers the merged rests", ed.HasSelection && endCell >= 8);

        var (ed2, song2, bar2, _) = Make();
        ed2.SelectRange(0, 0, 0, 6);
        ed2.DeleteBeat();
        Check("rest merge: 4 eighth rests become one half rest", bar2[0].DurationDenominator == 2 && bar2[0].Dots == 0 && Rests(bar2) == 5 && !MusicTime.AnalyzeBar(song2, 0).Marked);
        var (ed3, song3, bar3, _) = Make();
        ed3.SelectRange(0, 0, 0, 10);
        ed3.DeleteBeat();
        Check("rest merge: 6 eighth rests become one dotted half rest", bar3[0].DurationDenominator == 2 && bar3[0].Dots == 1 && Rests(bar3) == 3 && !MusicTime.AnalyzeBar(song3, 0).Marked);

        // A whole rest-only bar selected: one whole-bar rest; a second Delete changes nothing.
        var (ed4, song4, bar4, _) = Make();
        ed4.SelectRange(0, 0, 0, 14);
        ed4.DeleteBeat();
        Check("rest merge: a fully selected rest-only bar becomes one whole-bar rest", Rests(bar4) == 1 && bar4[0].DurationDenominator == 1 && !MusicTime.AnalyzeBar(song4, 0).Marked);

        // - and + on a rest-only range: the bar stays exactly full with the fewest rests.
        foreach (var shorter in new[] { true, false })
        {
            var (e5, s5, b5, st5) = Make();
            e5.SelectRange(0, 0, 0, 14);
            if (shorter) e5.Shorter(); else e5.Longer();
            var state = MusicTime.AnalyzeBar(s5, 0);
            Check(shorter ? "rest merge: - on a whole bar of 8 eighth rests gives 16 sixteenth rests"
                          : "rest merge: + turns 8 eighth rests into 4 quarter rests",
                shorter ? Enumerable.Range(0, 16).All(i => b5[i].IsRest && b5[i].DurationDenominator == 16) && Rests(b5) == 16
                        : new[] { 0, 4, 8, 12 }.All(i => b5[i].IsRest && b5[i].DurationDenominator == 4) && Rests(b5) == 4);
            Check($"rest merge: after {(shorter ? "-" : "+")} the bar is complete, one undo step, and the selection covers all the new rests",
                !state.Marked && state.Complete && st5() == 1 && e5.HasSelection && Sel(e5).Item2 == 0 && Sel(e5).Item4 == (shorter ? 15 : 12));
        }

        // 4 quarter rests selected, set eighth: 8 eighth rests; the rest of the bar is untouched.
        var (eq, sq, bq, _) = Make();
        for (var i = 0; i < 16; i++) bq[i] = new TabCell();
        for (var i = 0; i < 4; i++) bq[i * 4] = Rest(4);
        eq.SelectRange(0, 0, 0, 4);
        eq.SetDuration(8);
        Check("rest merge: 2 quarter rests set to eighth give 4 eighth rests, the rest of the bar stays", Rests(bq) == 6 && new[] { 0, 2, 4, 6 }.All(i => bq[i].IsRest && bq[i].DurationDenominator == 8) && bq[8].DurationDenominator == 4 && !MusicTime.AnalyzeBar(sq, 0).Marked);

        // A span that is not a multiple: 3 eighth rests (6 slots) set to quarter: one quarter, then the remainder as one eighth.
        var (ep, sp, bp, _) = Make();
        ep.SelectRange(0, 0, 0, 4);
        ep.SetDuration(4);
        var (_, _, _, pEnd) = Sel(ep);
        Check("rest merge: a span that is not a multiple fills as many as fit and completes the remainder with the fewest rests",
            bp[0].IsRest && bp[0].DurationDenominator == 4 && bp[4].IsRest && bp[4].DurationDenominator == 8 && bp[6].DurationDenominator == 8 && !MusicTime.AnalyzeBar(sp, 0).Marked);
        Check("rest merge: the selection covers every refilled rest of the span", ep.HasSelection && Sel(ep).Item2 == 0 && pEnd == 4);

        // With a note in the selection: today's behaviour, the note stays and the bar is full.
        var (e6, s6, b6, _) = Make();
        for (var i = 0; i < 16; i++) b6[i] = new TabCell();
        b6[0] = Note(4, 3); b6[4] = Rest(8); b6[6] = Rest(8); b6[8] = Rest(2);
        e6.SelectRange(0, 0, 0, 8);
        e6.Shorter();
        Check("rest merge: - on a selection with a note keeps the note and the bar full", b6[0].Notes.Count == 1 && !MusicTime.AnalyzeBar(s6, 0).Marked);

        // Delete notes: a) a rest of the same length, b) merged rests.
        foreach (var merge in new[] { false, true })
        {
            var (e7, s7, b7, st7) = Make(merge);
            for (var i = 0; i < 16; i++) b7[i] = new TabCell();
            b7[0] = Note(4, 3); b7[4] = Rest(4); b7[8] = Rest(2);
            e7.SetPosition(0, 0, 1, false);
            e7.DeleteBeat();
            Check(merge ? "delete leaves merged rests: a cleared bar becomes one whole-bar rest" : "delete leaves a rest of the same length",
                merge ? b7[0].IsRest && b7[0].DurationDenominator == 1 && Rests(b7) == 1 : b7[0].IsRest && b7[0].DurationDenominator == 4 && Rests(b7) == 3);
            Check("delete: no note left behind and the bar is full, one undo step", b7.All(c => c.Notes.Count == 0) && !MusicTime.AnalyzeBar(s7, 0).Marked && st7() == 1);
        }

        // Notes are never dropped: growing a rest next to notes keeps them.
        var (e8, s8, b8, _) = Make();
        for (var i = 0; i < 16; i++) b8[i] = new TabCell();
        b8[0] = Rest(8); b8[2] = Note(8, 1); b8[4] = Note(8, 2); b8[6] = Rest(2);
        e8.SelectRange(0, 0, 0, 0);
        e8.Longer();
        Check("rest merge: + on a rest never drops the notes next to it", b8.Sum(c => c.Notes.Count) == 2);

        Check("delete option: the default leaves a rest of the same length", !new EditingSettings().MergeRestsOnDelete);
    }
}
