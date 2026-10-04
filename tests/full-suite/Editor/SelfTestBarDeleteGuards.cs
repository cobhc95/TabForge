using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

/// <summary>
/// Guards around deleting bars: a full bar offers no append slot after its beats; Delete on selected empty bars asks first and removes them in one undo step;
/// "Delete empty bars" removes only the empty bars of a range; deleting bars under audio clips warns first and Cancel changes nothing.
/// </summary>
public static partial class SelfTest
{
    private static void TestBarDeleteGuards()
    {
        // Full bar: no cursor position after its only beat (6/8 = 12 slots in a 16-cell grid); an incomplete bar keeps the append slot.
        var full = new MeasureModel();
        full.Cells[0].IsRest = true; full.Cells[0].DurationDenominator = 2; full.Cells[0].Dots = 1;   // dotted half = 12 slots
        Check("a full bar offers its beats only (no append slot)", CursorPositions.Allowed(full.Cells, 12).SequenceEqual(new[] { 0 }), string.Join(",", CursorPositions.Allowed(full.Cells, 12)));
        Check("a click past the end of a full bar lands on its last beat", TabEditorControl.ResolveBeatHitCell(full, 11.5, 11, null, 12) == 0);
        Check("arrow right from the only beat of a full bar finds no further position", CursorPositions.Next(full.Cells, 0, 12) == -1);
        var part = new MeasureModel();
        part.Cells[0].IsRest = true; part.Cells[0].DurationDenominator = 4;
        Check("an incomplete bar keeps its append slot", CursorPositions.Allowed(part.Cells, 12).SequenceEqual(new[] { 0, 4 }), string.Join(",", CursorPositions.Allowed(part.Cells, 12)));

        // The selection menu offers "Delete empty bars" only when the range holds empty bars.
        var with = TimelineMenus.Selection(new SelectionMenuState("Bars 1-2 selected", false, false, false, false, 2), k => "");
        var without = TimelineMenus.Selection(new SelectionMenuState("Bars 1-2 selected", false, false, false, false), k => "");
        Check("the selection menu lists Delete empty bars when the range has empty bars", with.Any(m => m.Command == TimelineCommand.DeleteEmptyBars) && !without.Any(m => m.Command == TimelineCommand.DeleteEmptyBars));

        RunInWindowFixture((window, context) =>
        {
            var sections = LtField<SectionEditFlow>(window, "_sections")!;
            var previous = DialogHost.Capture;
            var asked = new List<string>();
            var answer = MessageBoxResult.No;
            DialogHost.Capture = w =>
            {
                asked.Add(string.Join(" ", Logical<TextBlock>(w).Select(t => t.Text)));
                ((ThemedConfirmDialog)w).AnswerForTest(answer);
                return true;
            };
            try
            {
                int Bars(DocumentSession d) => BarRangeEditor.MaxMeasures(d.Project);
                DocumentSession Open()
                {
                    var song = DoSong(2, 8);
                    song.Tracks[0].Measures[3].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
                    return DoOpen(window, song);
                }

                // Delete on selected empty bars: Cancel keeps them, Delete removes them in one undo step.
                var doc = Open();
                asked.Clear(); answer = MessageBoxResult.No;
                var handled = sections.TryDeleteSelectedEmpty(doc, 0, 5, 0, 6, 15);
                Check("Delete on two empty bars asks and Cancel leaves them", handled && asked.Count == 1 && asked[0].Contains("Delete 2 empty bars?") && Bars(doc) == 8, $"{handled} {Bars(doc)} {string.Join("|", asked)}");
                var undoBefore = doc.Undo.UndoCount;
                answer = MessageBoxResult.Yes;
                sections.TryDeleteSelectedEmpty(doc, 0, 5, 0, 6, 15);
                Check("Yes removes the empty bars in one undo step", Bars(doc) == 6 && doc.Undo.UndoCount == undoBefore + 1, $"{Bars(doc)} {doc.Undo.UndoCount - undoBefore}");
                asked.Clear();
                Check("a selection that includes bar 4 (with a note) is not handled, so Delete clears beats", !sections.TryDeleteSelectedEmpty(doc, 0, 2, 0, 3, 15) && asked.Count == 0 && Bars(doc) == 6);

                // Delete empty bars in a range: only the empty ones go, one undo step.
                doc = Open();
                undoBefore = doc.Undo.UndoCount;
                sections.DeleteEmptyInRange(doc, 2, 5);
                Check("Delete empty bars removes only the empty bars of the range", Bars(doc) == 5 && doc.Project.Tracks[0].Measures.Any(m => m.Cells[0].Notes.Count > 0) && doc.Undo.UndoCount == undoBefore + 1, $"{Bars(doc)}");

                // Deleting bars under a clip warns first.
                doc = Open();
                doc.Project.Tracks[1].AudioClips.Add(new AudioClip { File = @"C:\songs\a\x.wav", Name = "x", StartSec = 0, SourceLengthSec = 1, FileLengthSec = 1 });
                asked.Clear(); answer = MessageBoxResult.No;
                sections.DeleteArea(doc, 0, 0, "Deleted");
                Check("deleting a bar over a clip warns and Cancel changes nothing", asked.Count == 1 && asked[0].Contains("1 clip") && Bars(doc) == 8, $"{Bars(doc)} {string.Join("|", asked)}");
                answer = MessageBoxResult.Yes;
                sections.DeleteArea(doc, 0, 0, "Deleted");
                Check("Continue deletes the bar", Bars(doc) == 7);
                asked.Clear();
                sections.DeleteArea(doc, 3, 3, "Deleted");
                Check("deleting bars away from every clip does not warn", asked.Count == 0 && Bars(doc) == 6, string.Join("|", asked));
            }
            finally { DialogHost.Capture = previous; }
        });
    }
}
