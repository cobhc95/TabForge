using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the portability ratchet (cross-platform Stage 0): no new Windows-only call (P/Invoke, COM,
//   Microsoft.Win32, System.Management, NAudio WASAPI/ASIO/DirectSound/Media Foundation/CoreAudioApi, HWND, Windows folders, Windows shell
//   programs, '/' to '\' rewrites, hard-coded Ctrl) outside the platform folders. Per-file shrink-only baseline in PortabilityBaseline.txt. Also: the portable
//   <Compile Remove> lists of Core and Contracts name existing files (drift alarm).
// Does not own: the code it counts, or the net10.0 Core build (the windows-ci "Core portable build" step).
// Tests: itself.
public sealed class PortabilityGuardTests
{
    private const string WriteBaselineEnvVar = "TABFORGE_ARCH_WRITE_BASELINE";

    // Comments are stripped, strings are kept: "HWND", "user32.dll" and the shell program names live in string literals.
    private static readonly Regex WindowsOnlyPattern = new(
        @"\b(?:DllImport|LibraryImport)\b"
        + @"|\bComImport\b"
        + @"|\bMicrosoft\.Win32\b"
        + @"|\bSystem\.Management\b"
        + @"|\bNAudio\.(?:Wasapi|Asio|CoreAudioApi|MediaFoundation|Wave\.Asio)\b"
        + @"|\b(?:WasapiOut|WasapiCapture|AsioOut|DirectSoundOut|MMDeviceEnumerator|MediaFoundationReader)\b"
        + @"|\bTryGetPlatformHandle\b|""HWND"""
        + @"|\bSpecialFolder\.(?:Windows|ProgramFiles\w*)\b"
        + @"|\.Replace\(\s*'/'\s*,\s*'\\'\s*\)"
        + @"|""(?:notepad|explorer|cmd)\.exe"""
        + @"|\bKeyModifiers\.Control\b"
        // Stage 2: user folders only through IAppFolders (AppFolders.cs reads them), the shell only through IShellLauncher, fonts only through the font tables.
        + @"|\bSpecialFolder\.(?:ApplicationData|LocalApplicationData|CommonApplicationData|MyMusic|MyVideos|MyDocuments|Personal)\b"
        + @"|\bUseShellExecute\s*=\s*true\b"
        + @"|""(?:Consolas|Segoe UI[^""]*)""",
        RegexOptions.Compiled);

    // Windows-only code belongs here (later behind ports); everything else may only lose hits.
    private static readonly string[] AllowedFolders =
    {
        "/Platform/Windows/", "/Platform/Linux/", "/Shell/Win32/", "/TabForge.AudioEngine/Output/", "/TabForge.AudioEngine/Editors/",
    };

    [Fact]
    public void WindowsOnlyApisOnlyShrinkOutsidePlatformFolders()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo);
        Assert.True(files.Count > 100, $"Portability guard scanned no source files from {repo}");
        ArchitectureScan.ScanCounts(repo, files, WindowsOnlyPattern, stripStrings: false, f => !InAllowedFolder(repo, f), out var counts, out var lines);
        ArchitectureScan.AssertCountRatchet(counts, lines,
            Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "PortabilityBaseline.txt"), WriteBaselineEnvVar, "Portability");
    }

    [Fact]
    public void PatternCatchesEachWindowsOnlyForm()
    {
        string[] hits =
        {
            "[DllImport(\"user32.dll\")]", "[LibraryImport(\"x\")]", "[ComImport]", "using Microsoft.Win32;", "using System.Management;",
            "using NAudio.CoreAudioApi;", "new WasapiOut()", "new AsioOut(name)", "TryGetPlatformHandle()", "\"HWND\"",
            "Environment.SpecialFolder.ProgramFilesX86", "p.Replace('/', '\\')", "\"explorer.exe\"", "KeyModifiers.Control",
            "Environment.SpecialFolder.LocalApplicationData", "Environment.SpecialFolder.MyMusic", "UseShellExecute = true", "\"Consolas\"", "\"Segoe UI Symbol\"",
        };
        foreach (var text in hits)
            Assert.Matches(WindowsOnlyPattern, text);
        Assert.DoesNotMatch(WindowsOnlyPattern, "Path.Combine(a, b); KeyModifiers.Shift; UseShellExecute = false; AppFolders.Current.Settings");
    }

    [Theory]
    [InlineData("TabForge.Core")]
    [InlineData("TabForge.Audio.Contracts")]
    public void PortableCompileRemovesNameExistingFiles(string project)
    {
        var dir = Path.Combine(ArchitectureScan.RepoRoot(), "Source", "src", project);
        var csproj = File.ReadAllText(Path.Combine(dir, project + ".csproj"));
        var missing = new List<string>();
        foreach (Match m in Regex.Matches(csproj, @"<Compile Remove=""([^""]+)"""))
        {
            foreach (var entry in m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var rel = entry.Replace('\\', Path.DirectorySeparatorChar);
                var folder = Path.Combine(dir, Path.GetDirectoryName(rel) ?? "");
                if (!Directory.Exists(folder) || Directory.GetFiles(folder, Path.GetFileName(rel)).Length == 0)
                    missing.Add(entry);
            }
        }
        Assert.True(missing.Count == 0, $"{project}.csproj excludes files that do not exist (stale portable list): " + string.Join(", ", missing));
    }

    // Platform adapter files by name: WindowsXxx.cs, Xxx.Windows.cs (the Windows half of a partial class) PrimaryModifier.cs (the one place that maps the primary key) and AppFolders.cs (the one place that reads the special folders).
    private static bool IsWindowsAdapterFile(string file)
    {
        var name = Path.GetFileName(file);
        return name.StartsWith("Windows", StringComparison.Ordinal) || name.EndsWith(".Windows.cs", StringComparison.Ordinal)
            || name.Equals("PrimaryModifier.cs", StringComparison.Ordinal) || name.Equals("AppFolders.cs", StringComparison.Ordinal);
    }

    private static bool InAllowedFolder(string repo, string file)
    {
        if (IsWindowsAdapterFile(file)) return true;
        var rel = "/" + ArchitectureScan.Relative(repo, file);
        foreach (var folder in AllowedFolders)
        {
            if (rel.Contains(folder, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
