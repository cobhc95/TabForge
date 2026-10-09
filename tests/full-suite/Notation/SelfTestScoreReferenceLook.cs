using System.Linq;
using System.Windows;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Views;
using TabForge.Views.EffectEditors;

namespace TabForge;

/// <summary>
/// The score as the reference engraves it: a short system starts at the left margin by its clef, a dead note has an x head
/// and no accidental, P.M. / vibrato / let ring above the TAB stack without overprinting (a one-beat let ring has no end tick),
/// "Harm." sits just above its TAB fret, and the harmonic hotkey removes an existing harmonic.
/// </summary>
public static partial class SelfTest
{
    private static void TestScoreReferenceLook()
    {
        var shortRow = ScorePageLayout.Create(40, 900, new[] { 120d, 120d });
        Check("a one- or two-bar song starts at the left margin next to its clef (never centred)", shortRow.Systems[0].X == 40 && shortRow.Systems[0].Width < 900);

        var project = SingleTrack(2);
        var track = project.Tracks[0];
        track.Kind = TrackKind.Guitar;
        track.StringTunings.Clear();
        foreach (var tuning in new[] { 64, 59, 55, 50, 45, 40 }) track.StringTunings.Add(tuning);
        TabNote Note(int s, int fret) => new() { StringIndex = s, Fret = fret, MidiValue = track.StringTunings[s] + fret, Velocity = 80 };
        var bar = track.Measures[0];
        foreach (var i in new[] { 0, 4, 8, 12 }) bar.Cells[i].DurationDenominator = 4;
        bar.Cells[0].Notes.Add(Note(1, 5));
        foreach (var t in new[] { "PalmMute", "LetRing", "Vibrato" }) bar.Cells[0].Notes[0].Techniques.Add(t);
        bar.Cells[4].Notes.Add(Note(1, 7));
        bar.Cells[4].Notes[0].Techniques.Add("Harmonic");
        var second = track.Measures[1];                                      // a harmonic on its own: "Harm." right over the fret
        second.Cells[0].DurationDenominator = 4;
        second.Cells[0].Notes.Add(Note(0, 12));
        second.Cells[0].Notes[0].Techniques.Add("Harmonic");
        bar.Cells[8].Notes.Add(Note(0, 2));                                  // F#: a dead note there takes no sharp
        bar.Cells[8].Notes[0].Dead = true;
        bar.Cells[12].Notes.Add(Note(1, 8));

        var deadBeat = new StaffNotationRenderer().CreateLayout(track, bar, 0, 16, 0, 0, 10).Beats.First(b => b.Notes.Any(n => n.Source.Dead));
        Check("a dead note is an x head with no accidental", deadBeat.Notes.All(n => n.Accidental is null));

        var found = LayoutAudit.Run(project, 0);
        Check("P.M., let ring, vibrato, a technique label and a dynamic stack in their own rows between the staff and the TAB (no overprint)", found.Count == 0, string.Join("; ", found.Take(4).Select(c => c.ToString())));

        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, Appearance = { DarkPaper = false }, HideCursor = true };
        editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        editor.Arrange(new Rect(editor.DesiredSize));
        editor.UpdateLayout();
        var items = new List<LayoutAudit.Item>();
        foreach (var (_, drawing) in editor.AuditSystemDrawings()) LayoutAudit.Walk(drawing, System.Windows.Media.Matrix.Identity, items);
        var texts = items.Where(i => i.Kind == LayoutAudit.Kind.Text).ToList();
        var harm = texts.Where(i => i.Label == "Harm.").OrderByDescending(i => i.Box.X).FirstOrDefault();
        var seven = texts.FirstOrDefault(i => i.Label == "12");   // the TAB fret on the top string
        Check("\"Harm.\" sits just above its TAB fret, not under the staff", harm is not null && seven is not null
            && harm.Box.Bottom <= seven.Box.Top + 0.5 && seven.Box.Top - harm.Box.Bottom < 16 && Math.Abs((harm.Box.Left + harm.Box.Right) / 2 - (seven.Box.Left + seven.Box.Right) / 2) < 8,
            $"Harm. {harm?.Box} fret {seven?.Box}");
        var letRing = texts.FirstOrDefault(i => i.Label == "let ring");
        var staffBottom = texts.Where(i => i.Label == "4").Max(i => i.Box.Bottom);   // the time signature fills the staff
        Check("let ring stays between the staff and the TAB (the gap grows for the stacked rows)", letRing is not null && letRing.Box.Top > staffBottom, $"let ring {letRing?.Box}, staff bottom {staffBottom:0}");
        Check("a one-beat let ring is the label alone (no stray end tick after it)", letRing is not null && !items.Any(i => i.Kind == LayoutAudit.Kind.Line
            && i.Box.Left >= letRing.Box.Right - 1 && i.Box.Left <= letRing.Box.Right + 14 && i.Box.Height > 6 && i.Box.Width < 2
            && i.Box.Top < letRing.Box.Bottom && i.Box.Bottom > letRing.Box.Top));

        // Y on a note that has a harmonic removes it (one undo step); on a plain note it opens the editor.
        var rock = TemplateFactory.Create("Rock Band");
        var pipeline = new EePipeline(rock);
        var rockEditor = new TabEditorControl { Project = rock, SelectedTrackIndex = 0, EditHost = pipeline };
        var cell = rock.Tracks[0].Measures[0].Cells[0];
        cell.DurationDenominator = 4;
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = 1, Fret = 12, MidiValue = rock.Tracks[0].PitchOf(1, 12), Velocity = 95 });
        rockEditor.SetPosition(0, 0, 1, false);
        var flow = new EffectEditorFlow(new EeHost(rockEditor.Effects));
        Check("the harmonic hotkey on a plain note opens the editor (nothing removed)", !flow.ToggledOff("Note.Harmonic"));
        cell.Notes[0].Techniques.Add("Harmonic");
        var undoBefore = pipeline.Session.Undo.UndoCount;
        Check("the harmonic hotkey on a harmonic removes it in one undo step; the editor command never toggles",
            !flow.ToggledOff("Note.HarmonicEditor") && cell.Notes[0].Techniques.Contains("Harmonic")
            && flow.ToggledOff("Note.Harmonic") && !cell.Notes[0].Techniques.Contains("Harmonic") && pipeline.Session.Undo.UndoCount == undoBefore + 1);
    }
}
