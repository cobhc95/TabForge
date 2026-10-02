using TabForge.Models;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

/// <summary>Notation staff polish from the full review (A7-V21 harmonics, A7-V22 flags, A7-V26 slide strokes).</summary>
public static partial class SelfTest
{
    private static double StaffNoteY(string? technique, int midiOffset)
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var track = project.Tracks[0];
        var bar = track.Measures[0];
        var fretted = track.PitchOf(1, 5);
        var note = new TabNote { StringIndex = 1, Fret = 5, MidiValue = fretted + midiOffset, Velocity = 90 };
        if (technique is not null) note.Techniques.Add(technique);
        bar.Cells[0] = new TabCell { DurationDenominator = 4, Notes = { note } };
        return new StaffNotationRenderer().CreateLayout(track, bar, 0, 16, 0, 0, 10).Beats.First(b => b.CellIndex == 0).Notes[0].Y;
    }

    private static void TestPickStrokeClearance()
    {
        var plain = new TabCell { Notes = { new TabNote { StringIndex = 5, Fret = 3 } } };
        var down = new TabCell { Notes = { new TabNote { StringIndex = 5, Fret = 3, Techniques = { "PickDown" } } } };
        Check("pick stroke: the mark sits 5 px lower than before, only on beats that have one", ScoreMarkText.PickStrokeShift(down) == 5 && ScoreMarkText.PickStrokeShift(plain) == 0);
        Check("pick stroke: the lyric row under it moves down with it", ScoreMarkText.FingeringHeight(down) >= ScoreMarkText.FingeringHeight(plain) + 5);
    }

    private static void TestSlideStrokesOnStaff()
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var track = project.Tracks[0];
        var bar = track.Measures[0];
        TabNote Note(int fret, params string[] techniques)
        {
            var n = new TabNote { StringIndex = 1, Fret = fret, MidiValue = track.PitchOf(1, fret), Velocity = 90 };
            foreach (var t in techniques) n.Techniques.Add(t);
            return n;
        }
        for (var i = 0; i < bar.Cells.Count; i++) bar.Cells[i] = new TabCell();
        bar.Cells[0] = new TabCell { DurationDenominator = 4, Notes = { Note(5, "SlideInBelow") } };
        bar.Cells[4] = new TabCell { DurationDenominator = 4, Notes = { Note(5, "ShiftSlide") } };
        bar.Cells[8] = new TabCell { DurationDenominator = 4, Notes = { Note(9) } };
        bar.Cells[12] = new TabCell { DurationDenominator = 4, Notes = { Note(7, "SlideOutDown") } };
        var layout = new StaffNotationRenderer().CreateLayout(track, bar, 0, 16, 0, 0, 10);
        var heads = layout.Beats.ToDictionary(b => b.CellIndex, b => b.Notes[0]);
        var slides = layout.Slides;
        Check("slides: slide in, shift slide and slide out each get a stroke in the notation staff", slides.Count == 3, slides.Count.ToString());
        if (slides.Count != 3) return;
        Check("slides: a slide in leads into the head from the left and below", slides[0].X2 < heads[0].X && slides[0].Y1 > heads[0].Y);
        Check("slides: a shift slide is a stroke from the first head to the next one", slides[1].X1 > heads[4].X && slides[1].X2 < heads[8].X && Math.Abs(slides[1].Y1 - heads[4].Y) < 0.01 && Math.Abs(slides[1].Y2 - heads[8].Y) < 0.01);
        Check("slides: a slide out downwards trails from the head to the right and down", slides[2].X1 > heads[12].X && slides[2].Y2 > slides[2].Y1);
    }

    private static void TestFlagShape()
    {
        var down = StaffNotationRenderer.FlagGeometry(100, 50, up: true).Bounds;   // stem up: the flag hangs down from the stem tip
        var up = StaffNotationRenderer.FlagGeometry(100, 50, up: false).Bounds;
        Check("flags: an eighth flag is a full flag (about 4 px wide and 13 px long, body 2.8 px), not a thin hook",
            down.Width >= 3.6 && down.Height >= 12.5 && down.Top >= 50 - 0.01 && up.Width >= 3.6 && up.Height >= 12.5 && up.Bottom <= 50 + 0.01,
            $"{down.Width:0.0}x{down.Height:0.0}");
    }

    private static void TestHarmonicNoteheadPositions()
    {
        var plain = StaffNoteY(null, 0);
        Check("harmonics: tap, artificial, pinch and semi harmonics are written at the fretted pitch, not the high sounding one",
            new[] { "TapHarmonic", "ArtificialHarmonic", "PinchHarmonic", "SemiHarmonic" }.All(t => Math.Abs(StaffNoteY(t, 24) - plain) < 0.01));
        Check("harmonics: a natural harmonic keeps its stored pitch", StaffNoteY("Harmonic", 12) < plain - 1);
        Check("harmonics: the techniques the reference writes at the fret are exactly the four",
            StaffNotationRenderer.IsWrittenAtFret(new[] { "PinchHarmonic" }) && !StaffNotationRenderer.IsWrittenAtFret(new[] { "Harmonic" }));
    }
}
