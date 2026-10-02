using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Diagnostics;

namespace TabForge;

public static partial class SelfTest
{
    private static readonly string[] StartHereDocs = { "START_HERE.md", "docs/RECIPES.md" };
    /// <summary>First-level source folders without a README: assets and licence texts, not code.</summary>
    private static readonly string[] FolderReadmeExempt = { "Assets", "licenses", "bin", "obj" };
    private static readonly string[] StartHereKnownOptions = { "--selftest", "--only", "--areas", "--require", "--profile", "--feature-map", "--playtest" };

    /// <summary>
    /// START_HERE.md and docs/RECIPES.md agree with the code: every file or folder path, type, test and command they name in backticks exists,
    /// and START_HERE.md stays within 150 lines. Every code folder under src/ has a README of at most 40 lines that is checked the same way
    /// and linked from START_HERE.md.
    /// </summary>
    private static void TestStartHereAndRecipesInSync()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("START_HERE.md and RECIPES.md agree with the code", "no source checkout found", "source-hygiene"); return; }

        var declared = new HashSet<string>(StringComparer.Ordinal);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            fileNames.Add(Path.GetFileName(file));
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match m in DeclaredType.Matches(File.ReadAllText(file))) declared.Add(m.Groups["name"].Value);
        }
        var tests = new HashSet<string>(FeatureMapGenerator.ReadRegistry(root).Select(e => e.Test), StringComparer.Ordinal);
        var commands = new HashSet<string>(DiagnosticCommands.CommandNames.Concat(FullSuiteOnlyCommands).Concat(StartHereKnownOptions), StringComparer.OrdinalIgnoreCase);
        var pathLike = new Regex(@"^[\w.\-/\\]+$", RegexOptions.Compiled);

        var folderReadmes = new List<string>();
        foreach (var project in Directory.GetDirectories(Path.Combine(root, "src")))
        {
            var projectRel = "src/" + Path.GetFileName(project);
            if (project.EndsWith("TabForge", StringComparison.Ordinal))
            {
                foreach (var folder in Directory.GetDirectories(project))
                    if (!FolderReadmeExempt.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)) folderReadmes.Add(projectRel + "/" + Path.GetFileName(folder) + "/README.md");
            }
            else folderReadmes.Add(projectRel + "/README.md");
        }
        var startHereText = File.ReadAllText(Path.Combine(root, "START_HERE.md"));
        foreach (var readme in folderReadmes)
        {
            var full = Path.Combine(root, readme.Replace('/', Path.DirectorySeparatorChar));
            Check($"{readme} exists, within 40 lines and linked from START_HERE.md",
                File.Exists(full) && File.ReadAllLines(full).Length <= 40 && startHereText.Contains("`" + readme + "`", StringComparison.Ordinal));
        }

        foreach (var doc in StartHereDocs.Concat(folderReadmes.Where(r => File.Exists(Path.Combine(root, r.Replace('/', Path.DirectorySeparatorChar))))))
        {
            var path = Path.Combine(root, doc.Replace('/', Path.DirectorySeparatorChar));
            Check($"{doc} exists", File.Exists(path));
            if (!File.Exists(path)) continue;
            var text = File.ReadAllText(path);
            var missing = new List<string>();
            foreach (Match span in Regex.Matches(text, @"`([^`\r\n]+)`"))
            {
                var token = span.Groups[1].Value.Trim();
                var first = token.Split(' ')[0];
                if (first.StartsWith("--", StringComparison.Ordinal)) { if (!commands.Contains(first)) missing.Add(first); continue; }
                if (token.Contains(' ') || token.Contains('<') || token.Contains('(') || token.Contains('*')) continue;
                if (Regex.IsMatch(token, "^Test[A-Z]\\w+$")) { if (!tests.Contains(token)) missing.Add(token); continue; }
                if (!pathLike.IsMatch(token)) continue;
                var isPath = token.Contains('/') || Regex.IsMatch(token, @"\.(cs|md|json|cmd|ps1|xaml|yml|sln|props)$");
                if (isPath)
                {
                    var rel = token.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
                    var found = File.Exists(Path.Combine(root, rel)) || Directory.Exists(Path.Combine(root, rel))
                        || (!token.Contains('/') && fileNames.Contains(token));
                    if (!found) missing.Add(token);
                    continue;
                }
                var type = TypeToken.Match(token);
                if (type.Success && !declared.Contains(type.Groups["type"].Value)) missing.Add(token);
            }
            Check($"{doc}: every file, type, test and command it names exists", missing.Count == 0, string.Join(", ", missing.Distinct().Take(10)));
        }

        var startHere = File.ReadAllLines(Path.Combine(root, "START_HERE.md"));
        Check("START_HERE.md stays within 150 lines", startHere.Length <= 150, $"{startHere.Length} lines");
        var contributing = File.ReadAllText(Path.Combine(root, "CONTRIBUTING.md"));
        Check("CONTRIBUTING.md links START_HERE.md and docs/RECIPES.md",
            contributing.Contains("START_HERE.md", StringComparison.Ordinal) && contributing.Contains("docs/RECIPES.md", StringComparison.Ordinal));
    }
}
