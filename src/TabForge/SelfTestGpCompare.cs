using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using TabForge.Services;

namespace TabForge;

// `TabForge.exe --gp-compare <a.gp> <b.gp> [report.txt]`: compares two Guitar Pro files as TabForge reads them (the same round-trip FACTS the semantic suite
// compares: pitch, rhythm, techniques, bends, fingering, mixer ...) and, separately, what each gpif holds (which elements and note/beat properties
// appear how often). Made for "an original and the same song re-saved by Guitar Pro 8" (work/night-2026-10-02). No tolerance: any difference is listed.
// Facts that TabForge's importer does not read cannot show up in the first part; the second part is the check on those.
public static partial class SelfTest
{
    /// <summary>The report of one pair; <paramref name="differences"/> is the number of fact differences (the exit code is 1 when there are any).</summary>
    internal static string GpCompareReport(string pathA, string pathB, out int differences)
    {
        var a = GuitarProImporter.Import(pathA); var b = GuitarProImporter.Import(pathB);
        var fa = RtScoreFacts(a, out var notesA, out var beatsA); var fb = RtScoreFacts(b, out var notesB, out var beatsB);
        foreach (var (k, v) in RtAudioFacts(a)) fa[k] = v;
        foreach (var (k, v) in RtAudioFacts(b)) fb[k] = v;
        var diffs = RtCompare(fa, fb);
        differences = diffs.Count;
        var sb = new StringBuilder();
        sb.AppendLine($"A: {Path.GetFileName(pathA)} ({a.Tracks.Count} tracks, {beatsA} beats, {notesA} notes)");
        sb.AppendLine($"B: {Path.GetFileName(pathB)} ({b.Tracks.Count} tracks, {beatsB} beats, {notesB} notes)");
        sb.AppendLine($"FACTS compared {fa.Count} / {fb.Count}; DIFFERENCES {diffs.Count}");
        foreach (var group in diffs.GroupBy(d => d.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {group.Key}: {group.Count()}");
            foreach (var d in group.Take(8)) sb.AppendLine($"    {d.Where}: {d.Expected}  ->  {d.Actual}");
            if (group.Count() > 8) sb.AppendLine($"    ... {group.Count() - 8} more");
        }

        // per fact name: how many facts the original holds, how many are not the default, how many changed (the coverage of each capability row)
        static string NameOf(string key) { var i = key.LastIndexOf('|'); return i < 0 ? key : key[(i + 1)..]; }
        var changed = new HashSet<string>(diffs.Select(d => d.Where), StringComparer.Ordinal);
        foreach (var group in fa.GroupBy(kv => NameOf(kv.Key)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var nonDefault = group.Count(kv => kv.Value is not ("" or "0" or "-" or "(none)"));
            var diff = group.Count(kv => changed.Contains(kv.Key));
            sb.AppendLine($"STAT\t{group.Key}\t{group.Count()}\t{nonDefault}\t{diff}");
        }

        // what the gpif holds: element / property names per owner, original against the re-save
        var ia = GpifInventory(pathA); var ib = GpifInventory(pathB);
        // Presence only: Guitar Pro 7+ shares identical beats, notes and rhythms between voices, so the counts of a re-save are not comparable.
        sb.AppendLine("GPIF inventory (INV name, count in A, count in B: only names present in one file and absent from the other)");
        foreach (var key in ia.Keys.Union(ib.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var ca = ia.GetValueOrDefault(key); var cb = ib.GetValueOrDefault(key);
            if ((ca == 0) != (cb == 0)) sb.AppendLine($"INV\t{key}\t{ca}\t{cb}");
        }
        return sb.ToString();
    }

    /// <summary>Counts of the gpif's child elements and of each note/beat Property name, keyed "Owner/child" and "Owner.Property:name".</summary>
    private static Dictionary<string, int> GpifInventory(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var bytes = File.ReadAllBytes(path);
        var part = GuitarProExporter.ReadZip(bytes).FirstOrDefault(p => p.Name.EndsWith("score.gpif", StringComparison.OrdinalIgnoreCase));
        if (part.Data is null) return map;
        var doc = XDocument.Parse(Encoding.UTF8.GetString(part.Data));
        foreach (var owner in new[] { "Note", "Beat", "Track", "MasterBar", "Bar", "Voice", "Rhythm" })
            foreach (var element in doc.Descendants(owner))
            {
                if (element.Parent?.Name.LocalName is not ("Notes" or "Beats" or "Tracks" or "MasterBars" or "Bars" or "Voices" or "Rhythms")) continue;
                foreach (var child in element.Elements())
                {
                    var key = $"{owner}/{child.Name.LocalName}";
                    map[key] = map.GetValueOrDefault(key) + 1;
                    if (child.Name.LocalName == "Properties")
                        foreach (var prop in child.Elements("Property"))
                        {
                            var pk = $"{owner}.Property:{(string?)prop.Attribute("name")}";
                            map[pk] = map.GetValueOrDefault(pk) + 1;
                        }
                }
            }
        return map;
    }

    /// <summary>`--gp-open &lt;file.gp&gt;`: what opening the file in TabForge does (the same document seam the Open command uses, in this process): the notice shown, what the file holds, and each track's mixer and first notes.</summary>
    internal static int RunGpOpen(string path)
    {
        var sb = new StringBuilder();
        var controller = new Documents.DocumentController();
        var opened = controller.Open(path);
        var project = opened.Project;
        var embedded = GuitarProExporter.HasEmbeddedEntry(path);
        var sidecar = AudioDataFile.PathFor(path);
        sb.AppendLine($"file: {Path.GetFileName(path)}; embedded TabForge project entry: {embedded}; sidecar beside it: {File.Exists(sidecar)}");
        sb.AppendLine($"notice: {opened.Notice ?? "(none)"}");
        sb.AppendLine($"song: '{project.Title}' tempo {project.Tempo}, {project.Tracks.Count} track(s)");
        foreach (var track in project.Tracks)
        {
            var first = track.Measures.Count > 0 ? string.Join(" ", track.Measures[0].Cells.Where(c => c.Notes.Count > 0).Take(3).Select(c => "[" + string.Join("+", c.Notes.Select(n => $"s{n.StringIndex + 1}f{n.Fret}")) + "]")) : "";
            sb.AppendLine($"track '{track.Name}': volume {track.Volume} pan {track.Pan} mute {track.Mute} solo {track.Solo} reverb {track.Reverb} chorus {track.Chorus} rig '{track.Rig.Name}' performer '{track.Performer}' | bar 1: {first}");
        }
        Console.Out.WriteLine(sb.ToString().TrimEnd());
        return 0;
    }

    /// <summary>
    /// `--write-gp-probes &lt;dir&gt;`: one-question files for Guitar Pro 8 (open, save, then `--gp-compare` the pair). ghost-combos.gp: seven chords of a ghost note and a plain note,
    /// where the ghost note ALSO carries each accent mark of the gpif (staccato, heavy accent, accent, tenuto, and two pairs) and one carries none (the control).
    /// The exporter never writes such a note any more; the file is made by editing the gpif, which is what Guitar Pro 8 is being asked about.
    /// </summary>
    internal static int RunWriteGpProbes(string dir)
    {
        Directory.CreateDirectory(dir);
        var marks = new[] { 1, 4, 8, 16, 9, 17, 0 };
        var song = GfSong(2, t =>
        {
            for (var i = 0; i < marks.Length; i++)
            {
                var chord = GfPut(t, i / 4, (i % 4) * 4, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); chord.Notes[0].Ghost = true;
            }
        }, s => s.Title = "probe ghost combos");
        var bytes = GuitarProExporter.ToBytes(song, embedProject: false);
        bytes = GfRewriteEntry(bytes, GuitarProExporter.ScoreEntry, gpif =>
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(gpif));
            var beats = doc.Descendants("Beat").Where(b => b.Element("Notes") is not null).ToList();
            for (var i = 0; i < marks.Length; i++)
            {
                var first = ((string)beats[i].Element("Notes")!).Split(' ')[0];
                var note = doc.Descendants("Note").First(n => (string?)n.Attribute("id") == first);
                if (marks[i] != 0) note.Element("AntiAccent")!.AddAfterSelf(new XElement("Accent", marks[i].ToString(CultureInfo.InvariantCulture)));
            }
            return Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
        });
        var path = Path.Combine(dir, "ghost-combos.gp");
        File.WriteAllBytes(path, bytes);
        File.WriteAllText(Path.Combine(dir, "ghost-combos.md"),
            "# ghost-combos.gp\n\nQuestion: which accent marks does Guitar Pro 8 keep on a note that also has the ghost mark (<AntiAccent>)?\n\n" +
            "Seven beats (bars 1-2), each a chord of string 1 fret 3 (the ghost note) and string 2 fret 2 (plain). The ghost note also has, in the gpif, `<Accent>` of:\n\n" +
            "| beat | Accent value | meaning |\n|---|---|---|\n| 1 | 1 | staccato |\n| 2 | 4 | heavy accent |\n| 3 | 8 | accent (known: Guitar Pro 8 drops the ghost mark) |\n| 4 | 16 | tenuto |\n| 5 | 9 | staccato + accent |\n| 6 | 17 | staccato + tenuto |\n| 7 | none | control: ghost mark alone |\n\n" +
            "Open it in Guitar Pro 8, save it, then run `TabForge.exe --gp-compare ghost-combos.gp <the re-save>.gp`: a `note.ghost 1 -> 0` line on a beat means that mark replaced the ghost mark; no line means both kept. Also look at what Guitar Pro 8 draws on each beat.\n");
        Console.Out.WriteLine($"Wrote {path}");
        return 0;
    }

    internal static int RunGpCompare(string pathA, string pathB, string? reportPath)
    {
        var text = GpCompareReport(pathA, pathB, out var differences);
        if (reportPath is not null) File.WriteAllText(reportPath, text, new UTF8Encoding(false));
        // the console gets the verdict and the differences; the coverage statistics and the gpif inventory are in the report file
        Console.Out.WriteLine(string.Join(Environment.NewLine, text.Split('\n').Select(l => l.TrimEnd('\r')).TakeWhile(l => !l.StartsWith("STAT\t", StringComparison.Ordinal))));
        return differences == 0 ? 0 : 1;
    }
}
