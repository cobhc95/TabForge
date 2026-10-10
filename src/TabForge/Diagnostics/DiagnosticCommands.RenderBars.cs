using System.Globalization;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Owns: the `--render-bars` command: parses --tracks and --views, writes one PNG per track, bar and view, plus checks.json and summary.md.
// Does not own: drawing the notation and tab views (Views/Score, Views/TabEditorControl*) or the checks' thresholds.
// Tests: no named test.

internal static partial class DiagnosticCommands
{
    /// <summary>
    /// `--render-bars &lt;song&gt; &lt;outdir&gt; [--tracks all|1,3,5-7] [--views notation,tab,both]`: one PNG per track, bar and view
    /// (cropped to the bar) plus checks.json and summary.md. Off-screen, no window. Exit 1 when any check found something.
    /// </summary>
    private static int RunRenderBars(string[] args)
    {
        if (args.Length < 3) return Usage("--render-bars <song> <outdir> [--tracks all|1,3,5-7] [--views notation,tab,both]");
        return Guard("Render bars", () =>
        {
            var outDir = FilePathPolicy.OutputDirectory(args[2], "bar render folder");
            var project = LoadAny(args[1]);
            var trackSpec = "all"; var viewSpec = "notation,tab";
            for (var i = 3; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--tracks", StringComparison.OrdinalIgnoreCase)) trackSpec = args[i + 1];
                else if (args[i].Equals("--views", StringComparison.OrdinalIgnoreCase)) viewSpec = args[i + 1];
            }
            var tracks = new SortedSet<int>();
            if (trackSpec.Equals("all", StringComparison.OrdinalIgnoreCase)) for (var t = 0; t < project.Tracks.Count; t++) tracks.Add(t);
            else
                foreach (var part in trackSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var range = part.Split('-');
                    if (!int.TryParse(range[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ||
                        !int.TryParse(range[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var to)) return Usage("--tracks expects all or numbers like 1,3,5-7");
                    for (var t = from; t <= to; t++) if (t >= 1 && t <= project.Tracks.Count) tracks.Add(t - 1);
                }
            var views = viewSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => v.ToLowerInvariant()).Distinct().ToList();
            if (views.Count == 0 || views.Any(v => !BarAuditRunner.AllViews.Contains(v))) return Usage("--views expects notation, tab and/or both");
            var result = BarAuditRunner.Run(project, outDir, tracks.ToList(), views, System.IO.Path.GetFileName(args[1]));
            Console.WriteLine($"Rendered {result.Bars.Count} bars x {views.Count} views in {result.Elapsed.TotalSeconds:0.0} s; {result.IssueCount} findings ({result.ConsistencyIssues} data-vs-drawing). See {System.IO.Path.Combine(outDir, "summary.md")}");
            return result.IssueCount == 0 ? Ok : CheckFailed;
        });
    }
}
