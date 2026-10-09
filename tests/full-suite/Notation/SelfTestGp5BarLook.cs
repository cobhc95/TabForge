using System.Linq;
using System.Windows;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

/// <summary>
/// Bars and marks drawn as the reference draws them: an overfull bar has a red bar number and red staff and string lines (no shaded
/// box), a bar holding only the rest fill's whole-bar rest is drawn empty while written rests still draw, a lone triplet note keeps
/// its "3", and consecutive vibrato notes share one unbroken wavy line.
/// </summary>
public static partial class SelfTest
{
    private static void TestGp5BarLook()
    {
        // Which bars count as "only the rest fill".
        TabCell Rest(int d, int dots = 0) => new() { IsRest = true, DurationDenominator = d, Dots = dots };
        List<TabCell> Bar(int slots, params (int At, TabCell Cell)[] beats)
        {
            var cells = Enumerable.Range(0, slots).Select(_ => new TabCell()).ToList();
            foreach (var (at, cell) in beats) cells[at] = cell;
            return cells;
        }
        Check("a 4/4 bar holding only the fill's whole rest draws empty", ScoreRenderer.OnlyFillRests(Bar(16, (0, Rest(1))), 16));
        Check("a 3/4 bar holding only the fill's dotted half rest draws empty", ScoreRenderer.OnlyFillRests(Bar(12, (0, Rest(2, 1))), 12));
        Check("a bar with no beats at all draws empty", ScoreRenderer.OnlyFillRests(Bar(16), 16));
        Check("written rests (a quarter rest and the fill after it) still draw", !ScoreRenderer.OnlyFillRests(Bar(16, (0, Rest(4)), (4, Rest(4)), (8, Rest(2))), 16));
        var fermata = Rest(1); fermata.Fermata = true;
        Check("a whole rest with a fermata still draws", !ScoreRenderer.OnlyFillRests(Bar(16, (0, fermata)), 16));

        // A lone triplet note keeps its "3".
        var project = new SongProject { Tempo = 120 };
        var track = new TrackModel { Name = "Gtr", Kind = TrackKind.Guitar, Measures = Presets.TemplateFactory.Measures(3) };
        project.Tracks.Add(track);
        track.StringTunings.Clear();
        foreach (var tuning in new[] { 64, 59, 55, 50, 45, 40 }) track.StringTunings.Add(tuning);
        TabNote Note(int s, int fret) => new() { StringIndex = s, Fret = fret, MidiValue = track.StringTunings[s] + fret, Velocity = 80 };
        var lone = track.Measures[2];
        lone.Cells[0].DurationDenominator = 4; lone.Cells[0].IsTriplet = true; lone.Cells[0].Notes.Add(Note(1, 5));
        var tuplets = new StaffNotationRenderer().CreateLayout(track, lone, 2, 16, 0, 0, 10).TupletGroups;
        Check("a single triplet note shows its tuplet number", tuplets.Count == 1 && tuplets[0].Numerator == 3, $"{tuplets.Count} groups");
        lone.Cells[0] = new TabCell();

        // Bar 1 overfull (two halves and a quarter), bar 2 the fill's whole rest only, bar 3 four quarters, the first two with vibrato.
        var over = track.Measures[0];
        foreach (var at in new[] { 0, 8 }) { over.Cells[at].DurationDenominator = 2; over.Cells[at].Notes.Add(Note(1, 5)); }
        over.Cells[15].DurationDenominator = 4; over.Cells[15].Notes.Add(Note(1, 7));
        track.Measures[1].Cells[0].IsRest = true; track.Measures[1].Cells[0].DurationDenominator = 1;
        foreach (var at in new[] { 0, 4, 8, 12 }) { lone.Cells[at].DurationDenominator = 4; lone.Cells[at].Notes.Add(Note(1, 5)); }
        foreach (var at in new[] { 0, 4 }) lone.Cells[at].Notes[0].Techniques.Add("Vibrato");
        Check("the overfull bar is an error bar", MusicTime.AnalyzeBar(project, 0).Error);

        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, Appearance = { DarkPaper = false, ShowBarNumbers = false }, HideCursor = true };
        editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        editor.Arrange(new Rect(editor.DesiredSize));
        editor.UpdateLayout();
        var items = new List<LayoutAudit.Item>();
        foreach (var (_, drawing) in editor.AuditSystemDrawings()) LayoutAudit.Walk(drawing, System.Windows.Media.Matrix.Identity, items);
        var texts = items.Where(i => i.Kind == LayoutAudit.Kind.Text).Select(i => i.Label).ToList();
        Check("with bar numbers off, the overfull bar still shows its (red) number and the others do not", texts.Contains("1") && !texts.Contains("2") && !texts.Contains("3"), string.Join(",", texts));
        Check("no shaded box over the overfull bar", !items.Any(i => i.Kind == LayoutAudit.Kind.Shape && i.Box.Width > 60 && i.Box.Height > 60));
        var horizontal = items.Where(i => i.Kind == LayoutAudit.Kind.Line && i.Box.Height < 2 && i.Box.Width > 60).ToList();
        var widest = horizontal.Max(i => i.Box.Width);
        Check("the overfull bar redraws its 5 staff lines and 6 string lines (in red)", horizontal.Count(i => i.Box.Width < widest - 20) == 11, $"{horizontal.Count} lines");

        var waves = items.Where(i => i.Kind == LayoutAudit.Kind.Curve && i.Box.Height < 9 && i.Box.Width > 12).OrderBy(i => Math.Round(i.Box.Top / 10)).ThenBy(i => i.Box.Left).ToList();
        var staffPair = waves.Take(2).ToList();
        Check("two consecutive vibrato notes draw one unbroken wavy line (the pieces meet on one row)", staffPair.Count == 2
            && Math.Abs(staffPair[1].Box.Left - staffPair[0].Box.Right) < 2.5 && Math.Abs(staffPair[1].Box.Top - staffPair[0].Box.Top) < 1.5,
            string.Join("; ", waves.Select(w => w.Box.ToString())));
    }
}
