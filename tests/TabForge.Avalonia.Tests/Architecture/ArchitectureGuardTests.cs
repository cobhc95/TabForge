using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Phase 1 architecture seams guard: no new static mutable state or static events, no new
//   TabForge namespace cycle, no Core to Avalonia reference, no new Debug.WriteLine outside the log
//   facade and no new untagged empty catch block. Each rule is a shrink-only per-file baseline.
// Does not own: product code, the theme/drawing/layout token rules (their own test files) or docs truth.
// Tests: ArchitectureGuardTests (this file).
public sealed class ArchitectureGuardTests
{
    private const string WriteBaselineEnvVar = "TABFORGE_ARCH_WRITE_BASELINE";

    private static readonly Regex StaticTokenPattern = new(@"\bstatic\b", RegexOptions.Compiled);
    private static readonly Regex StaticEventPattern = new(@"\bstatic\s+event\b", RegexOptions.Compiled);
    private static readonly Regex StaticClassPattern = new(@"\bstatic\s+(?:partial\s+)?class\b", RegexOptions.Compiled);
    private static readonly Regex UsingStaticPattern = new(@"^\s*(?:global\s+)?using\s+static\b", RegexOptions.Compiled);
    private static readonly Regex ThreadStaticPattern = new(@"ThreadStatic", RegexOptions.Compiled);
    private static readonly char[] FieldTerminators = { '=', ';', '{' };
    private static readonly Regex MemberModifierPattern = new(@"\b(?:public|private|protected|internal|static|readonly|volatile|unsafe|new|required)b\s*", RegexOptions.Compiled);
    private static readonly Regex DelegateTypePattern = new(@"^(?:Func|Action|Predicate|Comparison|Converter)\b", RegexOptions.Compiled);
    // Mutable by construction: growable collections, builders and arrays (elements stay writable after init).
    private static readonly Regex MutableTypePattern = new(
        @"\b(?:List|Dictionary|HashSet|Queue|Stack|LinkedList|SortedDictionary|SortedList|SortedSet|ConcurrentDictionary|ConcurrentBag|ConcurrentQueue|ConcurrentStack|ConditionalWeakTable|BlockingCollection|ObservableCollection|StringBuilder)\b|\[\s*,*\s*\]",
        RegexOptions.Compiled);

    private static readonly Regex NamespacePattern = new(@"^\s*namespace\s+([A-Za-z_][\w\.]*)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex TabForgeUsingPattern = new(@"^\s*(?:global\s+)?using\s+(?:[A-Za-z_]\w*\s*=\s*)?TabForge\.([A-Za-z_][\w\.]*)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex AvaloniaUsingPattern = new(@"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_]\w*\s*=\s*)?(Avalonia(?=\s*;|\.)|TabForge\.Avalonia(?=\s*;|\.))", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex AvaloniaReferencePattern = new(@"<(ProjectReference|PackageReference)\s[^>]*Include=""[^""]*Avalonia", RegexOptions.Compiled);   // a real reference, not InternalsVisibleTo
    private static readonly Regex XmlCommentPattern = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex DebugWriteLinePattern = new(@"Debug\.WriteLine", RegexOptions.Compiled);

    private static readonly Regex EmptyCatchPattern = new(@"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.Compiled | RegexOptions.Singleline);

    private static string BaselinePath(string repo, string file) =>
        Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", file);

    private static IReadOnlyList<string> ScannedSources(string repo, string rule)
    {
        var files = ArchitectureScan.SourceFiles(repo);
        Assert.True(files.Count > 100, $"{rule} guard scanned no source files from {repo}");
        return files;
    }

    [Fact]
    public void StaticStateOnlyShrinks()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ScannedSources(repo, "StaticState");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string text;
            try
            {
                text = ArchitectureScan.ReadText(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            var stripped = ArchitectureScan.Strip(text, stripStrings: true);
            var split = stripped.Split('\n');
            string? previousSignificant = null;
            var hits = new List<int>();
            for (int i = 0; i < split.Length; i++)
            {
                if (IsStaticStateLine(split[i], previousSignificant))
                    hits.Add(i + 1);
                if (split[i].Trim().Length > 0)
                    previousSignificant = split[i];
            }
            if (hits.Count > 0)
            {
                var rel = ArchitectureScan.Relative(repo, file);
                counts[rel] = hits.Count;
                lines[rel] = hits;
            }
        }
        ArchitectureScan.AssertCountRatchet(counts, lines, BaselinePath(repo, "StaticStateBaseline.txt"), WriteBaselineEnvVar, "StaticState");
    }

    internal static bool IsStaticStateLine(string line, string? previousSignificantLine)
    {
        if (!StaticTokenPattern.IsMatch(line))
            return false;
        if (StaticEventPattern.IsMatch(line))
            return true;
        if (UsingStaticPattern.IsMatch(line))
            return false;
        if (StaticClassPattern.IsMatch(line))
            return false;
        if (line.Contains("=>", StringComparison.Ordinal))
            return false;
        if (line.Contains("const ", StringComparison.Ordinal))
            return false;
        bool isReadonly = Regex.IsMatch(line, @"\breadonly\b");
        if (previousSignificantLine is not null && ThreadStaticPattern.IsMatch(previousSignificantLine))
            return false;
        // A method has its parameter list before any field terminator or body; a field initializer
        // keeps the = before any call parens, so only the method shape is skipped here.
        // A tuple type's paren follows a space, comma or angle bracket; a method's follows its name.
        int end = line.IndexOfAny(FieldTerminators);
        for (int paren = line.IndexOf('('); paren > 0 && (end < 0 || paren < end); paren = line.IndexOf('(', paren + 1))
        {
            if (char.IsLetterOrDigit(line[paren - 1]) || line[paren - 1] is '_' or '>')
                return false;
        }
        if (line.Contains('{'))
        {
            // A setter of any access is state; a get-only property is state only when its type is mutable.
            return line.Contains("set;", StringComparison.Ordinal) || HasMutableMemberType(line);
        }
        // A readonly field is state only when its type is mutable (collection, builder, array).
        return !isReadonly || HasMutableMemberType(line);
    }

    // The declared type is the text before the member name: modifiers and the initializer are dropped.
    private static bool HasMutableMemberType(string line)
    {
        int end = line.IndexOfAny(FieldTerminators);
        var head = MemberModifierPattern.Replace(end < 0 ? line : line.Substring(0, end), string.Empty).Trim();
        int space = head.LastIndexOf(' ');
        if (space <= 0)
            return false;
        var type = head.Substring(0, space).Trim();
        return !DelegateTypePattern.IsMatch(type) && MutableTypePattern.IsMatch(type);
    }

    [Theory]
    [InlineData("    private static int _count;", true)]
    [InlineData("    public static event Action? Changed;", true)]
    [InlineData("    private static Lazy<int> _cache = new(Make);", true)]
    [InlineData("    public static Foo Current { get; private set; }", true)]
    [InlineData("    public static Foo Current { get; set; } = new();", true)]
    [InlineData("    private static readonly List<string> Names = new();", true)]
    [InlineData("    private static readonly Dictionary<string, int> Map = new();", true)]
    [InlineData("    private static readonly string[] Keys = { \"a\" };", true)]
    [InlineData("    public static List<string> Notes { get; } = new();", true)]
    [InlineData("    private static readonly StringBuilder Sb = new();", true)]
    [InlineData("    private const int Max = 3;", false)]
    [InlineData("    private static readonly int Max = 3;", false)]
    [InlineData("    private static readonly string Name = \"a\";", false)]
    [InlineData("    private static readonly object Gate = new();", false)]
    [InlineData("    private static readonly Lazy<int> Cache = new(Make);", false)]
    [InlineData("    private static readonly IReadOnlyList<string> Names = new[] { \"a\" };", false)]
    [InlineData("    private static readonly ImmutableArray<int> Ids = ImmutableArray.Create(1);", false)]
    [InlineData("    private static readonly Func<List<int>> Make = () => new();", false)]
    [InlineData("    public static Foo Default { get; } = new();", false)]
    [InlineData("    public static int Compute(int x) { return x; }", false)]
    [InlineData("    private static readonly (string Name, int Id)[] Rows =", true)]
    [InlineData("    private static readonly Dictionary<string, (int, int)> Pairs = new();", true)]
    [InlineData("    private static readonly IReadOnlyList<(string Name, int Id)> Rows = Make();", false)]
    [InlineData("    private static (int, int) Pair(int x) { return (x, x); }", false)]
    [InlineData("    public static Task<List<int>> Load(int x) => null;", false)]
    public void StaticStateRuleClassifiesPatterns(string line, bool expectedHit)
    {
        Assert.Equal(expectedHit, IsStaticStateLine(line, null));
    }

    [Fact]
    public void NamespaceGraphHasNoNewCycle()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ScannedSources(repo, "NamespaceCycle");
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string text;
            try
            {
                text = ArchitectureScan.ReadText(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            // Limitation: only using directives are edges; fully qualified TabForge names are not seen.
            var stripped = ArchitectureScan.Strip(text, stripStrings: true);
            var ns = NamespacePattern.Match(stripped);
            if (!ns.Success)
                continue;
            var from = ns.Groups[1].Value;
            if (!graph.TryGetValue(from, out var edges))
            {
                edges = new HashSet<string>(StringComparer.Ordinal);
                graph[from] = edges;
            }
            foreach (Match u in TabForgeUsingPattern.Matches(stripped))
            {
                var to = "TabForge." + u.Groups[1].Value;
                if (!to.Equals(from, StringComparison.Ordinal))
                    edges.Add(to);
            }
        }
        foreach (var to in graph.Values.SelectMany(e => e).Where(n => !graph.ContainsKey(n)).ToList())
            graph[to] = new HashSet<string>(StringComparer.Ordinal);
        ArchitectureScan.AssertSetRatchet(FindNamespaceCycles(graph), BaselinePath(repo, "CycleBaseline.txt"), WriteBaselineEnvVar, "NamespaceCycle");
    }

    private static HashSet<string> FindNamespaceCycles(Dictionary<string, HashSet<string>> graph)
    {
        var cycles = new HashSet<string>(StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        int counter = 0;
        foreach (var node in graph.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!index.ContainsKey(node))
                Visit(node);
        }
        return cycles;

        void Visit(string v)
        {
            index[v] = counter;
            low[v] = counter;
            counter++;
            stack.Push(v);
            onStack.Add(v);
            foreach (var w in graph[v].OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!index.ContainsKey(w))
                {
                    Visit(w);
                    low[v] = Math.Min(low[v], low[w]);
                }
                else if (onStack.Contains(w))
                {
                    low[v] = Math.Min(low[v], index[w]);
                }
            }
            if (low[v] == index[v])
            {
                var component = new List<string>();
                string w;
                do
                {
                    w = stack.Pop();
                    onStack.Remove(w);
                    component.Add(w);
                } while (!w.Equals(v, StringComparison.Ordinal));
                if (component.Count > 1)
                    cycles.Add(CanonicalCycle(component));
            }
        }
    }

    private static string CanonicalCycle(List<string> component) =>
        string.Join("<->", component
            .Select(n => n.StartsWith("TabForge.", StringComparison.Ordinal) ? n.Substring("TabForge.".Length) : n)
            .OrderBy(n => n, StringComparer.Ordinal));

    [Fact]
    public void CoreNeverReferencesAvalonia()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ScannedSources(repo, "CoreNoAvalonia");
        var hits = new List<string>();
        foreach (var file in files)
        {
            var rel = ArchitectureScan.Relative(repo, file);
            if (!rel.StartsWith("Source/src/TabForge.Core/", StringComparison.Ordinal))
                continue;
            string text;
            try
            {
                text = ArchitectureScan.ReadText(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            var stripped = ArchitectureScan.Strip(text, stripStrings: true);
            foreach (Match m in AvaloniaUsingPattern.Matches(stripped))
                hits.Add($"{rel}:{ArchitectureScan.LineOf(stripped, m.Index)}");
        }
        var csproj = Path.Combine(repo, "Source", "src", "TabForge.Core", "TabForge.Core.csproj");
        try
        {
            var csprojText = XmlCommentPattern.Replace(File.ReadAllText(csproj), " ");
            foreach (Match m in AvaloniaReferencePattern.Matches(csprojText))
                hits.Add($"Source/src/TabForge.Core/TabForge.Core.csproj:{ArchitectureScan.LineOf(csprojText, m.Index)}");
        }
        catch (IOException)
        {
            hits.Add("Source/src/TabForge.Core/TabForge.Core.csproj: unreadable");
        }
        catch (UnauthorizedAccessException)
        {
            hits.Add("Source/src/TabForge.Core/TabForge.Core.csproj: unreadable");
        }
        hits.Sort(StringComparer.Ordinal);
        Assert.True(hits.Count == 0, "Core references Avalonia, which is never allowed:\n" + string.Join("\n", hits.Take(20)) + (hits.Count > 20 ? $"\n... and {hits.Count - 20} more" : string.Empty));
    }

    [Fact]
    public void DebugWriteLineOnlyShrinks()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ScannedSources(repo, "DebugWriteLine");
        ArchitectureScan.ScanCounts(repo, files, DebugWriteLinePattern, stripStrings: true, static f => !IsLogFacade(f), out var counts, out var lines);
        ArchitectureScan.AssertCountRatchet(counts, lines, BaselinePath(repo, "DebugWriteLineBaseline.txt"), WriteBaselineEnvVar, "DebugWriteLine");
    }

    private static bool IsLogFacade(string path)
    {
        var name = Path.GetFileName(path);
        return name is not null
            && (name.Equals("Trace.cs", StringComparison.OrdinalIgnoreCase) || name.Equals("ErrorLog.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyCatchOnlyShrinks()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ScannedSources(repo, "EmptyCatch");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string text;
            try
            {
                text = ArchitectureScan.ReadText(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            var stripped = ArchitectureScan.Strip(text, stripStrings: true);
            var matches = EmptyCatchPattern.Matches(stripped);
            if (matches.Count == 0)
                continue;
            var originalLines = text.Split('\n');
            var untagged = new List<int>();
            foreach (Match m in matches)
            {
                int start = ArchitectureScan.LineOf(stripped, m.Index);
                int end = ArchitectureScan.LineOf(stripped, m.Index + m.Length);
                bool tagged = false;
                for (int line = Math.Max(1, start - 1); line <= end && line <= originalLines.Length; line++)
                {
                    if (originalLines[line - 1].Contains("swallow:", StringComparison.Ordinal))
                    {
                        tagged = true;
                        break;
                    }
                }
                if (!tagged)
                    untagged.Add(start);
            }
            if (untagged.Count > 0)
            {
                var rel = ArchitectureScan.Relative(repo, file);
                counts[rel] = untagged.Count;
                lines[rel] = untagged;
            }
        }
        ArchitectureScan.AssertCountRatchet(counts, lines, BaselinePath(repo, "BareCatchBaseline.txt"), WriteBaselineEnvVar, "EmptyCatch");
    }
}
