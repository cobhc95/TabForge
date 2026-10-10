using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// Public documents whose repository references and version claims are checked. They are documents a reader of the
    /// public GitHub tree sees. README.md and CHANGELOG.md are checked only for links / version (the public README is
    /// maintained outside the repository and copied in at export).
    /// </summary>
    private static readonly string[] PublicDocFiles =
    {
        "SECURITY.md", "ARCHITECTURE.md", "THIRD_PARTY.md", "TOOLS_AND_HOTKEYS.md", "CONTRIBUTING.md",
        "docs/REPRODUCIBLE_BUILDS.md", "docs/SBOM.md", "docs/TESTING.md", "docs/DEBUGGING.md", "docs/DEBUG_SYMPTOMS.md", "docs/FEATURE_MAP.md", "docs/RECIPES.md", "START_HERE.md", "docs/COMPATIBILITY.md",
        "native/tfvst3/BUILD.md", "vendor/alphatab/README.md",
    };

    /// <summary>
    /// What the public export ships (a mirror of the allowlist in work/publish/Export-Public.ps1: keep both in step; a public
    /// document may only name a file in this set). Entries ending in "/" are folders.
    /// </summary>
    private static readonly string[] PublicExportAllowlist =
    {
        "TabForge.sln", "global.json", "Directory.Build.props", ".editorconfig", ".gitattributes", ".gitignore", "LICENSE", "THIRD_PARTY.md",
        "nuget.config", "README.md", "CHANGELOG.md",
        // documents the export must ship (add them to Export-Public.ps1 when they are added here)
        "SECURITY.md", "ARCHITECTURE.md", "TOOLS_AND_HOTKEYS.md", "CONTRIBUTING.md", "docs/SBOM.md", "docs/TESTING.md", "docs/DEBUGGING.md", "docs/DEBUG_SYMPTOMS.md", "docs/FEATURE_MAP.md", "docs/RECIPES.md", "START_HERE.md", "docs/COMPATIBILITY.md",
        "docs/REPRODUCIBLE_BUILDS.md", "docs/screenshots/", "docs/animations/", "docs/tutorial/", "docs/feature-map/",
        "src/", "tests/full-suite/", "samples/", "vendor/alphatab/",
        ".github/workflows/windows-ci.yml", ".github/workflows/release.yml", ".github/workflows/build-bridge.yml", ".github/workflows/fuzz-weekly.yml",
        "tools/Compare-Release.ps1", "tools/Build-AlphaTab.ps1", "tools/Compare-NativeBridge.ps1", "tools/Package-Release.ps1", "tools/Publish.ps1", "tools/Write-Sbom.ps1", "tools/run-test.ps1",
        "installer/TabForge.iss",
        "native/BUILD_PROVENANCE.md", "native/build-tfvst3.ps1", "native/fetch-vst3sdk.ps1",
        "native/tfvst3/BUILD.md", "native/tfvst3/CMakeLists.txt", "native/tfvst3/tfvst3.cpp", "native/tfvst3/tfvst3.h", "native/tfvst3/test/tfv3test.cpp",
    };

    /// <summary>Names a public document may mention although they are not repository files (build output, upstream files, files created on the user's PC).</summary>
    private static readonly string[] DocReferenceOutputPrefixes = { "dist/", "build/", "licenses/", "bin/", "obj/", "artifacts/" };
    private static readonly string[] DocReferenceTopFolders = { "src", "tests", "tools", "native", "docs", "installer", "vendor", "samples", ".github", "work", "backup", "Tabs" };
    private static readonly string[] DocReferenceExtensions = { "cmd", "ps1", "yml", "md", "iss", "props", "sln", "csproj", "cpp", "patch", "json" };
    /// <summary>Upstream files named in the vendored alphaTab notes (they are not in this repository).</summary>
    private static readonly string[] DocReferenceExternalNames = { "AlphaTab.csproj" };

    private static bool IsInPublicAllowlist(string rel)
    {
        rel = rel.Replace('\\', '/');
        // a folder is shipped when anything below it is
        if (PublicExportAllowlist.Any(e => e.StartsWith(rel.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))) return true;
        foreach (var entry in PublicExportAllowlist)
            if (entry.EndsWith('/') ? rel.StartsWith(entry, StringComparison.OrdinalIgnoreCase) || (rel + "/").Equals(entry, StringComparison.OrdinalIgnoreCase)
                                    : rel.Equals(entry, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Path-like references (backtick spans, code blocks and link targets) found in a document.</summary>
    private static IEnumerable<string> DocPathReferences(string text)
    {
        foreach (Match link in Regex.Matches(text, @"\]\(([^)\s#]+)(?:#[^)]*)?\)"))
        {
            var target = link.Groups[1].Value;
            if (!target.Contains("://") && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) yield return "link:" + target;
        }
        // Tokens in code spans and fenced blocks only: prose words such as "e.g." are never path candidates.
        var code = new List<string>();
        foreach (Match m in Regex.Matches(text, @"```.*?```", RegexOptions.Singleline)) code.Add(m.Value);
        foreach (Match m in Regex.Matches(Regex.Replace(text, @"```.*?```", "", RegexOptions.Singleline), @"`([^`\r\n]+)`")) code.Add(m.Groups[1].Value);
        foreach (var span in code)
            foreach (var raw in Regex.Split(span, @"[\s`""'(),;=|]+"))
            {
                var t = raw.Trim().TrimEnd('.', ':', '*').Replace('\\', '/');
                while (t.StartsWith("./", StringComparison.Ordinal)) t = t[2..];
                if (t.Length < 3 || t.Contains("...") || t.Contains('<') || t.Contains('>') || t.Contains('*') || t.Contains('%') || t.Contains('{') || t.Contains('$')
                    || t.Contains("://") || t.StartsWith('/') || t.StartsWith('-') || t.Contains(':')) continue;
                if (!Regex.IsMatch(t, @"^[A-Za-z0-9_.][A-Za-z0-9_./+-]*$")) continue;
                var slash = t.IndexOf('/');
                if (t.Contains("/bin/") || t.Contains("/obj/")) continue;   // build output
                if (slash > 0)
                {
                    if (DocReferenceTopFolders.Contains(t[..slash], StringComparer.OrdinalIgnoreCase)) yield return "path:" + t;
                    else if (DocReferenceOutputPrefixes.Any(p => t.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                }
                else
                {
                    var ext = Path.GetExtension(t).TrimStart('.');
                    if (ext.Length > 0 && DocReferenceExtensions.Contains(ext, StringComparer.Ordinal)) yield return "path:" + t;
                }
            }
    }

    /// <summary>The folder READMEs under src/ (public with the source tree), as repository-relative paths.</summary>
    private static IEnumerable<string> FolderReadmeFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "README.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'));

    /// <summary>The feature-map pages (docs/feature-map/*.md, forward slashes): checked like the public documents.</summary>
    private static IEnumerable<string> FeatureMapPages(string root) =>
        Directory.Exists(Path.Combine(root, "docs", "feature-map"))
            ? Directory.EnumerateFiles(Path.Combine(root, "docs", "feature-map"), "*.md").Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            : Enumerable.Empty<string>();

    /// <summary>Repository-relative files (forward slashes) outside build output and local-only folders.</summary>
    private static List<string> DocRepositoryFiles(string root)
    {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "build", "dist", "backup", "work", "Tabs" };
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!skipped.Contains(Path.GetFileName(sub)) && (!Path.GetFileName(sub).StartsWith('.') || Path.GetFileName(sub) == ".github")) pending.Push(sub);   // dot-folders (tooling, VCS) are local-only; the CI workflows are public
            foreach (var file in Directory.EnumerateFiles(dir))
                result.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }
        return result;
    }

    /// <summary>
    /// Documentation consistency (public docs): every repository path, script, workflow or markdown link a public document
    /// names exists and is shipped by the public export; the version claims match Directory.Build.props.
    /// Born from a real drift: REPRODUCIBLE_BUILDS.md said the commit id is part of the version (it is not) and told readers to
    /// run RELEASE.cmd, a local-only script that is not in the public tree.
    /// </summary>
    private static void TestPublicDocsConsistency()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("public documents agree with the repository", "no source checkout found", "source-hygiene"); return; }

        var all = DocRepositoryFiles(root);
        var byName = all.ToLookup(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
        var allSet = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var docsChecked = 0;

        foreach (var doc in PublicDocFiles.Concat(FolderReadmeFiles(root)).Concat(TabForge.Diagnostics.FeatureMapGenerator.PageFiles(root)))
        {
            var full = Path.Combine(root, doc.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;
            docsChecked++;
            var text = File.ReadAllText(full);
            var docDir = Path.GetDirectoryName(doc.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
            foreach (var reference in DocPathReferences(text).Distinct())
            {
                var isLink = reference.StartsWith("link:", StringComparison.Ordinal);
                var r = reference[(isLink ? 5 : 5)..];
                if (DocReferenceExternalNames.Contains(r, StringComparer.OrdinalIgnoreCase)) continue;
                var rel = r.Replace('\\', '/');
                string? resolved = null;
                if (isLink)
                {
                    var candidate = docDir.Length == 0 ? rel : docDir + "/" + rel;
                    candidate = Path.GetFullPath(Path.Combine(root, candidate)).Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (allSet.Contains(candidate) || Directory.Exists(Path.Combine(root, candidate))) resolved = candidate;
                }
                else if (rel.Contains('/'))
                {
                    var trimmed = rel.TrimEnd('/');
                    if (allSet.Contains(trimmed) || Directory.Exists(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar)))) resolved = trimmed;
                    else if (docDir.Length > 0 && allSet.Contains(docDir + "/" + trimmed)) resolved = docDir + "/" + trimmed;
                }
                else
                {
                    // A bare file name: the file next to the document, at the root, or anywhere else a build step names it.
                    var local = docDir.Length == 0 ? rel : docDir + "/" + rel;
                    if (allSet.Contains(local)) resolved = local;
                    else if (allSet.Contains(rel)) resolved = rel;
                    else
                    {
                        var hit = byName[rel].FirstOrDefault(f => IsInPublicAllowlist(f)) ?? byName[rel].FirstOrDefault();
                        if (hit is not null) resolved = hit;
                    }
                }
                if (resolved is null) { problems.Add($"{doc}: '{r}' does not exist in the repository"); continue; }
                if (!IsInPublicAllowlist(resolved)) problems.Add($"{doc}: '{r}' is not shipped by the public export (local-only: {resolved})");
            }
        }
        Log.Add($"  info  checked repository references in {docsChecked} public document(s)");
        Check("public documents name only files that exist and are shipped by the public export",
            docsChecked > 0 && problems.Count == 0, string.Join("; ", problems.Take(10)));

        // The exported scripts and workflows are read by outsiders too: they must not send them to a local-only script.
        var localOnly = new[] { "REBUILD.cmd", "RELEASE.cmd", "BUILD.cmd", "BACKUP-SOURCE.cmd", "HANDOFF.md", "AGENTS.md" };
        var scriptProblems = new List<string>();
        foreach (var rel in all.Where(f => IsInPublicAllowlist(f) && (f.StartsWith(".github/workflows/") || f.StartsWith("tools/") || f.StartsWith("installer/") ||
                 (f.StartsWith("native/") && !f.Contains("/tfvst3/") && !f.EndsWith(".dll") && !f.EndsWith(".lib")))))
        {
            var text = File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var name in localOnly.Where(n => text.Contains(n, StringComparison.Ordinal))) scriptProblems.Add($"{rel} mentions the local-only {name}");
        }
        Check("exported scripts and workflows name no local-only script", scriptProblems.Count == 0, string.Join("; ", scriptProblems.Take(10)));

        // README link targets (the public README is copied in at export; here the repository's README is checked for links).
        var readmeProblems = new List<string>();
        foreach (var doc in new[] { "README.md" })
        {
            var full = Path.Combine(root, doc);
            if (!File.Exists(full)) continue;
            foreach (var reference in DocPathReferences(File.ReadAllText(full)).Where(r => r.StartsWith("link:", StringComparison.Ordinal)).Distinct())
            {
                var target = reference[5..].Replace('\\', '/');
                if (!allSet.Contains(target) && !Directory.Exists(Path.Combine(root, target))) readmeProblems.Add(target);
            }
        }
        Check("README.md links point at files that exist", readmeProblems.Count == 0, string.Join(", ", readmeProblems.Take(10)));

        VerifyVersionClaims(root);
    }

    private static void VerifyVersionClaims(string root)
    {
        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var version = Regex.Match(props, @"<Version>\s*([0-9]+\.[0-9]+\.[0-9]+[^<\s]*)\s*</Version>").Groups[1].Value;
        var commitInVersion = !Regex.IsMatch(props, @"<IncludeSourceRevisionInInformationalVersion>\s*false\s*</IncludeSourceRevisionInInformationalVersion>", RegexOptions.IgnoreCase);
        Check("Directory.Build.props sets one <Version>", version.Length > 0, props.Length.ToString());

        var problems = new List<string>();
        foreach (var doc in PublicDocFiles.Concat(FolderReadmeFiles(root)).Concat(TabForge.Diagnostics.FeatureMapGenerator.PageFiles(root)).Append("README.md"))
        {
            var full = Path.Combine(root, doc.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;
            var lineNo = 0;
            foreach (var line in File.ReadAllLines(full))
            {
                lineNo++;
                // "the commit id is part of the informational version" must match the build setting.
                var mentionsCommitInVersion = Regex.IsMatch(line, @"\b(commit|revision)\b.{0,60}\b(is|are)\b.{0,30}\b(part of|in|appended to|stamped into)\b.{0,40}\b(informational )?version", RegexOptions.IgnoreCase);
                if (mentionsCommitInVersion && !commitInVersion) problems.Add($"{doc}:{lineNo} says the commit id is in the version, but IncludeSourceRevisionInInformationalVersion is false");
                foreach (Match m in Regex.Matches(line, @"<Version>\s*([0-9]+\.[0-9]+\.[0-9]+[^<\s]*)\s*</Version>"))
                    if (m.Groups[1].Value != version) problems.Add($"{doc}:{lineNo} names <Version> {m.Groups[1].Value}, the build has {version}");
            }
        }
        // The newest release heading of the changelog is the build's version.
        var changelog = Path.Combine(root, "CHANGELOG.md");
        if (File.Exists(changelog) && version.Length > 0)
        {
            var top = File.ReadLines(changelog).Where(l => l.StartsWith("## ", StringComparison.Ordinal)).FirstOrDefault(l => !l.StartsWith("## Unreleased", StringComparison.OrdinalIgnoreCase));
            // A x.y.0 release is shown as "x.y" (like the app's own display version).
            var display = version.EndsWith(".0", StringComparison.Ordinal) ? version[..^2] : version;
            var heading = Regex.Match(top ?? "", @"^##\s+(\S+)").Groups[1].Value;
            if (top is not null && heading != version && heading != display) problems.Add($"CHANGELOG.md newest release heading '{top}' is not version {version}");
        }
        Check("version and informational-version claims in the docs match Directory.Build.props", problems.Count == 0, string.Join("; ", problems.Take(10)));
    }
}
