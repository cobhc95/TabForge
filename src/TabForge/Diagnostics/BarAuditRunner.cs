using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Diagnostics;

/// <summary>
/// `--render-bars` engine: every track, every bar, one PNG per view cropped to the bar's slice, plus checks.json (per-bar findings) and
/// summary.md. Fully off-screen (no window is created); the layout of a track/view is computed once and reused for every bar.
/// </summary>
internal static class BarAuditRunner
{
    internal sealed record BarRecord(int Track, string TrackName, int Bar, int System, Dictionary<string, string> Images, List<BarIssue> Issues);

    internal sealed record Result(List<BarRecord> Bars, string OutDir, TimeSpan Elapsed)
    {
        public int IssueCount => Bars.Sum(b => b.Issues.Count);
        public int ConsistencyIssues => Bars.Sum(b => b.Issues.Count(i => i.Type.StartsWith("missing:") || i.Type.StartsWith("extra:")));
    }

    public static readonly string[] AllViews = { "notation", "tab", "both" };
    private const int ImageScale = 2;

    public static Result Run(SongProject project, string outDir, IReadOnlyList<int> tracks, IReadOnlyList<string> views, string songName, bool images = true)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Directory.CreateDirectory(outDir);
        var bars = new List<BarRecord>();
        var dump = Environment.GetEnvironmentVariable("TF_BARAUDIT_DUMP") is { Length: > 0 } ? new StringBuilder() : null;   // debugging aid: the first bars' drawn items
        foreach (var t in tracks)
        {
            var track = project.Tracks[t];
            var folder = $"{t + 1:00} {Safe(track.Name)}";
            if (images) Directory.CreateDirectory(Path.Combine(outDir, folder));
            var records = Enumerable.Range(0, track.Measures.Count)
                .Select(b => new BarRecord(t + 1, track.Name, b + 1, 0, new Dictionary<string, string>(), new List<BarIssue>())).ToList();
            foreach (var view in views)
            {
                var audit = new TrackViewAudit(project, t, view);
                for (var b = 0; b < track.Measures.Count; b++)
                {
                    var record = records[b];
                    records[b] = record = record with { System = audit.Layout.Measure(b).SystemIndex };
                    if (dump is not null && b < 12)
                        foreach (var item in audit.BarItems(b).Concat(audit.AllSystemItems(audit.Layout.Measure(b).SystemIndex).Where(i => i.Kind is LayoutAudit.Kind.DashedLine or LayoutAudit.Kind.Line && i.Box.Width is > 5 and < 200 && i.Box.Height < 2)))
                            dump.AppendLine($"t{t + 1} b{b + 1} {view} {item.Kind} \"{item.Label}\" size={item.Size:0.0} w={item.Thickness:0.0} font={item.Font} base={item.BaseY:0.0} box=[{item.Box.X:0.0},{item.Box.Y:0.0},{item.Box.Width:0.0},{item.Box.Height:0.0}]");
                    try { record.Issues.AddRange(new BarChecker(audit, b).Run()); }
                    catch (Exception ex) { record.Issues.Add(new BarIssue(view, "audit-error", ex.GetBaseException().Message)); }
                    if (!images) continue;
                    var name = $"{b + 1:000}-{view}.png";
                    record.Images[view] = $"{folder}/{name}";
                    WriteImage(audit, b, Path.Combine(outDir, folder, name));
                }
            }
            bars.AddRange(records);
        }
        if (dump is not null) DiagnosticFileService.WriteText(Path.Combine(outDir, "items-dump.txt"), dump.ToString(), maximumBytes: 64L * 1024 * 1024);
        var result = new Result(bars, outDir, watch.Elapsed);
        DiagnosticFileService.WriteText(Path.Combine(outDir, "checks.json"), Json(result, songName, views), maximumBytes: 256L * 1024 * 1024);
        DiagnosticFileService.WriteText(Path.Combine(outDir, "summary.md"), Summary(result, songName, views, tracks.Count), maximumBytes: 64L * 1024 * 1024);
        return result;
    }

    private static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var text = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return text.Length == 0 ? "track" : text;
    }

    private static void WriteImage(TrackViewAudit audit, int bar, string path)
    {
        var position = audit.Layout.Measure(bar);
        var drawing = audit.DrawingOf(position.SystemIndex);
        var crop = audit.CropOf(bar);
        var width = Math.Max(1, (int)Math.Ceiling(crop.Width * ImageScale));
        var height = Math.Max(1, (int)Math.Ceiling(crop.Height * ImageScale));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            dc.PushTransform(new ScaleTransform(ImageScale, ImageScale));
            dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
            if (drawing is not null) dc.DrawDrawing(drawing);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string Json(Result result, string song, IReadOnlyList<string> views)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("song", song);
            w.WriteString("views", string.Join(",", views));
            w.WriteNumber("bars", result.Bars.Count);
            w.WriteNumber("issues", result.IssueCount);
            w.WriteStartArray("records");
            foreach (var b in result.Bars)
            {
                w.WriteStartObject();
                w.WriteNumber("track", b.Track);
                w.WriteString("trackName", b.TrackName);
                w.WriteNumber("bar", b.Bar);
                w.WriteNumber("system", b.System + 1);
                w.WriteBoolean("ok", b.Issues.Count == 0);
                w.WriteStartObject("images");
                foreach (var (view, file) in b.Images) w.WriteString(view, file);
                w.WriteEndObject();
                w.WriteStartArray("checks");
                foreach (var i in b.Issues)
                {
                    w.WriteStartObject();
                    w.WriteString("view", i.View); w.WriteString("type", i.Type); w.WriteString("detail", i.Detail);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Summary(Result result, string song, IReadOnlyList<string> views, int trackCount)
    {
        var sb = new StringBuilder();
        var all = result.Bars.SelectMany(b => b.Issues.Select(i => (Bar: b, Issue: i))).ToList();
        var flagged = result.Bars.Where(b => b.Issues.Count > 0).ToList();
        sb.AppendLine($"# Bar audit: {song}");
        sb.AppendLine();
        sb.AppendLine($"- tracks {trackCount}, bars checked {result.Bars.Count} (x {views.Count} views: {string.Join(", ", views)}), time {result.Elapsed.TotalSeconds:0.0} s");
        sb.AppendLine($"- flagged bars {flagged.Count}, findings {all.Count} (collisions {all.Count(a => a.Issue.Type == "collision")}, clipping {all.Count(a => a.Issue.Type.StartsWith("clip:"))}, close marks {all.Count(a => a.Issue.Type == "gap")}, data-vs-drawing {all.Count(a => a.Issue.Type.StartsWith("missing:") || a.Issue.Type.StartsWith("extra:"))})");
        sb.AppendLine();
        sb.AppendLine("## Counts by check type");
        sb.AppendLine();
        sb.AppendLine("| check | findings | bars |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var g in all.GroupBy(a => a.Issue.Type).OrderBy(g => g.Key.Split(':')[0] == "missing" ? 1 : g.Key.Split(':')[0] == "extra" ? 2 : 0).ThenByDescending(g => g.Count()))
            sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Select(a => (a.Bar.Track, a.Bar.Bar)).Distinct().Count()} |");
        sb.AppendLine();
        sb.AppendLine("## Flagged bars (track, bar, issue)");
        sb.AppendLine();
        const int Limit = 600;
        var lines = 0;
        foreach (var b in flagged)
            foreach (var i in b.Issues.GroupBy(i => (i.View, i.Type, i.Detail)).Select(g => g.Key))
            {
                if (lines++ >= Limit) break;
                sb.AppendLine($"- track {b.Track:00} {b.TrackName}, bar {b.Bar}, {i.View}: {i.Type} - {i.Detail}");
            }
        var total = flagged.Sum(b => b.Issues.Select(i => (i.View, i.Type, i.Detail)).Distinct().Count());
        if (total > Limit) sb.AppendLine($"- ... {total - Limit} more in checks.json");
        return sb.ToString();
    }
}
