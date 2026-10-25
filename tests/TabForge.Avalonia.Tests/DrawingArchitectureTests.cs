using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the drawing token guard: every literal stroke width, pen width, font size, font family and
//   window size in Source/src must come from the stroke, font and window-metrics entry points, with
//   only the listed owner files exempt. Counts per file may only shrink, via AllowedViolations.json.
// Does not own: the stroke, font and metrics entry points themselves or the other two rule sections.
// Tests: DrawingArchitectureTests (this file).
public sealed class DrawingArchitectureTests
{
    private static readonly Regex Pattern = new(
        @"StrokeThickness\s*=\s*\d|Pen\(.*,\s*\d(\.\d+)?\)|FontSize\s*=\s*\d|FontFamily\s*=\s*""|(?:Min|Max)?(?:Width|Height)\s*=\s*\d{2,4}|Window\(.*\d{3,4}",
        RegexOptions.Compiled);

    private static readonly HashSet<string> OwnerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "UiTokens.cs",
        "CaptureMetrics.cs",
        "UiStrokes.cs",
        "UiFonts.cs",
        "UiDensity.cs",
        "UiMetrics.cs",
        "ScoreText.cs",
        "ChromeMetrics.cs",
        "DialogMetrics.cs",
        "WindowMetrics.cs",
        "VideoViewSpec.cs",
        "ScorePdfPagination.cs",
    };

    private static bool IsExcluded(string path) =>
        ArchitectureScan.HasPathSegment(path, "SelfTests") || IsOwnerFile(path);

    private static bool IsOwnerFile(string path) =>
        Path.GetFileName(path) is string name && OwnerFiles.Contains(name);

    [Fact]
    public void DrawingSizesOnlyFromTokens()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo, IsExcluded);
        Assert.True(files.Count > 100, $"drawing architecture guard scanned no source files from {repo}");
        ArchitectureScan.ScanCounts(repo, files, Pattern, stripStrings: true, f => !IsOwnerFile(f), out var counts, out var lines);
        ArchitectureScan.AssertJsonSectionRatchet("drawing", counts, lines,
            Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "AllowedViolations.json"),
            "TABFORGE_ARCH_SEED", "DrawingTokens");
    }
}
