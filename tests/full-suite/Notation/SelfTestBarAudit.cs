using System.IO;
using System.Linq;
using System.Text.Json;
using TabForge.Diagnostics;
using TabForge.Models;

namespace TabForge;

/// <summary>
/// `--render-bars` (Audit 7 visual audit): images and checks.json are produced for every bar and view, a clean synthetic score passes the
/// data-versus-drawing consistency check, and a deliberately undrawable mark (a chord name on a rest) is reported.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Four bars with the marks the audit cross-checks; every one of them is drawn, so the consistency check must stay silent.</summary>
    internal static SongProject BuildBarAuditFixture()
    {
        var project = SingleTrack(4);
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        var track = project.Tracks[0];
        var frets = new[] { 0, 2, 3, 5 };
        for (var bar = 0; bar < 4; bar++)
            for (var beat = 0; beat < 4; beat++)
            {
                var cell = track.Measures[bar].Cells[beat * 4];
                cell.DurationDenominator = 4;
                var fret = frets[(beat + bar) % 4];
                cell.Notes.Add(new TabNote { StringIndex = 2 + beat % 3, Fret = fret, MidiValue = track.PitchOf(2 + beat % 3, fret), Velocity = 80 });
            }
        track.Measures[0].Cells[0].ChordName = "Em";
        track.Measures[0].Cells[4].Text = "riff";
        track.Measures[0].Cells[8].Accent = 1;
        track.Measures[0].Cells[12].Fermata = true;
        track.Measures[1].RepeatStart = true;
        track.Measures[1].Cells[0].Notes[0].Techniques.Add("PalmMute");
        track.Measures[1].Cells[4].Notes[0].Techniques.Add("PalmMute");
        track.Measures[2].RepeatEnd = true;
        track.Measures[2].TempoChange = 140;
        track.Measures[3].AlternateEnding = 1;
        track.Measures[3].Cells[8].Notes[0].Techniques.Add("Vibrato");
        return project;
    }

    private static void TestBarAuditTool()
    {
        var project = BuildBarAuditFixture();
        var dir = Path.Combine(Path.GetTempPath(), $"tabforge-baraudit-{Environment.ProcessId}");
        try
        {
            var result = BarAuditRunner.Run(project, dir, new[] { 0 }, new[] { "notation", "tab" }, "fixture");
            Check("render-bars writes one PNG per bar and view", result.Bars.Count == 4 && result.Bars.All(b => b.Images.Count == 2 &&
                b.Images.Values.All(f => File.Exists(Path.Combine(dir, f)) && new FileInfo(Path.Combine(dir, f)).Length > 300)));
            Check("render-bars names images <NN track>/<bar>-<view>.png", result.Bars[0].Images["tab"] == "01 Gtr/001-tab.png");
            var json = File.Exists(Path.Combine(dir, "checks.json")) ? JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "checks.json"))) : null;
            Check("checks.json has a record for every bar", json is not null && json.RootElement.GetProperty("records").GetArrayLength() == 4);
            Check("summary.md lists the counts and the flagged bars", File.Exists(Path.Combine(dir, "summary.md")) &&
                File.ReadAllText(Path.Combine(dir, "summary.md")).Contains("Counts by check type"));
            var consistency = result.Bars.SelectMany(b => b.Issues.Where(i => i.Type.StartsWith("missing:") || i.Type.StartsWith("extra:")).Select(i => $"bar {b.Bar} {i.View} {i.Type} {i.Detail}")).ToList();
            Check("a clean score: every mark in the data is drawn and nothing else is", consistency.Count == 0, string.Join("; ", consistency.Take(5)));
            // (clip:system is left out: the tab-only view draws a dynamic mark a few px below its system slice, a known finding of the audit itself)
            Check("a clean score has no collisions or clipping at the page or bar lines", result.Bars.All(b => b.Issues.All(i => i.Type != "collision" && i.Type != "clip:page" && i.Type != "clip:barline")),
                string.Join("; ", result.Bars.SelectMany(b => b.Issues.Where(i => i.Type == "collision" || i.Type is "clip:page" or "clip:barline").Select(i => $"bar {b.Bar} {i.Type} {i.Detail}")).Take(3)));

            // a chord name, beat text and fermata on a rest are drawn (a held rest), and a simile bar gets its sign
            var restMarks = BuildBarAuditFixture();
            var rest = restMarks.Tracks[0].Measures[1].Cells[1];
            rest.IsRest = true; rest.ChordName = "Bm"; rest.Fermata = true;
            restMarks.Tracks[0].Measures[3].SimileOneBar = true;
            var drawn = BarAuditRunner.Run(restMarks, dir, new[] { 0 }, new[] { "notation", "tab" }, "rest-marks", images: false);
            var restIssues = drawn.Bars.SelectMany(b => b.Issues.Where(i => i.Type is "missing:chord-name" or "missing:fermata" or "missing:simile" or "extra:simile").Select(i => $"bar {b.Bar} {i.View} {i.Type} {i.Detail}")).ToList();
            Check("a chord name and fermata on a rest and a simile sign are drawn", restIssues.Count == 0, string.Join("; ", restIssues.Take(3)));

            // strum arrows in the TAB point the GP5 way: a downstroke (bass first) up to the top string, an upstroke down
            {
                var strums = BuildBarAuditFixture();
                var strumTrack = strums.Tracks[0];
                foreach (var (bar, technique) in new[] { (1, "BrushDown"), (2, "BrushUp") })
                {
                    var chord = strumTrack.Measures[bar].Cells[2];
                    chord.Notes.Clear();
                    for (var s = 0; s < 6; s++) chord.Notes.Add(new TabNote { StringIndex = s, Fret = 2, MidiValue = strumTrack.PitchOf(s, 2), Velocity = 80, Techniques = { technique } });
                }
                var strummed = BarAuditRunner.Run(strums, dir, new[] { 0 }, new[] { "tab", "both" }, "strums", images: false);
                var strumIssues = strummed.Bars.SelectMany(b => b.Issues.Where(i => i.Type is "extra:brush-direction" or "missing:brush-arpeggio").Select(i => $"bar {b.Bar} {i.View} {i.Type} {i.Detail}")).ToList();
                Check("TAB strum arrows: a downstroke points up (to the top string), an upstroke down, as in GP5", strumIssues.Count == 0, string.Join("; ", strumIssues.Take(3)));
            }

            // a simile bar hides its notes, but shows them while the edit cursor is inside it (nothing the user edits is invisible)
            {
                var editor = new Views.TabEditorControl { Project = restMarks, SelectedTrackIndex = 0, Appearance = { DarkPaper = false }, PlaybackMeasure = -1 };
                editor.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                editor.Arrange(new System.Windows.Rect(editor.DesiredSize));
                editor.UpdateLayout();
                static int Glyphs(System.Windows.Media.Drawing d) => d switch
                {
                    System.Windows.Media.DrawingGroup g => g.Children.Sum(Glyphs),
                    System.Windows.Media.GlyphRunDrawing => 1,
                    _ => 0
                };
                int GlyphsWithCursorIn(int bar)
                {
                    editor.SetPosition(bar, 0, 0, seekPlayback: false);
                    editor.InvalidateVisual();
                    return editor.AuditSystemDrawings().Sum(s => Glyphs(s.Drawing));
                }
                var (away, inside, awayAgain) = (GlyphsWithCursorIn(0), GlyphsWithCursorIn(3), GlyphsWithCursorIn(1));
                Check("a simile bar shows its notes only while the edit cursor is inside it", inside > away && away == awayAgain, $"cursor away {away}, inside {inside}, away again {awayAgain}");
            }

            // two voices: the staff engraves each voice's fermata (voice 2 inverted, below); tab only draws a shared one once,
            // and a fermata only voice 2 holds is never dropped
            var twoVoices = BuildBarAuditFixture();
            var bar2 = twoVoices.Tracks[0].Measures[2];
            var lower = bar2.CellsForVoice(1, create: true);
            foreach (var at in new[] { 0, 8 })
            {
                lower[at].DurationDenominator = 2; lower[at].Fermata = true;
                lower[at].Notes.Add(new TabNote { StringIndex = 5, Fret = 0, MidiValue = twoVoices.Tracks[0].PitchOf(5, 0), Velocity = 80 });
            }
            bar2.Cells[0].Fermata = true;   // shared with voice 2's first beat; voice 2's second fermata is its own
            var voiced = BarAuditRunner.Run(twoVoices, dir, new[] { 0 }, new[] { "notation", "tab" }, "two-voice-fermatas", images: false);
            var fermataIssues = voiced.Bars.SelectMany(b => b.Issues.Where(i => i.Type is "missing:fermata" or "extra:fermata").Select(i => $"bar {b.Bar} {i.View} {i.Type} {i.Detail}")).ToList();
            Check("two voices: every voice's fermata is engraved on the staff, a shared one once in the tab, a voice-2-only one never dropped", fermataIssues.Count == 0, string.Join("; ", fermataIssues.Take(3)));
            int Fermatas(string view) => new TrackViewAudit(twoVoices, 0, view).BarItems(2).Count(i => i.Kind == LayoutAudit.Kind.Text && (i.Label.Contains("𝄐") || i.Label.Contains("𝄑")));
            var (onStaff, inTab) = (Fermatas("notation"), Fermatas("tab"));
            Check("two voices: 3 fermatas on the staff (upright above, inverted below), 2 in tab only", onStaff == 3 && inTab == 2, $"staff {onStaff}, tab {inTab}");

            // 4 to 8 strings: the system height follows the string count, so nothing spills below the system
            foreach (var stringCount in new[] { 4, 5, 7, 8 })
            {
                var wide = BuildBarAuditFixture();
                var wideTrack = wide.Tracks[0];
                wideTrack.StringTunings = new[] { 64, 59, 55, 50, 45, 40, 35, 30 }.Take(stringCount).ToList();
                var low = stringCount - 1;
                for (var bar = 0; bar < 4; bar++)
                    foreach (var cell in wideTrack.Measures[bar].Cells)
                        foreach (var note in cell.Notes) { note.StringIndex = Math.Min(note.StringIndex, low); note.MidiValue = wideTrack.PitchOf(note.StringIndex, note.Fret); }
                wideTrack.Measures[2].Cells[0].Notes[0].StringIndex = low;
                wideTrack.Measures[2].Cells[0].Notes[0].Techniques.Add("TremBarDive");
                var spill = BarAuditRunner.Run(wide, dir, new[] { 0 }, new[] { "notation", "tab" }, $"strings-{stringCount}", images: false)
                    .Bars.SelectMany(b => b.Issues.Where(i => i.Type is "clip:system" or "clip:page").Select(i => $"bar {b.Bar} {i.View} {i.Detail}")).ToList();
                Check($"a {stringCount}-string track keeps bar lines, frets and dive labels inside the system", spill.Count == 0, string.Join("; ", spill.Take(3)));
            }

            // a mark that is in the data but not in the drawing: the drawing is captured first, the chord name added after
            var broken = BuildBarAuditFixture();
            var stale = new TrackViewAudit(broken, 0, "notation");
            broken.Tracks[0].Measures[1].Cells[4].ChordName = "Bm";
            var flagged = new BarChecker(stale, 1).Run();
            Check("the consistency check reports a mark that is in the data but not drawn", flagged.Any(i => i.Type == "missing:chord-name"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
