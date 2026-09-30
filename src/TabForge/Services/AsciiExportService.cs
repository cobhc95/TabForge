using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;

namespace TabForge.Services;

// standard ASCII tab exporter.
public static class AsciiExportService
{
    public static void Export(SongProject project, string path)
    {
        path = FilePathPolicy.OutputFile(path, "ASCII tab export", ".txt");
        var sb = new StringBuilder();
        sb.AppendLine($"{project.Title} - {project.Artist}");
        if (!string.IsNullOrWhiteSpace(project.Album)) sb.AppendLine($"Album: {project.Album}");
        sb.AppendLine($"Tempo: {project.Tempo}  Time: {project.TimeSignatureNumerator}/{project.TimeSignatureDenominator}");
        sb.AppendLine(new string('-', 64));
        foreach (var track in project.Tracks)
        {
            sb.AppendLine();
            sb.AppendLine($"[{track.Name}] ({track.Kind}, capo {track.Capo}, program {track.MidiProgram})");
            var strings = Math.Max(1, track.StringTunings.Count);
            var lines = Enumerable.Range(0, strings).Select(_ => new StringBuilder()).ToList();
            for (var s = 0; s < strings; s++) lines[s].Append(s == 0 ? "e||" : s == strings - 1 ? "E||" : $"B||");
            var maxM = track.Measures.Count;
            var systemAnnotations = new List<string>();
            for (var mi = 0; mi < maxM; mi++)
            {
                var m = track.Measures[mi];
                var cells = m.Cells.Take(16).ToList();
                var width = new string[cells.Count];
                for (var ci = 0; ci < cells.Count; ci++)
                {
                    var cell = cells[ci];
                    // Comments, chord names and lyrics are content too: list them under the system so an
                    // ASCII export never silently drops what the file carried.
                    var bits = new List<string>();
                    if (!string.IsNullOrWhiteSpace(cell.ChordName)) bits.Add(cell.ChordName!);
                    if (!string.IsNullOrWhiteSpace(cell.Text)) bits.Add(cell.Text!);
                    if (!string.IsNullOrWhiteSpace(cell.Lyrics)) bits.Add(cell.Lyrics.Replace("\n", " / "));
                    if (bits.Count > 0) systemAnnotations.Add($"  bar {mi + 1} beat {ci + 1}: {string.Join(" | ", bits)}");
                    string token;
                    if (cell.IsRest) token = "--";
                    else if (cell.Notes.Count == 0) token = "--";
                    else
                    {
                        var per = new string[strings];
                        for (var s = 0; s < strings; s++) per[s] = "--";
                        foreach (var n in cell.Notes)
                            if (n.StringIndex >= 0 && n.StringIndex < strings)
                                per[n.StringIndex] = n.Fret.ToString().PadLeft(2, '-').Substring(0, 2);
                        token = string.Join("|", per); // expanded below per-string
                        for (var s = 0; s < strings; s++) lines[s].Append(per[s] + "-");
                        continue;
                    }
                    for (var s = 0; s < strings; s++) lines[s].Append(token + "-");
                }
                for (var s = 0; s < strings; s++) lines[s].Append("|");
                if ((mi + 1) % 4 == 0 || mi == maxM - 1)
                {
                    foreach (var l in lines) { sb.AppendLine(l.ToString()); l.Clear(); for (var s = 0; s < strings; s++) l.Append(s == 0 ? "e||" : s == strings - 1 ? "E||" : "B||"); }
                    if (systemAnnotations.Count > 0)
                    {
                        sb.AppendLine("annotations:");
                        foreach (var annotation in systemAnnotations) sb.AppendLine(annotation);
                        systemAnnotations.Clear();
                    }
                    sb.AppendLine();
                }
            }
        }
        FilePathPolicy.WriteAtomically(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024, leaveOpen: true);
            writer.Write(sb);
            writer.Flush();
        });
    }
}
