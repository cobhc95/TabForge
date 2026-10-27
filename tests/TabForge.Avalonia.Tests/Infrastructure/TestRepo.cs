using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: where the checkout is on this machine and which file is the demo song in it (the Samples folder's *.gp, "Ashen Meridian" first).
/// Does not own: what the tests check with those files (each test class).
/// Tests: ArchitectureScan, DocTruthTests, ParityShotsTests, HotkeyTipTests, RenderParityTests, VideoExportParityTests.
/// </summary>
internal static class TestRepo
{
    /// <summary>The checkout root (the folder above Source\TabForge.sln). TABFORGE_REPO names it when set; otherwise the first folder that holds
    /// Source\TabForge.sln, walking up from the test binaries, then from the working directory, then from the folder this project was built from (binaries may be built elsewhere).</summary>
    public static string CheckoutRoot()
    {
        var builtFrom = typeof(TestRepo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "TabForgeCheckout")?.Value;
        var named = Environment.GetEnvironmentVariable("TABFORGE_REPO");
        if (!string.IsNullOrWhiteSpace(named) && !File.Exists(Path.Combine(named, "Source", "TabForge.sln")))
            throw new InvalidOperationException($"TABFORGE_REPO is '{named}', which has no Source/TabForge.sln: set it to the checkout root.");
        foreach (var start in new[] { Environment.GetEnvironmentVariable("TABFORGE_REPO"), AppContext.BaseDirectory, Directory.GetCurrentDirectory(), builtFrom })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Source", "TabForge.sln"))) return dir.FullName;
        }
        throw new InvalidOperationException("The checkout root (Source/TabForge.sln) was not found from TABFORGE_REPO, the test binaries or the working directory.");
    }

    /// <summary>The demo song: the checkout's Samples/Ashen Meridian.gp when present, else its first .gp by name. A missing song gives the path
    /// "Samples/Ashen Meridian.gp" in the root, which does not exist, so the caller's existence check reports it.</summary>
    public static string DemoSong()
    {
        var root = CheckoutRoot();
        var samples = Path.Combine(root, "Samples");
        if (!Directory.Exists(samples)) throw new InvalidOperationException($"The checkout has no Samples folder at '{samples}'.");
        var songs = Directory.GetFiles(samples, "*.gp").OrderBy(s => Path.GetFileName(s), StringComparer.Ordinal).ToList();
        return songs.FirstOrDefault(s => Path.GetFileName(s) == "Ashen Meridian.gp") ?? songs.FirstOrDefault() ?? Path.Combine(root, "Samples", "Ashen Meridian.gp");
    }
}
