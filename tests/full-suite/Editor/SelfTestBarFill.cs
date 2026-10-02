using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>"Fill incomplete bars with rests": edited bars stay complete, a deleted beat becomes a rest, an insert never drops a note; off keeps the old behaviour; opening a song rewrites nothing.</summary>
    private static void TestBarFill()
    {
        (Views.TabEditorControl Editor, SongProject Project, Func<int> Steps) Make(bool fill)
        {
            var project = Presets.TemplateFactory.Create("Rock Band");
            var e = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, FillBarsWithRests = fill, AutoAdvanceAfterEntry = false };
            var steps = 0;
            e.EditStarting += (_, _) => steps++;
            return (e, project, () => steps);
        }
        static TabCell Note(int denominator, int fret) => new() { DurationDenominator = denominator, Notes = { new TabNote { StringIndex = 1, Fret = fret, MidiValue = 50 + fret } } };
        static int NoteCount(List<TabCell> cells) => cells.Sum(c => c.Notes.Count);

        // Opening: assigning the project and moving around changes no cell.
        var (ed, song, steps) = Make(true);
        var before = song.Tracks[0].Measures.SelectMany(m => m.Cells).Count(c => c.IsRest);
        ed.SetPosition(0, 0, 1, false);
        Check("rest fill: opening a song and moving the cursor rewrites nothing", song.Tracks[0].Measures.SelectMany(m => m.Cells).Count(c => c.IsRest) == before && steps() == 0);

        // A note in an empty bar: the rest of the bar fills with rests, one undo step, only this bar.
        ed.CurrentDurationDenominator = 4;
        var bar0 = song.Tracks[0].Measures[0].Cells;
        var bar1Before = song.Tracks[0].Measures[1].Cells.Count(c => c.IsRest);
        ed.EnterFret(3);
        Check("rest fill: a note in an empty bar completes the bar with rests (no red), one undo step",
            bar0[0].Notes.Count == 1 && !bar0[0].IsRest && bar0[4].IsRest && bar0[8].IsRest && bar0[8].DurationDenominator == 2 &&
            !MusicTime.AnalyzeBar(song, 0).Marked && steps() == 1);
        Check("rest fill: the next bar was not touched", song.Tracks[0].Measures[1].Cells.Count(c => c.IsRest) == bar1Before);

        // Delete turns the beat into a rest of the same length.
        ed.SetPosition(0, 0, 1, false);
        ed.DeleteBeat();
        Check("rest fill: Delete turns the beat into a rest of the same length", bar0[0].IsRest && bar0[0].Notes.Count == 0 && bar0[0].DurationDenominator == 4 && !MusicTime.AnalyzeBar(song, 0).Marked);

        // Empty bar: one whole-bar rest.
        ed.EmptyBar();
        Check("rest fill: an emptied bar is one whole-bar rest", bar0[0].IsRest && bar0[0].DurationDenominator == 1 && bar0.Skip(1).All(c => !c.IsRest) && !MusicTime.AnalyzeBar(song, 0).Marked);

        // A note typed onto the rest splits it.
        ed.CurrentDurationDenominator = 8;
        ed.SetPosition(0, 0, 1, false);
        ed.EnterFret(5);
        Check("rest fill: a note typed onto the whole-bar rest takes its own length and the rest is refilled",
            bar0[0].Notes.Count == 1 && bar0[0].DurationDenominator == 8 && bar0[2].IsRest && !MusicTime.AnalyzeBar(song, 0).Marked);

        // Insert beat: consumes the following rest, notes stay.
        var (ed2, song2, _) = Make(true);
        var cells2 = song2.Tracks[0].Measures[0].Cells;
        cells2[0] = Note(4, 1);
        ed2.CurrentDurationDenominator = 4;
        ed2.SetPosition(0, 0, 1, false);
        ed2.EnterFret(1);                       // quarter note, rest of the bar filled
        ed2.SetPosition(0, 0, 1, false);
        ed2.InsertBeat();
        Check("rest fill: insert beat moves the note right, takes the room from the rests and the bar still adds up",
            cells2[0].IsRest && cells2[4].Notes.Count == 1 && NoteCount(cells2) == 1 && !MusicTime.AnalyzeBar(song2, 0).Marked);

        // Insert into a full bar: no note is dropped, the bar turns red.
        var (ed3, song3, _) = Make(true);
        var cells3 = song3.Tracks[0].Measures[0].Cells;
        for (var i = 0; i < 4; i++) cells3[i * 4] = Note(4, i + 1);
        ed3.CurrentDurationDenominator = 4;
        ed3.SetPosition(0, 0, 1, false);
        ed3.InsertBeat();
        Check("rest fill: insert into a full bar never drops a note, the bar shows red", NoteCount(song3.Tracks[0].Measures[0].Cells) == 4 && MusicTime.AnalyzeBar(song3, 0).Marked);

        // Off: the old behaviour (a half-empty bar is short and red).
        var (ed4, song4, _) = Make(false);
        ed4.CurrentDurationDenominator = 4;
        ed4.EnterFret(3);
        var cells4 = song4.Tracks[0].Measures[0].Cells;
        Check("rest fill off: no rests are added and the half-empty bar is marked", !cells4[4].IsRest && !cells4.Skip(1).Any(c => c.IsRest) && MusicTime.AnalyzeBar(song4, 0).Marked);
        ed4.SetPosition(0, 0, 1, false);
        ed4.DeleteBeat();
        Check("rest fill off: Delete leaves an empty beat, not a rest", !cells4[0].IsRest && cells4[0].Notes.Count == 0);

        // Insert bar: with the fill on the new bar is one whole-bar rest in every track; off it stays empty.
        foreach (var fill in new[] { true, false })
        {
            var doc = Documents.DocumentSession.FromProject(Presets.TemplateFactory.Create("Rock Band"), null);
            var barsBefore = doc.Project.Tracks[0].Measures.Count;
            new Controllers.ArrangementController().InsertBar(doc, 1, 0, moveMarkers: false, fillRests: fill);
            var added = doc.Project.Tracks.Select(t => t.Measures[1].Cells).ToList();
            var ok = fill ? added.All(c => c[0].IsRest && c[0].DurationDenominator == 1 && !c.Skip(1).Any(x => x.IsRest))
                          : added.All(c => !c.Any(x => x.IsRest));
            Check(fill ? "rest fill: Insert bar starts every track's new bar as one whole-bar rest" : "rest fill off: Insert bar leaves the new bar empty",
                ok && doc.Project.Tracks[0].Measures.Count == barsBefore + 1 && !MusicTime.AnalyzeBar(doc.Project, 1).Marked);
        }

        // Paste that leaves a bar short: the remainder becomes rests, no pasted note is lost.
        {
            var doc = Documents.DocumentSession.FromProject(Presets.TemplateFactory.Create("Rock Band"), null);
            var cells = doc.Project.Tracks[0].Measures[0].Cells;
            cells[0] = Note(4, 2); cells[4] = Note(4, 4);
            var clip = ClipboardService.CaptureBeats(doc.Project, 0, 0, 0, 0, 0, 4);
            var settings = new EditingSettings { FillBarsWithRests = true };
            var outcome = EditCommands.RunPaste(doc, clip, new PasteTarget(0, 0, 1, 0), settings, new RecommendedPasteAnswers());
            var pasted = doc.Project.Tracks[0].Measures[1].Cells;
            Check("rest fill: a paste that leaves the bar short fills the rest with rests and keeps both notes",
                outcome.Changed && NoteCount(pasted) == 2 && pasted[8].IsRest && !MusicTime.AnalyzeBar(doc.Project, 1).Marked);
        }

        // The setting defaults on and the model fill leaves a bar that is already complete alone.
        Check("rest fill: the setting is on by default", new EditingSettings().FillBarsWithRests);
        var full = new List<TabCell> { Note(1, 1) };
        for (var i = 1; i < 16; i++) full.Add(new TabCell());
        Check("rest fill: a complete bar is left as it is", !BarFill.FillCells(full, 16, always: true) && full.Count(c => c.IsRest) == 0);
    }
}
