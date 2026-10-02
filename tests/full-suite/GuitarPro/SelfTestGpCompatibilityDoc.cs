using System.IO;
using System.Linq;
using System.Text;

namespace TabForge;

// docs/COMPATIBILITY.md: the public, plain-English page on what a compatible .gp file keeps, changes and cannot hold. It is GENERATED from the capability record
// (GfCases, SelfTestGpFidelityCapability.cs) and the loss-coverage rules (LcRules, SelfTestGpLossCoverage.cs), which are themselves checked against real exports,
// so the page cannot drift: TestGpCompatibilityDoc regenerates it and compares. `TabForge.exe --gp-compat-doc <out.md>` writes the page (run it after changing
// a row, then commit the result). The page never names other programs; file-format names (.gp, .tforge, .tfaudio) are fine.
public static partial class SelfTest
{
    private enum CdOutcome { Kept, Same, Changed, LeftOut, Internal }

    /// <summary>The plain-English line of each capability-record row. Every row must have one (a test enforces it), so a new row cannot be left off the page.</summary>
    private static readonly Dictionary<string, (CdOutcome Outcome, string Text)> CdText = new()
    {
        ["A01"] = (CdOutcome.Changed, "A note's exact loudness. Loudness is kept as the nearest of the eight dynamic marks (ppp to fff), so a value between two marks moves to the nearer one; the note keeps its mark."),
        ["A02"] = (CdOutcome.Changed, "Different loudness inside one chord or drum beat. A beat holds one dynamic mark, so every note of the beat takes the first note's."),
        ["A03"] = (CdOutcome.Kept, "Track volume, exactly."),
        ["A04"] = (CdOutcome.Kept, "Track pan (left and right balance), exactly."),
        ["A05"] = (CdOutcome.Changed, "Bend curves with more turns than the format holds. A .gp file keeps where a bend starts, one flat stretch and where it ends. A bend that rises and then holds, or releases and then holds, is written as drawn; a curve with more turns is reduced to the closest such shape."),
        ["A06"] = (CdOutcome.Changed, "Whammy-bar curves with more than four points are reduced to four."),
        ["A07"] = (CdOutcome.Kept, "Left-hand fingering."),
        ["A08"] = (CdOutcome.Kept, "Right-hand fingering."),
        ["A09"] = (CdOutcome.Kept, "Tenuto marks."),
        ["A10"] = (CdOutcome.Same, "An old per-note Tenuto tag is written as the beat's tenuto mark."),
        ["A11"] = (CdOutcome.LeftOut, "Volume, pan and sound changes placed on a single beat, including on a rest. The track keeps its starting mix. Tempo changes are kept."),
        ["A12"] = (CdOutcome.Kept, "Palm mute on one note of a chord."),
        ["A13"] = (CdOutcome.Changed, "A fermata placed on one track only. A .gp file stores a fermata on the bar, so it shows on every track."),
        ["A14"] = (CdOutcome.Same, "MIDI channel numbers are handed out again in track order; drums stay on the drum channel."),
        ["A15"] = (CdOutcome.Same, "The target note of a slide is worked out from the note that follows it."),
        ["A16"] = (CdOutcome.Same, "A trill with no target set gets a target two semitones up."),
        ["A17"] = (CdOutcome.Kept, "Trill speed."),
        ["A18"] = (CdOutcome.Changed, "Tremolo picking at 1/64 is written as 1/32 (a .gp file has three speeds: 1/8, 1/16 and 1/32)."),
        ["A19"] = (CdOutcome.Changed, "A fade-in on one note of a chord applies to the whole beat."),
        ["A20"] = (CdOutcome.Changed, "A fade-out on one note of a chord applies to the whole beat."),
        ["A21"] = (CdOutcome.Same, "Navigation marks (Segno, Coda, D.S., D.C. and so on) are kept under the standard names."),
        ["A22"] = (CdOutcome.Same, "A tempo marking that repeats the tempo already playing is not kept as a change; the tempo you hear is the same."),
        ["A23"] = (CdOutcome.Same, "Whammy-bar types (dip, dive and so on) are worked out again from the curve; the curve itself is kept."),
        ["A24"] = (CdOutcome.Same, "Every kind of harmonic also carries the general harmonic label."),
        ["A25"] = (CdOutcome.Same, "A dead note also carries a dead-note label."),
        ["A26"] = (CdOutcome.Same, "A ghost note also carries a ghost-note label."),
        ["A27"] = (CdOutcome.Same, "A hammer-on or pull-off is stored as an origin and a destination."),
        ["A28"] = (CdOutcome.Same, "The destination of a hammer-on or pull-off carries its own label."),
        ["A29"] = (CdOutcome.Kept, "Legato slurs."),
        ["A30"] = (CdOutcome.Kept, "Rasgueado patterns."),
        ["A31"] = (CdOutcome.Kept, "Pick slide up."),
        ["A32"] = (CdOutcome.Kept, "Pick slide down."),
        ["A33"] = (CdOutcome.Kept, "Left-hand tap."),
        ["A34"] = (CdOutcome.Changed, "The length of a grace note is normalised when the file is reopened; playback does not use it."),
        ["A35"] = (CdOutcome.Same, "A grace note before the beat also carries a grace label."),
        ["A36"] = (CdOutcome.Same, "A grace note on the beat also carries a grace label."),
        ["A37"] = (CdOutcome.Same, "An arpeggio stroke down also carries a brush-stroke label."),
        ["A38"] = (CdOutcome.Same, "An arpeggio stroke up also carries a brush-stroke label."),
        ["A39"] = (CdOutcome.Same, "A hammer-on that ends a bar marks the first note of the next bar as its destination."),
        ["A40"] = (CdOutcome.Changed, "A bend on a grace note is kept; its separate \"bend grace\" label is not."),
        ["A41"] = (CdOutcome.Internal, ""),
        ["A42"] = (CdOutcome.LeftOut, "The MIDI output device of a track (a device number belongs to your machine)."),
        ["A43"] = (CdOutcome.LeftOut, "The record input choice of a track."),
        ["A44"] = (CdOutcome.LeftOut, "The input monitoring switch of a track."),
        ["A45"] = (CdOutcome.LeftOut, "The tint of a track row in the track list."),
        ["A46"] = (CdOutcome.LeftOut, "The reverb send of a track. It reopens at the default."),
        ["A47"] = (CdOutcome.LeftOut, "The chorus send of a track. It reopens at the default."),
        ["A48"] = (CdOutcome.Same, "A track's playback transpose is written into the tuning (or into string and fret), so every note sounds the same; the setting itself reads back as 0."),
        ["A49"] = (CdOutcome.LeftOut, "The performer name of a track."),
        ["A50"] = (CdOutcome.LeftOut, "The notes written on a track."),
        ["A51"] = (CdOutcome.Same, "A track's instrument name is named again from its sound."),
        ["A52"] = (CdOutcome.LeftOut, "The drum-map preset of a drum track."),
        ["A53"] = (CdOutcome.Kept, "Track colours."),
        ["A54"] = (CdOutcome.Changed, "A beat made only of ghost notes loses its accent, tenuto or staccato mark (in a .gp file a ghost mark and those marks exclude each other). With a plain note in the chord, the mark stays on that note."),
    };

    /// <summary>True when the save question lists the row's loss (the preflight names it) rather than leaving it unlisted by design.</summary>
    private static bool CdIsListed(GfCase row) => CdRuleFor(row) is { Kind: LcKind.Feature };

    /// <summary>The loss-coverage rule of a record row. A09 (tenuto, written exactly) has none: its category, beat.tenuto, is also how a mark lost on a ghost-only beat (A54) shows up.</summary>
    private static LcRule? CdRuleFor(GfCase row) => row.Id == "A09" ? null : LcRuleFor(row.Allowance.Split(' ')[0]);

    internal static string BuildCompatibilityDoc()
    {
        var rows = GfCases();
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        string Section(CdOutcome outcome, bool listed) => string.Join("\n", rows
            .Where(r => CdText.TryGetValue(r.Id, out var t) && t.Outcome == outcome && (outcome is not (CdOutcome.Changed or CdOutcome.LeftOut) || CdIsListed(r) == listed))
            .Select(r => $"- {CdText[r.Id].Text}"));

        Line("# Compatible .gp files: what is kept, what changes, what cannot be held");
        Line();
        Line("<!-- Generated from TabForge's compatibility record by `TabForge.exe --gp-compat-doc`; a self-test regenerates it and compares, so do not edit it by hand. -->");
        Line();
        Line("TabForge can write a compatible .gp file that other programs open. A .gp file holds the music and the common marks, but it cannot hold everything TabForge knows. "
            + "This page says exactly what a compatible file keeps, what it changes and what it cannot hold. When you save or export a song that uses something in the changed or left out lists below, "
            + "TabForge names it in a question before anything is written and offers to keep a full TabForge copy as well, so nothing is lost without you knowing.");
        Line();
        Line("## The short version");
        Line();
        Line("- **Kept:** the notes (pitch, string and fret), their rhythm, rests, ties, tuplets, grace notes and voices; drums; tunings and capo; time signatures, key changes and the tempo (steps and ramps); "
            + "repeats, endings and jumps; section markers; the eight dynamic marks; track volume, pan, mute and solo; and the common playing marks (bends, slides, hammer-ons and pull-offs, harmonics, vibrato, palm mute, whammy bar, brush strokes and more). "
            + $"Each of the {rows.Count} single-feature cases and the {GfFixtures().Count} test songs behind this page is exported, reopened and compared, note by note.");
        Line("- **Changed or left out:** the items listed below.");
        Line("- **Cannot be held at all:** plug-ins, FX chains, audio and MIDI clips and mixer groups (see \"TabForge audio settings\" below).");
        Line("- **To keep everything:** save a full TabForge copy (a .tforge project). It is the lossless option, and it is offered every time the question appears.");
        Line();
        Line("## Kept exactly");
        Line();
        Line("These are written to the .gp file and read back unchanged:");
        Line();
        Line(Section(CdOutcome.Kept, true));
        Line();
        Line("## Kept, spelled the way other programs spell it");
        Line();
        Line("These come back as the same music; only the internal label or name differs. The save question does not mention them:");
        Line();
        Line(Section(CdOutcome.Same, true));
        Line();
        Line("## Changed or left out, and named in the save question");
        Line();
        Line("When your song uses any of these, the question lists it with the bars where it occurs:");
        Line();
        Line(string.Join("\n", new[] { Section(CdOutcome.Changed, true), Section(CdOutcome.LeftOut, true) }.Where(s => s.Length > 0)));
        Line();
        Line("## Changed or left out, not named in the save question");
        Line();
        Line("These are small, or belong to your machine or to TabForge's own screens. The question stays quiet about them; a full TabForge copy keeps all of them:");
        Line();
        Line(string.Join("\n", new[] { Section(CdOutcome.Changed, false), Section(CdOutcome.LeftOut, false) }.Where(s => s.Length > 0)));
        Line();
        Line("## TabForge audio settings: plug-ins, FX chains, clips and mixer groups");
        Line();
        Line("A .gp file has nowhere to keep these. What happens depends on how you save:");
        Line();
        Line("- **Save as .gp:** TabForge asks how to keep them: the whole project embedded in the .gp, a .gp with a .tfaudio file beside it (keep the two together), or a .tforge project. Nothing is lost and the save question does not repeat the list.");
        Line("- **Export compatible .gp file:** writes the .gp alone, so these settings are left out. TabForge asks first and offers to keep a full copy; exporting never changes your song or its file.");
        Line();
        Line("## How to keep everything");
        Line();
        Line("Choose \"Keep a full TabForge copy\" in the question, or save a .tforge project. The full copy holds every setting of the song, including everything on this page.");
        Line();
        Line("## How this page stays true");
        Line();
        Line("TabForge's own tests export a song for every row, reopen the file and compare it with the original, and they fail if a loss is found that the save question does not list or that this page does not describe. "
            + "This page is written from the same rows, and a test regenerates it and compares, so it cannot fall behind the program.");
        return sb.ToString().Replace("\n\n\n", "\n\n");
    }

    /// <summary>The page in the repository, found by walking up from the program's folder (a source tree or a checkout); null when there is none.</summary>
    private static string? CdPagePath()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "COMPATIBILITY.md");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static string CdNormalise(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static void TestGpCompatibilityDoc()
    {
        var rows = GfCases();
        Check("compatibility page: every capability-record row has a plain-English line", rows.All(r => CdText.ContainsKey(r.Id)), string.Join(", ", rows.Where(r => !CdText.ContainsKey(r.Id)).Select(r => r.Id)));
        Check("compatibility page: no line belongs to a row that no longer exists", CdText.Keys.All(k => rows.Any(r => r.Id == k)), string.Join(", ", CdText.Keys.Where(k => rows.All(r => r.Id != k))));
        // the page's grouping follows the record: a row the save question lists or leaves unlisted is described as changed / left out, and only those
        var disagree = rows.Where(r => CdText.TryGetValue(r.Id, out var t) && (t.Outcome is CdOutcome.Changed or CdOutcome.LeftOut) != (CdRuleFor(r) is { Kind: LcKind.Feature or LcKind.Exempt })).Select(r => r.Id).ToList();
        Check("compatibility page: a row is changed or left out exactly when the loss-coverage rules know a listed or exempted loss for it", disagree.Count == 0, string.Join(", ", disagree));
        var unfinished = rows.Where(r => CdText.TryGetValue(r.Id, out var t) && t.Outcome != CdOutcome.Internal && t.Text.Length == 0).Select(r => r.Id).ToList();
        Check("compatibility page: no line is empty", unfinished.Count == 0, string.Join(", ", unfinished));
        var listedFeatures = LcRules.Where(r => r.Kind == LcKind.Feature).Select(r => r.Category).Where(c => c is not ("beat.accent" or "beat.staccato" or "beat.tenuto")).ToList();
        Check("compatibility page: every feature the save question can list is described by a row",
            listedFeatures.All(c => rows.Any(r => CdIsListed(r) && r.Allowance.Split(' ')[0] == c)), string.Join(", ", listedFeatures.Where(c => !rows.Any(r => CdIsListed(r) && r.Allowance.Split(' ')[0] == c))));

        var generated = BuildCompatibilityDoc();
        var banned = new[] { "Guitar Pro", "alphaTab", "AlphaTab", "GP5", "GP7", "GP8", "REAPER" }.Where(w => generated.Contains(w, StringComparison.Ordinal)).ToList();
        Check("compatibility page: names no other program", banned.Count == 0, string.Join(", ", banned));
        var page = CdPagePath();
        if (page is null) { Skip("compatibility page: docs/COMPATIBILITY.md matches the record", "no docs/COMPATIBILITY.md above the program's folder (an installed copy, not a source tree)"); return; }
        var onDisk = CdNormalise(File.ReadAllText(page));
        Check("compatibility page: docs/COMPATIBILITY.md is what the record generates (run `TabForge.exe --gp-compat-doc docs\\COMPATIBILITY.md` after changing a row)", onDisk == CdNormalise(generated),
            $"first difference at character {Enumerable.Range(0, Math.Min(onDisk.Length, CdNormalise(generated).Length)).FirstOrDefault(i => onDisk[i] != CdNormalise(generated)[i])} of {onDisk.Length}");
    }

    /// <summary>`TabForge.exe --gp-compat-doc &lt;out.md&gt;`: writes the compatibility page generated from the capability record.</summary>
    internal static int RunGpCompatDoc(string outPath)
    {
        File.WriteAllText(outPath, BuildCompatibilityDoc(), new UTF8Encoding(false));
        Console.Out.WriteLine($"Wrote {outPath}");
        return 0;
    }
}
