using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the shape and footer-button guard: a literal corner radius (new CornerRadius(4)) must name a ChromeMetrics radius or a metrics
//   token, and a hand-built default (OK / Apply) button must come from DialogButtons or PromptChrome instead. Counts per file may only shrink,
//   via the shape section of AllowedViolations.json (seed with TABFORGE_ARCH_SEED=1).
// Does not own: the radii and button factories themselves or the theme, drawing and layout sections.
// Tests: ShapeArchitectureTests (this file).
public sealed class ShapeArchitectureTests
{
    private static readonly Regex Pattern = new(
        @"new CornerRadius\(\d|IsDefault\s*=\s*true",
        RegexOptions.Compiled);

    private static readonly HashSet<string> OwnerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ChromeMetrics.cs",
        "DialogMetrics.cs",
        "DialogButtons.cs",
        "PromptChrome.cs",
        "UiMetrics.cs",
        "WindowMetrics.cs",
    };

    private static bool IsExcluded(string path) =>
        ArchitectureScan.HasPathSegment(path, "SelfTests") || IsOwnerFile(path);

    private static bool IsOwnerFile(string path) =>
        Path.GetFileName(path) is string name && OwnerFiles.Contains(name);

    [Fact]
    public void CornerRadiiAndDefaultButtonsOnlyFromTokens()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo, IsExcluded);
        Assert.True(files.Count > 100, $"shape architecture guard scanned no source files from {repo}");
        ArchitectureScan.ScanCounts(repo, files, Pattern, stripStrings: true, f => !IsOwnerFile(f), out var counts, out var lines);
        ArchitectureScan.AssertJsonSectionRatchet("shape", counts, lines,
            Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "AllowedViolations.json"),
            "TABFORGE_ARCH_SEED", "ShapeTokens");
    }
}
