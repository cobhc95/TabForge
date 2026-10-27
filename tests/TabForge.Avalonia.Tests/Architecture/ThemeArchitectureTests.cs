using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the theme token guard (code, strings, dark-only lookups and look JSON): every hard-coded colour or opacity literal in Source/src must resolve
//   through the palette, the theme colours or the token entry points, with only the listed owner files
//   exempt. Counts per file may only shrink, via the theme section of AllowedViolations.json.
// Does not own: the token entry points themselves, product colours or the other two rule sections.
// Tests: ThemeArchitectureTests (this file).
public sealed class ThemeArchitectureTests
{
    private static readonly Regex Pattern = new(
        @"Color\.From(Rgb|Argb)|Color\.Parse|Brushes\.|Colors\.|#[0-9A-Fa-f]{6,8}|new UiColor\((255|0xFF),|WithOpacity\(0?\.|with\s*\{\s*A\s*=|A\s*=\s*(0x|90|46|85)",
        RegexOptions.Compiled);

    // What the first pattern cannot see because it blanks strings: a hex colour or a theme-blind colour name inside a string, the dark-only
    // one-argument palette lookup, a hard-coded variant (a positional true or false after the key), and a theme brush named White, Black or Gray.
    private static readonly Regex StringPattern = new(
        @"""#[0-9A-Fa-f]{6,8}""|Palette\.Get\(""\w+""\)|Get\([^(),]+,\s*(true|false)\)|(Palette\.Get|ink|Ink|Get)\(""(White|Black|Gray)""|DynamicResource(Extension)?\(""(White|Black|Gray)""",
        RegexOptions.Compiled);

    private static readonly Regex LookLiteralColour = new(
        @"""literal"":\s*""(#[0-9A-Fa-f]{6,8}|White|Black|Gray|Red|Transparent)""", RegexOptions.Compiled);

    // A hard-coded theme flag in a named argument. Every kept literal is reviewed here with its reason; the counts can only shrink.
    private static readonly Regex ThemeFlagLiteral = new(@"\blight:\s*(true|false)\b", RegexOptions.Compiled);

    private static readonly Dictionary<string, (int Count, string Reason)> AllowedThemeFlagLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HoverSurfaces.cs"] = (8, "derives the dark and the light table up front; the literal picks which table, not the theme on screen"),
        ["PreferencesCatalog.cs"] = (2, "the chosen mode names the preset to write, not the theme on screen"),
        ["TrackPillButton.cs"] = (1, "fallback key is a base token with one value for both themes"),
        ["KeyboardChromePainter.cs"] = (2, "White and Black are base tokens with one value for both themes"),
        ["SvgIcon.cs"] = (1, "Core icon ink master token, read in the light theme by design; has no view to ask"),
    };

    private static readonly HashSet<string> OwnerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Palette.cs",
        "ThemeColours.cs",
        "UiTokens.cs",
        "UiStrokes.cs",
        "UiFonts.cs",
        "UiDensity.cs",
        "KeyboardInk.cs",
        "AvaloniaPalette.cs",
        "WindowMetrics.cs",
        "UiMetrics.cs",
    };

    private static bool IsExcluded(string path) =>
        ArchitectureScan.HasPathSegment(path, "SelfTests") || IsOwnerFile(path);

    private static bool IsOwnerFile(string path) =>
        Path.GetFileName(path) is string name && OwnerFiles.Contains(name);

    [Fact]
    public void ThemeColoursOnlyFromTokens()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo, IsExcluded);
        Assert.True(files.Count > 100, $"theme architecture guard scanned no source files from {repo}");
        ArchitectureScan.ScanCounts(repo, files, Pattern, stripStrings: true, f => !IsOwnerFile(f), out var counts, out var lines);
        ArchitectureScan.AssertJsonSectionRatchet("theme", counts, lines,
            Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "AllowedViolations.json"),
            "TABFORGE_ARCH_SEED", "ThemeTokens");
    }

    [Fact]
    public void ThemeColourStringsAndDarkOnlyLookupsOnlyFromTokens()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo, IsExcluded);
        ArchitectureScan.ScanCounts(repo, files, StringPattern, stripStrings: false, f => !IsOwnerFile(f), out var counts, out var lines);
        ArchitectureScan.AssertJsonSectionRatchet("themeStrings", counts, lines,
            Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "AllowedViolations.json"),
            "TABFORGE_ARCH_SEED", "ThemeTokens");
    }

    [Fact]
    public void ThemeFlagLiteralsOnlyWhereReviewed()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo, IsExcluded);
        ArchitectureScan.ScanCounts(repo, files, ThemeFlagLiteral, stripStrings: true, f => !IsOwnerFile(f), out var counts, out _);
        var problems = new List<string>();
        foreach (var (file, count) in counts)
        {
            var name = Path.GetFileName(file);
            if (!AllowedThemeFlagLiterals.TryGetValue(name, out var allowed)) problems.Add($"{file}: {count} hard-coded light: true/false (read the theme the view shows, or add a reviewed reason)");
            else if (count > allowed.Count) problems.Add($"{file}: {count} literals, only {allowed.Count} allowed ({allowed.Reason})");
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void LooksNameBrushesNotColourLiterals()
    {
        var looks = Path.Combine(ArchitectureScan.RepoRoot(), "Source", "src", "TabForge.Core", "Platform", "Theme", "Looks");
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(looks, "*.json"))
            foreach (Match m in LookLiteralColour.Matches(File.ReadAllText(file)))
                hits.Add($"{Path.GetFileName(file)}: {m.Value}");
        Assert.True(hits.Count == 0, "a look names a theme brush, never a colour literal: " + string.Join("; ", hits));
    }
}
