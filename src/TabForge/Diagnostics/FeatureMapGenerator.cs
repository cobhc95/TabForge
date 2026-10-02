using System.IO;
using TabForge.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace TabForge.Diagnostics;

/// <summary>
/// Builds the self-test part of <c>docs/FEATURE_MAP.md</c> from the test registry: the <c>Guard</c> / <c>GuardGroup</c> calls in
/// <c>SelfTests/SelfTest.cs</c>, its <c>AreaOf</c> table, and the file that declares each test method. The generated block sits between two
/// marker lines; everything outside the markers is hand-kept. <c>TabForge.exe --feature-map [file]</c> rewrites the block in place and a
/// self-test compares it with the registry, so the map cannot drift.
/// </summary>
internal static class FeatureMapGenerator
{
    public const string BeginMarker = "<!-- BEGIN GENERATED TESTS (TabForge.exe --feature-map rewrites this block) -->";
    public const string EndMarker = "<!-- END GENERATED TESTS -->";
    public const string MapRelativePath = "docs/FEATURE_MAP.md";
    private const string RegistryRelativePath = "src/TabForge/SelfTests/SelfTest.cs";
    /// <summary>Registrations and areas of the full suite; read when the checkout has it (it is compiled in only with -p:TabForgeFullSuite=true).</summary>
    private const string FullSuiteRegistryRelativePath = "tests/full-suite/SelfTestFullSuite.cs";
    private const string FullSuiteFolderRelativePath = "tests/full-suite";

    internal sealed record Entry(string Test, string Area, string? Group, string File);

    private static readonly Regex GuardCall = new(@"\bGuard(?:Group)?\(\s*(?:""(?<group>[^""]+)""\s*,\s*)?(?<test>Test\w+)\s*[\),]", RegexOptions.Compiled);
    private static readonly Regex AreaEntry = new(@"\[""(?<test>Test\w+)""\]\s*=\s*""(?<area>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex TestMethod = new(@"\bstatic\s+(?:async\s+)?(?:void|Task)\s+(?<test>Test\w+)\s*\(", RegexOptions.Compiled);

    /// <summary>The registered tests in registration order, with area ("core" when untagged), group and declaring file.</summary>
    internal static List<Entry> ReadRegistry(string root)
    {
        var registry = File.ReadAllText(Path.Combine(root, RegistryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var fullRegistry = Path.Combine(root, FullSuiteRegistryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        // The full suite registers first (see SelfTest.Run), then the basic set.
        if (File.Exists(fullRegistry)) registry = File.ReadAllText(fullRegistry) + "\n" + registry;
        var areas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in AreaEntry.Matches(registry)) areas[m.Groups["test"].Value] = m.Groups["area"].Value;

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var testsFolder = Path.Combine(root, "src", "TabForge", "SelfTests");
        var folders = new List<string> { testsFolder };
        var fullFolder = Path.Combine(root, FullSuiteFolderRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(fullFolder)) folders.Add(fullFolder);
        foreach (var path in folders.SelectMany(f => Directory.EnumerateFiles(f, "*.cs", SearchOption.AllDirectories)).OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            foreach (Match m in TestMethod.Matches(File.ReadAllText(path))) files.TryAdd(m.Groups["test"].Value, relative);
        }

        var result = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in GuardCall.Matches(registry))
        {
            var test = m.Groups["test"].Value;
            if (!seen.Add(test)) continue;
            result.Add(new Entry(test, areas.TryGetValue(test, out var area) ? area : "core",
                m.Groups["group"].Success ? m.Groups["group"].Value : null, files.TryGetValue(test, out var file) ? file : "(not found)"));
        }
        return result;
    }

    /// <summary>The Markdown between the markers: a table of areas, a table of groups, then every test by area.</summary>
    internal static string RenderBlock(List<Entry> registry)
    {
        var sb = new StringBuilder();
        var byArea = registry.GroupBy(e => e.Area).OrderBy(g => g.Key == "core" ? "" : g.Key, StringComparer.Ordinal).ToList();

        sb.Append("### Areas\n\n");
        sb.Append("| Area | Tests | Run only this area |\n| --- | --- | --- |\n");
        foreach (var area in byArea)
            sb.Append(area.Key == "core"
                ? $"| core (no area) | {area.Count()} | always runs |\n"
                : $"| {area.Key} | {area.Count()} | `--selftest <log> --areas {area.Key}` |\n");

        sb.Append("\n### Groups\n\n");
        sb.Append("A group is a named set of tests with a minimum check count; `--require ci` makes the CI groups mandatory (see `docs/TESTING.md`).\n\n");
        sb.Append("| Group | Tests |\n| --- | --- |\n");
        foreach (var group in registry.Where(e => e.Group is not null).GroupBy(e => e.Group!).OrderBy(g => g.Key, StringComparer.Ordinal))
            sb.Append($"| {group.Key} | {string.Join(", ", group.Select(e => e.Test))} |\n");

        sb.Append("\n### Every test\n\n");
        foreach (var area in byArea)
        {
            sb.Append($"#### {(area.Key == "core" ? "core (no area)" : area.Key)}\n\n");
            sb.Append("| Test | Group | File |\n| --- | --- | --- |\n");
            foreach (var e in area.OrderBy(e => e.Test, StringComparer.Ordinal))
                sb.Append($"| {e.Test} | {e.Group ?? ""} | `{e.File}` |\n");
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>The block as it must appear in the document (line endings are compared as LF).</summary>
    internal static string ExpectedBlock(string root) => RenderBlock(ReadRegistry(root));

    /// <summary>The generated block found in <paramref name="document"/>, or null when a marker is missing. LF line endings.</summary>
    internal static string? ExtractBlock(string document)
    {
        var text = document.Replace("\r\n", "\n");
        var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin) return null;
        return text[(begin + BeginMarker.Length)..end].Trim('\n') + "\n";
    }

    /// <summary>The document with its generated block replaced by <paramref name="block"/>.</summary>
    internal static string ReplaceBlock(string document, string block)
    {
        var text = document.Replace("\r\n", "\n");
        var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin) throw new InvalidDataException($"{MapRelativePath} needs the lines '{BeginMarker}' and '{EndMarker}'.");
        return (text[..(begin + BeginMarker.Length)] + "\n\n" + block + "\n" + text[end..]).Replace("\n", "\r\n");
    }

    /// <summary>The repository root: the nearest folder above the working directory or the executable that holds <c>TabForge.sln</c>.</summary>
    internal static string? FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "TabForge.sln"))) return dir.FullName;
        return null;
    }

    /// <summary>`--feature-map [file]`: rewrites the generated block of the feature map (default <c>docs/FEATURE_MAP.md</c> of the repository).</summary>
    public static int Run(string[] args)
    {
        var root = FindRoot();
        if (root is null) { Console.Error.WriteLine("feature map: no repository root (TabForge.sln) found above the working directory"); return 2; }
        var path = args.Length > 1 ? FilePathPolicy.OutputFile(args[1], "feature map", ".md") : Path.Combine(root, MapRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var updated = ReplaceBlock(File.ReadAllText(path), ExpectedBlock(root));
        File.WriteAllText(path, updated, new UTF8Encoding(false));
        return 0;
    }
}
