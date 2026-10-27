using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Recording feature dependency guard: every using TabForge directive in a file under
//   Features/Recording (Core plus Avalonia) that points outside the allow-list is frozen in
//   RecordingFeatureBaseline.txt as file|namespace lines, and the set only shrinks. Seed or
//   re-seed the baseline with TABFORGE_ARCH_WRITE_BASELINE=1, then commit the result.
// Does not own: the product code, the allow-list itself (the Area 1 Recording pass owns which
//   namespaces the feature may reference) or the other architecture guards.
// Tests: RecordingFeatureGuardTests (this file).
public sealed class RecordingFeatureGuardTests
{
    private const string WriteBaselineEnvVar = "TABFORGE_ARCH_WRITE_BASELINE";

    private static readonly Regex TabForgeUsingPattern = new(
        @"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_]\w*\s*=\s*)?TabForge\.([A-Za-z_][\w\.]*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // The feature's own namespaces plus the shared layers it may reference: the read model, the
    // ports, the engine contracts and the Core platform tokens. Anything else is a baseline entry.
    private static readonly string[] AllowedPrefixes =
    {
        "TabForge.Features.Recording",
        "TabForge.Avalonia.Features.Recording",
        "TabForge.Models",
        "TabForge.Ports",
        "TabForge.Audio.Contracts",
        "TabForge.Platform",
    };

    private static string BaselinePath(string repo) =>
        Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "RecordingFeatureBaseline.txt");

    private static bool IsUnderRecordingFeature(string repo, string file)
    {
        var rel = ArchitectureScan.Relative(repo, file);
        return rel.StartsWith("Source/src/TabForge.Core/Features/Recording/", StringComparison.Ordinal)
            || rel.StartsWith("Source/src/TabForge.Avalonia/Features/Recording/", StringComparison.Ordinal);
    }

    private static bool IsAllowed(string ns) =>
        AllowedPrefixes.Any(p => ns.Equals(p, StringComparison.Ordinal) || ns.StartsWith(p + ".", StringComparison.Ordinal));

    [Fact]
    public void RecordingFeatureReferencesOnlyShrink()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo);
        Assert.True(files.Count > 100, "RecordingFeature guard scanned no source files from " + repo);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        int scanned = 0;
        foreach (var file in files)
        {
            if (!IsUnderRecordingFeature(repo, file))
                continue;
            scanned++;
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
            var rel = ArchitectureScan.Relative(repo, file);
            foreach (Match m in TabForgeUsingPattern.Matches(stripped))
            {
                var ns = "TabForge." + m.Groups[1].Value;
                if (!IsAllowed(ns))
                    actual.Add(rel + "|" + ns);
            }
        }
        Assert.True(scanned >= 10, $"RecordingFeature guard scanned too few feature files ({scanned}) from {repo}");
        ArchitectureScan.AssertSetRatchet(actual, BaselinePath(repo), WriteBaselineEnvVar, "RecordingFeature");
    }
}
