using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Testing;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Avalonia half of the score geometry proof: the demo song's first 4 bars drawn through the recorder and the Skia adapter (geometry.json and
/// score.png under %TEMP%\tf-avalonia-tests\avalonia), compared with the WPF file `TabForge.exe --score-geometry %TEMP%\tf-avalonia-tests\wpf` writes.
/// Tests: equal call counts per kind; lines, rects and paths within 1 DIP; text positions within 2 DIP; the differences go to compare.txt.
/// </summary>
public class ScoreGeometryProofTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests");

    [AvaloniaFact]
    public void ScoreGeometryMatchesWpf()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Root, "avalonia")).FullName;
        var draw = new AvaloniaDrawContext();
        var probe = new ScoreGeometryProbe(DemoSongFactory.Create(), draw);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96));
        List<DrawRecord> mine;
        using (var dc = bitmap.CreateDrawingContext())
        using (dc.PushRenderOptions(new RenderOptions { TextRenderingMode = TextRenderingMode.Antialias })) mine = probe.Draw(draw.Use(dc), 4);
        bitmap.Save(Path.Combine(dir, "score.png"));
        File.WriteAllText(Path.Combine(dir, "geometry.json"), JsonSerializer.Serialize(mine));

        var wpfFile = Path.Combine(Root, "wpf", "geometry.json");
        if (!File.Exists(wpfFile)) return; // The WPF half runs only on Windows through the app; without it this test only renders.
        var wpf = JsonSerializer.Deserialize<List<DrawRecord>>(File.ReadAllText(wpfFile))!;
        var (report, failures) = Compare(wpf, mine);
        File.WriteAllText(Path.Combine(Root, "compare.txt"), report);
        Assert.True(failures.Count == 0, report + string.Join("\n", failures.Take(20)));
    }

    private static (string Report, List<string> Failures) Compare(List<DrawRecord> wpf, List<DrawRecord> mine)
    {
        var failures = new List<string>();
        var report = new StringBuilder("kind | count | max diff | mean diff (DIP)\n");
        foreach (var kind in wpf.Select(r => r.Kind).Union(mine.Select(r => r.Kind)).OrderBy(k => k))
        {
            var a = wpf.Where(r => r.Kind == kind).ToList();
            var b = mine.Where(r => r.Kind == kind).ToList();
            if (a.Count != b.Count) { failures.Add($"{kind}: {a.Count} WPF vs {b.Count} Avalonia"); continue; }
            var isText = kind == "Text";
            var limit = isText ? 2.0 : 1.0;
            var diffs = new List<double>();
            var widths = new List<double>();
            for (var i = 0; i < a.Count; i++)
            {
                var values = isText ? 2 : a[i].Values.Length; // text: position only; its size is reported apart
                var d = Enumerable.Range(0, values).Select(j => Math.Abs(a[i].Values[j] - b[i].Values[j])).DefaultIfEmpty(0).Max();
                diffs.Add(d);
                if (d > limit) failures.Add($"{kind} #{i} '{a[i].Text}': [{string.Join(", ", a[i].Values)}] vs [{string.Join(", ", b[i].Values)}]");
                if (isText)
                {
                    widths.Add(b[i].Values[2] - a[i].Values[2]);
                    if (a[i].Text != b[i].Text) failures.Add($"Text #{i}: '{a[i].Text}' vs '{b[i].Text}'");
                }
            }
            report.Append($"{kind} | {a.Count} | {diffs.DefaultIfEmpty(0).Max():0.###} | {diffs.DefaultIfEmpty(0).Average():0.###}\n");
            if (widths.Count > 0)
                report.Append($"Text width (Avalonia - WPF) | {widths.Count} | {widths.Max(Math.Abs):0.###} | {widths.Average():0.###} signed, {widths.Average(Math.Abs):0.###} abs\n");
        }
        return (report.ToString(), failures);
    }
}
