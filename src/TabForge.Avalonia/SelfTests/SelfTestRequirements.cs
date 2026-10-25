using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TabForge;

// Owns: required self-test group gate (--require).

/// <summary>
/// Required self-test groups. <c>--require &lt;name&gt;[,&lt;name&gt;]</c> (or TABFORGE_SELFTEST_REQUIRE) makes a group's absence a failure.
///
/// A group is a named set of tests with a minimum number of executed checks. Tests register by running through
/// <see cref="GuardGroup"/> (see below). A required group fails when it did not run, a test in it threw, or it executed fewer
/// checks than its minimum ("not yet populated" when it executed none), so an emptied or deleted group cannot pass the gate.
/// A group that is not required only logs an info line. Names that are not groups: "source-hygiene" and "installer-parity"
/// (enforced by <c>Skip(..., name)</c> in their tests) and "all" (every skip is a failure).
///
/// "ci" is an alias for the groups every gate must run: gp-fixtures, synthetic-fixtures, source-hygiene, installer-parity, fuzz, architecture.
/// windows-ci.yml, release.yml, tools/Package-Release.ps1 and REBUILD.cmd pass only "--require ci".
///
/// Registering a test into a group (the whole API):
///   Guard-style call in SelfTest.Run:   GuardGroup("fuzz", TestMalformedInputFuzz);
///   Several tests may share one group; their checks add up. The test body only uses Check/Eq/Skip as usual;
///   a skip inside a group should pass the group name: Skip("name", "reason", "source-hygiene").
///   Map the test method to an area in AreaOf as before (an area that --areas excludes means the group did not run).
/// </summary>
public static partial class SelfTest
{
    private sealed record GroupDefinition(string Name, int MinChecks, bool InCi);

    private sealed class GroupState { public bool Ran; public bool Threw; public int Checks; public bool Partial; }   // Partial: --only left tests of the group out

    // Minimum passed checks of each group: below the measured count, so an emptied or deleted group cannot pass the gate.
    private const int GpFixturesMinChecks = 40;
    private const int SyntheticFixturesMinChecks = 250;
    private const int FuzzMinChecks = 12;
    private const int ArchitectureMinChecks = 6;

    /// <summary>The group registry. MinChecks is a lower bound (set below the measured count); owners raise it as they populate a group.</summary>
    private static readonly GroupDefinition[] Groups =
    {
        new("gp-fixtures", GpFixturesMinChecks, true),
        new("synthetic-fixtures", SyntheticFixturesMinChecks, true),
        new("fuzz", FuzzMinChecks, true),
        new("architecture", ArchitectureMinChecks, true),
    };

    /// <summary>Required names that are enforced through <c>Skip(..., name)</c> rather than a group.</summary>
    private static readonly string[] SkipOnlyRequirements = { "source-hygiene", "installer-parity" };

    private static readonly string[] CiRequirement = { "gp-fixtures", "synthetic-fixtures", "source-hygiene", "installer-parity", "fuzz", "architecture" };

    private static readonly Dictionary<string, GroupState> GroupStates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs a test like <see cref="Guard"/> and counts its checks into <paramref name="group"/>.</summary>
    private static void GuardGroup(string group, Action test, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(test))] string name = "")
    {
        if (!Groups.Any(g => g.Name.Equals(group, StringComparison.OrdinalIgnoreCase)))
        { Check($"test group '{group}' is registered (GuardGroup({name}))", false, "add it to SelfTest.Groups"); return; }
        if (!GroupStates.TryGetValue(group, out var state)) GroupStates[group] = state = new GroupState();
        var before = _pass + _fail;
        var failBefore = _fail;
        var skippedBefore = _skippedByArea;
        var onlyBefore = _skippedByOnly;
        var threw = !GuardRan(test, name);
        if (_skippedByOnly != onlyBefore) { state.Partial = true; return; }   // not named by --only
        if (_skippedByArea != skippedBefore) return;   // outside the selected --areas: the group did not run
        state.Ran = true;
        state.Threw |= threw;
        state.Checks += _pass + _fail - before - (_fail - failBefore);   // passed checks only
    }

    /// <summary>Normalises raw --require values: expands "ci", returns names that are neither groups nor known requirements.</summary>
    private static HashSet<string> NormalizeRequirements(IEnumerable<string> raw, out List<string> unknown)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        unknown = new List<string>();
        foreach (var name in raw)
        {
            if (name.Equals("ci", StringComparison.OrdinalIgnoreCase)) { foreach (var c in CiRequirement) set.Add(c); continue; }
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase) || SkipOnlyRequirements.Contains(name, StringComparer.OrdinalIgnoreCase)
                || Groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) set.Add(name);
            else unknown.Add(name);
        }
        return set;
    }

    /// <summary>One result per registered group: required groups give pass/fail, the rest an info line.</summary>
    private static List<(string Name, bool Required, bool Ok, string Detail)> EvaluateGroups(HashSet<string> required, IReadOnlyDictionary<string, GroupState> states)
    {
        var result = new List<(string, bool, bool, string)>();
        foreach (var g in Groups)
        {
            states.TryGetValue(g.Name, out var s);
            var checks = s?.Checks ?? 0;
            var isRequired = required.Contains(g.Name);
            string detail; bool ok;
            if (s is null || !s.Ran) { ok = false; detail = checks == 0 ? "not yet populated: the group did not run (0 checks)" : "the group did not run"; }
            else if (s.Threw) { ok = false; detail = "a test in the group threw"; }
            else if (checks < g.MinChecks) { ok = false; detail = checks == 0 ? $"not yet populated (0 of the minimum {g.MinChecks} checks)" : $"too few checks: {checks} of the minimum {g.MinChecks}"; }
            else { ok = true; detail = $"{checks} checks (minimum {g.MinChecks})"; }
            result.Add((g.Name, isRequired, ok, detail));
        }
        return result;
    }

    private static void ReportRequirements(HashSet<string> required, List<string> unknown)
    {
        foreach (var name in unknown) Check($"--require name '{name}' is a known group or requirement", false, "unknown name (typo?); known: " + string.Join(", ", Groups.Select(g => g.Name).Concat(SkipOnlyRequirements).Concat(new[] { "ci", "all" })));
        foreach (var (name, isRequired, ok, detail) in EvaluateGroups(required, GroupStates))
        {
            if (isRequired) Check($"required: group {name} ran to completion ({detail})", ok, detail);
            else Log.Add($"  info  group {name}: {detail}");
        }
    }

    // ---------- tests of the gate itself ----------

    private static void TestRequiredGroupGate()
    {
        var full = Groups.ToDictionary(g => g.Name, g => new GroupState { Ran = true, Checks = g.MinChecks }, StringComparer.OrdinalIgnoreCase);
        var allRequired = NormalizeRequirements(Groups.Select(g => g.Name), out var none);
        Check("gate: every registered group name is accepted", none.Count == 0);
        Check("gate: a fully populated group passes when required", EvaluateGroups(allRequired, full).All(r => r.Ok));

        foreach (var g in Groups)
        {
            var emptied = new Dictionary<string, GroupState>(full, StringComparer.OrdinalIgnoreCase) { [g.Name] = new GroupState { Ran = true, Checks = 0 } };
            var r = EvaluateGroups(NormalizeRequirements(new[] { g.Name }, out _), emptied).Single(x => x.Name == g.Name);
            Check($"gate: emptied group {g.Name} fails when required", r.Required && !r.Ok && r.Detail.Contains("not yet populated"), r.Detail);
            var notRequired = EvaluateGroups(NormalizeRequirements(Array.Empty<string>(), out _), emptied).Single(x => x.Name == g.Name);
            Check($"gate: emptied group {g.Name} does not fail when not required", !notRequired.Required);
            var tooFew = new Dictionary<string, GroupState>(full, StringComparer.OrdinalIgnoreCase) { [g.Name] = new GroupState { Ran = true, Checks = g.MinChecks - 1 } };
            Check($"gate: group {g.Name} below its minimum fails when required",
                !EvaluateGroups(NormalizeRequirements(new[] { g.Name }, out _), tooFew).Single(x => x.Name == g.Name).Ok);
            var missing = new Dictionary<string, GroupState>(full, StringComparer.OrdinalIgnoreCase); missing.Remove(g.Name);
            Check($"gate: group {g.Name} that never ran fails when required",
                !EvaluateGroups(NormalizeRequirements(new[] { g.Name }, out _), missing).Single(x => x.Name == g.Name).Ok);
            var threw = new Dictionary<string, GroupState>(full, StringComparer.OrdinalIgnoreCase) { [g.Name] = new GroupState { Ran = true, Threw = true, Checks = g.MinChecks } };
            Check($"gate: group {g.Name} with a throwing test fails when required",
                !EvaluateGroups(NormalizeRequirements(new[] { g.Name }, out _), threw).Single(x => x.Name == g.Name).Ok);
        }

        NormalizeRequirements(new[] { "gp-fixture", "fuzz" }, out var typos);
        Check("gate: a misspelt --require name is reported as unknown", typos.SequenceEqual(new[] { "gp-fixture" }));
        NormalizeRequirements(new[] { "gp-fixtures,fuzz" }, out var unsplit);
        Check("gate: names are compared one by one (the caller splits on commas)", unsplit.Count == 1);

        var ci = NormalizeRequirements(new[] { "ci" }, out var ciUnknown);
        Check("gate: --require ci expands to the CI list including fuzz",
            ciUnknown.Count == 0 && ci.SetEquals(CiRequirement), string.Join(",", ci));
        Check("gate: the registry flags exactly the CI groups as InCi",
            Groups.Where(g => g.InCi).Select(g => g.Name).OrderBy(x => x).SequenceEqual(CiRequirement.Where(c => Groups.Any(g => g.Name == c)).OrderBy(x => x)));
    }

    /// <summary>Every --require in the gate scripts uses known names, and no gate file hand-writes the list of CI groups that "ci" names.</summary>
    private static void TestRequireArgumentStrings()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("--require argument strings in CI, release and REBUILD scripts", "no source checkout found", "source-hygiene"); return; }
        var ciFiles = new[] { @".github\workflows\windows-ci.yml", @".github\workflows\release.yml", @"Source\tools\Package-Release.ps1", @"Source\tools\REBUILD.cmd" };
        var pattern = new Regex(@"--require[\s'""|,@(]+([A-Za-z][A-Za-z0-9,;\-]*)");
        foreach (var rel in ciFiles)
        {
            var path = Path.Combine(root, rel);
            // Source\tools\REBUILD.cmd is the local clean-rebuild script and is not part of the published source: its absence is expected there.
            // .github\workflows is not part of this local checkout (CI runs from the published repository): its absence is expected here, so it is reported as info, not a skip.
            if (!File.Exists(path)) { if (rel == @"Source\tools\REBUILD.cmd" || rel.StartsWith(".github", StringComparison.Ordinal)) Log.Add($"  info  {rel} is not in this local checkout: not checked here"); else Skip($"{rel} exists", "file not found", "source-hygiene"); continue; }
            // Comments are not arguments: drop whole-line comments (# in yml/ps1, rem and :: in cmd).
            var text = string.Join(' ', File.ReadAllLines(path).Where(l => { var t = l.TrimStart(); return !t.StartsWith('#') && !t.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("::"); }));
            var names = pattern.Matches(text).SelectMany(m => m.Groups[1].Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
            NormalizeRequirements(names, out var unknown);
            Check($"{rel}: every --require name is known", unknown.Count == 0, string.Join(",", unknown));
            Check($"{rel}: does not hand-write the CI group list (--require ci names it)",
                !text.Contains("gp-fixtures,synthetic-fixtures", StringComparison.Ordinal), "found a literal list of the CI groups");
        }
        foreach (var rel in new[] { @".github\workflows\fuzz-weekly.yml", @".github\workflows\build-bridge.yml" })
        {
            var path = Path.Combine(root, rel);
            if (!File.Exists(path)) continue;
            var names = pattern.Matches(File.ReadAllText(path)).SelectMany(m => m.Groups[1].Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
            NormalizeRequirements(names, out var unknown);
            Check($"{rel}: every --require name is known", unknown.Count == 0, string.Join(",", unknown));
        }
    }
}
