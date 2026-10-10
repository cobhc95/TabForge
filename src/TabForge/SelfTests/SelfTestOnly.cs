using System.Diagnostics;
using System.IO;

namespace TabForge;

/// <summary>
/// <c>--selftest &lt;log&gt; --only &lt;TestName&gt;[,&lt;TestName&gt;...]</c> runs just the named tests, written as in the Guard / GuardGroup call
/// (the test method name). The usual "N passed, M failed" line is printed. An unknown name fails the run before anything executes and lists
/// the closest registered names. Every test has an entry in <c>AreaOf</c> (a test enforces it), so that table is the registry of names.
/// A group named by <c>--require</c> must be fully included in the selection; otherwise the run says so instead of reporting the group as missing.
/// </summary>
public static partial class SelfTest
{
    private static HashSet<string>? _only;   // null: no --only
    private static int _skippedByOnly;

    /// <summary>The names given with --only (comma/semicolon separated, the option may repeat); null when the option is absent.</summary>
    private static HashSet<string>? ParseOnly(IReadOnlyList<string> args)
    {
        HashSet<string>? set = null;
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].Equals("--only", StringComparison.OrdinalIgnoreCase))
                (set ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(
                    args[i + 1].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return set;
    }

    /// <summary>Names in <paramref name="requested"/> that are not in <paramref name="known"/>, each with up to five close registered names.</summary>
    private static List<(string Name, List<string> Close)> UnknownOnlyNames(IEnumerable<string> requested, IReadOnlyCollection<string> known)
    {
        var knownSet = new HashSet<string>(known, StringComparer.Ordinal);
        var result = new List<(string, List<string>)>();
        foreach (var name in requested.Where(n => !knownSet.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
        {
            var limit = Math.Max(3, name.Length / 4);
            var close = known
                .Select(k => (Name: k, Rank: k.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(k, StringComparison.OrdinalIgnoreCase) ? 0 : EditDistance(name.ToLowerInvariant(), k.ToLowerInvariant())))
                .Where(x => x.Rank <= limit)
                .OrderBy(x => x.Rank).ThenBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => x.Name).Take(5).ToList();
            result.Add((name, close));
        }
        return result;
    }

    private static int EditDistance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[b.Length];
    }

    /// <summary>True when --only is active and does not name this test.</summary>
    private static bool OutsideOnly(string testMethodName) => _only is not null && !_only.Contains(testMethodName);

    /// <summary>Validates --only before any test runs. Returns false (with the failure logged) when the list is empty or a name is unknown.</summary>
    private static bool ValidateOnly()
    {
        if (_only is null) return true;
        if (_only.Count == 0) { Check("--only names at least one test", false, "the list is empty"); return false; }
        var unknown = UnknownOnlyNames(_only, AreaOf.Keys);
        foreach (var (name, close) in unknown)
            Check($"--only name '{name}' is a registered test", false, close.Count == 0 ? "unknown name; no close match (full-suite tests exist only in a build with -p:TabForgeFullSuite=true, see tests/full-suite/README.md)" : "unknown name; did you mean: " + string.Join(", ", close));
        if (unknown.Count == 0) Log.Add($"  info  only: {string.Join(", ", _only.OrderBy(x => x, StringComparer.Ordinal))}");
        return unknown.Count == 0;
    }

    /// <summary>
    /// With --only, a required group counts only when every test of it was selected. Groups that were cut short get one clear failure
    /// and leave the requirement set, so the ordinary group evaluation does not also report them as missing.
    /// </summary>
    private static HashSet<string> RequiredGroupsFullyIncluded(HashSet<string> required)
    {
        if (_only is null) return required;
        var kept = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase);
        foreach (var g in Groups.Where(g => required.Contains(g.Name)))
        {
            GroupStates.TryGetValue(g.Name, out var state);
            if (state is not null && state.Ran && !state.Partial) continue;
            kept.Remove(g.Name);
            Check($"--require {g.Name} works with --only only when the whole group is selected", false,
                "--only left tests of this group out; name every test of the group, or drop --only (or --require) for this run");
        }
        return kept;
    }

    private static void TestOnlyOption()
    {
        var names = new[] { "TestRequireArgumentStrings", "TestRequiredGroupGate", "TestOnlyOption" };
        var parsed = ParseOnly(new[] { "--selftest", "x.log", "--only", "TestRequireArgumentStrings, TestRequiredGroupGate;TestOnlyOption", "--only", "TestRequireArgumentStrings" });
        Check("--only: a comma / semicolon list and a repeated option are read as a set", parsed is not null && parsed.SetEquals(names));
        Check("--only: absent when the option is not given or has no value", ParseOnly(new[] { "--selftest", "x.log" }) is null && ParseOnly(new[] { "--only" }) is null);

        var known = AreaOf.Keys.ToList();
        Check("--only: registered names are all known", UnknownOnlyNames(names, known).Count == 0);
        var unknown = UnknownOnlyNames(new[] { "TestRequireArgumentString", "TestNothingLikeThisAtAll" }, known);
        Check("--only: an unknown name is reported with the close matches", unknown.Count == 2
            && unknown.Single(u => u.Name == "TestRequireArgumentString").Close.Contains("TestRequireArgumentStrings")
            && unknown.Single(u => u.Name == "TestNothingLikeThisAtAll").Close.Count == 0);

        var saved = _only;
        try
        {
            _only = new HashSet<string>(new[] { "TestRequireArgumentStrings" }, StringComparer.Ordinal);
            Check("--only: a test outside the list is not run, a listed one is", OutsideOnly("TestRequiredGroupGate") && !OutsideOnly("TestRequireArgumentStrings"));
            _only = null;
            Check("--only: without the option every test is selected", !OutsideOnly("TestRequiredGroupGate"));
        }
        finally { _only = saved; }

        Check("--only: the group names of --require stay valid", NormalizeRequirements(new[] { Groups[0].Name }, out var badNames).Contains(Groups[0].Name) && badNames.Count == 0);

        // End to end: real child runs of two cheap tests, an unknown name, and a required group that is not fully selected.
        var exe = Environment.ProcessPath;
        if (exe is null || !File.Exists(exe)) { Skip("--only runs only the named tests (child run)", "no executable to start"); return; }
        var work = Path.Combine(Path.GetTempPath(), $"tf-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            string Run(string only, string? require = null)
            {
                var log = Path.Combine(work, $"{Guid.NewGuid():N}.log");
                var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work };
                foreach (var a in new[] { "--selftest", log, "--profile", Path.Combine(work, "profile"), "--only", only }) info.ArgumentList.Add(a);
                if (require is not null) { info.ArgumentList.Add("--require"); info.ArgumentList.Add(require); }
                using var child = Process.Start(info)!;
                if (!child.WaitForExit(120_000)) { try { child.Kill(true); } catch (InvalidOperationException) { } return "TIMED OUT"; } // Not logged: kill of a child that may have exited; the timeout result is reported.
                return File.Exists(log) ? File.ReadAllText(log) : "no log";
            }
            static string Last(string log) => log.Split('\n').LastOrDefault(l => l.Contains("self-test")) ?? log;
            var ok = Run("TestRequireArgumentStrings,TestLooseSoundTouchAndLicenseTexts");
            Check("--only: a child run executes just the two named tests and says so", ok.Contains(" 0 failed") && ok.Contains("passed") && !ok.Contains("FAIL") && !ok.Contains("TestRequiredGroupGate"), Last(ok));
            var unknownRun = Run("TestRequireArgumentString");
            Check("--only: a child run with an unknown name fails and suggests the right one",
                unknownRun.Contains("FAIL") && unknownRun.Contains("TestRequireArgumentStrings") && unknownRun.Contains(" 1 failed"), Last(unknownRun));
            var partial = Run("TestArchitectureLayering", "architecture");
            Check("--only: --require of a group that is not fully selected gives the clear message",
                partial.Contains("works with --only only when the whole group is selected"), Last(partial));
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } } // Not logged: test cleanup of a temporary folder.
    }
}
