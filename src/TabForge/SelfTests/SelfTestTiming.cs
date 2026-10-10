using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TabForge;

/// <summary>
/// Runner options for sharded and measured runs (owns the parsing and file formats; does not own test selection beyond the core shard).
/// <c>--timing &lt;csv&gt;</c> records wall-clock, checks and failures of every test and area (columns area,test,ms,checks,failed;
/// area rows use test "(area)", the last row "(total wall)") and logs the 20 slowest tests; without the flag nothing is recorded.
/// <c>--core-shard &lt;n&gt;</c> runs the untagged core tests only when n is 0 (other values skip them); <c>--areas</c> runs include core unless this flag says otherwise.
/// <c>--group-report &lt;json&gt;</c> writes every --require group's state; <c>--merge-group-reports &lt;a.json,b.json&gt;</c> runs no test, sums the reports
/// and prints the same gate lines. Tests: TestRunnerTimingAndShardOptions.
/// </summary>
public static partial class SelfTest
{
    private sealed record TimingRow(string Area, string Test, long Ms, int Checks, int Failed);

    private static List<TimingRow>? _timing;   // null: no --timing
    private static int? _coreShard;   // null: no --core-shard

    private static string? OptionValue(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static int? ParseCoreShard(IReadOnlyList<string> args) =>
        int.TryParse(OptionValue(args, "--core-shard"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>True when the core (untagged) tests run for this shard value.</summary>
    private static bool RunsCore(int? shard) => shard is null or 0;

    private static string FormatTimingCsv(IReadOnlyList<TimingRow> rows, long wallMs)
    {
        static string Q(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder("area,test,ms,checks,failed\n");
        foreach (var r in rows) sb.Append(Q(r.Area)).Append(',').Append(Q(r.Test)).Append(',').Append(r.Ms).Append(',').Append(r.Checks).Append(',').Append(r.Failed).Append('\n');
        foreach (var g in rows.GroupBy(r => r.Area).OrderBy(g => g.Key, StringComparer.Ordinal))
            sb.Append(Q(g.Key)).Append(",(area),").Append(g.Sum(r => r.Ms)).Append(',').Append(g.Sum(r => r.Checks)).Append(',').Append(g.Sum(r => r.Failed)).Append('\n');
        sb.Append("(all),(total wall),").Append(wallMs).Append(',').Append(rows.Sum(r => r.Checks)).Append(',').Append(rows.Sum(r => r.Failed)).Append('\n');
        return sb.ToString();
    }

    private static void WriteTiming(string? path, long wallMs)
    {
        if (path is null || _timing is null) return;
        foreach (var r in _timing.OrderByDescending(r => r.Ms).Take(20)) Log.Add($"  time  slowest: {r.Test} [{r.Area}] {r.Ms} ms, {r.Checks} checks");
        Log.Add($"  time  total wall {wallMs / 1000.0:0.0} s over {_timing.Count} tests");
        try { File.WriteAllText(path, FormatTimingCsv(_timing, wallMs)); }
        catch (Exception ex) { Check("--timing csv could be written", false, ex.Message); }   // Not logged elsewhere: the failure is a check result
    }

    // ---------- group reports ----------

    private sealed class GroupReportEntry { public string Name { get; set; } = ""; public bool Ran { get; set; } public bool Threw { get; set; } public bool Partial { get; set; } public int Checks { get; set; } }
    private sealed class GroupReport { public List<GroupReportEntry> Groups { get; set; } = new(); }

    private static string GroupReportJson(IReadOnlyDictionary<string, GroupState> states) =>
        JsonSerializer.Serialize(new GroupReport { Groups = states.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Select(s => new GroupReportEntry { Name = s.Key, Ran = s.Value.Ran, Threw = s.Value.Threw, Partial = s.Value.Partial, Checks = s.Value.Checks }).ToList() },
            new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Sums Checks, ORs Ran / Threw / Partial per group name across reports.</summary>
    private static Dictionary<string, GroupState> MergeGroupReports(IEnumerable<string> jsonReports)
    {
        var merged = new Dictionary<string, GroupState>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in jsonReports)
            foreach (var e in JsonSerializer.Deserialize<GroupReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Groups ?? new())
            {
                if (!merged.TryGetValue(e.Name, out var s)) merged[e.Name] = s = new GroupState();
                s.Ran |= e.Ran; s.Threw |= e.Threw; s.Partial |= e.Partial; s.Checks += e.Checks;
            }
        return merged;
    }

    /// <summary>Merge mode: no test runs; the gate lines come from the summed reports.</summary>
    private static int RunMerge(string outputPath, string reportList)
    {
        var jsons = new List<string>();
        foreach (var file in reportList.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { jsons.Add(File.ReadAllText(file)); }
            catch (Exception ex) { Check($"group report {Path.GetFileName(file)} could be read", false, ex.Message); }   // Not logged elsewhere: a check result
        }
        try
        {
            GroupStates.Clear();
            foreach (var kv in MergeGroupReports(jsons)) GroupStates[kv.Key] = kv.Value;
        }
        catch (JsonException ex) { Check("group reports are valid JSON", false, ex.Message); }   // Not logged elsewhere: a check result
        Log.Add($"  info  merged {jsons.Count} group report(s)");
        if (_required.Count > 0) Log.Add($"  info  required: {string.Join(", ", _required.OrderBy(x => x))}");
        ReportRequirements(_required, _unknownRequired);
        return FinishRun(outputPath, "");
    }

    private static void WriteGroupReport(string? path)
    {
        if (path is null) return;
        try { File.WriteAllText(path, GroupReportJson(GroupStates)); }
        catch (Exception ex) { Check("--group-report could be written", false, ex.Message); }   // Not logged elsewhere: a check result
    }

    // ---------- test ----------

    private static void TestRunnerTimingAndShardOptions()
    {
        var rows = new List<TimingRow> { new("ui", "TestA", 1500, 10, 0), new("ui", "TestB,x", 20, 2, 1), new("core", "TestC", 5, 3, 0) };
        var lines = FormatTimingCsv(rows, 2000).TrimEnd('\n').Split('\n');
        Check("--timing: csv has the header and one row per test, area and the total", lines[0] == "area,test,ms,checks,failed" && lines.Length == 1 + 3 + 2 + 1, string.Join("|", lines));
        Check("--timing: area rows sum their tests and a comma in a name is quoted", lines.Contains("ui,(area),1520,12,1") && lines.Contains("ui,\"TestB,x\",20,2,1"));
        Check("--timing: the last row is the total wall-clock", lines[^1] == "(all),(total wall),2000,15,1", lines[^1]);

        var args = new[] { "--selftest", "x.log", "--core-shard", "1", "--timing", "t.csv" };
        Check("--core-shard: value parsed, absent when not given", ParseCoreShard(args) == 1 && ParseCoreShard(new[] { "--selftest" }) is null && OptionValue(args, "--timing") == "t.csv");
        Check("--core-shard: shard 0 and no flag run core, shard 1 does not", RunsCore(0) && RunsCore(null) && !RunsCore(1) && !RunsCore(2));

        var (savedShard, savedAreas, savedBasic, savedOnly, savedSkipped, savedTiming, savedDepth) = (_coreShard, _areas, _basicOnly, _only, _skippedByArea, _timing, _guardDepth);
        try
        {
            _areas = null; _basicOnly = false; _only = null; _timing = null;
            var ran = 0; _guardDepth = 0;   // this check runs inside a Guard: the probes are top-level
            _coreShard = 1; GuardRan(() => ran++, "coreProbe");
            var skippedOnOne = ran == 0;
            _coreShard = 0; GuardRan(() => ran++, "coreProbe");
            _coreShard = null; GuardRan(() => ran++, "coreProbe");
            Check("--core-shard: GuardRan skips a core test on shard 1 and runs it on shard 0 and without the flag", skippedOnOne && ran == 2, $"ran {ran}");
        }
        finally { (_coreShard, _areas, _basicOnly, _only, _skippedByArea, _timing, _guardDepth) = (savedShard, savedAreas, savedBasic, savedOnly, savedSkipped, savedTiming, savedDepth); }

        var g = Groups[0];
        var a = new Dictionary<string, GroupState>(StringComparer.OrdinalIgnoreCase) { [g.Name] = new GroupState { Ran = true, Checks = g.MinChecks - 5, Partial = true } };
        var b = new Dictionary<string, GroupState>(StringComparer.OrdinalIgnoreCase) { [g.Name] = new GroupState { Ran = true, Threw = true, Checks = 7 } };
        var back = MergeGroupReports(new[] { GroupReportJson(a) })[g.Name];
        Check("--group-report: a report reads back unchanged", back.Ran && back.Partial && !back.Threw && back.Checks == g.MinChecks - 5);

        var required = NormalizeRequirements(new[] { g.Name }, out _);
        var half = MergeGroupReports(new[] { GroupReportJson(a) });
        Check("--merge-group-reports: one partial report is below the minimum", !EvaluateGroups(required, half).Single(x => x.Name == g.Name).Ok);
        var c = new Dictionary<string, GroupState>(StringComparer.OrdinalIgnoreCase) { [g.Name] = new GroupState { Ran = true, Checks = 7 } };
        var both = MergeGroupReports(new[] { GroupReportJson(a), GroupReportJson(c) });
        Check("--merge-group-reports: two partial reports sum to a satisfied group", both[g.Name].Checks == g.MinChecks + 2 && EvaluateGroups(required, both).Single(x => x.Name == g.Name).Ok);
        Check("--merge-group-reports: a throw in any report fails the group", !EvaluateGroups(required, MergeGroupReports(new[] { GroupReportJson(a), GroupReportJson(b) })).Single(x => x.Name == g.Name).Ok);
        Check("--merge-group-reports: a group absent from every report fails when required", !EvaluateGroups(required, MergeGroupReports(new[] { "{\"groups\":[]}", "{\"groups\":[]}" })).Single(x => x.Name == g.Name).Ok);
    }
}
