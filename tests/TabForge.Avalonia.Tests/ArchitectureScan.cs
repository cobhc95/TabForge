using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the shared source-scan helpers for the architecture guard tests: repo-root discovery, Source/src
//   enumeration excluding obj and bin, comment and string stripping, per-file regex hit counts and the
//   shrink-only baseline ratchet (seed with an env var, new hits fail, fixed hits must lower the baseline).
// Does not own: any product rule or threshold (each guard test owns its regexes and baseline files).
// Tests: ArchitectureGuardTests, ThemeArchitectureTests, DrawingArchitectureTests, LayoutArchitectureTests.
internal static class ArchitectureScan
{
    private static readonly ConcurrentDictionary<string, string> TextCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object SeedLock = new();

    /// <summary>The checkout root holding Source/TabForge.sln (TestRepo.CheckoutRoot).</summary>
    public static string RepoRoot() => TestRepo.CheckoutRoot();

    public static bool HasPathSegment(string path, string segment)
    {
        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Every Source/src .cs file, sorted, skipping build output and whatever <paramref name="skip"/> rejects.</summary>
    public static IReadOnlyList<string> SourceFiles(string repoRoot, Func<string, bool>? skip = null)
    {
        var list = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "Source", "src"), "*.cs", SearchOption.AllDirectories))
            {
                if (HasPathSegment(file, "obj") || HasPathSegment(file, "bin"))
                    continue;
                if (skip is not null && skip(file))
                    continue;
                list.Add(file);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public static string Relative(string repoRoot, string absolutePath) =>
        Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');

    /// <summary>The file text, cached per process so the guard facts share one read of each file.</summary>
    public static string ReadText(string absolutePath) =>
        TextCache.GetOrAdd(absolutePath, static p => File.ReadAllText(p));

    /// <summary>
    /// Blanks // line comments, /* */ block comments and (when <paramref name="stripStrings"/> is set)
    /// string and char literals with spaces, preserving newlines so line numbers are unchanged.
    /// </summary>
    public static string Strip(string text, bool stripStrings)
    {
        var buf = text.ToCharArray();
        int i = 0;
        while (i < buf.Length)
        {
            char c = buf[i];
            if (c == '/' && i + 1 < buf.Length && buf[i + 1] == '/')
            {
                buf[i] = ' ';
                buf[i + 1] = ' ';
                i += 2;
                while (i < buf.Length && buf[i] != '\n')
                {
                    buf[i] = ' ';
                    i++;
                }
            }
            else if (c == '/' && i + 1 < buf.Length && buf[i + 1] == '*')
            {
                buf[i] = ' ';
                buf[i + 1] = ' ';
                i += 2;
                while (i < buf.Length && !(buf[i] == '*' && i + 1 < buf.Length && buf[i + 1] == '/'))
                {
                    if (buf[i] != '\n')
                        buf[i] = ' ';
                    i++;
                }
                if (i < buf.Length)
                {
                    buf[i] = ' ';
                    buf[i + 1] = ' ';
                    i += 2;
                }
            }
            else if (stripStrings && (c == '"' || c == '\''))
            {
                char quote = c;
                buf[i] = ' ';
                i++;
                while (i < buf.Length)
                {
                    if (quote == '\'' && buf[i] == '\n')
                        break;
                    if (buf[i] == quote)
                    {
                        buf[i] = ' ';
                        i++;
                        if (quote == '"' && i < buf.Length && buf[i] == '"')
                        {
                            buf[i] = ' ';
                            i++;
                            continue;
                        }
                        break;
                    }
                    if (buf[i] == '\\' && i + 1 < buf.Length && buf[i + 1] != '\n')
                    {
                        buf[i] = ' ';
                        buf[i + 1] = ' ';
                        i += 2;
                        continue;
                    }
                    if (buf[i] != '\n')
                        buf[i] = ' ';
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
        return new string(buf);
    }

    public static int LineOf(string text, int index)
    {
        int line = 1;
        int end = Math.Min(index, text.Length);
        for (int i = 0; i < end; i++)
        {
            if (text[i] == '\n')
                line++;
        }
        return line;
    }

    /// <summary>
    /// Matches <paramref name="pattern"/> against each included file (comments and optionally strings
    /// stripped first) and reports per-file hit counts keyed by repo-relative path, plus hit lines.
    /// </summary>
    public static void ScanCounts(
        string repoRoot,
        IReadOnlyList<string> files,
        Regex pattern,
        bool stripStrings,
        Func<string, bool> include,
        out Dictionary<string, int> counts,
        out Dictionary<string, List<int>> lines)
    {
        counts = new Dictionary<string, int>(StringComparer.Ordinal);
        lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!include(file))
                continue;
            string text;
            try
            {
                text = ReadText(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            var stripped = Strip(text, stripStrings);
            var matches = pattern.Matches(stripped);
            if (matches.Count == 0)
                continue;
            var rel = Relative(repoRoot, file);
            counts[rel] = matches.Count;
            var fileLines = new List<int>(matches.Count);
            foreach (Match m in matches)
                fileLines.Add(LineOf(stripped, m.Index));
            lines[rel] = fileLines;
        }
    }

    /// <summary>Loads a text baseline of repo-relative-path|count lines; # and // lines are comments.</summary>
    public static Dictionary<string, int> LoadCountBaseline(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            int bar = line.LastIndexOf('|');
            if (bar <= 0)
                continue;
            if (!int.TryParse(line.Substring(bar + 1).Trim(), out int count) || count < 0)
                continue;
            map[line.Substring(0, bar).Trim()] = count;
        }
        return map;
    }

    public static void WriteCountBaseline(string path, Dictionary<string, int> counts)
    {
        var ordered = counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}|{kv.Value}").ToArray();
        WriteLines(path, ordered);
    }

    private static void WriteLines(string path, string[] lines)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, lines.Length == 0 ? string.Empty : string.Join("\n", lines) + "\n");
    }

    /// <summary>
    /// Shrink-only ratchet over per-file counts: a missing baseline is seeded only when
    /// <paramref name="seedEnvVar"/>=1 (otherwise the test fails telling the user to seed); a file
    /// above its baseline count or missing from the baseline fails listing file:line samples; a file
    /// below its baseline count fails asking to lower the baseline.
    /// </summary>
    public static void AssertCountRatchet(
        Dictionary<string, int> actual,
        Dictionary<string, List<int>> lines,
        string baselinePath,
        string seedEnvVar,
        string ruleName)
    {
        bool seed = Environment.GetEnvironmentVariable(seedEnvVar) == "1";
        if (!File.Exists(baselinePath))
        {
            if (seed)
            {
                lock (SeedLock)
                    WriteCountBaseline(baselinePath, actual);
            }
            else
            {
                Assert.Fail($"{ruleName}: baseline file is missing: {baselinePath}. Run with {seedEnvVar}=1 to seed it, then commit the result.");
            }
            return;
        }
        if (seed)
        {
            lock (SeedLock)
                WriteCountBaseline(baselinePath, actual);
            return;
        }
        CheckCounts(ruleName, seedEnvVar, actual, lines, LoadCountBaseline(baselinePath));
    }

    private static void CheckCounts(
        string ruleName,
        string seedEnvVar,
        Dictionary<string, int> actual,
        Dictionary<string, List<int>> lines,
        Dictionary<string, int> baseline)
    {
        var grown = new List<string>();
        foreach (var kv in actual.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!baseline.TryGetValue(kv.Key, out int allowed) || kv.Value > allowed)
            {
                string sample = lines.TryGetValue(kv.Key, out var fileLines)
                    ? string.Join(", ", fileLines.Take(10))
                    : "?";
                string was = baseline.ContainsKey(kv.Key) ? $"allowed {allowed}" : "missing from baseline";
                grown.Add($"{kv.Key} has {kv.Value} ({was}); e.g. lines {sample}");
            }
        }
        if (grown.Count > 0)
        {
            Assert.Fail($"{ruleName}: new or grown violations (fix the code, do not raise the baseline):\n"
                + string.Join("\n", grown.Take(20))
                + (grown.Count > 20 ? $"\n... and {grown.Count - 20} more files" : string.Empty));
            return;
        }
        var shrunk = baseline
            .Where(kv => !actual.TryGetValue(kv.Key, out int value) || value < kv.Value)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        if (shrunk.Count > 0)
        {
            Assert.Fail($"{ruleName}: these baseline entries no longer occur; lower the baseline (or re-run with {seedEnvVar}=1):\n"
                + string.Join("\n", shrunk.Take(20))
                + (shrunk.Count > 20 ? $"\n... and {shrunk.Count - 20} more files" : string.Empty));
        }
    }

    /// <summary>Loads a set baseline of one entry per line; # and // lines are comments.</summary>
    public static HashSet<string> LoadSetBaseline(string path)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            set.Add(line);
        }
        return set;
    }

    public static void WriteSetBaseline(string path, IEnumerable<string> items) =>
        WriteLines(path, items.OrderBy(x => x, StringComparer.Ordinal).ToArray());

    /// <summary>Shrink-only ratchet over a set of entries (namespace cycles): additions fail, removals must be deleted.</summary>
    public static void AssertSetRatchet(HashSet<string> actual, string baselinePath, string seedEnvVar, string ruleName)
    {
        bool seed = Environment.GetEnvironmentVariable(seedEnvVar) == "1";
        if (!File.Exists(baselinePath))
        {
            if (seed)
            {
                lock (SeedLock)
                    WriteSetBaseline(baselinePath, actual);
            }
            else
            {
                Assert.Fail($"{ruleName}: baseline file is missing: {baselinePath}. Run with {seedEnvVar}=1 to seed it, then commit the result.");
            }
            return;
        }
        if (seed)
        {
            lock (SeedLock)
                WriteSetBaseline(baselinePath, actual);
            return;
        }
        var baseline = LoadSetBaseline(baselinePath);
        var added = actual.Where(x => !baseline.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (added.Count > 0)
        {
            Assert.Fail($"{ruleName}: new entries that are not in the baseline (fix the code, do not extend the baseline):\n" + string.Join("\n", added));
            return;
        }
        var removed = baseline.Where(x => !actual.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (removed.Count > 0)
        {
            Assert.Fail($"{ruleName}: baseline entries that no longer occur; remove them from the baseline (or re-run with {seedEnvVar}=1):\n" + string.Join("\n", removed));
        }
    }

    /// <summary>Loads the shared JSON baseline of section to repo-relative-path to count.</summary>
    public static Dictionary<string, Dictionary<string, int>> LoadJsonBaseline(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var section in doc.RootElement.EnumerateObject())
        {
            var inner = new Dictionary<string, int>(StringComparer.Ordinal);
            if (section.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in section.Value.EnumerateObject())
                {
                    if (kv.Value.ValueKind == JsonValueKind.Number && kv.Value.TryGetInt32(out int value) && value >= 0)
                        inner[kv.Name] = value;
                }
            }
            map[section.Name] = inner;
        }
        return map;
    }

    /// <summary>
    /// Shrink-only ratchet over one section of the shared JSON baseline: seeding rewrites only that
    /// section and keeps the others, so the theme, drawing and layout facts can seed independently.
    /// </summary>
    public static void AssertJsonSectionRatchet(
        string section,
        Dictionary<string, int> actual,
        Dictionary<string, List<int>> lines,
        string baselinePath,
        string seedEnvVar,
        string ruleName)
    {
        bool seed = Environment.GetEnvironmentVariable(seedEnvVar) == "1";
        if (!File.Exists(baselinePath))
        {
            if (seed)
            {
                lock (SeedLock)
                    WriteJsonSection(baselinePath, section, actual);
            }
            else
            {
                Assert.Fail($"{ruleName}: baseline file is missing: {baselinePath}. Run with {seedEnvVar}=1 to seed it, then commit the result.");
            }
            return;
        }
        if (seed)
        {
            lock (SeedLock)
                WriteJsonSection(baselinePath, section, actual);
            return;
        }
        Dictionary<string, Dictionary<string, int>> all;
        try
        {
            all = LoadJsonBaseline(baselinePath);
        }
        catch (IOException)
        {
            Assert.Fail($"{ruleName}: baseline file cannot be read: {baselinePath}. Run with {seedEnvVar}=1 to seed it.");
            return;
        }
        catch (JsonException ex)
        {
            Assert.Fail($"{ruleName}: baseline file is not valid JSON: {baselinePath} ({ex.Message}). Run with {seedEnvVar}=1 to reseed it.");
            return;
        }
        if (!all.TryGetValue(section, out var allowed) || allowed is null)
            allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        CheckCounts(ruleName, seedEnvVar, actual, lines, allowed);
    }

    private static void WriteJsonSection(string path, string section, Dictionary<string, int> counts)
    {
        Dictionary<string, Dictionary<string, int>>? all = null;
        if (File.Exists(path))
        {
            try
            {
                all = LoadJsonBaseline(path);
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        all ??= new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        all[section] = counts.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var ordered = all.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
