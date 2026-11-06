using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TabForge;

// Owns: docs image-reference check (every screenshot/animation named by Markdown), except the pictures kept for an older release in its own
//   versioned folder (docs/media/screenshots/0.8.75/...: older releases' notes link to them on main); a picture anywhere else must be named.

public static partial class SelfTest
{
    /// <summary>
    /// Every file in docs/media/screenshots and docs/media/animations is named by a Markdown file (README.md, docs/*.md, docs/tutorial/**), so unused
    /// pictures never reach the public tree.
    /// </summary>
    private static void TestDocImagesAreReferenced()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("Documentation pictures are referenced", "no source checkout found", "source-hygiene"); return; }
        var docs = new List<string>();
        var readme = Path.Combine(root, "Source", "docs", "README.md");
        if (File.Exists(readme)) docs.Add(readme);
        var docsDir = Path.Combine(root, "Source", "docs");
        if (Directory.Exists(docsDir)) docs.AddRange(Directory.EnumerateFiles(docsDir, "*.md", SearchOption.AllDirectories));
        var text = string.Join("\n", docs.Select(File.ReadAllText));
        var unused = new List<string>();
        foreach (var folder in new[] { "screenshots", "animations" })
        {
            var dir = Path.Combine(docsDir, "media", folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (!KeptForOlderRelease(Path.GetRelativePath(dir, file), TabForge.Services.AppInfo.Version) && !text.Contains(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
                    unused.Add(Path.GetRelativePath(root, file));
        }
        Check("Every documentation picture is referenced by a Markdown file", unused.Count == 0, string.Join(", ", unused.Take(10)));
    }

    /// <summary>
    /// True for a picture in the versioned folder of a release before <paramref name="currentVersion"/> (its first folder under screenshots or
    /// animations is a version such as 0.8.75, compared part by part as whole numbers): kept for that release's notes, so no doc must name it.
    /// A picture at the top of the folder, in another folder or in the current release's folder is not.
    /// </summary>
    internal static bool KeptForOlderRelease(string pathUnderMediaFolder, string currentVersion)
    {
        var parts = pathUnderMediaFolder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || VersionParts(parts[0]) is not { } folder || VersionParts(currentVersion.Split('-')[0]) is not { } current) return false;
        for (var i = 0; i < Math.Max(folder.Length, current.Length); i++)
        {
            var a = i < folder.Length ? folder[i] : 0;
            var b = i < current.Length ? current[i] : 0;
            if (a != b) return a < b;
        }
        return false;   // the current release's own folder
    }

    private static int[]? VersionParts(string text)
    {
        var parts = text.Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i])) return null;
        return parts.Length >= 2 ? numbers : null;
    }
}
