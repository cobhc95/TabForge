using System.Text;

namespace ParityRecorder;

/// <summary>
/// Records text baselines of TabForge (element trees, hit-test and cursor maps, scenarios) and compares them with the
/// committed copies in tests/parity/flaui/baseline, or re-records them with --accept.
/// Usage: ParityRecorder [--check] [--accept] [--exe &lt;TabForge.exe&gt;]
/// Owns: the mode switch, the baseline folder and the comparison. Does not own: what is recorded (Recorder).
/// </summary>
internal static class Program
{
    /// <summary>Ends the process with the run's code: UI Automation event threads can otherwise keep it alive after the work is done.</summary>
    private static void Main(string[] args) => Environment.Exit(Run(args));

    private static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("ParityRecorder runs on Windows only.");
            return 2;
        }
        if (args.Contains("--ab")) return AbCompare.Run(args);
        Native.SetProcessDpiAwarenessContext(Native.PerMonitorV2);
        var accept = args.Contains("--accept");
        var exe = Option(args, "--exe") ?? Path.Combine(Path.GetTempPath(), "tf-j2", "TabForge.exe");
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"TabForge.exe not found at {exe}. Build it first (see the README).");
            return 2;
        }

        var baseline = Option(args, "--baseline") ?? Path.Combine(Directory.GetCurrentDirectory(), "tests", "parity", "flaui", "baseline");
        if (!Directory.Exists(Path.Combine(baseline, "..")))
        {
            Console.Error.WriteLine($"baseline folder parent not found at {baseline}. Run from the repository root or pass --baseline.");
            return 2;
        }
        var recorded = Recorder.RecordAll(exe, args.Contains("--keep-scratch"), args.Contains("--functions-only"));
        if (Option(args, "--dump") is { } dump)
        {
            Directory.CreateDirectory(dump);
            foreach (var (name, text) in recorded) File.WriteAllText(Path.Combine(dump, name), Normalize(text), new UTF8Encoding(false));
        }
        if (accept) return Accept(recorded, baseline);

        var diffs = Compare(recorded, baseline, out var fileCount);
        Console.WriteLine($"BASELINE: {fileCount} files, {diffs.Count} differences");
        foreach (var diff in diffs) Console.WriteLine($"  DIFF {diff}");
        return diffs.Count == 0 ? 0 : 1;
    }

    private static int Accept(IDictionary<string, string> recorded, string baseline)
    {
        Directory.CreateDirectory(baseline);
        foreach (var stale in Directory.GetFiles(baseline, "*.txt").Where(f => !recorded.ContainsKey(Path.GetFileName(f))))
            File.Delete(stale);
        foreach (var (name, text) in recorded)
            File.WriteAllText(Path.Combine(baseline, name), Normalize(text), new UTF8Encoding(false));
        Console.WriteLine($"ACCEPTED: {recorded.Count} files written to {baseline}");
        return 0;
    }

    private static List<string> Compare(IDictionary<string, string> recorded, string baseline, out int fileCount)
    {
        var expected = Directory.Exists(baseline)
            ? Directory.GetFiles(baseline, "*.txt").Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList()
            : new List<string>();
        var names = recorded.Keys.Union(expected, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        fileCount = names.Count;
        var diffs = new List<string>();
        foreach (var name in names)
        {
            var path = Path.Combine(baseline, name);
            var hasBaseline = File.Exists(path);
            var hasRecording = recorded.TryGetValue(name, out var text);
            if (!hasBaseline) diffs.Add($"{name}: no baseline (run with --accept)");
            else if (!hasRecording) diffs.Add($"{name}: not recorded this run");
            else
            {
                var want = Normalize(File.ReadAllText(path));
                var got = Normalize(text!);
                if (want != got) diffs.Add($"{name}: {FirstDifference(want, got)}");
            }
        }
        return diffs;
    }

    private static string FirstDifference(string want, string got)
    {
        var a = want.Split('\n');
        var b = got.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var left = i < a.Length ? a[i] : "<end>";
            var right = i < b.Length ? b[i] : "<end>";
            if (left != right) return $"line {i + 1} differs (baseline: {Clip(left)} | now: {Clip(right)})";
        }
        return "differs";
    }

    private static string Clip(string line) => line.Length <= 120 ? line : line[..120] + "...";

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static string? Option(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

}
