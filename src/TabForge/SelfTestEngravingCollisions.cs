using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Corpus layout-audit regressions: a ghost chord (one bracket pair, clear of the accidentals), a chord with a displaced
/// second and an accidental, a grace note before a sharpened note, and a time-signature change all engrave without overlaps.
/// </summary>
public static partial class SelfTest
{
    private static void TestEngravingCollisions()
    {
        var project = SingleTrack(3);
        var track = project.Tracks[0];
        track.Kind = TrackKind.Guitar;
        track.StringTunings.Clear();
        foreach (var tuning in new[] { 64, 59, 55, 50, 45, 40 }) track.StringTunings.Add(tuning);
        var bar = track.Measures[0];
        TabNote Note(int s, int fret, bool ghost = false)
        {
            var n = new TabNote { StringIndex = s, Fret = fret, MidiValue = track.StringTunings[s] + fret, Velocity = 80, Ghost = ghost };
            return n;
        }
        bar.Cells[0].DurationDenominator = 4;
        bar.Cells[0].Notes.Add(Note(0, 2, true)); bar.Cells[0].Notes.Add(Note(1, 3, true)); bar.Cells[0].Notes.Add(Note(2, 4, true));   // ghost chord with sharps
        bar.Cells[4].DurationDenominator = 4;
        bar.Cells[4].Notes.Add(Note(2, 4)); bar.Cells[4].Notes.Add(Note(3, 3));                                                        // a second with an accidental
        bar.Cells[8].DurationDenominator = 4;
        bar.Cells[8].Notes.Add(new TabNote { StringIndex = 1, Fret = 2, MidiValue = 61, IsGraceNote = true, Velocity = 80 });
        bar.Cells[8].Notes.Add(Note(2, 4));                                                                                            // grace note before a sharpened note
        track.Measures[1].TimeSigNum = 3; track.Measures[1].TimeSigDenom = 4;
        track.Measures[1].Cells[0].DurationDenominator = 4;
        track.Measures[1].Cells[0].Notes.Add(Note(0, 1));
        var third = track.Measures[2];
        third.Cells[0].DurationDenominator = 4;                                                                                        // a tall ghost cluster (stacked thirds F-A-C-E):
        foreach (var (s, f) in new[] { (3, 3), (2, 2), (1, 1), (0, 0) }) third.Cells[0].Notes.Add(Note(s, f, true));                    // the stretched brackets stay clear of the heads
        third.Cells[4].DurationDenominator = 4;
        third.Cells[4].Notes.Add(Note(5, 2, true)); third.Cells[4].Notes.Add(Note(4, 4, true));                                        // ledger-line ghosts with sharps (bracket pad)
        third.Cells[8].DurationDenominator = 4;
        third.Cells[8].Notes.Add(new TabNote { StringIndex = 4, Fret = 0, MidiValue = 45, IsGraceNote = true, Velocity = 80 });
        third.Cells[8].Notes.Add(Note(5, 2, true));                                                                                    // grace note before a ghost on a ledger line
        // Two grace notes: the later one is drawn nearest the main note so they read left to right in time.
        bar.Cells[12].DurationDenominator = 4;
        bar.Cells[12].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 69, IsGraceNote = true, GraceOnsetOffsetSlots = -0.2, Velocity = 80 });
        bar.Cells[12].Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = 64, IsGraceNote = true, GraceOnsetOffsetSlots = -0.1, Velocity = 80 });
        bar.Cells[12].Notes.Add(Note(2, 5));
        // A long beat text at the end of a short bar runs on into the next bar: that bar's number stacks above it (it landed on the text).
        track.Measures[1].Cells[1].Text = "a long note that is wider than the whole bar it is written in, running on";
        // An up-arpeggio chord: its arrowhead above the staff is ink in the skyline (the bar number of a bar starting with it landed on the head).
        third.Cells[12].DurationDenominator = 4;
        third.Cells[12].Notes.Add(Note(0, 12)); third.Cells[12].Notes.Add(Note(1, 12));
        foreach (var arp in third.Cells[12].Notes) arp.Techniques.Add("ArpeggioUp");
        var graceBeat = new StaffNotationRenderer().CreateLayout(track, bar, 0, 16, 0, 0, 10).Beats.First(x => x.GraceNotes.Count == 2);
        var drawn = StaffNotationRenderer.GraceDrawOrder(graceBeat);
        Check("grace notes are drawn latest-first (nearest the main note), so they read left to right in time",
            drawn.Count == 2 && drawn[0].Source.GraceOnsetOffsetSlots > drawn[1].Source.GraceOnsetOffsetSlots);
        var drawnLabels = new[] { ("full", new System.Windows.Rect(10, 0, 14, 8)) };
        Check("a bend label is left out only when the same amount already overlaps it (a different amount, a touching or a separate box still prints)",
            TabEditorControl.DuplicateBendLabel(drawnLabels, "full", new System.Windows.Rect(16, 2, 14, 8))
            && !TabEditorControl.DuplicateBendLabel(drawnLabels, "1/2", new System.Windows.Rect(16, 2, 14, 8))
            && !TabEditorControl.DuplicateBendLabel(drawnLabels, "full", new System.Windows.Rect(24, 0, 14, 8))
            && !TabEditorControl.DuplicateBendLabel(drawnLabels, "full", new System.Windows.Rect(10, -9, 14, 8)));
        var found = LayoutAudit.Run(project, 0);
        Check("ghost chords, displaced seconds, grace notes and signature changes engrave without overlaps", found.Count == 0, string.Join("; ", found.Take(4).Select(c => c.ToString())));
    }

    private static void TestClefShapesAndChanges()
    {
        Check("the clef text maps to its shape (guitar G8 = treble, F4 bass, alto, tenor, percussion, 8vb flag)",
            TabEditorControl.ClefShapeOf(Clefs.Guitar) == (TabEditorControl.ClefShape.Treble, false) &&
            TabEditorControl.ClefShapeOf(Clefs.Bass) == (TabEditorControl.ClefShape.Bass, false) &&
            TabEditorControl.ClefShapeOf("Alto").Shape == TabEditorControl.ClefShape.Alto &&
            TabEditorControl.ClefShapeOf("Tenor").Shape == TabEditorControl.ClefShape.Tenor &&
            TabEditorControl.ClefShapeOf("neutral").Shape == TabEditorControl.ClefShape.Percussion &&
            TabEditorControl.ClefShapeOf("G8vb") == (TabEditorControl.ClefShape.Treble, true));
        var project = SingleTrack(4);
        var track = project.Tracks[0];
        track.Measures[2].Clef = Clefs.Bass;
        Check("a bar whose clef differs from the previous bar's is a clef change (also the bar that goes back)",
            !TabEditorControl.ClefChanges(track, 0) && !TabEditorControl.ClefChanges(track, 1) && TabEditorControl.ClefChanges(track, 2) && TabEditorControl.ClefChanges(track, 3));
        track.Measures[3].Clef = Clefs.Bass;
        Check("G and G8 are the same clef (no change drawn between them)", !TabEditorControl.ClefChanges(track, 1) && TabEditorControl.ClefShapeOf("G") == TabEditorControl.ClefShapeOf("G8"));
    }

    private static void TestSimileBarHidesLinesAndTies()
    {
        var project = SingleTrack(4);
        var track = project.Tracks[0];
        track.Kind = TrackKind.Guitar;
        track.StringTunings.Clear();
        foreach (var tuning in new[] { 64, 59, 55, 50, 45, 40 }) track.StringTunings.Add(tuning);
        TabNote Note(int s, int fret, bool tied = false) => new() { StringIndex = s, Fret = fret, MidiValue = track.StringTunings[s] + fret, Velocity = 80, Tied = tied };
        var one = track.Measures[0];
        one.Cells[12].DurationDenominator = 4;
        var palm = Note(2, 0); palm.Techniques.Add(TechniqueNames.PalmMute);
        one.Cells[12].Notes.Add(palm);
        one.Cells[12].Notes.Add(Note(3, 2));
        var simile = track.Measures[1];
        simile.SimileOneBar = true;
        simile.Cells[0].DurationDenominator = 4;
        var palm2 = Note(2, 0, tied: true); palm2.Techniques.Add(TechniqueNames.PalmMute);
        simile.Cells[0].Notes.Add(palm2);
        var passages = TabEditorControl.BuildPalmMutePassages(track, project);
        Check("a P.M. line ends at the bar line before a simile bar (the hidden copy's notes do not extend it)", passages.Count == 1 && passages[0].LastMeasure == 0);
        var ties = new StaffNotationRenderer().CreateLayout(track, one, 0, 16, 0, 0, 10).Ties;
        Check("no tie stub runs into a simile bar", ties.Count == 0);
        var back = new StaffNotationRenderer().CreateLayout(track, track.Measures[2], 2, 16, 0, 0, 10).Ties;
        Check("no tie stub points back into a simile bar", back.Count == 0);
        Check("slide and bend ends measure a ghost fret with its brackets (lines meet the digit edge, not its middle)",
            TabEditorControl.FretLabelWidthText(new TabNote { Fret = 9, Ghost = true }) == "(9)" && TabEditorControl.FretLabelWidthText(new TabNote { Fret = 9 }) == "9" && TabEditorControl.FretLabelWidthText(new TabNote { Fret = 9, Dead = true }) == "X");
    }
}
