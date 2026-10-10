using System.IO;
using System.Text.RegularExpressions;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Window options documented next to the headless commands; they are registered in App.xaml.cs, not in the diagnostic command table.</summary>
    private static readonly string[] DebuggingDocWindowOptions = { "--capture", "--screenshots", "--speed-audit" };

    /// <summary>
    /// docs/DEBUGGING.md lists every headless diagnostic command, and only real ones: each command in the registry has a table row, each row
    /// names a registered command (or one of the two window options, found in App.xaml.cs), and the page names the test-runner options it explains.
    /// </summary>
    /// <summary>Commands compiled in only with the full suite (-p:TabForgeFullSuite=true); the page documents them in every build.</summary>
    private static readonly string[] FullSuiteOnlyCommands =
    {
        "--write-gp-fixture", "--roundtrip-diff", "--write-gp-fixtures", "--gp-capability", "--gp-compare", "--write-gp-probes", "--gp-open",
        "--gp-compat-doc", "--gp-loss-coverage", "--roundtrip-semantics",
    };

    private static void TestDebuggingDocInSync()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("docs/DEBUGGING.md matches the diagnostic commands", "no source checkout found", "source-hygiene"); return; }
        var path = Path.Combine(root, "docs", "DEBUGGING.md");
        Check("docs/DEBUGGING.md exists", File.Exists(path));
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path);
        var rows = lines.Select(l => Regex.Match(l, @"^\| `(--[a-z0-9-]+)[ `]")).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        var registered = TabForge.Diagnostics.DiagnosticCommands.CommandNames.Concat(FullSuiteOnlyCommands).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var undocumented = registered.Where(c => !rows.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        Check("DEBUGGING.md has a row for every diagnostic command", undocumented.Count == 0, string.Join(", ", undocumented));
        var unknown = rows.Where(r => !registered.Contains(r, StringComparer.OrdinalIgnoreCase) && !DebuggingDocWindowOptions.Contains(r)).ToList();
        Check("DEBUGGING.md names only commands that exist", unknown.Count == 0, string.Join(", ", unknown));
        Check("DEBUGGING.md lists each command once", rows.Count == rows.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var app = File.ReadAllText(Path.Combine(root, "src", "TabForge", "App.xaml.cs"));
        Check("the window options in DEBUGGING.md exist in App.xaml.cs", DebuggingDocWindowOptions.All(o => app.Contains($"\"{o}\"") && rows.Contains(o)));

        var text = string.Join("\n", lines);
        foreach (var needed in new[] { "--only", "--areas", "--require ci", "TABFORGE_SELFTEST_SMALLSCREEN", "--profile" })
            Check($"DEBUGGING.md explains {needed}", text.Contains(needed, StringComparison.Ordinal));
        Check("DEBUGGING.md stays within 150 lines", lines.Length <= 150, $"{lines.Length} lines");
        var contributing = File.ReadAllText(Path.Combine(root, "CONTRIBUTING.md"));
        Check("CONTRIBUTING.md links docs/DEBUGGING.md", contributing.Contains("docs/DEBUGGING.md", StringComparison.Ordinal));
    }
}
