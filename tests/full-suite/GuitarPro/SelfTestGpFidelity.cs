using System.IO;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

// Score fidelity: required group `gp-fidelity` (--require gp-fidelity). Each family below has its own fixture, checks the
// reopened clean .gp EXACTLY (no tolerance), and goes with the removal or narrowing of the loss allowance it used to need.
// Clean means: no embedded TabForge project and no .tfaudio beside it (Gf*Clean verifies that before reading the file back).
public static partial class SelfTest
{
    /// <summary>Exports clean (verified: no embedded project, no sidecar), reads the file back through the importer only.</summary>
    private static SongProject GfClean(SongProject song, string folder, string name)
    {
        var path = Path.Combine(folder, name + ".gp");
        GuitarProExporter.Save(song, path, embedProject: false);
        Check($"fidelity [{name}]: the clean .gp holds no embedded TabForge project", GuitarProExporter.TryReadEmbedded(path) is null);
        Check($"fidelity [{name}]: no .tfaudio sidecar beside the clean .gp", !File.Exists(AudioDataFile.PathFor(path)));
        return GuitarProImporter.Import(path);
    }

    private static void TestGpFidelity()
    {
        var folder = RtFolder();
        try
        {
            Guard(() => GpFidelityFingeringTenutoMute(folder));
            Guard(() => GpFidelityBendCurves(folder));
            Guard(() => GpFidelityLegatoRasgueado(folder));
            Guard(() => GpFidelityGhostAccent(folder));
            Guard(() => GpFidelityGhostStaccato(folder));
            Guard(() => GpFidelityPickSlidesAndRecord(folder));
            Guard(() => GpFidelityMidiShortTripletBars(folder));
            Guard(() => GpFidelityFixtureSet(folder));
            Guard(() => GpFidelityMetadataSafety(folder));
            Guard(() => GpFidelityLossyExportPreflight(folder));
            Guard(() => GpFidelityExportDialogFlow(folder));
        }
        finally { RtCleanup(folder); }
    }

    // ---- family 1: fingering, tenuto, per-note palm mute, left-hand tap, colour (allowances removed) ----
    // REGION:fidelity-marks
    private static void GpFidelityFingeringTenutoMute(string folder)
    {
        var song = GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Notes[0].LeftHandFinger = 2;
            var chord = GfPut(t, 0, 4, 4, RtNote(t, 1, 5), RtNote(t, 2, 4));
            chord.Notes[0].RightHandFinger = 1; chord.Notes[1].RightHandFinger = 3; chord.Notes[1].LeftHandFinger = 4;
            GfPut(t, 0, 8, 4, RtNote(t, 1, 7)).Tenuto = true;
            GfPut(t, 0, 12, 4, RtNote(t, 1, 8, 95, "PalmMute"), RtNote(t, 2, 6));
            t.ColorHex = "#12AB34";
        });
        song.Tracks[0].Measures[0].Cells[12].Notes.Add(new TabNote { StringIndex = 3, Fret = 2, MidiValue = song.Tracks[0].PitchOf(3, 2) });   // a third tone, no mark
        song.Tracks[0].Measures[0].Cells[12].Notes[2].Techniques.Add("LeftTap");
        var back = GfClean(song, folder, "marks");
        var beats = GfBeats(back);
        Check("fidelity [marks]: left-hand finger of a single note is kept", beats[0].Notes[0].LeftHandFinger == 2, $"{beats[0].Notes[0].LeftHandFinger}");
        Check("fidelity [marks]: right and left fingers of a chord are kept per note (none stays none)",
            beats[1].Notes.OrderBy(n => n.StringIndex).Select(n => $"{n.RightHandFinger}/{n.LeftHandFinger}").SequenceEqual(new[] { "1/", "3/4" }), string.Join(",", beats[1].Notes.Select(n => $"{n.RightHandFinger}/{n.LeftHandFinger}")));
        Check("fidelity [marks]: a beat with no fingering has none (no spurious addition)", beats[2].Notes[0].LeftHandFinger is null && beats[2].Notes[0].RightHandFinger is null && !beats[0].Tenuto);
        Check("fidelity [marks]: tenuto is kept on its beat and only there", beats[2].Tenuto && !beats[0].Tenuto && !beats[1].Tenuto && !beats[3].Tenuto);
        var chord4 = beats[3].Notes.OrderBy(n => n.StringIndex).ToList();
        Check("fidelity [marks]: palm mute stays on the one muted note of a chord", chord4.Count == 3 && chord4.Count(n => n.Techniques.Contains("PalmMute")) == 1 && chord4[0].Techniques.Contains("PalmMute"),
            string.Join(" | ", chord4.Select(n => string.Join("+", n.Techniques))));
        Check("fidelity [marks]: a left-hand tap comes back as the LeftTap technique", chord4.Any(n => n.Techniques.Contains("LeftTap")));
        Check("fidelity [marks]: track colour is kept", back.Tracks[0].ColorHex.Equals("#12AB34", StringComparison.OrdinalIgnoreCase), back.Tracks[0].ColorHex);
        var profile = RtGpClean();
        foreach (var removed in new[] { "note.lhFinger", "note.rhFinger", "beat.tenuto", "note.technique:Tenuto", "note.technique:PalmMute.extra", "note.technique:LeftTap.missing" })
            Check($"fidelity: the allowance '{removed}' is gone", profile.ReasonFor(removed, new RtDiff(removed, "k", "x", "y")) is null);
        Check("fidelity: no wildcard allowance is left in the clean .gp profile", !profile.Losses.Keys.Any(k => k.EndsWith('*')));
    }
    // ENDREGION

    // ---- family: ghost note beside an accent (found by the Guitar Pro 8 re-save of fixture 05) ----
    // REGION:fidelity-ghost-accent
    /// <summary>The marks of each note of a clean .gp's gpif, in document order: (hasAntiAccent, Accent value or 0).</summary>
    private static List<(bool Ghost, int Accent)> GfGpifNoteMarks(byte[] gp)
    {
        var score = GuitarProExporter.ReadZip(gp).First(p => p.Name == GuitarProExporter.ScoreEntry).Data;
        var doc = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(score));
        return doc.Descendants("Note").Select(n => (n.Element("AntiAccent") is not null, int.TryParse((string?)n.Element("Accent"), out var a) ? a : 0)).ToList();
    }

    private static void GpFidelityGhostAccent(string folder)
    {
        var song = GfSong(1, t =>
        {
            var normal = GfPut(t, 0, 0, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); normal.Notes[0].Ghost = true; normal.Accent = 1;
            var heavy = GfPut(t, 0, 4, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); heavy.Notes[1].Ghost = true; heavy.Accent = 2;
            var tenuto = GfPut(t, 0, 8, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); tenuto.Notes[0].Ghost = true; tenuto.Tenuto = true;
            var allGhost = GfPut(t, 0, 12, 4, RtNote(t, 1, 5), RtNote(t, 2, 4)); foreach (var n in allGhost.Notes) n.Ghost = true; allGhost.Accent = 1;
        });
        var marks = GfGpifNoteMarks(GuitarProExporter.ToBytes(song, embedProject: false));
        var text = string.Join(" ", marks.Select(m => $"{(m.Ghost ? "G" : "-")}{m.Accent}"));
        Check("ghost+accent: a ghost note beside an accented one is written as a ghost mark with NO accent, the other note keeps the accent (Guitar Pro 8 holds one or the other)",
            marks.Count == 8 && marks[0] == (true, 0) && marks[1] == (false, 8), text);
        Check("ghost+accent: the same for a heavy accent (ghost on the second note) and for tenuto",
            marks[2] == (false, 4) && marks[3] == (true, 0) && marks[4] == (true, 0) && marks[5] == (false, 16), text);
        Check("ghost+accent: a beat of ghost notes only keeps the ghost marks and writes no accent", marks[6] == (true, 0) && marks[7] == (true, 0), text);
        Check("ghost+accent: no written note carries both marks", marks.All(m => !(m.Ghost && m.Accent != 0)), text);
        var back = GfClean(song, folder, "ghost-accent");
        var beats = GfBeats(back);
        string Row(TabCell c) => string.Join(",", c.Notes.OrderBy(n => n.StringIndex).Select(n => n.Ghost ? "g" : "n"));
        Check("ghost+accent: reopened, each ghost note is still a ghost note and the beat keeps its accent / heavy accent / tenuto",
            Row(beats[0]) == "g,n" && beats[0].Accent == 1 && Row(beats[1]) == "n,g" && beats[1].Accent == 2 && Row(beats[2]) == "g,n" && beats[2].Tenuto, $"{Row(beats[0])}/{beats[0].Accent} {Row(beats[1])}/{beats[1].Accent} {Row(beats[2])}/{beats[2].Tenuto}");
        Check("ghost+accent: a beat of ghost notes only reopens as ghost notes and loses the accent (the one narrow loss)", Row(beats[3]) == "g,g" && beats[3].Accent == 0, $"{Row(beats[3])}/{beats[3].Accent}");
        var report = GpExportPreflight.Analyze(song);
        Check("ghost+accent: the preflight names exactly that loss (bar 1) and nothing for the mixed beats",
            report.Losses.Count == 1 && report.Losses[0].Feature.StartsWith("Accent, tenuto or staccato on ghost notes only", StringComparison.Ordinal) && report.Losses[0].Count == 1 && report.Losses[0].Where.Contains("bar 1", StringComparison.Ordinal), report.Summary());
        allGhost(song).Accent = 0;
        Check("ghost+accent: with the ghost-only beat unmarked, nothing is asked", !GpExportPreflight.Analyze(song).ShouldAskFor(GpExportKind.Export));
        // a legacy per-note Tenuto tag: on the ghost note alone it is lost (listed); on the plain note it is written there (nothing listed); a rest asks nothing
        var legacy = GfSong(1, t =>
        {
            var onGhost = GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Tenuto"), RtNote(t, 2, 2)); onGhost.Notes[0].Ghost = true;
            var onPlain = GfPut(t, 0, 4, 4, RtNote(t, 1, 3), RtNote(t, 2, 2, 95, "Tenuto")); onPlain.Notes[0].Ghost = true;
            GfPut(t, 0, 8, 4).Accent = 1;
        });
        var legacyLosses = GpExportPreflight.Analyze(legacy).Losses;
        Check("ghost+accent: a legacy Tenuto tag on the ghost note alone is listed (once); on the plain note, or an accented rest, is not",
            legacyLosses.Count == 1 && legacyLosses[0].Feature.StartsWith("Accent, tenuto or staccato on ghost notes only", StringComparison.Ordinal) && legacyLosses[0].Count == 1,
            string.Join(",", legacyLosses.Select(l => $"{l.Feature} x{l.Count}")));
        var legacyMarks = GfGpifNoteMarks(GuitarProExporter.ToBytes(legacy, embedProject: false));
        Check("ghost+accent: the legacy tag on the plain note is written there as tenuto, the ghost note has no mark",
            legacyMarks.Count >= 4 && legacyMarks[2] == (true, 0) && legacyMarks[3] == (false, 16), string.Join(" ", legacyMarks.Select(m => $"{(m.Ghost ? "G" : "-")}{m.Accent}")));

        static TabCell allGhost(SongProject s) => s.Tracks[0].Measures[0].Cells[12];
    }

    // REGION:fidelity-legato-rasgueado
    private static void GpFidelityLegatoRasgueado(string folder)
    {
        var song = GfSong(2, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Legato"));
            GfPut(t, 0, 4, 4, RtNote(t, 1, 5));
            GfPut(t, 0, 12, 4, RtNote(t, 1, 7, 95, "Legato"));      // last beat of the bar: its slur ends on the first beat of the next
            GfPut(t, 1, 0, 4, RtNote(t, 1, 8));
            GfPut(t, 1, 4, 4, RtNote(t, 1, 3, 95, "Rasgueado"));
            GfPut(t, 1, 8, 4, RtNote(t, 1, 3, 95, "Rasgueado", "RasgueadoPeami"));
            GfPut(t, 1, 12, 4, RtNote(t, 1, 3));
        });
        var gpif = GfGpif(GuitarProExporter.ToBytes(song, embedProject: false));
        var origins = System.Text.RegularExpressions.Regex.Matches(gpif, "<Legato origin=\"true\" destination=\"false\"\\s*/>").Count;
        var destinations = System.Text.RegularExpressions.Regex.Matches(gpif, "<Legato origin=\"false\" destination=\"true\"\\s*/>").Count;
        Check("legato: each tagged beat is written as a slur origin and the next beat of its voice (the first of the next bar included) as the destination", origins == 2 && destinations == 2, $"origins {origins}, destinations {destinations}");
        Check("rasgueado: the bare tag is written as the first pattern and a named pattern as that pattern", gpif.Contains("<Rasgueado>ii_1</Rasgueado>", StringComparison.Ordinal) && gpif.Contains("<Rasgueado>peami_1</Rasgueado>", StringComparison.Ordinal),
            string.Join(",", System.Text.RegularExpressions.Regex.Matches(gpif, "<Rasgueado>[^<]*</Rasgueado>").Select(m => m.Value)));
        var back = GfClean(song, folder, "legato-rasgueado");
        string Tags(int bar, int beat) => string.Join("+", GfBeats(back, bar).Where(c => c.Notes.Count > 0).ToList()[beat].Notes[0].Techniques.Where(x => x.StartsWith("Legato", StringComparison.Ordinal) || x.StartsWith("Rasgueado", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal));
        Check("legato: reopened, the origin beats carry Legato and their destinations and other beats do not", Tags(0, 0) == "Legato" && Tags(0, 1) == "" && Tags(0, 2) == "Legato" && Tags(1, 0) == "", $"{Tags(0, 0)} | {Tags(0, 1)} | {Tags(0, 2)} | {Tags(1, 0)}");
        Check("rasgueado: reopened, the bare tag stays bare and a named pattern keeps its name", Tags(1, 1) == "Rasgueado" && Tags(1, 2) == "Rasgueado+RasgueadoPeami" && Tags(1, 3) == "", $"{Tags(1, 1)} | {Tags(1, 2)} | {Tags(1, 3)}");
        Check("legato and rasgueado: the save question lists neither", !GpExportPreflight.Analyze(song).ShouldAskFor(GpExportKind.Export), GpExportPreflight.Analyze(song).Summary());
    }
    // ENDREGION

    // REGION:fidelity-bend-curves
    private static List<BendPointModel> GfCurve(string text) =>
        text.Split(' ').Select(x => x.Split(':')).Select(a => new BendPointModel { Offset = double.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), Value = double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) }).ToList();

    /// <summary>The curve written to a clean .gp and read back: the model's text form, and the share of the note at which the compiled bend reaches its last value.</summary>
    private static (string Back, double ReachedAt) GfBendRoundTrip(string curve, string folder, string name)
    {
        var song = GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Bend")).Notes[0].BendPoints = GfCurve(curve));
        var note = GfN(GfClean(song, folder, name));
        var compiled = TabForge.Playback.ScoreToMidiCompiler.BendCurve(note);
        var target = compiled[^1].Semitones;
        var reached = compiled.First(p => Math.Abs(p.Semitones - target) < 1e-9).Fraction;
        return (GfBend(note.BendPoints), reached);
    }

    /// <summary>A bend that rises quickly and holds, one that releases and holds, and one with a flat stretch must come back as drawn: alphaTab's writer put the
    /// middle stretch at the midpoint or the peak, so a quick rise was saved as a slow whole-note ramp (the target was reached at 300 ms, not 150 ms).</summary>
    private static void GpFidelityBendCurves(string folder)
    {
        var rise = GfBendRoundTrip("0:0 15:4 60:4", folder, "bend-rise");
        Check("bend curve: a quick rise that then holds (0:0 15:4 60:4) is written with its target a quarter into the note, not a slow ramp to the end", rise.Back == "0:0 15:4" && Math.Abs(rise.ReachedAt - 0.25) < 1e-9, $"{rise.Back} reached at {rise.ReachedAt}");
        var release = GfBendRoundTrip("0:4 15:0 60:0", folder, "bend-release");
        Check("bend curve: a release that then holds (0:4 15:0 60:0) comes back reaching its end a quarter in", release.Back == "0:4 15:0" && Math.Abs(release.ReachedAt - 0.25) < 1e-9, $"{release.Back} reached at {release.ReachedAt}");
        var plateau = GfBendRoundTrip("0:0 15:4 45:4 60:0", folder, "bend-plateau");
        Check("bend curve: a bend held flat between two points and then released (0:0 15:4 45:4 60:0) comes back exactly", plateau.Back == "0:0 15:4 45:4 60:0", plateau.Back);
        var plain = GfBendRoundTrip("0:0 60:4", folder, "bend-plain");
        Check("bend curve: a plain two-point bend is unchanged and still reaches its target at the middle of the note", plain.Back == "0:0 60:4" && Math.Abs(plain.ReachedAt - 0.5) < 1e-9, $"{plain.Back} reached at {plain.ReachedAt}");
        var turns = GfBendRoundTrip("0:0 15:4 30:2 45:4 60:0", folder, "bend-turns");
        Check("bend curve: a curve with more turns than the file holds keeps its first, highest and last values and a single point at each turn (no repeated point)",
            turns.Back.StartsWith("0:0 15:4 ", StringComparison.Ordinal) && turns.Back.EndsWith(" 60:0", StringComparison.Ordinal) && turns.Back.Split(' ').Length == 4, turns.Back);
        // several bends in one file: each is found by its place, the one in a chord beside a plain note and the ones in later bars included
        var song = GfSong(3, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 2, 2), RtNote(t, 1, 3, 95, "Bend")).Notes[1].BendPoints = GfCurve("0:0 15:4 60:4");
            GfPut(t, 1, 0, 4, RtNote(t, 1, 5, 95, "Bend")).Notes[0].BendPoints = GfCurve("0:4 15:0 60:0");
            GfPut(t, 1, 4, 4, RtNote(t, 3, 6));
            GfPut(t, 2, 0, 4, RtNote(t, 1, 7, 95, "Bend")).Notes[0].BendPoints = GfCurve("0:0 15:4 45:4 60:0");
        });
        var back = GfClean(song, folder, "bend-many");
        string Curve(int bar, int beat, int fret) => GfBend(GfBeats(back, bar)[beat].Notes.First(n => n.Fret == fret).BendPoints);
        Check("bend curve: three bends in different bars (one in a chord beside a plain note) each come back with their own curve, and the plain notes stay unbent",
            Curve(0, 0, 3) == "0:0 15:4" && Curve(1, 0, 5) == "0:4 15:0" && Curve(2, 0, 7) == "0:0 15:4 45:4 60:0" && Curve(0, 0, 2) == "-" && Curve(1, 1, 6) == "-",
            $"{Curve(0, 0, 3)} | {Curve(1, 0, 5)} | {Curve(2, 0, 7)} | {Curve(0, 0, 2)} | {Curve(1, 1, 6)}");
        // the preflight names only a curve the file cannot hold
        var held = GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Bend")).Notes[0].BendPoints = GfCurve("0:0 15:4 60:4");
            GfPut(t, 0, 4, 4, RtNote(t, 1, 3, 95, "Bend")).Notes[0].BendPoints = GfCurve("0:4 15:0 60:0");
            GfPut(t, 0, 8, 4, RtNote(t, 1, 3, 95, "Bend")).Notes[0].BendPoints = GfCurve("0:0 15:4 45:4 60:0");
        });
        Check("bend curve: the preflight does not list a rise-then-hold, a release-then-hold or a flat-stretch bend", !GpExportPreflight.Analyze(held).ShouldAskFor(GpExportKind.Export), GpExportPreflight.Analyze(held).Summary());
        var lost = GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Bend")).Notes[0].BendPoints = GfCurve("0:0 15:4 30:2 45:4 60:0"));
        var lostReport = GpExportPreflight.Analyze(lost);
        Check("bend curve: the preflight lists a curve with more turns than the file holds, once", lostReport.Losses.Count == 1 && lostReport.Losses[0].Feature.StartsWith("Bend curve with more turns", StringComparison.Ordinal) && lostReport.Losses[0].Count == 1, lostReport.Summary());
        // a patch that does not match the file leaves it byte for byte as written
        var gp = GuitarProExporter.ToBytes(lost, embedProject: false);
        var gpif = GuitarProExporter.ReadZip(gp).First(p => p.Name == GuitarProExporter.ScoreEntry).Data;
        Check("bend curve: a plan that does not match the score leaves the written file untouched", ReferenceEquals(GuitarProBendCurve.Patch(gpif, new AlphaTab.Model.Score()), gpif));
    }
    // ENDREGION

    /// <summary>Staccato is an &lt;Accent&gt; bit as well: a Guitar Pro 8 re-save replaced the ghost mark of a note that carried it (probe round 2), so it follows the accent rule.</summary>
    private static void GpFidelityGhostStaccato(string folder)
    {
        var song = GfSong(1, t =>
        {
            var stac = GfPut(t, 0, 0, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); stac.Notes[0].Ghost = true; stac.Staccato = true;
            var both = GfPut(t, 0, 4, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); both.Notes[0].Ghost = true; both.Staccato = true; both.Accent = 1;
            var allGhost = GfPut(t, 0, 8, 4, RtNote(t, 1, 5), RtNote(t, 2, 4)); foreach (var n in allGhost.Notes) n.Ghost = true; allGhost.Staccato = true;
            var plainStac = GfPut(t, 0, 12, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); plainStac.Staccato = true;
        });
        var marks = GfGpifNoteMarks(GuitarProExporter.ToBytes(song, embedProject: false));
        var text = string.Join(" ", marks.Select(m => $"{(m.Ghost ? "G" : "-")}{m.Accent}"));
        Check("ghost+staccato: a ghost note beside a staccato one is written as a ghost mark with NO accent bit, the other note keeps the staccato bit (1)",
            marks.Count == 8 && marks[0] == (true, 0) && marks[1] == (false, 1), text);
        Check("ghost+staccato: with an accent as well the plain note carries staccato + accent (9), the ghost note nothing; a plain staccato chord is unchanged",
            marks[2] == (true, 0) && marks[3] == (false, 9) && marks[6] == (false, 1) && marks[7] == (false, 1), text);
        Check("ghost+staccato: a beat of ghost notes only keeps the ghost marks and writes no staccato", marks[4] == (true, 0) && marks[5] == (true, 0), text);
        Check("ghost+staccato: no written note carries both a ghost mark and an accent value", marks.All(m => !(m.Ghost && m.Accent != 0)), text);
        var beats = GfBeats(GfClean(song, folder, "ghost-staccato"));
        string Row(TabCell c) => string.Join(",", c.Notes.OrderBy(n => n.StringIndex).Select(n => n.Ghost ? "g" : "n"));
        Check("ghost+staccato: reopened, the ghost notes are still ghost notes and the beats keep their staccato (and accent)",
            Row(beats[0]) == "g,n" && beats[0].Staccato && Row(beats[1]) == "g,n" && beats[1].Staccato && beats[1].Accent == 1 && Row(beats[3]) == "n,n" && beats[3].Staccato,
            $"{Row(beats[0])}/{beats[0].Staccato} {Row(beats[1])}/{beats[1].Staccato}/{beats[1].Accent} {Row(beats[3])}/{beats[3].Staccato}");
        Check("ghost+staccato: a beat of ghost notes only reopens as ghost notes without the staccato (the one narrow loss)", Row(beats[2]) == "g,g" && !beats[2].Staccato, $"{Row(beats[2])}/{beats[2].Staccato}");
        var losses = GpExportPreflight.Analyze(song).Losses;
        Check("ghost+staccato: the preflight names exactly that loss (once, bar 1) and nothing for the beats a plain note carries",
            losses.Count == 1 && losses[0].Feature.StartsWith("Accent, tenuto or staccato on ghost notes only", StringComparison.Ordinal) && losses[0].Count == 1, string.Join(",", losses.Select(l => $"{l.Feature} x{l.Count}")));
        song.Tracks[0].Measures[0].Cells[8].Staccato = false;
        Check("ghost+staccato: with the ghost-only beat unmarked, nothing is asked", !GpExportPreflight.Analyze(song).ShouldAskFor(GpExportKind.Export));
    }
    // ENDREGION

    // ---- family: pick slides (written since the Guitar Pro 8 comparison resolved A31/A32) and the capability record's own consistency ----
    // REGION:fidelity-pick-slides
    private static void GpFidelityPickSlidesAndRecord(string folder)
    {
        var song = GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "PickSlideUp")); GfPut(t, 0, 4, 4, RtNote(t, 1, 5, 95, "PickSlideDown")); GfPut(t, 0, 8, 4, RtNote(t, 1, 7)); GfPut(t, 0, 12, 4, RtNote(t, 1, 8, 95, "SlideOutUp"));
        });
        var gpif = GfGpif(GuitarProExporter.ToBytes(song, embedProject: false));
        Check("pick slides: written as the Slide property with flags 128 (up) and 64 (down), the plain note with none", gpif.Contains("<Flags>128</Flags>", StringComparison.Ordinal) && gpif.Contains("<Flags>64</Flags>", StringComparison.Ordinal) && gpif.Split("name=\"Slide\"").Length == 4);
        var beats = GfBeats(GfClean(song, folder, "pick-slides"));
        Check("pick slides: reopened as PickSlideUp / PickSlideDown on their own notes, the others unchanged (slide out up is not a pick slide)",
            beats[0].Notes[0].Techniques.Contains("PickSlideUp") && !beats[0].Notes[0].Techniques.Contains("PickSlideDown") && beats[1].Notes[0].Techniques.Contains("PickSlideDown") && !beats[1].Notes[0].Techniques.Contains("PickSlideUp")
            && !beats[2].Notes[0].Techniques.Any(x => x.Contains("Slide", StringComparison.Ordinal)) && beats[3].Notes[0].Techniques.Contains("SlideOutUp") && !beats[3].Notes[0].Techniques.Any(x => x.StartsWith("PickSlide", StringComparison.Ordinal)),
            string.Join(" | ", beats.Select(b => string.Join("+", b.Notes[0].Techniques))));
        var tagged = GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "PickSlideUp", "Rasgueado", "Legato")));
        Check("pick slides: the preflight lists neither them nor a rasgueado or a legato slur (all are written now)",
            !GpExportPreflight.Analyze(tagged).ShouldAskFor(GpExportKind.Export), string.Join(",", GpExportPreflight.Analyze(tagged).Losses.Select(l => l.Feature)));

        // the capability record: every row has its Guitar Pro 8 result, and the six rows that were Unknown stay resolved
        var ids = GfCases().Select(x => x.Id).ToList();
        Check("capability record: every row has a Guitar Pro 8 result entry, and no entry names a row that does not exist", ids.All(Gp8Results.ContainsKey) && Gp8Results.Keys.All(ids.Contains), string.Join(",", ids.Where(i => !Gp8Results.ContainsKey(i))));
        var cases = GfCases().ToDictionary(x => x.Id);
        Check("capability record: the rows resolved by the Guitar Pro 8 comparison are no longer Unknown (A29-A32, A46, A47); only A01 and A02 (a per-note velocity may exist) stay open",
            new[] { "A29", "A30", "A31", "A32", "A46", "A47" }.All(i => cases[i].Class != GfClass.Unknown) && cases.Values.Where(x => x.Class == GfClass.Unknown).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "A01", "A02" }),
            string.Join(",", cases.Values.Where(x => x.Class == GfClass.Unknown).Select(x => x.Id)));
    }
    // ENDREGION

    // ---- family: metadata safety (embedded project, sidecar, a .gp re-saved by another program) ----
    // REGION:fidelity-metadata
    /// <summary>Another program's edit, simulated: the zip is rewritten with <paramref name="edit"/> applied to the named entry (all other entries kept).</summary>
    private static byte[] GfRewriteEntry(byte[] gp, string entryName, Func<byte[], byte[]?> edit)
    {
        using var input = new System.IO.Compression.ZipArchive(new MemoryStream(gp), System.IO.Compression.ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in input.Entries)
            {
                using var read = entry.Open(); using var buffer = new MemoryStream(); read.CopyTo(buffer);
                var data = entry.FullName == entryName ? edit(buffer.ToArray()) : buffer.ToArray();
                if (data is null) continue;   // the edit removes the entry
                using var write = zip.CreateEntry(entry.FullName).Open(); write.Write(data);
            }
        return output.ToArray();
    }

    private static byte[] GfSetTempoInGpif(byte[] gpif, int bpm)
    {
        var doc = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(gpif));
        foreach (var a in doc.Descendants("Automation").Where(a => (string?)a.Element("Type") == "Tempo")) a.Element("Value")!.Value = $"{bpm} 2";
        return System.Text.Encoding.UTF8.GetBytes(doc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
    }

    private static void GpFidelityMetadataSafety(string folder)
    {
        var song = GfSong(2, t => { GfPut(t, 0, 0, 1, RtNote(t, 1, 3)); GfPut(t, 1, 0, 1, RtNote(t, 1, 5)); t.Performer = "embedded only"; t.Volume = 100; }, s => s.Tempo = 100);
        var embeddedBytes = GuitarProExporter.ToBytes(song, embedProject: true);
        string Open(byte[] bytes, string name, out SongProject opened, out string? notice)
        {
            var path = Path.Combine(folder, name + ".gp"); File.WriteAllBytes(path, bytes);
            var context = new ImportContext(); opened = GuitarProImporter.Import(path, context); notice = context.EmbeddedRejection; return path;
        }
        // fresh and unchanged: the embedded project is used, with its TabForge-only field and no notice
        Open(embeddedBytes, "meta-fresh", out var fresh, out var freshNotice);
        Check("metadata: an untouched embedded .gp opens as the embedded project, no notice", fresh.Tracks[0].Performer == "embedded only" && freshNotice is null, freshNotice);
        Check("metadata: the embedded project carries an integrity record", GuitarProExporter.ReadZip(embeddedBytes).Any(p => p.Name == GuitarProExporter.EmbeddedBindingEntry));
        // stale: another program changed the score and kept the unknown entries: the score wins, nothing is merged, the user is told
        var stale = GfRewriteEntry(embeddedBytes, GuitarProExporter.ScoreEntry, g => GfSetTempoInGpif(g, 140));
        Open(stale, "meta-stale", out var staleSong, out var staleNotice);
        Check("metadata: a score changed after saving is NOT replaced by the older embedded project (the new tempo is what opens)", staleSong.Tempo == 140, $"tempo {staleSong.Tempo}");
        Check("metadata: the stale embedded settings are not applied, and the notice says why", staleSong.Tracks[0].Performer == "" && staleNotice is not null && staleNotice.Contains("changed in another program after TabForge saved", StringComparison.Ordinal), staleNotice);
        // missing: a file another program saved (no TabForge entries at all) opens as plain Guitar Pro, silently
        var stripped = GfRewriteEntry(GfRewriteEntry(embeddedBytes, GuitarProExporter.EmbeddedProjectEntry, _ => null), GuitarProExporter.EmbeddedBindingEntry, _ => null);
        Open(stripped, "meta-stripped", out var strippedSong, out var strippedNotice);
        Check("metadata: a .gp re-saved by another program (TabForge entries gone) opens as plain Guitar Pro with no notice", strippedSong.Tracks[0].Performer == "" && strippedNotice is null && strippedSong.Tracks.Count == 1, strippedNotice);
        // corrupt: bytes of the embedded project damaged
        var corrupt = GfRewriteEntry(embeddedBytes, GuitarProExporter.EmbeddedProjectEntry, b => { var c = (byte[])b.Clone(); for (var i = 10; i < c.Length; i += 7) c[i] ^= 0x5A; return c; });
        Open(corrupt, "meta-corrupt", out var corruptSong, out var corruptNotice);
        Check("metadata: a damaged embedded project is refused with a notice and the Guitar Pro content opens", corruptSong.Tracks.Count == 1 && corruptSong.Tracks[0].Performer == "" && corruptNotice is not null, corruptNotice);
        // damaged integrity record
        var badRecord = GfRewriteEntry(embeddedBytes, GuitarProExporter.EmbeddedBindingEntry, _ => System.Text.Encoding.UTF8.GetBytes("{not json"));
        Open(badRecord, "meta-badrecord", out var badSong, out var badNotice);
        Check("metadata: a damaged integrity record is refused, never trusted", badSong.Tracks[0].Performer == "" && badNotice is not null, badNotice);
        // a file written before the record existed: accepted as before while its bar count matches the score; refused when it cannot match
        var legacy = GfRewriteEntry(embeddedBytes, GuitarProExporter.EmbeddedBindingEntry, _ => null);
        Open(legacy, "meta-legacy", out var legacySong, out var legacyNotice);
        Check("metadata: an older embedded .gp (no record) with a matching bar count still opens as the embedded project", legacySong.Tracks[0].Performer == "embedded only" && legacyNotice is null, legacyNotice);
        var longer = GfSong(5, t => GfPut(t, 0, 0, 1, RtNote(t, 1, 3)));
        var longerBytes = GuitarProExporter.ToBytes(longer, embedProject: true);
        var legacyMismatch = GfRewriteEntry(GfRewriteEntry(longerBytes, GuitarProExporter.EmbeddedBindingEntry, _ => null), GuitarProExporter.EmbeddedProjectEntry, _ => GuitarProExporter.EmbeddedProjectBytes(song));
        Open(legacyMismatch, "meta-legacy-mismatch", out var mismatchSong, out var mismatchNotice);
        Check("metadata: an older embedded project with a different bar count than the score is not used", mismatchSong.Tracks[0].Measures.Count == 5 && mismatchNotice is not null, $"{mismatchSong.Tracks[0].Measures.Count} bars; {mismatchNotice}");

        // sidecar (.tfaudio) beside a clean .gp
        var controller = new DocumentController();
        var pairSong = GfSong(2, t => { GfPut(t, 0, 0, 1, RtNote(t, 1, 3)); GfPut(t, 1, 0, 1, RtNote(t, 1, 5)); t.Volume = 100; t.Rig.Name = "my rig"; });
        var pairPath = Path.Combine(folder, "meta-pair.gp");
        controller.SaveCleanGuitarProWithAudioData(DocumentSession.FromProject(RtCopy(pairSong), null), pairPath, "");
        var intact = controller.Open(pairPath);
        Check("sidecar: an intact pair restores the track's own volume exactly and has no notice", intact.Project.Tracks[0].Volume == 100 && intact.Project.Tracks[0].Rig.Name == "my rig" && intact.Notice is null, $"{intact.Project.Tracks[0].Volume} {intact.Notice}");
        File.WriteAllBytes(pairPath, GfRewriteEntry(File.ReadAllBytes(pairPath), GuitarProExporter.ScoreEntry, g => GfSetVolumeFractionInGpif(GfSetTempoInGpif(g, 133), 0.5)));
        var changed = controller.Open(pairPath);
        Check("sidecar: the .gp changed after the pair was saved: the new music wins (tempo 133), the sidecar's FX still apply, the user is told",
            changed.Project.Tempo == 133 && changed.Project.Tracks[0].Rig.Name == "my rig" && changed.Notice is not null && changed.Notice.Contains("different version", StringComparison.Ordinal), $"tempo {changed.Project.Tempo}; {changed.Notice}");
        Check("sidecar: ... and the .gp's own (exact) track volume is kept, not the older sidecar value", changed.Project.Tracks[0].Volume == 64, $"{changed.Project.Tracks[0].Volume}");
        // an embedded save over a name that once had a clean pair: the left-over sidecar must not replace the embedded project's newer mixer/FX
        var newer = RtCopy(pairSong); newer.Tracks[0].Rig.Name = "newer rig"; newer.Tracks[0].Volume = 60;
        GuitarProExporter.Save(newer, pairPath, embedProject: true);
        var mixed = controller.Open(pairPath);
        Check("sidecar: a .tfaudio left beside an embedded .gp is not applied (the embedded project's own rig and volume stay), and the user is told",
            mixed.Project.Tracks[0].Rig.Name == "newer rig" && mixed.Project.Tracks[0].Volume == 60 && mixed.Notice is not null && mixed.Notice.Contains("was not applied", StringComparison.Ordinal),
            $"{mixed.Project.Tracks[0].Rig.Name} {mixed.Project.Tracks[0].Volume}; {mixed.Notice}");
        var gone = Path.Combine(folder, "meta-nosidecar.gp"); GuitarProExporter.Save(pairSong, gone, embedProject: false);
        var noSidecar = controller.Open(gone);
        Check("sidecar: no sidecar is no notice and no change", noSidecar.Notice is null && noSidecar.Project.Tracks[0].Rig.Name != "my rig");
        File.WriteAllText(AudioDataFile.PathFor(gone), "{ this is not a sidecar");
        var damaged = controller.Open(gone);
        Check("sidecar: a damaged sidecar is reported, never skipped silently", damaged.Notice is not null && damaged.Notice.Contains("was not applied", StringComparison.Ordinal), damaged.Notice);
    }
    // ENDREGION

    // ---- family: lossy-export preflight ----
    // REGION:fidelity-preflight
    private static void GpFidelityLossyExportPreflight(string folder)
    {
        var plain = GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)));
        Check("preflight: a song with nothing a clean .gp cannot hold is not asked about (no warning on a harmless save)", !GpExportPreflight.Analyze(plain).ShouldAskFor(GpExportKind.Export));
        // marks that ARE written are not a loss: fingering, tenuto, palm mute, a short bend, a 4-point whammy
        var kept = GfSong(1, t => { var c = GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "PalmMute")); c.Tenuto = true; c.Notes[0].LeftHandFinger = 1; c.WhammyPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 30, Value = -4 }, new() { Offset = 60, Value = 0 } }; });
        Check("preflight: written features (fingering, tenuto, palm mute, a 3-point whammy) cause no warning", !GpExportPreflight.Analyze(kept).ShouldAskFor(GpExportKind.Export));

        var lossy = GfSong(4, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Bend")).Notes[0].BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 15, Value = 4 }, new() { Offset = 30, Value = 2 }, new() { Offset = 45, Value = 4 }, new() { Offset = 60, Value = 0 } };
            GfPut(t, 1, 0, 4, RtNote(t, 1, 3, 95, "FadeIn"), RtNote(t, 2, 2));
            GfPut(t, 2, 0, 4, RtNote(t, 1, 3)).Mix = new MixChange { Volume = 10 };
            GfPut(t, 3, 0, 4, RtNote(t, 1, 3)).TremoloPickDenominator = 64;
            t.Reverb = 60;
        });
        var report = GpExportPreflight.Analyze(lossy);
        var features = string.Join(" | ", report.Losses.Select(l => l.Feature));
        Check("preflight: lists each unsupported feature with where it is (bend curve, fade on part of a chord, mix change, 1/64 tremolo picking, reverb send)",
            report.ShouldAskFor(GpExportKind.Export) && report.Losses.Count == 5 && report.Losses.Any(l => l.Feature.StartsWith("Bend", StringComparison.Ordinal) && l.Where.Contains("bar 1", StringComparison.Ordinal))
            && report.Losses.Any(l => l.Feature.StartsWith("Fade", StringComparison.Ordinal) && l.Where.Contains("bar 2", StringComparison.Ordinal)) && report.Losses.Any(l => l.Feature.StartsWith("Reverb", StringComparison.Ordinal)), features);
        Check("preflight: the summary is concise (one line per feature)", report.Summary().Split('\n').Length == 5, report.Summary());

        var target = Path.Combine(folder, "pf", "song.gp"); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Check("preflight plan: Cancel writes nothing", !GpExportPreflight.Plan(report, GpExportChoice.Cancel, GpExportKind.Save, target, null).Proceed);
        var compatibleSave = GpExportPreflight.Plan(report, GpExportChoice.ExportCompatible, GpExportKind.Save, target, null);
        Check("preflight plan: a compatible Save at a free target writes the song's own pair and marks the song saved", compatibleSave.Proceed && compatibleSave.MarkDocumentClean && compatibleSave.ChangeDocumentPath && compatibleSave.NativeCopyPath is null && compatibleSave.CompatiblePath == target);
        var nativeSave = GpExportPreflight.Plan(report, GpExportChoice.KeepNativeCopy, GpExportKind.Save, target, null);
        Check("preflight plan: Save with a native copy follows the copy (it holds everything)", nativeSave.NativeCopyPath is not null && nativeSave.NativeCopyPath.EndsWith("song (full copy).tforge", StringComparison.Ordinal) && nativeSave.MarkDocumentClean && nativeSave.ChangeDocumentPath);
        var nativeExport = GpExportPreflight.Plan(report, GpExportChoice.KeepNativeCopy, GpExportKind.Export, target, null);
        Check("preflight plan: an Export never touches the document, even with a native copy", nativeExport.NativeCopyPath is not null && !nativeExport.MarkDocumentClean && !nativeExport.ChangeDocumentPath);

        // end to end through the document controller: the original holds the native project; a compatible export must not replace it
        var controller = new DocumentController();
        var original = Path.Combine(folder, "pf", "orig.gp");
        GuitarProExporter.Save(lossy, original, embedProject: true);
        var originalHash = AudioDataFile.Sha256Hex(File.ReadAllBytes(original));
        var doc = DocumentSession.FromProject(RtCopy(lossy), original); doc.Project.IsDirty = true;
        var dirtyBefore = doc.IsDirty;
        var export = controller.ExportCleanGuitarPro(doc, original, GpExportKind.Export, GpExportChoice.ExportCompatible, "");
        Check("preflight export: the original (embedded native project) is byte-for-byte unchanged", AudioDataFile.Sha256Hex(File.ReadAllBytes(original)) == originalHash);
        Check("preflight export: the compatible copy is a separate, clean .gp (no embedded project, no sidecar)", export.CompatiblePath is not null && export.CompatiblePath != original && File.Exists(export.CompatiblePath)
            && GuitarProExporter.TryReadEmbedded(export.CompatiblePath) is null && !File.Exists(AudioDataFile.PathFor(export.CompatiblePath)), export.CompatiblePath);
        Check("preflight export: the document keeps its path and its unsaved state", doc.Path == original && doc.IsDirty == dirtyBefore);
        var withNative = controller.ExportCleanGuitarPro(doc, Path.Combine(folder, "pf", "second.gp"), GpExportKind.Export, GpExportChoice.KeepNativeCopy, "");
        Check("preflight export: the native copy loads back with everything (the bend curve has five points)", withNative.NativeCopyPath is not null && ProjectService.Load(withNative.NativeCopyPath).Tracks[0].Measures[0].Cells.First(c => c.Notes.Count > 0).Notes[0].BendPoints.Count == 5);
        var again = controller.ExportCleanGuitarPro(doc, Path.Combine(folder, "pf", "second.gp"), GpExportKind.Export, GpExportChoice.KeepNativeCopy, "");
        Check("preflight export: a second native copy never overwrites the first", again.NativeCopyPath != withNative.NativeCopyPath && File.Exists(withNative.NativeCopyPath));
        var cancelled = controller.ExportCleanGuitarPro(doc, Path.Combine(folder, "pf", "never.gp"), GpExportKind.Export, GpExportChoice.Cancel, "");
        Check("preflight export: Cancel writes nothing", cancelled.CompatiblePath is null && !File.Exists(Path.Combine(folder, "pf", "never.gp")));
        // injected write failure: the target's folder is a file
        var blocker = Path.Combine(folder, "pf", "blocker"); File.WriteAllText(blocker, "x");
        var failed = false;
        try { controller.ExportCleanGuitarPro(doc, Path.Combine(blocker, "x.gp"), GpExportKind.Save, GpExportChoice.ExportCompatible, ""); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DirectoryNotFoundException) { failed = true; }
        Check("preflight export: a failed write throws, leaves the original and the document's path and unsaved state intact", failed && doc.Path == original && doc.IsDirty == dirtyBefore && AudioDataFile.Sha256Hex(File.ReadAllBytes(original)) == originalHash,
            $"threw {failed}; path {doc.Path == original}; dirty {doc.IsDirty} vs {dirtyBefore}");
        // injected failure AFTER the native copy was written (Save that would follow the copy): the document must stay unsaved, on its own path
        var lateFailed = false;
        FilePathPolicy.FaultInjection = s => { if (s.StartsWith("staged:late.gp", StringComparison.OrdinalIgnoreCase)) throw new IOException("injected"); };
        try { controller.ExportCleanGuitarPro(doc, Path.Combine(folder, "pf", "late.gp"), GpExportKind.Save, GpExportChoice.KeepNativeCopy, ""); }
        catch (IOException) { lateFailed = true; }
        finally { FilePathPolicy.FaultInjection = null; }
        Check("preflight export: a .gp write failing after the native copy leaves the document unsaved on its own path",
            lateFailed && doc.Path == original && doc.Project.IsDirty && doc.IsDirty == dirtyBefore && !File.Exists(Path.Combine(folder, "pf", "late.gp")), $"threw {lateFailed}; path {doc.Path}; dirty {doc.Project.IsDirty}");
        // a song the preflight finds nothing lost in still never replaces a file holding an embedded project (plug-ins, TabForge-only fields)
        var plainPlan = GpExportPreflight.Plan(GpExportPreflight.Analyze(plain), GpExportChoice.ExportCompatible, GpExportKind.Save, original, null);
        Check("preflight plan: a clean .gp never replaces a file holding an embedded project, even when nothing is listed as lost",
            plainPlan.CompatiblePath != original && Path.GetFileName(plainPlan.CompatiblePath).StartsWith("orig (compatible", StringComparison.Ordinal), plainPlan.CompatiblePath);
    }
    // ENDREGION

    /// <summary>The lossy-export dialog (buttons, default, Esc) and the flow around it: choice passed through, Cancel writes nothing, no ask for a harmless song, Export leaves the document alone.</summary>
    private static void GpFidelityExportDialogFlow(string folder)
    {
        var report = new GpPreflightReport();
        report.Losses.Add(new GpLoss("Reverb / chorus sends", "not written to Guitar Pro; they reopen at the defaults", 1, "Track 1"));
        System.Windows.Controls.Button? Find(System.Windows.DependencyObject root, string id)
        {
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
            {
                if (child is System.Windows.Controls.Button b && System.Windows.Automation.AutomationProperties.GetAutomationId(b) == id) return b;
                if (Find(child, id) is { } found) return found;
            }
            return null;
        }
        foreach (var (id, expected) in new[] { (Views.GpExportPreflightDialog.KeepId, GpExportChoice.KeepNativeCopy), (Views.GpExportPreflightDialog.CompatibleId, GpExportChoice.ExportCompatible), (Views.GpExportPreflightDialog.CancelId, GpExportChoice.Cancel) })
        {
            var handle = Views.GpExportPreflightDialog.Build(report, GpExportKind.Export, "song.gp");
            var button = Find(handle.Window, id);
            Check($"export dialog: the {id} button exists, is named and passes its choice through", button is not null && !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(button)));
            button?.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check($"export dialog: pressing {id} gives {expected}", handle.Choice == expected, handle.Choice.ToString());
        }
        var fresh = Views.GpExportPreflightDialog.Build(report, GpExportKind.Save, "song.gp");
        Check("export dialog: Esc is Cancel, the default (Enter) keeps the full copy, and the result stays Cancel until a button is pressed",
            Find(fresh.Window, Views.GpExportPreflightDialog.CancelId)?.IsCancel == true && Find(fresh.Window, Views.GpExportPreflightDialog.KeepId)?.IsDefault == true && fresh.Choice == GpExportChoice.Cancel);
        fresh.Window.Close();

        var controller = new DocumentController();
        var dir = Path.Combine(folder, "dlg"); Directory.CreateDirectory(dir);
        var plain = GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)));
        var plainDoc = DocumentSession.FromProject(RtCopy(plain), null);
        var asked = 0;
        var plainTarget = Path.Combine(dir, "plain.gp");
        var plainResult = controller.ExportCleanGuitarPro(plainDoc, plainTarget, GpExportKind.Export, "", _ => { asked++; return GpExportChoice.Cancel; });
        Check("export flow: a song with nothing lost is never asked about and is written", asked == 0 && plainResult.CompatiblePath is not null && File.Exists(plainTarget));

        var lossy = GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); t.Reverb = 60; });
        var doc = DocumentSession.FromProject(RtCopy(lossy), Path.Combine(dir, "doc.tforge")); doc.Project.IsDirty = true;
        var dirty = doc.IsDirty; var docPath = doc.Path;
        var cancelTarget = Path.Combine(dir, "cancelled.gp");
        var cancelled = controller.ExportCleanGuitarPro(doc, cancelTarget, GpExportKind.Export, "", r => { asked++; return GpExportChoice.Cancel; });
        Check("export flow: a song that loses something is asked once; Cancel writes no .gp and no .tforge", asked == 1 && cancelled.CompatiblePath is null && !File.Exists(cancelTarget)
            && Directory.GetFiles(dir, "cancelled*").Length == 0 && Directory.GetFiles(dir, "*full copy*").Length == 0);
        var compatible = controller.ExportCleanGuitarPro(doc, Path.Combine(dir, "compat.gp"), GpExportKind.Export, "", _ => GpExportChoice.ExportCompatible);
        Check("export flow: 'compatible only' writes the .gp and no native copy", compatible.CompatiblePath is not null && compatible.NativeCopyPath is null && File.Exists(compatible.CompatiblePath) && !File.Exists(Path.Combine(dir, "compat (full copy).tforge")));
        var kept = controller.ExportCleanGuitarPro(doc, Path.Combine(dir, "keep.gp"), GpExportKind.Export, "", _ => GpExportChoice.KeepNativeCopy);
        Check("export flow: 'keep a full copy' writes the .tforge beside the .gp", kept.NativeCopyPath is not null && File.Exists(kept.NativeCopyPath) && kept.CompatiblePath is not null && File.Exists(kept.CompatiblePath));
        Check("export flow: Export leaves the document's path and unsaved state unchanged", doc.Path == docPath && doc.IsDirty == dirty && doc.Project.IsDirty);

        // Save with the compatible choice (clean mode): the song's own .gp + .tfaudio pair, overwritten in place, and the song is saved
        var saveTarget = Path.Combine(dir, "mine.gp");
        var saveDoc = DocumentSession.FromProject(RtCopy(lossy), null); saveDoc.Project.IsDirty = true;
        var first = controller.ExportCleanGuitarPro(saveDoc, saveTarget, GpExportKind.Save, "", _ => GpExportChoice.ExportCompatible);
        Check("save flow: a compatible Save writes the song's own .gp and the .tfaudio beside it", first.CompatiblePath == saveTarget && File.Exists(saveTarget) && File.Exists(AudioDataFile.PathFor(saveTarget)) && first.NativeCopyPath is null
            && GuitarProExporter.TryReadEmbedded(saveTarget) is null, first.CompatiblePath);
        Check("save flow: the song is marked saved on that path (closing the tab does not ask again)", saveDoc.Path == saveTarget && !saveDoc.HasUnsavedChanges && !saveDoc.IsDirty);
        saveDoc.Project.IsDirty = true; saveDoc.MarkClean(ProjectService.ContentHash(saveDoc.Project));
        var second = controller.ExportCleanGuitarPro(saveDoc, saveTarget, GpExportKind.Save, "", _ => GpExportChoice.ExportCompatible);
        Check("save flow: a second compatible Save overwrites the same pair (no '(compatible)' file piles up)", second.CompatiblePath == saveTarget && Directory.GetFiles(dir, "mine*").Length == 2 && !saveDoc.HasUnsavedChanges,
            string.Join(", ", Directory.GetFiles(dir, "mine*").Select(Path.GetFileName)));
        // protected targets: an embedded project or a .tforge at the target is never replaced by a lossy pair
        var embedded = Path.Combine(dir, "embedded.gp");
        GuitarProExporter.Save(lossy, embedded, embedProject: true);
        var embeddedHash = AudioDataFile.Sha256Hex(File.ReadAllBytes(embedded));
        var protectedDoc = DocumentSession.FromProject(RtCopy(lossy), embedded); protectedDoc.Project.IsDirty = true;
        var redirected = controller.ExportCleanGuitarPro(protectedDoc, embedded, GpExportKind.Save, "", _ => GpExportChoice.ExportCompatible);
        Check("save flow: an embedded project at the target is not replaced; the compatible file goes to a sibling and the song stays unsaved",
            redirected.CompatiblePath != embedded && Path.GetFileName(redirected.CompatiblePath ?? "").StartsWith("embedded (compatible", StringComparison.Ordinal) && AudioDataFile.Sha256Hex(File.ReadAllBytes(embedded)) == embeddedHash && protectedDoc.Project.IsDirty && protectedDoc.Path == embedded);
        var tforgePath = Path.Combine(dir, "t.tforge"); File.WriteAllText(tforgePath, "x");
        Check("save flow: an existing .tforge counts as native content", GpExportPreflight.OriginalHoldsNative(tforgePath, ignoreSidecar: true));
    }

    // ---- family: MIDI export timing in short (content-length) bars holding triplets ----
    // REGION:fidelity-midi
    private static void GpFidelityMidiShortTripletBars(string folder)
    {
        // An imported song's short bar plays only as long as its content: two triplet eighths are 8/3 slots, so the bar is not a whole number of
        // slots. The MIDI file used to round the bar to 3 slots of ticks and then stretch the tempo over it: notes inside landed early.
        var song = new SongProject { Title = "gf midi", Tempo = 150, ImportedFrom = "synthetic.gp5" };
        var t = GpFixGuitar(8);
        song.Tracks.Add(t);
        for (var b = 0; b < 8; b++)
        {
            if (b % 2 == 0) { for (var k = 0; k < 4; k++) GfPut(t, b, k * 4, 4, RtNote(t, 1, 3 + k)); continue; }
            for (var k = 0; k < 2; k++) { var c = RtPut(t, b, k * 4 / 3.0, 8, 0, RtNote(t, 1, 5 + k)); c.IsTriplet = true; }   // two triplet eighths: a short bar
        }
        var path = Path.Combine(folder, "short-triplet.mid");
        var r = TabForge.Diagnostics.MidiTimingAudit.Measure(song, path);
        Check("fidelity [midi]: bar starts within 1 ms of playback in short triplet bars", r.MaxBarStartErrMs <= 1, $"{r.MaxBarStartErrMs:0.000} ms at bar {r.WorstBar}");
        Check("fidelity [midi]: note-ons within 1 ms of playback in short triplet bars", r.MaxNoteErrMs <= 1 && r.NoteCountFile == r.NoteCountPlayback, $"{r.MaxNoteErrMs:0.000} ms; {r.WorstNoteAt}");
    }
    // ENDREGION
}
