using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the TabForge half of the import/export audit: every file of a list is imported, exported in each mode (.gp clean, .gp with the embedded project,
//   .tforge, .mid, .musicxml, .txt), and each result is re-imported (.gp, .tforge: dumped in the ImportParityDump schema) or checked structurally
//   (.mid, .musicxml, .txt have no TabForge importer). Output: one JSON line per (file, mode). compare_io.py diffs it against the source dump and the TuxGuitar half (TgExport.java).
// Does not own: the comparison, the TuxGuitar side, the export code. Runs only when TABFORGE_PARITY_DIR is set (it is not a regression test).
// Env: TABFORGE_PARITY_DIR = samples folder; TABFORGE_PARITY_LIST = text file of relative sample paths (default: every Guitar Pro file);
//   TABFORGE_PARITY_EXPORT_DIR = where exports are written (index.tsv + one folder per file); TABFORGE_PARITY_OUT = output .jsonl;
//   TABFORGE_PARITY_STAGE = "export" (default) or "readtg" (import the tg.gp5 files TgExport.java wrote into the export folders).
public class ExportParityDump
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private static readonly MethodInfo WriteSongMethod = typeof(ImportParityDump).GetMethod("WriteSong", BindingFlags.NonPublic | BindingFlags.Static)!;

    public ExportParityDump() => AlphaTabWarmUp.EnsureReady();

    [Fact]
    public void ExportEveryFileInEveryMode()
    {
        if (!Begin("export", out var dir, out var exportDir, out var outPath, out var files)) return;
        using var writer = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
        var index = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            var id = i.ToString("D3");
            index.Add(id + "\u0009" + files[i]);
            var folder = Path.Combine(exportDir, id);
            Directory.CreateDirectory(folder);
            foreach (var line in AuditOne(Path.Combine(dir, files[i]), files[i], folder)) writer.WriteLine(line);
            writer.Flush();
        }
        File.WriteAllLines(Path.Combine(exportDir, "index.tsv"), index);
    }

    [Fact]
    public void ReadTuxGuitarExports()
    {
        if (!Begin("readtg", out _, out var exportDir, out var outPath, out _)) return;
        using var writer = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
        foreach (var row in File.ReadAllLines(Path.Combine(exportDir, "index.tsv")))
        {
            var cols = row.Split('\u0009');
            var tg = Path.Combine(exportDir, cols[0], "tg.gp5");
            if (File.Exists(tg)) writer.WriteLine(Dump(cols[1], "tfread_tg_gp5", () => GuitarProImporter.Import(tg, new ImportContext())));
            writer.Flush();
        }
    }

    private static bool Begin(string stage, out string dir, out string exportDir, out string outPath, out List<string> files)
    {
        dir = Environment.GetEnvironmentVariable("TABFORGE_PARITY_DIR") ?? "";
        exportDir = Environment.GetEnvironmentVariable("TABFORGE_PARITY_EXPORT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "export");
        outPath = Environment.GetEnvironmentVariable("TABFORGE_PARITY_OUT") ?? Path.Combine(AppContext.BaseDirectory, "tabforge-io.jsonl");
        files = new List<string>();
        var root = dir;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;   // skipped: not a parity run
        if (!string.Equals(Environment.GetEnvironmentVariable("TABFORGE_PARITY_STAGE") ?? "export", stage, StringComparison.OrdinalIgnoreCase)) return false;
        Directory.CreateDirectory(exportDir);
        var list = Environment.GetEnvironmentVariable("TABFORGE_PARITY_LIST");
        var extensions = new[] { ".gp3", ".gp4", ".gp5", ".gpx", ".gp" };
        files = !string.IsNullOrWhiteSpace(list) && File.Exists(list)
            ? File.ReadAllLines(list).Where(l => l.Trim().Length > 0).ToList()
            : Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar.ToString(), "/")).OrderBy(f => f, StringComparer.Ordinal).ToList();
        return true;
    }

    private static IEnumerable<string> AuditOne(string path, string name, string folder)
    {
        SongProject? src = null;
        var srcLine = Dump(name, "src", () => src = GuitarProImporter.Import(path, new ImportContext()));
        yield return srcLine;
        if (src is null) yield break;
        var expected = Expected(src);

        string gpClean = Path.Combine(folder, "gp_clean.gp"), gpEmbed = Path.Combine(folder, "gp_embed.gp"), tforge = Path.Combine(folder, "out.tforge");
        string mid = Path.Combine(folder, "out.mid"), xml = Path.Combine(folder, "out.musicxml"), txt = Path.Combine(folder, "out.txt");

        yield return Dump(name, "gp_clean", () => { GuitarProExporter.Save(src, gpClean, embedProject: false); return GuitarProImporter.Import(gpClean, new ImportContext()); }, gpClean);
        yield return Dump(name, "gp_embed", () => { GuitarProExporter.Save(src, gpEmbed, embedProject: true); return GuitarProImporter.Import(gpEmbed, new ImportContext()); }, gpEmbed,
            w => w.WriteBoolean("hasEmbedded", GuitarProExporter.TryReadEmbedded(gpEmbed) is not null));
        yield return Dump(name, "tforge", () => { ProjectService.Save(tforge, src); return ProjectService.Load(tforge); }, tforge);
        yield return Struct(name, "mid", mid, () => MidiExportService.Export(src, mid), w => MidiFacts(w, mid, expected));
        yield return Struct(name, "musicxml", xml, () => MusicXmlExportService.Export(src, xml), w => XmlFacts(w, xml, expected));
        yield return Struct(name, "txt", txt, () => AsciiExportService.Export(src, txt), w => AsciiFacts(w, txt, src, expected));
    }

    /// <summary>Runs an import/round trip under a timeout and writes one line: the ImportParityDump schema of the result plus the file size and optional extras.</summary>
    private static string Dump(string name, string mode, Func<SongProject?> work, string? outFile = null, Action<Utf8JsonWriter>? extra = null)
    {
        SongProject? project = null; string? error = null;
        var clock = Stopwatch.StartNew();
        var task = Task.Run(() => { try { project = work(); } catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; } });
        if (!task.Wait(Timeout)) error = "timeout";
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("file", name); w.WriteString("mode", mode);
            w.WriteBoolean("ok", error is null && project is not null);
            if (error is not null) w.WriteString("error", error);
            w.WriteNumber("ms", clock.ElapsedMilliseconds);
            if (outFile is not null && File.Exists(outFile)) w.WriteNumber("bytes", new FileInfo(outFile).Length);
            if (error is null && project is not null)
            {
                try { WriteSongMethod.Invoke(null, new object[] { w, project }); }
                catch (Exception ex) { w.WriteString("dumpError", (ex.InnerException ?? ex).Message); }
                try { w.WriteStartObject("facts"); WriteFacts(w, Expected(project)); w.WriteEndObject(); }
                catch (Exception ex) { w.WriteString("factsError", ex.Message); }
            }
            extra?.Invoke(w);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Struct(string name, string mode, string file, Action export, Action<Utf8JsonWriter> facts)
    {
        string? error = null;
        var clock = Stopwatch.StartNew();
        var task = Task.Run(() => { try { export(); } catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; } });
        if (!task.Wait(Timeout)) error = "timeout";
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("file", name); w.WriteString("mode", mode);
            w.WriteBoolean("ok", error is null && File.Exists(file) && new FileInfo(file).Length > 0);
            if (error is not null) w.WriteString("error", error);
            w.WriteNumber("ms", clock.ElapsedMilliseconds);
            if (File.Exists(file))
            {
                w.WriteNumber("bytes", new FileInfo(file).Length);
                var inner = new MemoryStream();
                try
                {
                    using (var iw = new Utf8JsonWriter(inner)) { iw.WriteStartObject(); facts(iw); iw.WriteEndObject(); }
                    w.WritePropertyName("struct"); w.WriteRawValue(inner.ToArray());
                }
                catch (Exception ex) { w.WriteString("structError", ex.GetType().Name + ": " + ex.Message); }
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    // ---- what the source model says the export should carry ----
    private sealed record Facts(int Bars, int Tracks, int Notes, int Ties, int TimeSigChanges, int TempoChanges, int RepeatCloses, int Markers, int PlayedNotes, int PlayedBars,
        int[] TrackBars);

    private static Facts Expected(SongProject p)
    {
        var tracks = p.NotationTracks.ToList();
        var bars = p.MasterBarTrack?.Measures ?? new List<MeasureModel>();
        int notes = 0, ties = 0;
        foreach (var t in tracks)
            foreach (var m in t.Measures)
                foreach (var c in m.Cells.Concat(m.Voice2Cells)) { notes += c.Notes.Count; ties += c.Notes.Count(n => n.Tied); }
        int tsChanges = 0, tempoChanges = 0; (int, int)? last = null; int? lastTempo = null;
        foreach (var b in bars)
        {
            var ts = (b.TimeSigNum ?? p.TimeSignatureNumerator, b.TimeSigDenom ?? p.TimeSignatureDenominator);
            if (last != ts) tsChanges++;
            last = ts;
            var tempo = b.TempoChange ?? lastTempo ?? p.Tempo;
            if (tempo != lastTempo) tempoChanges++;
            lastTempo = tempo;
        }
        int played = 0, playedBars = 0;
        try
        {
            foreach (var bi in PlaybackOrder.Build(p, new PlaybackOptions()).Take(20000))
            {
                playedBars++;
                foreach (var t in tracks)
                    if (bi >= 0 && bi < t.Measures.Count)
                        foreach (var c in t.Measures[bi].Cells.Concat(t.Measures[bi].Voice2Cells)) played += c.Notes.Count(n => !n.Tied);
            }
        }
        catch (Exception) { /* left short: reported as such */ }
        return new Facts(bars.Count, tracks.Count, notes, ties, tsChanges, tempoChanges, bars.Count(b => b.RepeatEnd), p.Markers.Count, played, playedBars,
            tracks.Select(t => t.Measures.Count).ToArray());
    }

    private static void WriteFacts(Utf8JsonWriter w, Facts f)
    {
        w.WriteNumber("bars", f.Bars); w.WriteNumber("tracks", f.Tracks); w.WriteNumber("notes", f.Notes); w.WriteNumber("ties", f.Ties);
        w.WriteNumber("timeSigChanges", f.TimeSigChanges); w.WriteNumber("tempoChanges", f.TempoChanges); w.WriteNumber("repeatCloses", f.RepeatCloses);
        w.WriteNumber("markers", f.Markers); w.WriteNumber("playedNotes", f.PlayedNotes); w.WriteNumber("playedBars", f.PlayedBars);
    }

    // ---- .mid: a small Standard MIDI File reader ----
    private static void MidiFacts(Utf8JsonWriter w, string path, Facts exp)
    {
        var d = File.ReadAllBytes(path);
        int pos = 0;
        int Be32() { var v = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3]; pos += 4; return v; }
        int Be16() { var v = (d[pos] << 8) | d[pos + 1]; pos += 2; return v; }
        if (d.Length < 14 || d[0] != 0x4D || d[1] != 0x54 || d[2] != 0x68 || d[3] != 0x64) throw new InvalidDataException("no MThd");
        pos = 4; Be32();
        int format = Be16(), ntrk = Be16(), division = Be16();
        long noteOn = 0, noteOff = 0, tempo = 0, timeSig = 0, cc7 = 0, cc10 = 0, maxTick = 0, drumOn = 0, unbalanced = 0, trackEndMissing = 0;
        var programs = new SortedSet<int>(); var channels = new SortedSet<int>(); var bpms = new SortedSet<int>();
        for (var t = 0; t < ntrk; t++)
        {
            if (pos + 8 > d.Length) throw new InvalidDataException("truncated track header");
            pos += 4; var len = Be32(); var end = pos + len;
            long tick = 0; int status = 0; bool ended = false;
            var open = new Dictionary<int, int>();
            while (pos < end)
            {
                long delta = 0; int b;
                do { b = d[pos++]; delta = (delta << 7) | (uint)(b & 0x7f); } while ((b & 0x80) != 0);
                tick += delta;
                if (d[pos] >= 0x80) status = d[pos++];
                if (status == 0xFF)
                {
                    int type = d[pos++]; long ml = 0; do { b = d[pos++]; ml = (ml << 7) | (uint)(b & 0x7f); } while ((b & 0x80) != 0);
                    if (type == 0x51 && ml == 3) { tempo++; bpms.Add((int)Math.Round(60000000.0 / ((d[pos] << 16) | (d[pos + 1] << 8) | d[pos + 2]))); }
                    if (type == 0x58) timeSig++;
                    if (type == 0x2F) ended = true;
                    pos += (int)ml;
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    long ml = 0; do { b = d[pos++]; ml = (ml << 7) | (uint)(b & 0x7f); } while ((b & 0x80) != 0);
                    pos += (int)ml;
                }
                else
                {
                    var kind = status & 0xF0; var ch = status & 0x0F;
                    var a = d[pos++]; var v = (kind == 0xC0 || kind == 0xD0) ? 0 : d[pos++];
                    channels.Add(ch);
                    if (kind == 0x90 && v > 0) { noteOn++; if (ch == 9) drumOn++; open[ch * 128 + a] = open.GetValueOrDefault(ch * 128 + a) + 1; }
                    else if (kind == 0x80 || kind == 0x90) { noteOff++; var k = ch * 128 + a; if (open.GetValueOrDefault(k) > 0) open[k]--; }
                    else if (kind == 0xC0) programs.Add(a);
                    else if (kind == 0xB0) { if (a == 7) cc7++; if (a == 10) cc10++; }
                }
            }
            if (!ended) trackEndMissing++;
            unbalanced += open.Values.Sum();
            maxTick = Math.Max(maxTick, tick);
            pos = end;
        }
        w.WriteNumber("format", format); w.WriteNumber("tracks", ntrk); w.WriteNumber("division", division);
        w.WriteNumber("noteOn", noteOn); w.WriteNumber("noteOff", noteOff); w.WriteNumber("unbalancedNotes", unbalanced); w.WriteNumber("drumNoteOn", drumOn);
        w.WriteNumber("tempoEvents", tempo); w.WriteNumber("timeSigEvents", timeSig); w.WriteNumber("cc7", cc7); w.WriteNumber("cc10", cc10);
        w.WriteNumber("maxTick", maxTick); w.WriteNumber("trackEndMissing", trackEndMissing);
        w.WriteNumber("programs", programs.Count); w.WriteNumber("channels", channels.Count); w.WriteNumber("distinctTempos", bpms.Count);
        w.WriteNumber("expectedNoteOn", exp.PlayedNotes); w.WriteNumber("expectedTracks", exp.Tracks); w.WriteNumber("expectedPlayedBars", exp.PlayedBars);
    }

    // ---- .txt ----
    private static void AsciiFacts(Utf8JsonWriter w, string path, SongProject p, Facts exp)
    {
        var lines = File.ReadAllLines(path);
        int sections = lines.Count(l => l.StartsWith("[", StringComparison.Ordinal));
        long bars = 0, tokens = 0, doubledPrefix = 0;
        var previousWasTab = false;
        foreach (var l in lines)
        {
            var isTab = l.StartsWith("e||", StringComparison.Ordinal) || l.StartsWith("B||", StringComparison.Ordinal) || l.StartsWith("E||", StringComparison.Ordinal);
            // after the first system every string line starts with all the string prefixes (e||B||B||..E||): counted, then stripped before counting bars and tokens
            var body = isTab ? System.Text.RegularExpressions.Regex.Replace(l, "^([eEB]\\|\\|)+", "") : "";
            if (isTab && body.Length < l.Length - 3) doubledPrefix++;
            foreach (var bar in body.Split("|", StringSplitOptions.RemoveEmptyEntries))
                for (var i = 0; i + 2 <= bar.Length; i += 3) if (bar.Substring(i, 2) != "--") tokens++;
            if (isTab && !previousWasTab) bars += body.Count(c => c == 124);   // bars of one system, counted on its first string line
            previousWasTab = isTab;
        }
        long expectedTokens = 0, droppedNotes = 0;
        foreach (var t in p.NotationTracks)
        {
            var strings = Math.Max(1, t.StringTunings.Count);
            foreach (var m in t.Measures)
            {
                var cells = m.Cells;
                for (var i = 0; i < cells.Count; i++)
                {
                    var perString = cells[i].IsRest ? 0 : cells[i].Notes.Where(n => n.StringIndex >= 0 && n.StringIndex < strings).Select(n => n.StringIndex).Distinct().Count();
                    if (i < 16) expectedTokens += perString; else droppedNotes += cells[i].Notes.Count;
                }
            }
        }
        w.WriteNumber("sections", sections); w.WriteNumber("bars", bars); w.WriteNumber("noteTokens", tokens); w.WriteNumber("lines", lines.Length); w.WriteNumber("doubledPrefixLines", doubledPrefix);
        w.WriteNumber("expectedSections", exp.Tracks); w.WriteNumber("expectedBars", exp.TrackBars.Sum()); w.WriteNumber("expectedTokens", expectedTokens);
        w.WriteNumber("notesBeyond16Cells", droppedNotes);
    }

    // ---- .musicxml ----
    private static void XmlFacts(Utf8JsonWriter w, string path, Facts exp)
    {
        XDocument doc;
        using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore })) doc = XDocument.Load(reader);
        var parts = doc.Root!.Elements("part").ToList();
        var first = parts.FirstOrDefault();
        var measures = first?.Elements("measure").Count() ?? 0;
        var notes = parts.Sum(p => p.Descendants("note").Count(n => n.Element("rest") is null));
        var chordNotes = parts.Sum(p => p.Descendants("note").Count(n => n.Element("chord") is not null));
        var rests = parts.Sum(p => p.Descendants("note").Count(n => n.Element("rest") is not null));
        var tieStops = parts.Sum(p => p.Descendants("tie").Count(t => (string?)t.Attribute("type") == "stop"));
        var times = first?.Descendants("attributes").Count(a => a.Element("time") is not null) ?? 0;
        var staves = parts.Select(p => p.Descendants("staves").Select(s => int.TryParse(s.Value, out var v) ? v : 1).DefaultIfEmpty(1).First()).DefaultIfEmpty(1).Max();
        var notesStaff1 = parts.Sum(p => p.Descendants("note").Count(n => n.Element("rest") is null && ((string?)n.Element("staff") ?? "1") == "1"));
        w.WriteNumber("parts", parts.Count); w.WriteNumber("measures", measures); w.WriteNumber("notes", notes); w.WriteNumber("notesStaff1", notesStaff1);
        w.WriteNumber("chordNotes", chordNotes); w.WriteNumber("rests", rests); w.WriteNumber("tieStops", tieStops); w.WriteNumber("timeSigs", times);
        w.WriteNumber("maxStaves", staves);
        w.WriteNumber("tempoSounds", doc.Descendants("sound").Count(s => s.Attribute("tempo") is not null));
        w.WriteNumber("repeats", doc.Descendants("repeat").Count()); w.WriteNumber("endings", doc.Descendants("ending").Count());
        w.WriteNumber("rehearsal", doc.Descendants("rehearsal").Count());
        w.WriteNumber("expectedParts", exp.Tracks); w.WriteNumber("expectedMeasures", exp.Bars); w.WriteNumber("expectedNotes", exp.Notes - exp.Ties);
        w.WriteNumber("expectedTies", exp.Ties); w.WriteNumber("expectedTimeSigs", exp.TimeSigChanges); w.WriteNumber("expectedMarkers", exp.Markers);
    }
}
