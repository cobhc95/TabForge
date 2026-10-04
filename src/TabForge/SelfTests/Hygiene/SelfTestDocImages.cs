using System.IO;
using System.Linq;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// Every file in docs/screenshots and docs/animations is named by a Markdown file (README.md, docs/*.md, docs/tutorial/**), so unused
    /// pictures never reach the public tree.
    /// </summary>
    private static void TestDocImagesAreReferenced()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("Documentation pictures are referenced", "no source checkout found", "source-hygiene"); return; }
        var docs = new List<string>();
        var readme = Path.Combine(root, "README.md");
        if (File.Exists(readme)) docs.Add(readme);
        var docsDir = Path.Combine(root, "docs");
        if (Directory.Exists(docsDir)) docs.AddRange(Directory.EnumerateFiles(docsDir, "*.md", SearchOption.AllDirectories));
        var text = string.Join("\n", docs.Select(File.ReadAllText));
        var unused = new List<string>();
        foreach (var folder in new[] { "screenshots", "animations" })
        {
            var dir = Path.Combine(docsDir, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (!text.Contains(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)) unused.Add(Path.GetRelativePath(root, file));
        }
        Check("Every documentation picture is referenced by a Markdown file", unused.Count == 0, string.Join(", ", unused.Take(10)));
    }
}
