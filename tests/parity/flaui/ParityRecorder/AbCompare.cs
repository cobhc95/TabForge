using System.Text;
using System.Text.RegularExpressions;

namespace ParityRecorder;

/// <summary>
/// The A/B mode: <c>--ab &lt;exeA&gt; &lt;exeB&gt; --out &lt;folder&gt;</c> runs every scenario and tree on build A, then on build B,
/// writes both result sets to <c>folder\A</c> and <c>folder\B</c>, prints a unified diff of the differing lines and ends with
/// <c>AB: N scenarios, M differences</c>. N counts the scenario lines recorded for A (ids such as F04 or S05). M counts the
/// lines removed from A (every changed or missing line, across all files).
/// Owns: the A/B argument handling, the output folders and the comparison. Does not own: what is recorded (Recorder).
/// </summary>
internal static class AbCompare
{
    private static readonly Regex ScenarioLine = new(@"^[SF]\d\d ", RegexOptions.Compiled);

    public static int Run(string[] args)
    {
        var at = Array.IndexOf(args, "--ab");
        if (at + 2 >= args.Length)
        {
            Console.Error.WriteLine("usage: ParityRecorder --ab <exeA> <exeB> --out <folder>");
            return 2;
        }
        var exeA = args[at + 1];
        var exeB = args[at + 2];
        foreach (var exe in new[] { exeA, exeB })
        {
            if (File.Exists(exe)) continue;
            Console.Error.WriteLine($"TabForge.exe not found at {exe}.");
            return 2;
        }
        var outDir = Option(args, "--out") ?? Path.Combine(Path.GetTempPath(), "tf-j14-ab");
        Native.SetProcessDpiAwarenessContext(Native.PerMonitorV2);

        var a = Recorder.RecordAll(exeA);
        var b = Recorder.RecordAll(exeB);
        var folderA = Path.Combine(outDir, "A");
        var folderB = Path.Combine(outDir, "B");
        var names = a.Keys.Union(b.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var scenarios = 0;
        var removed = 0;
        var report = new StringBuilder();
        foreach (var name in names)
        {
            var pathA = Write(folderA, name, a.TryGetValue(name, out var textA) ? textA : "# not recorded by build A\n");
            var pathB = Write(folderB, name, b.TryGetValue(name, out var textB) ? textB : "# not recorded by build B\n");
            scenarios += textA is null ? 0 : textA.Split('\n').Count(l => ScenarioLine.IsMatch(l));
            var (diff, minus) = Diff(pathA, pathB);
            if (diff.Length == 0) continue;
            report.Append(diff);
            removed += minus;
        }
        Console.Write(report);
        Console.WriteLine($"AB: {scenarios} scenarios, {removed} differences");
        return removed == 0 ? 0 : 1;
    }

    /// <summary>Writes one result file (replacing an older run's copy) and returns its path.</summary>
    private static string Write(string folder, string name, string text)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// A unified-style diff of two files and the number of lines removed from A; empty when they are equal. The common
    /// head and tail are skipped, and the middle is compared by longest common subsequence (the middle is small in practice).
    /// </summary>
    private static (string Diff, int Removed) Diff(string pathA, string pathB)
    {
        var a = File.ReadAllText(pathA).TrimEnd('\n').Split('\n');
        var b = File.ReadAllText(pathB).TrimEnd('\n').Split('\n');
        var head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
        var tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[a.Length - 1 - tail] == b[b.Length - 1 - tail]) tail++;
        var n = a.Length - head - tail;
        var m = b.Length - head - tail;
        if (n == 0 && m == 0) return ("", 0);
        if ((long)n * m > 4_000_000) return Hunk(pathA, pathB, head, a[head..(head + n)].Select(l => "-" + l).Concat(b[head..(head + m)].Select(l => "+" + l)), n);
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[head + i] == b[head + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var lines = new List<string>();
        var removed = 0;
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && a[head + x] == b[head + y]) { lines.Add(" " + a[head + x]); x++; y++; }
            else if (y < m && (x == n || lcs[x, y + 1] >= lcs[x + 1, y])) { lines.Add("+" + b[head + y]); y++; }
            else { lines.Add("-" + a[head + x]); x++; removed++; }
        }
        return Hunk(pathA, pathB, head, lines, removed);
    }

    private static (string Diff, int Removed) Hunk(string pathA, string pathB, int head, IEnumerable<string> lines, int removed)
    {
        var sb = new StringBuilder();
        sb.Append("--- ").AppendLine(pathA).Append("+++ ").AppendLine(pathB).Append("@@ from line ").Append(head + 1).AppendLine(" @@");
        foreach (var line in lines) sb.AppendLine(line);
        return (sb.ToString(), removed);
    }

    private static string? Option(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
