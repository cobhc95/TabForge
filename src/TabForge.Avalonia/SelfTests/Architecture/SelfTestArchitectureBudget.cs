using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TabForge;

// Owns: Source/src/TabForge.Avalonia/ArchitectureBudget.json: reading it, comparing the measured source file sizes with it (the ratchet) and recording a new one.
// Does not own: the budget file's numbers (they change by an explicit edit) or the source files it measures.
// Tests: TestArchitectureFileLineBudget.
public static partial class SelfTest
{
    /// <summary>The budget file, beside the app project.</summary>
    private const string ArchBudgetFile = "ArchitectureBudget.json";

    /// <summary>The hard cap used when the budget file does not set <c>maxFileLines</c>.</summary>
    private const int ArchDefaultMaxFileLines = 800;

    /// <summary>The size goal: files between this and the hard cap pass but are listed as info.</summary>
    private const int ArchGoalFileLines = 500;

    /// <summary>How many offenders a failing check names before it says "+N more".</summary>
    private const int ArchReportedOffenders = 8;

    /// <summary>The projects whose C# files are measured.</summary>
    private static readonly string[] ArchSourceProjects =
        { "Source/src/TabForge.Avalonia", "Source/src/TabForge.Core", "Source/src/TabForge.AudioEngine", "Source/src/TabForge.Audio.Contracts" };

    private static readonly JsonSerializerOptions ArchJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The file-size guard: every C# file over the line cap is listed in <c>fileLineExceptions</c> with its line count, and may not grow; a file not
    /// listed may not pass the cap. A value above its budget fails; below it, a normal run logs the line to paste and a strict run
    /// (TABFORGE_ARCH_STRICT=1) fails, so the file always states what the code is. A budget is raised only by an explicit edit with a reason.
    /// TABFORGE_ARCH_RECORD=&lt;file&gt; writes the measured values as a new budget file instead of checking.
    /// </summary>
    private static void TestArchitectureFileLineBudget()
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "Source", "src", "TabForge.Avalonia", ArchBudgetFile);
        if (root is null || path is null || !File.Exists(path)) { Skip($"architecture file-size guard ({ArchBudgetFile})", "no source checkout / budget file found", "architecture"); return; }
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var cap = document.RootElement.TryGetProperty("maxFileLines", out var capElement) && capElement.ValueKind == JsonValueKind.Number ? capElement.GetInt32() : ArchDefaultMaxFileLines;
        var budget = new SortedDictionary<string, long>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("fileLineExceptions", out var exceptions) && exceptions.ValueKind == JsonValueKind.Object)
            foreach (var property in exceptions.EnumerateObject()) budget[property.Name] = property.Value.GetInt64();
        var overGoal = ArchFilesOverCap(root, ArchGoalFileLines);
        var measured = new SortedDictionary<string, long>(overGoal.Where(kv => kv.Value > cap).ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal);

        var record = Environment.GetEnvironmentVariable("TABFORGE_ARCH_RECORD");
        if (!string.IsNullOrWhiteSpace(record))
        {
            File.WriteAllText(record, ArchRecord(cap, measured));
            Check($"architecture budget recorded to the file named by TABFORGE_ARCH_RECORD ({measured.Count} files over {cap} lines)", true);
            return;
        }

        var strictLower = Environment.GetEnvironmentVariable("TABFORGE_ARCH_STRICT") is "1" or "true" or "strict-lower" or "lower";
        var rises = new List<string>();
        var falls = new List<string>();
        foreach (var (file, lines) in measured)
        {
            var allowed = budget.GetValueOrDefault(file, cap);
            if (lines > allowed) rises.Add($"{file}: {lines} > budget {allowed}");
            else if (budget.ContainsKey(file) && lines < allowed) falls.Add($"{ArchQuote(file)}: {lines}");
        }
        foreach (var file in budget.Keys.Where(f => !measured.ContainsKey(f))) falls.Add($"remove {ArchQuote(file)}");
        Check($"file size: no source file is over {cap} lines or above its budget in fileLineExceptions ({measured.Count} measured, {budget.Count} budgeted)", rises.Count == 0,
            ArchOffenders(rises) + "  (raising a budget needs an explicit edit with a reason)");
        var hint = "lower the budget: " + ArchOffenders(falls, " | ");
        Check($"file size: fileLineExceptions is as tight as the code{(strictLower ? " (strict-lower)" : "")}", !strictLower || falls.Count == 0, hint);
        if (!strictLower && falls.Count > 0) Log.Add($"  info  file size: {hint}");
        var aboveGoal = overGoal.Where(kv => kv.Value <= cap).Select(kv => $"{kv.Key}: {kv.Value}").ToList();
        if (aboveGoal.Count > 0) Log.Add($"  info  file size: {aboveGoal.Count} files are over the {ArchGoalFileLines}-line goal (hard max {cap}): {ArchOffenders(aboveGoal, " | ")}");
    }

    private static string ArchQuote(string text) => JsonSerializer.Serialize(text, ArchJson);

    private static string ArchOffenders(IReadOnlyList<string> items, string separator = "; ") =>
        string.Join(separator, items.Take(ArchReportedOffenders)) + (items.Count > ArchReportedOffenders ? $"{separator}+{items.Count - ArchReportedOffenders} more" : "");

    /// <summary>The C# files (relative path with forward slashes) with more than <paramref name="cap"/> lines, and their line counts. Build output is skipped.</summary>
    private static SortedDictionary<string, long> ArchFilesOverCap(string root, int cap)
    {
        var over = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var project in ArchSourceProjects)
        {
            var directory = Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var inner = relative[(project.Length + 1)..];
                if (inner.StartsWith("obj/", StringComparison.Ordinal) || inner.StartsWith("bin/", StringComparison.Ordinal)
                    || inner.Contains("/obj/", StringComparison.Ordinal) || inner.Contains("/bin/", StringComparison.Ordinal)) continue;
                var lines = File.ReadLines(file).LongCount();
                if (lines > cap) over[relative] = lines;
            }
        }
        return over;
    }

    private static string ArchRecord(int cap, SortedDictionary<string, long> measured)
    {
        var entries = measured.Select(kv => $"    {ArchQuote(kv.Key)}: {kv.Value}");
        var sb = new StringBuilder();
        sb.Append("{\n  \"version\": 1,\n  \"maxFileLines\": ").Append(cap).Append(",\n  \"fileLineExceptions\": {\n")
          .Append(string.Join(",\n", entries)).Append("\n  }\n}\n");
        return sb.ToString();
    }
}
