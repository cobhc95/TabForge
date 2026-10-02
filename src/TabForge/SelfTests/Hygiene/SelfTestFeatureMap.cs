using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Diagnostics;

namespace TabForge;

public static partial class SelfTest
{
    private static readonly Regex DeclaredType = new(@"\b(?:class|record|struct|interface|enum)\s+(?<name>[A-Z]\w*)", RegexOptions.Compiled);
    private static readonly Regex TypeToken = new(@"^(?<type>[A-Z][A-Za-z0-9]*)(?:\.[A-Z][A-Za-z0-9]*)?$", RegexOptions.Compiled);

    /// <summary>
    /// The feature map (docs/FEATURE_MAP.md) agrees with the code: its generated test block equals what the registry produces, every test it
    /// names is registered, every type it names is declared, and every area or group it names exists. Fix: <c>TabForge.exe --feature-map</c>.
    /// </summary>
    private static void TestFeatureMapInSync()
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "docs", "FEATURE_MAP.md");
        if (path is null || !File.Exists(path)) { Skip("feature map agrees with the test registry", "no source checkout / feature map found", "source-hygiene"); return; }

        var document = File.ReadAllText(path);
        var registry = FeatureMapGenerator.ReadRegistry(root!);
        Check("feature map: the registry lists tests", registry.Count > 100, $"{registry.Count} tests parsed");
        Check("feature map: every registered test is declared in a test file", registry.All(e => e.File != "(not found)"),
            string.Join(", ", registry.Where(e => e.File == "(not found)").Select(e => e.Test).Take(5)));

        var actual = FeatureMapGenerator.ExtractBlock(document);
        Check("feature map: has the generated block markers", actual is not null);
        if (actual is null) return;
        var expected = FeatureMapGenerator.RenderBlock(registry);
        Check("feature map: the generated test block is current (run TabForge.exe --feature-map)", actual == expected, FirstDifference(expected, actual));

        // The hand-kept text: everything outside the generated block.
        var text = document.Replace("\r\n", "\n");
        var begin = text.IndexOf(FeatureMapGenerator.BeginMarker, StringComparison.Ordinal);
        var end = text.IndexOf(FeatureMapGenerator.EndMarker, StringComparison.Ordinal) + FeatureMapGenerator.EndMarker.Length;
        var handKept = text[..begin] + text[end..];

        var tests = new HashSet<string>(registry.Select(e => e.Test), StringComparer.Ordinal);
        var unknownTests = Regex.Matches(handKept, @"\bTest[A-Z]\w+").Select(m => m.Value).Distinct().Where(t => !tests.Contains(t)).ToList();
        Check("feature map: every test named by hand is registered", unknownTests.Count == 0, string.Join(", ", unknownTests.Take(8)));

        var areas = new HashSet<string>(registry.Select(e => e.Area), StringComparer.Ordinal);
        var groups = new HashSet<string>(registry.Where(e => e.Group is not null).Select(e => e.Group!), StringComparer.Ordinal);
        var unknownAreas = Regex.Matches(handKept, @"--areas (?<a>[a-z0-9-]+)").Select(m => m.Groups["a"].Value).Distinct().Where(a => !areas.Contains(a)).ToList();
        Check("feature map: every area named by hand exists", unknownAreas.Count == 0, string.Join(", ", unknownAreas));
        var unknownGroups = Regex.Matches(handKept, @"groups? (?<l>[a-z]+-[a-z-]+(?:, [a-z-]+)*)").SelectMany(m => m.Groups["l"].Value.Split(", ")).Distinct().Where(g => !groups.Contains(g)).ToList();
        Check("feature map: every group named by hand exists", unknownGroups.Count == 0, string.Join(", ", unknownGroups));

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root!, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            foreach (Match m in DeclaredType.Matches(File.ReadAllText(file))) declared.Add(m.Groups["name"].Value);
        }
        var unknownTypes = new List<string>();
        foreach (Match span in Regex.Matches(handKept, @"`([^`\r\n]+)`"))
        {
            var token = TypeToken.Match(span.Groups[1].Value);
            if (token.Success && !Regex.IsMatch(token.Value, "^Test[A-Z]") && !declared.Contains(token.Groups["type"].Value) && !unknownTypes.Contains(token.Groups["type"].Value)) unknownTypes.Add(token.Groups["type"].Value);
        }
        Check("feature map: every type named by hand is declared in the source", unknownTypes.Count == 0, string.Join(", ", unknownTypes.Take(8)));
    }

    private static string FirstDifference(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
            if (i >= a.Length || i >= b.Length || a[i] != b[i])
                return $"first difference at generated line {i + 1}: expected '{(i < a.Length ? a[i] : "(end)")}', found '{(i < b.Length ? b[i] : "(end)")}'";
        return "";
    }
}
