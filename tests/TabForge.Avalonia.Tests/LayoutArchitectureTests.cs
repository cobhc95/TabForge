using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the layout token guard: every literal thickness, padding, margin and popup or window placement
//   in Source/src must come from the metrics entry points, with only the listed owner files exempt.
//   Counts per file may only shrink, via the layout section of AllowedViolations.json.
// Does not own: the metrics entry points themselves, product layout or the other two rule sections.
// Tests: LayoutArchitectureTests (this file).
public sealed class LayoutArchitectureTests
{
    private static readonly Regex Pattern = new(
        @"new Thickness\(|\bPadding\s*=(?!=)|\bMargin\s*=(?!=)|ShowAt|Placement\s*=(?!=)|\bPosition\s*=(?!=)|ScreenFrom|Screens\.Primary|CenterOwner|CenterScreen",
        RegexOptions.Compiled);

    private static readonly HashSet<string> OwnerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "UiTokens.cs",
        "CaptureMetrics.cs",
        "UiStrokes.cs",
        "UiFonts.cs",
        "UiDensity.cs",
        "UiMetrics.cs",
        "DialogMetrics.cs",
        "ChromeMetrics.cs",
        "AvaloniaFormControlStyles.cs",
        "AvaloniaFormControlStyles.Combo.cs",
        "MainWindowPlacement.cs",
        "WindowUiScale.cs",
        "WindowMetrics.cs",
    };

    private static bool IsExcluded(string path) =>
        ArchitectureScan.HasPathSegment(path, "SelfTests") || IsOwnerFile(path);

    private static bool IsOwnerFile(string path) =>
        Path.GetFileName(path) is string name && OwnerFiles.Contains(name);

    [Fact]
    public void LayoutMetricsOnlyFromTokens()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo, IsExcluded);
        Assert.True(files.Count > 100, $"layout architecture guard scanned no source files from {repo}");
        ArchitectureScan.ScanCounts(repo, files, Pattern, stripStrings: true, f => !IsOwnerFile(f), out var counts, out var lines);
        ArchitectureScan.AssertJsonSectionRatchet("layout", counts, lines,
            Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "AllowedViolations.json"),
            "TABFORGE_ARCH_SEED", "LayoutTokens");
    }
}
