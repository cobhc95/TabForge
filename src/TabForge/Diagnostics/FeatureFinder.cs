using System.IO;
using System.Text;

namespace TabForge.Diagnostics;

/// <summary>
/// `TabForge.exe --find &lt;keyword&gt; [out.txt]`: prints the features, owning files and tests that match a keyword, in at most
/// <see cref="MaxLines"/> lines, so a reader does not have to open the whole feature map. Sources: the hand-kept feature map
/// (<c>docs/FEATURE_MAP.md</c> and <c>docs/feature-map/</c>), the registered tests (name, area, group, file) and the folder READMEs.
/// Owns: the lookup and its formatting. Does not own: the map itself (<see cref="FeatureMapGenerator"/>).
/// Tests: TestFindCommand.
/// </summary>
internal static class FeatureFinder
{
    public const int MaxLines = 40;

    /// <summary>The report for a keyword (case-insensitive substring), at most <see cref="MaxLines"/> lines.</summary>
    internal static string Search(string root, string keyword)
    {
        var lines = new List<string>();
        bool Hit(string s) => s.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        foreach (var mapPath in FeatureMapGenerator.HandKeptFiles(root))
        {
            var name = Path.GetRelativePath(root, mapPath).Replace('\\', '/');
            foreach (var l in File.ReadAllLines(mapPath).Where(l => l.Length > 0 && !l.StartsWith('#') && Hit(l)))
                lines.Add($"{name}: {Trim(l)}");
        }

        foreach (var e in FeatureMapGenerator.ReadRegistry(root).Where(e => Hit(e.Test) || Hit(e.File) || Hit(e.Area) || (e.Group is not null && Hit(e.Group))))
            lines.Add($"test  {e.Test} [{e.Area}] {e.File}");

        foreach (var readme in Directory.EnumerateFiles(Path.Combine(root, "src"), "README.md", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(root, readme).Replace('\\', '/');
            foreach (var l in File.ReadAllLines(readme).Where(l => l.Length > 0 && !l.StartsWith('#') && Hit(l)))
                lines.Add($"{rel}: {Trim(l)}");
        }

        var sb = new StringBuilder();
        sb.Append($"find '{keyword}': {lines.Count} match(es)\n");
        foreach (var l in lines.Take(MaxLines - 2)) sb.Append(l).Append('\n');
        if (lines.Count > MaxLines - 2) sb.Append($"... {lines.Count - (MaxLines - 2)} more; use a narrower keyword\n");
        return sb.ToString();
    }

    private static string Trim(string l) { l = l.Trim(); return l.Length > 160 ? l[..157] + "..." : l; }

    /// <summary>Prints the report to the console and, when a file is named, writes it there (the exe has no console of its own).</summary>
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: --find <keyword> [out.txt]"); return 2; }
        var root = FeatureMapGenerator.FindRoot();
        if (root is null) { Console.Error.WriteLine("find: no repository root (TabForge.sln) found above the working directory"); return 2; }
        var report = Search(root, args[1]);
        Console.Out.Write(report);
        if (args.Length > 2) File.WriteAllText(Services.FilePathPolicy.OutputFile(args[2], "find report", ".txt"), report, new UTF8Encoding(false));
        return 0;
    }
}
