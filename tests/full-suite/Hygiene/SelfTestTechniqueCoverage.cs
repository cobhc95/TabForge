using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge;

// TestTechniqueInfoTable (end of file) guards the technique table itself and the consumers that read it.
// Every technique in TechniqueNames, and every importer-only row, must reach each consumer family: engraving, the playback compile, GP import, GP export and MusicXML export.
// A name the GP importer adds as a literal (Add("Name")) must have a row, so a new importer name cannot skip the table.
// It is a source scan: a family handles a technique when one of its files names the constant (TechniqueNames.X) or its stored text ("X"), or one
// of the data fields the family reads for it (TechniqueAliases). A scan of text also sees a mention in a comment, so the check is a tripwire for
// missing wiring, not proof of behaviour. A technique a family does not handle must be listed in its TechniqueInfo row (NotSupported) with its reason. A listed gap
// that a family now handles fails too, so the list stays true. A new technique without handling or a listed gap fails.
public static partial class SelfTest
{
    /// <summary>Techniques a family does not handle yet, from the NotSupported entries of the technique table (Models/TechniqueInfo.cs). The reason says why the format or the playback has no equivalent.</summary>
    private static (string Technique, string Family, string Reason)[] TechniqueKnownGaps =>
        TechniqueInfo.All.SelectMany(r => r.NotSupported.Select(g => (r.Name, g.Family, g.Reason))).ToArray();

    /// <summary>Data a family reads for a technique, when the technique is recorded as data rather than as its tag (for example a bend is written from its curve).</summary>
    private static readonly Dictionary<string, string[]> TechniqueAliases = new()
    {
        ["PalmMute"] = new[] { @"TechniqueNames\.(?:Is|Has)PalmMute\b", "\"PM\"" },
        ["Bend"] = new[] { @"\bBendPoints\b" },
        ["TremoloBar"] = new[] { @"\bWhammyPoints\b" },
    };

    private static void TestTechniqueCoverage()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("TechniqueCoverage: every technique reaches each consumer family", "no source checkout found", "source-hygiene"); return; }
        var tf = Path.Combine(root, "src", "TabForge");
        var problems = new List<string>();

        // Constants whose name ends in Legacy are read-only aliases of another technique (PalmMuteLegacy), not techniques of their own.
        var techniques = Regex.Matches(File.ReadAllText(Path.Combine(tf, "Models", "TechniqueNames.cs")), @"public const string (\w+) = ""([^""]+)"";")
            .Select(m => (Name: m.Groups[1].Value, Value: m.Groups[2].Value))
            .Where(t => !t.Name.EndsWith("Legacy", StringComparison.Ordinal)).ToList();
        if (techniques.Count < 20) problems.Add($"only {techniques.Count} techniques parsed from TechniqueNames.cs");

        var families = ConsumerFamilyFiles(tf);
        var text = new Dictionary<string, string>();
        foreach (var (family, files) in families)
        {
            var missing = files.Where(f => !File.Exists(f)).ToList();
            if (files.Length == 0 || missing.Count > 0) problems.Add($"family {family} has no file to scan: {string.Join(", ", missing.DefaultIfEmpty("none listed"))}");
            text[family] = string.Join("\n", files.Where(File.Exists).Select(File.ReadAllText));
        }

        foreach (var (name, value) in techniques)
            if (TechniqueInfo.Find(value) is null) problems.Add($"{name} has no row in TechniqueInfo (Models/TechniqueInfo.cs)");

        // Importer-only rows are checked the same way; a name the GP importer adds as a literal must have a row, so a new one cannot skip the table.
        var importerOnly = TechniqueInfo.All.Where(r => r.ImporterOnly).Select(r => (Name: r.Name, Value: r.Name)).ToList();
        var everyName = techniques.Concat(importerOnly).ToList();
        foreach (var literal in ImporterAddedNames(text["gp-import"]))
            if (TechniqueInfo.Find(literal) is null) problems.Add($"the importer adds {literal} with no row in TechniqueInfo (Models/TechniqueInfo.cs)");

        var names = everyName.Select(t => t.Value).ToHashSet(StringComparer.Ordinal);
        var gaps = TechniqueKnownGaps.ToDictionary(g => (g.Technique, g.Family), g => g.Reason);
        foreach (var gap in TechniqueKnownGaps)
        {
            if (!names.Contains(gap.Technique)) problems.Add($"gap entry {gap.Technique}/{gap.Family}: no such technique (renamed or removed?)");
            if (!families.ContainsKey(gap.Family)) problems.Add($"gap entry {gap.Technique}/{gap.Family}: no such family");
            if (string.IsNullOrWhiteSpace(gap.Reason)) problems.Add($"gap entry {gap.Technique}/{gap.Family}: no reason");
        }

        var handled = 0;
        foreach (var (name, value) in everyName)
            foreach (var family in families.Keys)
            {
                var referenced = TechniqueReferenced(text[family], name, value) || family == "engraving" && TechniqueInfo.MarkOf(value).Length > 0
                    || family == "gp-import" && TechniqueInfo.Find(value) is { GpId.Length: > 0, NotSupported: var na } && !na.Any(g => g.Family == "gp-import")
                    || family == "musicxml-export" && TechniqueInfo.Find(value) is { MusicXmlArticulation.Length: > 0 } && text[family].Contains("MusicXmlArticulations", StringComparison.Ordinal);
                var listed = gaps.ContainsKey((value, family));
                if (referenced) handled++;
                if (referenced && listed) problems.Add($"{name} is now handled in {family}: remove its NotSupported entry in TechniqueInfo");
                else if (!referenced && !listed) problems.Add($"{name} is not handled in {family}: handle it or list it in its TechniqueInfo row (NotSupported) with a reason");
            }

        var cells = everyName.Count * families.Count;
        Check($"technique coverage: {techniques.Count} techniques and {importerOnly.Count} importer-only names x {families.Count} consumer families, {gaps.Count} listed gaps, {handled} of {cells} handled",
            problems.Count == 0, string.Join("; ", problems.Take(12)));
    }

    /// <summary>The names the GP importer adds as string literals (Add("Name")). A name built at run time (Rasgueado + pattern) is not seen here.</summary>
    private static HashSet<string> ImporterAddedNames(string importerText) =>
        Regex.Matches(importerText, @"\.Add\(""(\w+)""\)").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>The source files each consumer family reads. Families are the engraving, the playback compile, GP import, GP export and MusicXML export.</summary>
    private static Dictionary<string, string[]> ConsumerFamilyFiles(string tf)
    {
        var views = Path.Combine(tf, "Views");
        var score = Path.Combine(views, "Score");
        var services = Path.Combine(tf, "Services");
        static string[] Glob(string dir, string pattern) => Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>();
        var gpArticulations = Path.Combine(services, "GpArticulations.cs");
        var gpExporter = Path.Combine(services, "GuitarProExporter.cs");
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["engraving"] = Glob(score, "ScoreRenderer*.cs").Concat(Glob(score, "ScoreMarkText.cs")).Concat(Glob(score, "ScoreLayoutEngine*.cs"))
                .Concat(Glob(views, "StaffNotation*.cs")).Concat(Glob(views, "TabSlideNotation.cs")).ToArray(),
            ["playback"] = Glob(Path.Combine(tf, "Playback"), "ScoreToMidiCompiler*.cs"),
            ["gp-import"] = Glob(services, "GuitarPro*.cs").Where(f => f != gpExporter).Append(gpArticulations).ToArray(),
            ["gp-export"] = new[] { gpExporter, gpArticulations },
            ["musicxml-export"] = new[] { Path.Combine(services, "MusicXmlExportService.cs") },
        };
    }

    /// <summary>True when the family text names the technique by its constant, its stored text or one of its data aliases.</summary>
    private static bool TechniqueReferenced(string text, string name, string value)
    {
        var patterns = new List<string> { $@"TechniqueNames\.{Regex.Escape(name)}\b", Regex.Escape($"\"{value}\"") };
        if (TechniqueAliases.TryGetValue(name, out var aliases)) patterns.AddRange(aliases);
        return patterns.Any(p => Regex.IsMatch(text, p));
    }

    /// <summary>The engraved text the old ScoreMarkText.ShortTechnique switch returned, per name. Names not listed returned "".</summary>
    private static readonly (string Name, string Mark)[] GoldenMarks =
    {
        ("LetRing", "let ring"), ("Harmonic", "H"), ("ArtificialHarmonic", "A.H."), ("PinchHarmonic", "P.H."), ("TapHarmonic", "T.H."),
        ("SemiHarmonic", "S.H."), ("FeedbackHarmonic", "F.B."), ("Vibrato", ""), ("WideVibrato", ""), ("TremBar", "T"), ("Bend", "b"),
        ("LegatoSlide", "/"), ("ShiftSlide", "S"), ("SlideInBelow", "↗"), ("SlideInAbove", "↘"), ("SlideOutUp", "↗"), ("SlideOutDown", "↘"),
        ("PickSlideUp", "P.S.↑"), ("PickSlideDown", "P.S.↓"), ("DeadSlapped", "D.S."), ("HOPO", ""), ("HOPOOrigin", ""), ("HOPODestination", ""),
        ("Tapping", ""), ("LeftTap", ""), ("Slap", "S"), ("Pop", "P"), ("Trill", "tr"), ("TremoloPick", "𝄆"), ("GraceOnBeat", "gr"),
        ("GraceBend", "grb"), ("Ghost", "G"), ("Dead", "X"), ("FadeIn", "<"), ("FadeOut", ">"), ("WahOpen", "wah"), ("WahClose", "wah"),
        ("GraceBefore", "gr"), ("BrushDown", "↓"), ("BrushUp", "↑"), ("ArpeggioDown", "arp↓"), ("ArpeggioUp", "arp↑"),
        ("PalmMute", ""), ("PM", ""), ("Slide", ""), ("TremBarDive", ""), ("harmonic", ""), ("Unknown", ""), ("", ""),
    };

    /// <summary>What the old Guitar Pro import switches mapped (alphaTab enum name, value) to; any other value mapped to no technique.</summary>
    private static readonly (string Enum, string Value, string? Technique)[] GoldenGpMappings =
    {
        ("HarmonicType", "Natural", "Harmonic"), ("HarmonicType", "Artificial", "ArtificialHarmonic"), ("HarmonicType", "Pinch", "PinchHarmonic"),
        ("HarmonicType", "Tap", "TapHarmonic"), ("HarmonicType", "Semi", "SemiHarmonic"), ("HarmonicType", "Feedback", "FeedbackHarmonic"),
        ("HarmonicType", "None", null), ("BrushType", "ArpeggioDown", "ArpeggioDown"), ("BrushType", "ArpeggioUp", "ArpeggioUp"),
        ("BrushType", "BrushDown", null), ("BrushType", "BrushUp", null), ("BrushType", "None", null), ("WahPedal", "Open", "WahOpen"), ("WahPedal", "Closed", "WahClose"), ("WahPedal", "None", null),
        ("GraceType", "BeforeBeat", "GraceBefore"), ("GraceType", "OnBeat", "GraceOnBeat"), ("GraceType", "BendGrace", "GraceBend"), ("GraceType", "None", null),
    };

    private static void TestTechniqueInfoTable()
    {
        var problems = new List<string>();
        var constants = typeof(TechniqueNames).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string) && !f.Name.EndsWith("Legacy", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!).ToList();
        foreach (var name in constants)
            if (TechniqueInfo.Find(name) is null) problems.Add($"TechniqueNames value {name} has no TechniqueInfo row");
        foreach (var dup in TechniqueInfo.All.GroupBy(r => r.Name).Where(g => g.Count() > 1)) problems.Add($"duplicate row {dup.Key}");
        foreach (var dup in TechniqueInfo.All.Where(r => r.GpId.Length > 0).GroupBy(r => r.GpId).Where(g => g.Count() > 1)) problems.Add($"duplicate GpId {dup.Key}");
        foreach (var row in TechniqueInfo.All.Where(r => !r.ImporterOnly && !constants.Contains(r.Name))) problems.Add($"row {row.Name} is not an importer-only row but has no TechniqueNames constant");

        var marks = GoldenMarks.ToDictionary(g => g.Name, g => g.Mark);
        foreach (var (name, mark) in GoldenMarks)
            if (ScoreMarkText.ShortTechnique(name) != mark) problems.Add($"mark of {name}: '{ScoreMarkText.ShortTechnique(name)}' expected '{mark}'");
        // A table row with a mark the golden list does not know would change the engraving.
        foreach (var row in TechniqueInfo.All)
            if (!marks.TryGetValue(row.Name, out var golden) ? row.Mark.Length > 0 : row.Mark != golden) problems.Add($"row {row.Name} mark '{row.Mark}' differs from the golden list");

        foreach (var (enumName, value, technique) in GoldenGpMappings)
            if (TechniqueInfo.FromGp(enumName, value) != technique) problems.Add($"GP {enumName}.{value}: '{TechniqueInfo.FromGp(enumName, value)}' expected '{technique}'");

        Check($"technique table: {TechniqueInfo.All.Count} rows, {constants.Count} persisted names, {GoldenMarks.Length} golden marks, {GoldenGpMappings.Length} golden import mappings",
            problems.Count == 0, string.Join("; ", problems.Take(10)));
    }

    /// <summary>Technique tag sets exported to a .gp and read back: the tags and harmonic fret that come out, per input. Captured from the writer before it read the table.</summary>
    private static readonly (string Input, string Expected)[] GoldenGpExport =
    {
        ("Harmonic", "Harmonic|7"),
        ("ArtificialHarmonic", "ArtificialHarmonic|7"),
        ("PinchHarmonic", "Harmonic+PinchHarmonic|7"),
        ("TapHarmonic", "Harmonic+TapHarmonic|7"),
        ("SemiHarmonic", "Harmonic+SemiHarmonic|7"),
        ("FeedbackHarmonic", "FeedbackHarmonic+Harmonic|7"),
        ("PinchHarmonic+ArtificialHarmonic", "Harmonic+PinchHarmonic|7"),
        ("TapHarmonic+SemiHarmonic", "Harmonic+TapHarmonic|7"),
        ("Harmonic+FeedbackHarmonic", "FeedbackHarmonic+Harmonic|7"),
        ("ArtificialHarmonic+TapHarmonic", "ArtificialHarmonic|7"),
        ("FadeIn", "FadeIn|"),
        ("FadeOut", "FadeOut|"),
        ("FadeIn+FadeOut", "FadeIn|"),
        ("WahOpen", "WahOpen|"),
        ("WahClose", "WahClose|"),
        ("WahOpen+WahClose", "WahOpen|"),
        ("ArpeggioDown", "ArpeggioDown+BrushDown|"),
        ("ArpeggioUp", "ArpeggioUp+BrushUp|"),
        ("ArpeggioDown+ArpeggioUp", "ArpeggioDown+BrushDown|"),
        ("BrushDown", "BrushDown|"),
        ("BrushUp", "BrushUp|"),
        ("BrushDown+BrushUp", "BrushDown|"),
        ("ArpeggioDown+BrushUp", "ArpeggioDown+BrushDown|"),
        ("ArpeggioUp+BrushDown", "ArpeggioUp+BrushUp|"),
        ("ArpeggioDown+BrushDown", "ArpeggioDown+BrushDown|"),
        ("ArpeggioUp+BrushUp", "ArpeggioUp+BrushUp|"),
        ("Bend+Vibrato", "Vibrato|"),
        ("WideVibrato+Vibrato", "WideVibrato|"),
        ("Slap", "Slap|"),
        ("Pop", "Pop|"),
        ("Tapping", "Tapping|"),
        ("TremoloPick", "TremoloPick|"),
        ("Trill", "Trill|"),
    };

    private static void TestTechniqueGpExportGolden()
    {
        var problems = new List<string>();
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-tech-gpx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var actual = new List<(string Input, string Out)>();
            foreach (var input in GpExportInputs)
            {
                var song = BuildSyntheticGpSong();
                var note = song.Tracks[0].Measures[0].Cells[0].Notes[0];
                foreach (var tag in input.Split('+')) note.Techniques.Add(tag);
                note.HarmonicFret = 7;
                var path = Path.Combine(folder, "t.gp");
                GuitarProExporter.Save(song, path, embedProject: false);
                var back = GuitarProImporter.Import(path).Tracks[0].Measures[0].Cells[0].Notes[0];
                actual.Add((input, string.Join("+", back.Techniques.OrderBy(x => x, StringComparer.Ordinal)) + "|" + back.HarmonicFret));
            }
            if (GoldenGpExport.Length == 0) problems.Add("-CAPTURE " + string.Join(" ;; ", actual.Select(a => $"(\"{a.Input}\", \"{a.Out}\")")));
            foreach (var (input, expected) in GoldenGpExport)
                if (actual.FirstOrDefault(a => a.Input == input).Out != expected) problems.Add($"{input}: '{actual.FirstOrDefault(a => a.Input == input).Out}' expected '{expected}'");
            Check($"technique .gp export: {GpExportInputs.Length} inputs written and read back as before", problems.Count == 0, string.Join("; ", problems.Take(6)));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static readonly string[] GpExportInputs =
    {
        "Harmonic", "ArtificialHarmonic", "PinchHarmonic", "TapHarmonic", "SemiHarmonic", "FeedbackHarmonic",
        "PinchHarmonic+ArtificialHarmonic", "TapHarmonic+SemiHarmonic", "Harmonic+FeedbackHarmonic", "ArtificialHarmonic+TapHarmonic",
        "FadeIn", "FadeOut", "FadeIn+FadeOut", "WahOpen", "WahClose", "WahOpen+WahClose",
        "ArpeggioDown", "ArpeggioUp", "ArpeggioDown+ArpeggioUp", "BrushDown", "BrushUp", "BrushDown+BrushUp",
        "ArpeggioDown+BrushUp", "ArpeggioUp+BrushDown", "ArpeggioDown+BrushDown", "ArpeggioUp+BrushUp",
        "Bend+Vibrato", "WideVibrato+Vibrato", "Slap", "Pop", "Tapping", "TremoloPick", "Trill",
    };

    /// <summary>First 12 hex digits of the SHA-256 of the whole MusicXML file written for the synthetic song with this technique set on its first note. Captured before the writer read the table.</summary>
    private static readonly (string Input, string Hash)[] GoldenMusicXml =
    {
        ("PalmMute", "727C8632BF2E"),
        ("LetRing", "A0EA5EE3A8AE"),
        ("HOPO", "0CC677F2975F"),
        ("HOPOOrigin", "0CC677F2975F"),
        ("HOPODestination", "C451A89974F3"),
        ("Bend", "AAD70DB7056F"),
        ("LegatoSlide", "144FA166E64E"),
        ("ShiftSlide", "144FA166E64E"),
        ("Vibrato", "894307D7E7CA"),
        ("WideVibrato", "C85531E52401"),
        ("TremBar", "C451A89974F3"),
        ("Harmonic", "AD03C34EA300"),
        ("ArtificialHarmonic", "E19531D73CF6"),
        ("Tapping", "18FCE303D5C0"),
        ("Slap", "C451A89974F3"),
        ("Pop", "C451A89974F3"),
        ("Trill", "507742D854FE"),
        ("TremoloPick", "6E1970E513FC"),
        ("FadeIn", "C451A89974F3"),
        ("FadeOut", "C451A89974F3"),
        ("WahOpen", "C451A89974F3"),
        ("WahClose", "C451A89974F3"),
        ("BrushDown", "E58F0CC2C0DF"),
        ("BrushUp", "D3128E345DF9"),
        ("ArpeggioDown", "E58F0CC2C0DF"),
        ("ArpeggioUp", "D3128E345DF9"),
        ("PinchHarmonic", "D6A6E3419286"),
        ("TapHarmonic", "9EB19E6E6AD7"),
        ("SemiHarmonic", "AD03C34EA300"),
        ("FeedbackHarmonic", "AD03C34EA300"),
        ("SlideInBelow", "430FDC93CE6C"),
        ("SlideInAbove", "FE3D1F6DEBB0"),
        ("SlideOutUp", "F8B7D2E6CD06"),
        ("SlideOutDown", "890A7C191A5F"),
        ("PickSlideUp", "C451A89974F3"),
        ("PickSlideDown", "C451A89974F3"),
        ("DeadSlapped", "C451A89974F3"),
        ("LeftTap", "C451A89974F3"),
        ("GraceBefore", "C451A89974F3"),
        ("GraceOnBeat", "C451A89974F3"),
        ("GraceBend", "AAD70DB7056F"),
        ("Ghost", "C451A89974F3"),
        ("Dead", "C451A89974F3"),
        ("Accent", "C451A89974F3"),
        ("Legato", "C451A89974F3"),
        ("Rasgueado", "C451A89974F3"),
        ("Tie", "55AE6536E903"),
        ("PickDown", "E848A4AA9FE8"),
        ("PickUp", "E49A3789A25E"),
        ("PinchHarmonic+ArtificialHarmonic", "D6A6E3419286"),
        ("TapHarmonic+SemiHarmonic", "9EB19E6E6AD7"),
        ("Harmonic+FeedbackHarmonic", "AD03C34EA300"),
        ("ArtificialHarmonic+TapHarmonic", "9EB19E6E6AD7"),
        ("PinchHarmonic+TapHarmonic", "9EB19E6E6AD7"),
        ("ArpeggioDown+BrushUp", "E58F0CC2C0DF"),
        ("ArpeggioUp+BrushDown", "E58F0CC2C0DF"),
        ("BrushDown+BrushUp", "E58F0CC2C0DF"),
        ("Vibrato+WideVibrato", "C85531E52401"),
        ("Bend+Vibrato", "171481DB4234"),
        ("Bend+Vibrato+WideVibrato", "BB02FF368502"),
        ("SlideInBelow+SlideInAbove", "9CAAA6EB1265"),
        ("SlideOutUp+SlideOutDown", "0578FB775249"),
        ("SlideInBelow+SlideOutDown", "BF811FEFF9D9"),
        ("HOPO+HOPODestination", "C451A89974F3"),
        ("LegatoSlide+ShiftSlide", "144FA166E64E"),
    };

    private static IEnumerable<string> MusicXmlInputs() => TechniqueInfo.All.Select(r => r.Name).Concat(new[]
    {
        "PinchHarmonic+ArtificialHarmonic", "TapHarmonic+SemiHarmonic", "Harmonic+FeedbackHarmonic", "ArtificialHarmonic+TapHarmonic", "PinchHarmonic+TapHarmonic",
        "ArpeggioDown+BrushUp", "ArpeggioUp+BrushDown", "BrushDown+BrushUp", "Vibrato+WideVibrato", "Bend+Vibrato", "Bend+Vibrato+WideVibrato",
        "SlideInBelow+SlideInAbove", "SlideOutUp+SlideOutDown", "SlideInBelow+SlideOutDown", "HOPO+HOPODestination", "LegatoSlide+ShiftSlide",
    });

    private static void TestTechniqueMusicXmlGolden()
    {
        var problems = new List<string>();
        var actual = new List<(string Input, string Hash)>();
        foreach (var input in MusicXmlInputs())
        {
            string Hash()
            {
                var song = BuildSyntheticGpSong();
                var note = song.Tracks[0].Measures[0].Cells[0].Notes[0];
                foreach (var tag in input.Split('+')) note.Techniques.Add(tag);
                if (input.Contains("Bend")) { note.BendPoints.Add(new BendPointModel { Offset = 0, Value = 0 }); note.BendPoints.Add(new BendPointModel { Offset = 30, Value = 4 }); }
                note.HarmonicFret = 7;
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(MusicXmlExportService.ToBytes(song)))[..12];
            }
            var hash = Hash();
            if (hash != Hash()) problems.Add($"{input}: the export is not deterministic");
            actual.Add((input, hash));
        }
        if (GoldenMusicXml.Length == 0) problems.Add("GOLDEN-CAPTURE " + string.Join(" ;; ", actual.Select(a => $"(\"{a.Input}\", \"{a.Hash}\")")));
        foreach (var (input, hash) in GoldenMusicXml)
            if (actual.FirstOrDefault(a => a.Input == input).Hash != hash) problems.Add($"{input}: file differs from the golden export");
        foreach (var a in actual)
            if (GoldenMusicXml.Length > 0 && GoldenMusicXml.All(g => g.Input != a.Input)) problems.Add($"{a.Input}: no golden entry");
        Check($"technique MusicXML export: {actual.Count} inputs write the same file as before", problems.Count == 0, string.Join("; ", problems.Take(6)));
    }
}
