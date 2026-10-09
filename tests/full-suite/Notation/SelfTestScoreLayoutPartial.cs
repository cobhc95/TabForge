using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestScoreLayoutPartial() => ScoreLayoutPartialTest.Run((name, condition, detail) => Check(name, condition, detail));
}

internal static class ScoreLayoutPartialTest
{
    internal static void Run(Action<string, bool, string?> report)
    {
        VerifyStableReuseAndRangeSafety(report);
        VerifyWidthAndStructureFallbacks(report);
        VerifyExtentsAndCrossBarFacts(report);
        VerifyBoundaryAndSpillEdits(report);
    }

    private static void VerifyBoundaryAndSpillEdits(Action<string, bool, string?> report)
    {
        var (editor, track, project) = CreateScore();
        track.Measures[4].Cells[0].IsTied = true;
        var before = PrimeStaff(editor, track, 4, editor.Layout.GetLayout());
        track.Measures[3].Cells[0].Notes[0].Fret = 5;
        editor.Layout.InvalidateMeasures(3, 3);
        var page = editor.Layout.GetLayout();
        var after = PrimeStaff(editor, track, 4, page);
        Check(report, "an edit before a bar that starts tied falls back to a full relayout and rebuilds the tied bar",
            !editor.Layout.LastRelayoutWasPartial && !ReferenceEquals(before, after) &&
            SameScoreGeometry(page, CreateEditor(project).Layout.GetLayout()), Reason(editor));

        (editor, track, project) = CreateScore();
        var spilled = PrimeStaff(editor, track, 5, editor.Layout.GetLayout());
        editor.Layout.CaptureMeasureRange((3, 3));
        track.Measures[3].Cells[0].Notes[0].Fret = 4;
        track.Measures[5].Cells[0].Notes[0].Fret = 9;
        editor.Layout.InvalidatePendingMeasures();
        var spillPage = editor.Layout.GetLayout();
        Check(report, "an edit that rewrites bars beyond its captured range falls back to a full relayout",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            !ReferenceEquals(spilled, PrimeStaff(editor, track, 5, spillPage)) &&
            SameScoreGeometry(spillPage, CreateEditor(project).Layout.GetLayout()), Reason(editor));

        (editor, track, project) = CreateScore();
        _ = editor.Layout.GetLayout();
        track.Measures[3].Cells[0].Notes[0].Fret = 3;
        editor.Layout.InvalidateMeasures(3, 3);
        _ = editor.Layout.GetLayout();
        Check(report, "a plain single-bar edit still takes the partial path",
            editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == 1, Reason(editor));
    }

    private static void VerifyStableReuseAndRangeSafety(Action<string, bool, string?> report)
    {
        var (editor, track, project) = CreateScore();
        var original = editor.Layout.GetLayout();
        var untouched = PrimeStaff(editor, track, 7, original);
        editor.SetBar(3, seekPlayback: false);
        editor.Effects.SetDynamicVelocity(86);
        var partial = editor.Layout.GetLayout();
        var reused = PrimeStaff(editor, track, 7, partial);
        Check(report, "a stable edit measures only its changed bar and reuses an untouched staff layout",
            editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == 1 && ReferenceEquals(untouched, reused),
            Reason(editor));

        var fresh = CreateEditor(project).Layout.GetLayout();
        Check(report, "partial score geometry matches a fresh full layout", SameScoreGeometry(partial, fresh));

        editor.Layout.CaptureMeasureRange((3, 3));
        editor.Layout.InvalidatePendingMeasures(changed: false);
        track.Measures[2].Cells[0].Notes[0].Fret = 10;
        track.Measures[4].Cells[0].Notes[0].Fret = 12;
        editor.NotifyEdited();
        var external = editor.Layout.GetLayout();
        Check(report, "a no-change range cannot narrow a later external multi-bar edit",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(external, CreateEditor(project).Layout.GetLayout()), Reason(editor));

        editor.Layout.CaptureMeasureRange((3, 3));
        try { ScoreEditPreparation.RunMutation(() => throw new InvalidOperationException("edit fixture"), editor.Layout); }
        catch (InvalidOperationException) { }
        track.Measures[5].ForceLineBreak = true;
        editor.Layout.InvalidatePendingMeasures();
        var afterThrow = editor.Layout.GetLayout();
        Check(report, "a failed mutation clears its range before later layout invalidation",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(afterThrow, CreateEditor(project).Layout.GetLayout()), Reason(editor));
    }

    private static void VerifyWidthAndStructureFallbacks(Action<string, bool, string?> report)
    {
        var (editor, track, project) = CreateScore();
        _ = editor.Layout.GetLayout();
        track.Measures[3].Cells[0].ChordName = "Cmaj7add13";
        editor.Layout.InvalidateMeasures(3, 3);
        var wide = editor.Layout.GetLayout();
        Check(report, "a changed bar width uses the full fallback and matches fresh geometry",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastRelayoutReason == "bar width changed" &&
            editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(wide, CreateEditor(project).Layout.GetLayout()), Reason(editor));

        track.Measures[6].ForceLineBreak = true;
        editor.Layout.InvalidateMeasures(6, 6);
        var wrapped = editor.Layout.GetLayout();
        Check(report, "a system-break change uses the full width fallback and matches fresh geometry",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(wrapped, CreateEditor(project).Layout.GetLayout()), Reason(editor));

        track.Measures[3].Cells[0].IsTriplet = true;
        editor.Layout.InvalidateMeasures(3, 3);
        var tuplets = editor.Layout.GetLayout();
        Check(report, "a tuplet change uses the full width fallback and matches fresh geometry",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(tuplets, CreateEditor(project).Layout.GetLayout()), Reason(editor));
    }

    private static void VerifyExtentsAndCrossBarFacts(Action<string, bool, string?> report)
    {
        var (editor, track, project) = CreateScore();
        _ = editor.Layout.GetLayout();
        track.Measures[0].Cells[0].Notes[0].MidiValue = 112;
        editor.Layout.InvalidateMeasures(0, 0);
        var tall = editor.Layout.GetLayout();
        var freshTall = CreateEditor(project);
        Check(report, "a new tallest note extent triggers a full score relayout",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(tall, freshTall.Layout.GetLayout()) && SameExtents(editor.Layout, freshTall.Layout), Reason(editor));

        track.Measures[0].Cells[0].Notes[0].MidiValue = 60;
        editor.Layout.InvalidateMeasures(0, 0);
        var restored = editor.Layout.GetLayout();
        var freshRestored = CreateEditor(project);
        Check(report, "removing the tallest note recomputes the lower global extent",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(restored, freshRestored.Layout.GetLayout()) && SameExtents(editor.Layout, freshRestored.Layout), Reason(editor));

        project.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "New marker" });
        editor.Layout.InvalidateMeasures(3, 3);
        var marked = editor.Layout.GetLayout();
        Check(report, "a newly introduced project marker rejects the stable-range path",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastRelayoutReason == "cross-bar layout facts" &&
            editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(marked, CreateEditor(project).Layout.GetLayout()), Reason(editor));

        project.Markers.Clear();
        editor.Layout.Invalidate();
        _ = editor.Layout.GetLayout();
        track.Measures[0].Cells[12] = NotationCell(4, 60);
        track.Measures[0].Cells[12].Notes[0].Techniques.Add("PM");
        track.Measures[1].Cells[0].Notes[0].Techniques.Add("PM");
        editor.Layout.InvalidateMeasures(0, 0);
        var passage = editor.Layout.GetLayout();
        Check(report, "a newly introduced cross-bar palm mute passage rejects the stable-range path",
            !editor.Layout.LastRelayoutWasPartial && editor.Layout.LastRelayoutReason == "cross-bar layout facts" &&
            editor.Layout.LastNaturalMeasureCount == measuresCount(track) &&
            SameScoreGeometry(passage, CreateEditor(project).Layout.GetLayout()), Reason(editor));
    }

    private static (TabEditorControl Editor, TrackModel Track, SongProject Project) CreateScore()
    {
        var measures = Enumerable.Range(0, 9).Select(_ => NewMeasure()).ToList();
        foreach (var (measure, index) in measures.Select((measure, index) => (measure, index)))
            measure.Cells[0] = NotationCell(4, 60 + index);
        var track = new TrackModel { Name = "Layout", Kind = TrackKind.Guitar, Measures = measures };
        var project = new SongProject { Tempo = 120, Tracks = new List<TrackModel> { track } };
        return (CreateEditor(project), track, project);
    }

    private static TabEditorControl CreateEditor(SongProject project)
    {
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, PageWidthOverride = 1100 };
        editor.Appearance.ShowDynamics = false;
        return editor;
    }

    private static string Reason(TabEditorControl editor) =>
        $"partial={editor.Layout.LastRelayoutWasPartial} measured={editor.Layout.LastNaturalMeasureCount} reason={editor.Layout.LastRelayoutReason}";

    private static void Check(Action<string, bool, string?> report, string name, bool condition, string? detail = null) =>
        report(name, condition, detail);

    private static int measuresCount(TrackModel track) => track.Measures.Count;

    private static StaffNotationMeasureLayout PrimeStaff(TabEditorControl view, TrackModel track, int index, ScorePageLayout page)
    {
        var measure = track.Measures[index];
        var position = page.Measure(index);
        var slots = MusicTime.BarSlots(view.Project!, index);
        var numerator = measure.TimeSigNum ?? view.Project!.TimeSignatureNumerator;
        var denominator = measure.TimeSigDenom ?? view.Project!.TimeSignatureDenominator;
        return view.Layout.StaffLayoutFor(track, measure, index, slots, position.X, 40,
            position.Width / Math.Max(1, slots), numerator, denominator,
            measure.KeySignature ?? view.Project!.KeySignature, measure.Cells);
    }

    private static bool SameScoreGeometry(ScorePageLayout left, ScorePageLayout right) =>
        left.SystemCount == right.SystemCount &&
        Enumerable.Range(0, left.SystemCount).All(index =>
            left.Systems[index].FirstMeasure == right.Systems[index].FirstMeasure &&
            left.Systems[index].LastMeasure == right.Systems[index].LastMeasure) &&
        Enumerable.Range(0, 9).All(index =>
        {
            var a = left.Measure(index);
            var b = right.Measure(index);
            return a.SystemIndex == b.SystemIndex && a.ColumnIndex == b.ColumnIndex &&
                Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Width - b.Width) < 0.01 &&
                Math.Abs(a.NaturalWidth - b.NaturalWidth) < 0.01;
        });

    private static bool SameExtents(ScoreLayoutEngine left, ScoreLayoutEngine right) =>
        Math.Abs(left.ExtraAbove - right.ExtraAbove) < 0.01 &&
        Math.Abs(left.ExtraBelow - right.ExtraBelow) < 0.01 &&
        Math.Abs(left.ExtraTabBelow - right.ExtraTabBelow) < 0.01;

    private static MeasureModel NewMeasure() => new()
    {
        Clef = "G2",
        Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList()
    };

    private static TabCell NotationCell(int denominator, int midi) => new()
    {
        DurationDenominator = denominator,
        Notes = new List<TabNote> { new() { StringIndex = 0, Fret = 0, MidiValue = midi } }
    };
}
