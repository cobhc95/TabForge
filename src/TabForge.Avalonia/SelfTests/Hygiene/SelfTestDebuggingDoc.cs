using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Services;

namespace TabForge;

// Owns: the check that docs/guides/DEBUGGING.md names only process modes the executable has and explains the test-runner options.
// Does not own: the document itself or the process modes (Program).
// Tests: TestDebuggingDocInSync.
public static partial class SelfTest
{
    /// <summary>The page stays a quick reference.</summary>
    private const int DebuggingDocMaxLines = 150;

    private static readonly Regex DebuggingDocCommandRow = new(@"^\| `(--[a-z0-9-]+)[ `]", RegexOptions.CultureInvariant);
    private static readonly Regex ProgramModeLiteral = new(@"args\[0\] == ""(--[a-z0-9-]+)""", RegexOptions.CultureInvariant);

    /// <summary>
    /// docs/guides/DEBUGGING.md lists only real process modes: each command in its table is a first argument that Program routes (read from Program.cs, plus
    /// the self-test and import-worker constants), the table lists the self-test once, and the page explains the test-runner options.
    /// </summary>
    private static void TestDebuggingDocInSync()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("Source/docs/guides/DEBUGGING.md matches the process modes", "no source checkout found", "source-hygiene"); return; }
        var path = Path.Combine(root, "Source", "docs", "guides", "DEBUGGING.md");
        Check("docs/guides/DEBUGGING.md exists", File.Exists(path));
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path);
        var rows = lines.Select(l => DebuggingDocCommandRow.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        var program = File.ReadAllText(Path.Combine(root, "Source", "src", "TabForge.Avalonia", "App", "Program.cs"));
        var modes = ProgramModeLiteral.Matches(program).Select(m => m.Groups[1].Value).Append(Argument).Append(ImportWorker.Argument).ToList();

        var unknown = rows.Where(r => !modes.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        Check("DEBUGGING.md names only commands that exist", unknown.Count == 0, string.Join(", ", unknown));
        Check("DEBUGGING.md lists each command once", rows.Count == rows.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Check("DEBUGGING.md lists the self-test command", rows.Contains(Argument, StringComparer.OrdinalIgnoreCase));

        var text = string.Join("\n", lines);
        foreach (var needed in new[] { "--only", "--areas", "--require ci", "--profile" })
            Check($"DEBUGGING.md explains {needed}", text.Contains(needed, StringComparison.Ordinal));
        Check($"DEBUGGING.md stays within {DebuggingDocMaxLines} lines", lines.Length <= DebuggingDocMaxLines, $"{lines.Length} lines");
    }
}
