using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the repo-map truth guard: every backticked type/test/file named by the
//   docs map (Source/docs/*.md, AREA-MAP.md) and by // headers of src/**/*.cs
//   exists in the tree.
// Does not own: style, budgets or behaviour (architecture self-tests own those).
// Tests: DocTruthTests (this file).
public sealed class DocTruthTests
{
    // Removed names that must never come back (were real lies in R0).
    private static readonly HashSet<string> RemovedNames = new(StringComparer.Ordinal)
    {
        "DocumentSession", "AudioRouting", "SongClock", "ThemeService", "CommitEdit",
    };

    private static readonly Regex Backticked = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex TestName = new(@"^(Test\w+|\w+Tests)$", RegexOptions.Compiled);
    private static readonly Regex Dotted = new(@"([A-Z][A-Za-z0-9_]*)\.([A-Za-z][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex TypeDecl = new(@"(?:class|struct|interface|enum|record)\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex GuardCall = new(@"Guard(?:Group)?\s*\(\s*(?:""[^""]*"",\s*)?(Test\w+)", RegexOptions.Compiled);
    private static readonly Regex TestMethod = new(@"void\s+(Test\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex TestClass = new(@"class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex MethodName = new(@"\b([A-Z][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);
    private static readonly Regex LineNumber = new(@":\d+$", RegexOptions.Compiled);
    private static readonly Regex StartsWithDigit = new(@"^\d", RegexOptions.Compiled);

    private static readonly HashSet<string> KnownExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "md", "axaml", "ps1", "psm1", "json", "csproj", "sln", "yml", "yaml",
        "props", "xml", "txt", "exe", "dll", "cmd", "cpp", "h", "patch", "iss",
        "gz", "gp", "tforge", "tfaudio", "wav", "mp3", "log", "csv", "targets",
    };

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "General", "Example", "Examples", "None", "All", "Basic", "Advanced",
        "Custom", "Default", "Manual", "Above", "Beside",
    };

    private static readonly HashSet<string> LocalOnlyFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "samples", "tabs", "work", "backup", "backups", "dist", "build", "licenses", "bin", "obj", "artifacts",
    };

    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "build", "dist", "backup", "backups", "work", "tabs", ".git", ".vs", "TestResults",
    };

    [Fact]
    public void DocMapMatchesTree()
    {
        var repo = TestRepo.CheckoutRoot();
        var allow = LoadAllowList(repo);
        var source = Path.Combine(repo, "Source");

        // Repo-root file/dir index (excluding build output + local-only + dot-folders except .github).
        var allSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileRels = new List<string>();
        var fileRelsNoExt = new List<string>();
        var dirRels = new List<string>();
        var fileBaseMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var fileStemMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var dirBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var csprojStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var pending = new Stack<string>();
            pending.Push(repo);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                string[] subs;
                string[] files;
                try
                {
                    subs = Directory.GetDirectories(dir);
                    files = Directory.GetFiles(dir);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var sub in subs)
                {
                    var baseName = Path.GetFileName(sub);
                    if (SkippedDirs.Contains(baseName)) continue;
                    if (baseName.StartsWith(".", StringComparison.Ordinal) && !baseName.Equals(".github", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(sub);
                    try
                    {
                        var rel = RelativeTo(repo, sub);
                        allSet.Add(rel);
                        dirRels.Add(rel);
                        dirBaseNames.Add(baseName);
                    }
                    catch (IOException) { }
                }
                foreach (var file in files)
                {
                    var fn = Path.GetFileName(file);
                    if (fn.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                        csprojStems.Add(Path.GetFileNameWithoutExtension(fn));
                    try
                    {
                        var rel = RelativeTo(repo, file);
                        allSet.Add(rel);
                        fileRels.Add(rel);
                        var noExt = rel.EndsWith(".lock.json", StringComparison.OrdinalIgnoreCase)
                            ? rel.Substring(0, rel.Length - ".json".Length)
                            : rel.Substring(0, rel.Length - Path.GetExtension(rel).Length);
                        fileRelsNoExt.Add(noExt);
                        if (!fileBaseMap.TryGetValue(fn, out var l1)) fileBaseMap[fn] = l1 = new List<string>();
                        l1.Add(rel);
                        var stem = Path.GetFileNameWithoutExtension(fn);
                        if (!fileStemMap.TryGetValue(stem, out var l2)) fileStemMap[stem] = l2 = new List<string>();
                        l2.Add(rel);
                    }
                    catch (IOException) { }
                }
            }
        }
        catch (IOException) { }

        // Known names: types (declarations over src+tests), methods (Name(), PascalCase),
        // tests (xUnit classes + SelfTest guards/methods), files (stems).
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        var methodNames = new HashSet<string>(StringComparer.Ordinal);
        var testNames = new HashSet<string>(StringComparer.Ordinal);
        var fileStems = new HashSet<string>(StringComparer.Ordinal);
        var fileFirstParts = new HashSet<string>(StringComparer.Ordinal);
        var dottedInSrc = new HashSet<string>(StringComparer.Ordinal);
        var headerSpans = new List<(string Span, string Where, string BaseDir)>();
        var srcCs = new List<string>();
        try { srcCs.AddRange(Directory.GetFiles(Path.Combine(source, "src"), "*.cs", SearchOption.AllDirectories)); }
        catch (IOException) { }
        var testCs = new List<string>();
        try { testCs.AddRange(Directory.GetFiles(Path.Combine(source, "tests"), "*.cs", SearchOption.AllDirectories)); }
        catch (IOException) { }
        foreach (var file in srcCs.Concat(testCs))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            fileStems.Add(name);
            fileFirstParts.Add(name.Contains('.') ? name.Split('.')[0] : name);
            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { continue; }
            foreach (Match m in TypeDecl.Matches(text)) typeNames.Add(m.Groups[1].Value);
            foreach (Match m in MethodName.Matches(text))
            {
                var n = m.Groups[1].Value;
                if (n.Length > 1 && char.IsUpper(n[0])) methodNames.Add(n);
            }
            foreach (Match m in Dotted.Matches(text)) dottedInSrc.Add(m.Value);
            if (file.StartsWith(Path.Combine(source, "tests"), StringComparison.OrdinalIgnoreCase))
            {
                testNames.Add(Path.GetFileNameWithoutExtension(file));
                foreach (Match m in TestClass.Matches(text)) testNames.Add(m.Groups[1].Value);
            }
            else
            {
                var i = 0;
                foreach (var line in text.Split('\n'))
                {
                    if (++i > 30) break;
                    var t = line.TrimStart();
                    if (!t.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (t.Contains("removed in Batch 2") || t.Contains("former full-suite")) continue;
                    foreach (Match m in Backticked.Matches(t))
                        headerSpans.Add((m.Groups[1].Value, RelativeTo(source, file), Path.GetDirectoryName(file)!));
                }
            }
        }
        var selfTests = Path.Combine(source, "src", "TabForge.Avalonia", "SelfTests");
        if (Directory.Exists(selfTests))
        {
            foreach (var file in Directory.GetFiles(selfTests, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }
                foreach (Match m in GuardCall.Matches(text)) testNames.Add(m.Groups[1].Value);
                foreach (Match m in TestMethod.Matches(text)) testNames.Add(m.Groups[1].Value);
            }
        }
        foreach (var c in csprojStems) dirBaseNames.Add(c);

        var missing = new List<string>();
        var docsScanned = 0;

        bool HasKnownExt(string token)
        {
            if (token.EndsWith(".lock.json", StringComparison.OrdinalIgnoreCase)) return true;
            var slash = Math.Max(token.LastIndexOf('/'), token.LastIndexOf('\\'));
            var name = slash >= 0 ? token.Substring(slash + 1) : token;
            var dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1) return false;
            var ext = name.Substring(dot + 1);
            return KnownExts.Contains(ext);
        }

        bool ResolvePath(string norm, string baseDir)
        {
            // Exact repo-relative (Source/...), Source-prefixed src/..., and baseDir-relative.
            if (allSet.Contains(norm)) return true;
            if (allSet.Contains("Source/" + norm.TrimStart('/'))) return true;
            if (!string.IsNullOrEmpty(baseDir))
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(baseDir, norm.Replace('/', Path.DirectorySeparatorChar)));
                    var rel = RelativeTo(repo, full);
                    if (!rel.StartsWith("..", StringComparison.Ordinal) && allSet.Contains(rel)) return true;
                }
                catch (IOException) { }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            var isDir = norm.EndsWith("/", StringComparison.Ordinal);
            var trimmed = norm.Trim('/');
            if (isDir || !HasKnownExt(trimmed))
            {
                // Directory or extension-less file stem (Rendering/RenderJob, Theme/LookTable, Core/Presenters/).
                foreach (var d in dirRels)
                {
                    if (d.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return true;
                }
                if (!isDir)
                {
                    var slash = trimmed.LastIndexOf('/');
                    var dirPart = slash >= 0 ? trimmed.Substring(0, slash) : "";
                    var filePart = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
                    foreach (var f in fileRelsNoExt)
                    {
                        if (f.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    // Dir + file-stem suffix (handles MainWindow* prefix files).
                    if (dirPart.Length > 0)
                    {
                        foreach (var f in fileRels)
                        {
                            var fb = Path.GetFileName(f);
                            var fbStem = Path.GetFileNameWithoutExtension(fb);
                            if (!fbStem.EndsWith(filePart, StringComparison.OrdinalIgnoreCase)) continue;
                            var fd = f.Substring(0, Math.Max(0, f.Length - fb.Length)).TrimEnd('/');
                            if (fd.EndsWith(dirPart, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                // File with known extension (ArchitectureBudget.json, packages.lock.json, path/to/File.cs).
                var slash = trimmed.LastIndexOf('/');
                if (slash < 0)
                {
                    if (fileBaseMap.ContainsKey(trimmed)) return true;
                    // Bare stem with prefix (AutosaveHost.cs vs MainWindowAutosaveHost.cs).
                    foreach (var kv in fileBaseMap)
                    {
                        if (kv.Key.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                }
                var dirPart = trimmed.Substring(0, slash);
                var filePart = trimmed.Substring(slash + 1);
                foreach (var f in fileRels)
                {
                    if (f.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return true;
                }
                // Dir matches + file-name suffix (MainWindow* partials).
                foreach (var f in fileRels)
                {
                    var fb = f.Substring(Math.Max(0, f.LastIndexOf('/') + 1));
                    if (!fb.EndsWith(filePart, StringComparison.OrdinalIgnoreCase)) continue;
                    var fd = f.Substring(0, Math.Max(0, f.Length - fb.Length)).TrimEnd('/');
                    if (fd.EndsWith(dirPart, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            return false;
        }

        void CheckSpan(string span, string where, string baseDir)
        {
            span = span.Trim();
            if (span.Length == 0 || allow.Contains(span)) return;
            if (span.Contains('*')) return; // wildcards (MainWindow*.cs, SelfTest*.cs) are globbed, not files.
            if (span.Contains('(') || span.Contains(')') || span.Contains('<') || span.Contains('>')
                || span.Contains('%') || span.Contains(' ') || span.Contains('\t')) return;
            var noLine = LineNumber.Replace(span, "");
            // Removed names must never reappear, backticked or dotted.
            var stem = noLine.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? noLine.Substring(0, noLine.Length - 3) : noLine.Split('.')[0];
            if (RemovedNames.Contains(stem) || RemovedNames.Contains(noLine)) { missing.Add($"{where}: `{span}`"); return; }

            // Runtime/user paths: build output, user profile vars, wildcards, runtime project files.
            var nl = noLine.Replace('\\', '/').Trim().Trim('/');
            if (nl.StartsWith("build/", StringComparison.OrdinalIgnoreCase) || nl.Equals("build", StringComparison.OrdinalIgnoreCase)) return;
            if (nl.StartsWith("%", StringComparison.Ordinal) || nl.StartsWith("~", StringComparison.Ordinal) || nl.Contains("%")) return;
            if (nl.Contains('*') || nl.Contains('<') || nl.Contains('>')) return;
            if (nl.IndexOf(".tforge", StringComparison.OrdinalIgnoreCase) >= 0) return;
            if (nl.IndexOf(".tfaudio", StringComparison.OrdinalIgnoreCase) >= 0) return;
            if (nl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || nl.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
            if (nl.StartsWith(".", StringComparison.Ordinal) && !nl.StartsWith(".github/", StringComparison.OrdinalIgnoreCase)) return;
            if (LocalOnlyFolders.Contains(nl.Trim('/'))) return;
            if (nl.StartsWith("-", StringComparison.Ordinal)) return;
            if (StartsWithDigit.IsMatch(nl)) return;
            if (nl.IndexOfAny(new[] { '=', ';', '"', '\'', ':', ',', '|', '{', '}', '[', ']', '?', '!', '$' }) >= 0) return;

            var isPathLike = nl.Contains('/') || HasKnownExt(nl);
            if (isPathLike)
            {
                var norm = nl.Trim('/');
                if (ResolvePath(norm, baseDir)) return;
                if (!noLine.Contains('.'))
                {
                    if (!allow.Contains(noLine)) missing.Add($"{where}: `{span}`");
                    return;
                }
                // Path miss but dotted (Type.A/B/C): fall through to dotted checks.
            }

            // Test names (TestFoo, FooTests); dotted Net.Test names check their last part.
            var last = noLine.Contains('.') ? noLine.Split('.').Last().Split('/').Last().Split('\\').Last() : noLine;
            if (TestName.IsMatch(last))
            {
                if (!testNames.Contains(last) && !allow.Contains(last)) missing.Add($"{where}: `{span}`");
                return;
            }

            // Dotted code refs (Type.Member or Type.A/B/C): first segment is a declared type,
            // or the whole string is a namespace/project name (directory or .csproj name),
            // or the token appears verbatim in src (command ids like File.New), or the member is a method.
            if (noLine.Contains('.'))
            {
                if (dirBaseNames.Contains(noLine) || csprojStems.Contains(noLine)) return;
                var parts = noLine.Split(new[] { '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var first = parts.Length > 0 ? parts[0] : "";
                var member = parts.Length > 0 ? parts[parts.Length - 1] : "";
                if (first is "TabForge" or "Source" or "System") return;
                if (allow.Contains(first)) return;
                if (typeNames.Contains(first)) return;
                if (noLine.StartsWith("TabForge.", StringComparison.Ordinal))
                {
                    var rest = noLine.Substring("TabForge.".Length);
                    var lastSeg = rest.Contains('.') ? rest.Split('.').Last() : rest;
                    if (dirBaseNames.Contains(lastSeg) || dirBaseNames.Contains(rest)) return;
                }
                if (dottedInSrc.Contains(noLine)) return;
                if (methodNames.Contains(member) || fileStems.Contains(member) || fileFirstParts.Contains(member) || testNames.Contains(member)) return;
                if (!allow.Contains(noLine)) missing.Add($"{where}: `{span}`");
                return;
            }

            // Bare PascalCase identifier: type, method/property/test (Name(), or test list), or file stem.
            if (typeNames.Contains(noLine)) return;
            if (methodNames.Contains(noLine)) return;
            if (fileStems.Contains(noLine) || fileFirstParts.Contains(noLine)) return;
            if (testNames.Contains(noLine)) return;
            if (!noLine.Any(char.IsUpper)) return; // lowercase-only words are prose, not code.
            if (GenericWords.Contains(noLine)) return;
            if (!allow.Contains(noLine)) missing.Add($"{where}: `{span}`");
        }

        var docs = Path.Combine(source, "docs");
        foreach (var md in new[] { "", "architecture", "specs", "plans", "guides", "reference" }.SelectMany(d => Directory.GetFiles(Path.Combine(docs, d), "*.md", SearchOption.TopDirectoryOnly)))
        {
            var fn = Path.GetFileName(md);
            if (fn.Equals("ARCHITECTURE_SIMPLIFY.md", StringComparison.OrdinalIgnoreCase)) continue; // proposal-only, header says nothing moved.
            docsScanned++;
            foreach (var line in File.ReadLines(md))
            {
                if (line.Contains("removed in Batch 2") || line.Contains("former full-suite") || line.Contains("(former ")) continue;
                if (line.Contains("→") || line.Contains("->")) continue; // proposal arrows, not existing truth.
                foreach (Match m in Backticked.Matches(line)) CheckSpan(m.Groups[1].Value, Path.GetFileName(md), docs);
            }
        }
        var areaMap = Path.Combine(source, "src", "TabForge.Avalonia", "AREA-MAP.md");
        if (File.Exists(areaMap))
        {
            docsScanned++;
            foreach (var line in File.ReadLines(areaMap))
            {
                if (line.Contains("removed in Batch 2") || line.Contains("former full-suite")) continue;
                if (line.Contains("→") || line.Contains("->")) continue;
                foreach (Match m in Backticked.Matches(line)) CheckSpan(m.Groups[1].Value, "AREA-MAP.md", Path.GetDirectoryName(areaMap)!);
            }
        }
        var headersScanned = headerSpans.Count > 0 ? Directory.GetFiles(Path.Combine(source, "src"), "*.cs", SearchOption.AllDirectories).Length : 0;
        foreach (var (span, where, baseDir) in headerSpans) CheckSpan(span, where, baseDir);

        var distinct = missing.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(docsScanned >= 2 && headersScanned > 100,
            $"guard did not scan (docs={docsScanned}, headers={headersScanned}) from {repo}");
        // Ratchet: names already stale when the guard landed sit in DocTruthBaseline.txt and may only be fixed, never added to.
        var baselinePath = Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "DocTruthBaseline.txt");
        if (Environment.GetEnvironmentVariable("TABFORGE_DOCTRUTH_WRITE_BASELINE") == "1")
            File.WriteAllLines(baselinePath, distinct);
        var baseline = File.Exists(baselinePath)
            ? new HashSet<string>(File.ReadAllLines(baselinePath).Where(l => l.Length > 0), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var added = distinct.Where(name => !baseline.Contains(name)).ToList();
        Assert.True(added.Count == 0,
            "new doc map names that do not exist in src/tests (fix the doc or add to DocTruthAllowList.txt):\n" +
            string.Join("\n", added.Take(20)) + (added.Count > 20 ? $"\n... +{added.Count - 20} more" : ""));
    }

    private static HashSet<string> LoadAllowList(string repo)
    {
        var path = Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "DocTruthAllowList.txt");
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.Ordinal);
        return new HashSet<string>(File.ReadAllLines(path)
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("#", StringComparison.Ordinal)),
            StringComparer.Ordinal);
    }

    private static string RelativeTo(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Replace('\\', '/');
    }
}
