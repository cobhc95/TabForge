using System.Text;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Diagnostics;

internal static partial class DiagnosticCommands
{
    /// <summary>`--layout-audit &lt;song&gt; &lt;report.txt&gt; [track]`: engraves every track (or one) and lists colliding texts and markings.</summary>
    private static int RunLayoutAudit(string[] args)
    {
        if (args.Length < 3) return Usage("--layout-audit <song> <report.txt> [track]");
        return Guard("Layout audit", () =>
        {
            var project = args[1] == "@technique" ? GmSongAudit.TechniqueSong() : LoadAny(args[1]); // "@technique" = the built-in technique test song
            var only = args.Length > 3 && int.TryParse(args[3], out var t) ? t - 1 : -1;
            var report = new StringBuilder();
            var total = 0;
            for (var i = 0; i < project.Tracks.Count; i++)
            {
                if (only >= 0 && i != only) continue;
                var found = LayoutAudit.Run(project, i);
                total += found.Count;
                report.Append(LayoutAudit.Format(found, project.Tracks[i].Name, i));
            }
            var problems = MusicTime.FindBarProblems(project).Select(b => b.Describe()).ToList();
            report.Insert(0, $"bars marked incomplete/overfull: {(problems.Count == 0 ? "none" : string.Join("; ", problems))}{Environment.NewLine}");
            report.Insert(0, $"total collisions: {total}{Environment.NewLine}");
            DiagnosticFileService.WriteText(FilePathPolicy.OutputFile(args[2], "layout audit", ".txt"), report.ToString());
            return total == 0 ? Ok : CheckFailed;
        });
    }
}
