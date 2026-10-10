using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Row stacking of the markings around the staff: every mark claims its box and the next one is placed outside it.
/// The skyline itself, then an engraved bar that piles an accent, a fermata, a chord name, beat text, a tuplet and a
/// let-ring span onto the same beats (with low ledger notes and a dynamic) must come out with no two marks overlapping.
/// </summary>
public static partial class SelfTest
{
    private static void TestMarkStacking()
    {
        var sky = new MarkSkyline();
        sky.Claim(10, 30, 50, 60);
        var first = sky.PlaceAbove(12, 28, 10, 62);      // wants 52..62, collides with the claimed 50..60
        var second = sky.PlaceAbove(12, 28, 10, 62);     // the next mark stacks outside the first
        Check("a mark over a claimed box is placed above it", first + 10 <= 50, $"top {first:0.0}");
        Check("the next mark stacks outside the previous one", second + 10 <= first, $"{second:0.0} vs {first:0.0}");
        Check("a mark beside the claimed boxes keeps its row", Math.Abs(sky.PlaceAbove(100, 110, 10, 62) - 52) < 0.01);
        var below = new MarkSkyline();
        below.Claim(0, 20, 0, 12);
        Check("below stacking moves down past the claim", below.PlaceBelow(2, 18, 8, 4) >= 12);

        var project = SingleTrack(2);
        var track = project.Tracks[0];
        track.Kind = TrackKind.Guitar;
        track.StringTunings.Clear();
        foreach (var tuning in new[] { 64, 59, 55, 50, 45, 40 }) track.StringTunings.Add(tuning);
        var bar = track.Measures[0];
        // Beats on high and low (ledger) notes; the first carries several above-staff marks at once.
        for (var beat = 0; beat < 4; beat++)
        {
            var cell = bar.Cells[beat * 4];
            cell.DurationDenominator = 8;
            cell.Notes.Add(new TabNote { StringIndex = beat % 2 == 0 ? 0 : 5, Fret = beat % 2 == 0 ? 17 : 0, MidiValue = beat % 2 == 0 ? 81 : 40, Velocity = 60 + beat * 20 });
        }
        bar.Cells[0].Accent = 2; bar.Cells[0].Fermata = true; bar.Cells[0].ChordName = "Am7"; bar.Cells[0].Text = "riff";
        bar.Cells[4].Accent = 1;
        bar.Cells[8].Notes[0].Techniques.Add("LetRing");
        bar.Cells[12].Notes[0].Techniques.Add("LetRing");
        bar.SectionName = "Verse";
        track.Measures[1].AlternateEnding = 1;
        var found = LayoutAudit.Run(project, 0);
        Check("piled-up marks stack without overlapping", found.Count == 0, string.Join("; ", found.Take(3).Select(c => c.ToString())));
    }

    // A hammer-on looks its neighbours up in the cell list of its own voice, so a second voice
    // with more cells than the first threw ArgumentOutOfRange while drawing (the editor crashed on opening the song).
    private static void TestVoice2HopoSlurWithLongerVoice()
    {
        var project = SingleTrack(1);
        var track = project.Tracks[0];
        track.Kind = TrackKind.Guitar;
        track.StringTunings.Clear();
        foreach (var tuning in new[] { 64, 59, 55, 50, 45, 40 }) track.StringTunings.Add(tuning);
        var bar = track.Measures[0];
        bar.Cells.RemoveRange(8, bar.Cells.Count - 8);                        // voice 1: 8 cells
        bar.Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList(); // voice 2: 16 cells
        bar.Voice2Cells[10].Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = 64, Velocity = 90, Techniques = { "HOPOOrigin" } });
        bar.Voice2Cells[13].Notes.Add(new TabNote { StringIndex = 1, Fret = 7, MidiValue = 66, Velocity = 90, Techniques = { "HOPODestination" } });
        bar.Voice2Cells[15].Notes.Add(new TabNote { StringIndex = 2, Fret = 3, MidiValue = 58, Velocity = 90, Techniques = { "HOPO" } });
        var failure = "";
        try { LayoutAudit.Run(project, 0); }
        catch (Exception ex) { failure = ex.GetType().Name + ": " + ex.Message; }
        Check("a second voice longer than the first engraves without an index error", failure.Length == 0, failure);

        // Containment: in the running app a drawing error in one system is contained (the other systems still draw, no
        // exception reaches the dispatcher); command-line runs rethrow so audits still fail loudly.
        var rethrow = TabEditorControl.RethrowRenderFailures;
        var before = TabEditorControl.ContainedRenderFailures;
        var escaped = "";
        try
        {
            TabEditorControl.RethrowRenderFailures = false;
            TabEditorControl.RenderFaultInjection = system => { if (system == 0) throw new InvalidOperationException("injected drawing fault"); };
            try { LayoutAudit.Run(project, 0); }
            catch (Exception ex) { escaped = ex.GetType().Name + ": " + ex.Message; }
            Check("a drawing error in one system is contained in the app", escaped.Length == 0 && TabEditorControl.ContainedRenderFailures > before, escaped);
            TabEditorControl.RethrowRenderFailures = true;
            var thrown = false;
            try { LayoutAudit.Run(project, 0); }
            catch (InvalidOperationException) { thrown = true; }
            Check("a drawing error still fails a command-line run", thrown);
        }
        finally
        {
            TabEditorControl.RenderFaultInjection = null;
            TabEditorControl.RethrowRenderFailures = rethrow;
        }
    }
}
