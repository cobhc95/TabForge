using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: rendering every parity.capture.json shot that has an Avalonia twin (ParityShotMap) to &lt;A/B root&gt;\avalonia\&lt;shot&gt;.png in the
/// shot's theme and size, and writing &lt;A/B root&gt;\missing.txt with the reason for each shot without one. The A/B root is TF_AB_OUT.
/// Does not own: the twin choices and reasons (ParityShotMap), the pixel comparison (tools/ab/compare.py).
/// Tests: this class. It only renders: a pixel difference never fails it, a missing twin or a failed render does.
/// </summary>
public class ParityShotsTests
{
    /// <summary>
    /// The A/B output root: TF_AB_OUT when tools/ab/run.ps1 sets it (one folder per run), otherwise a folder of this test process.
    /// Key: the environment value, read on each call. Ceiling: none, the caller owns the folder. Thread: any.
    /// </summary>
    private static string AbRoot =>
        Environment.GetEnvironmentVariable("TF_AB_OUT") is { Length: > 0 } fromRun ? fromRun : Path.Combine(DialogTestKit.PngDirectory, "tf-ab");

    public static string OutputDirectory => Path.Combine(AbRoot, "avalonia");

    public static string MissingListPath => Path.Combine(AbRoot, "missing.txt");

    public static IEnumerable<object[]> TwinShots() =>
        ManifestShots().Where(shot => ParityShotMap.Entries.TryGetValue(BaseName(shot), out var entry) && entry.Build is not null)
            .Select(shot => new object[] { shot });

    [AvaloniaTheory]
    [MemberData(nameof(TwinShots))]
    public void RendersTwinInTheShotThemeAndSize(string shot)
    {
        using var theme = DialogTestKit.UseTheme(shot.EndsWith("-light", StringComparison.Ordinal));
        var capture = ParityShotMap.Entries[BaseName(shot)].Build!();
        var window = capture.Window;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var part = capture.Part?.Invoke(window);
            Directory.CreateDirectory(OutputDirectory);
            var path = Path.Combine(OutputDirectory, shot + ".png");
            if (part is null) SaveWindow(window, path);
            else SavePart(part, path);
            Assert.True(new FileInfo(path).Length > 0, $"{shot} rendered an empty file");
        }
        finally
        {
            // Closed on failure too, so a failed render does not leave its window open for the rest of the class.
            window.Close();
        }
    }

    [Fact]
    public void EveryManifestShotHasAMappingAndMissingShotsAreListed()
    {
        var missing = new List<string>();
        foreach (var shot in ManifestShots())
        {
            Assert.True(ParityShotMap.Entries.ContainsKey(BaseName(shot)), $"no ParityShotMap entry for {shot}");
            var entry = ParityShotMap.Entries[BaseName(shot)];
            if (entry.Build is null) missing.Add($"{shot}\t{entry.Note}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(MissingListPath)!);
        File.WriteAllLines(MissingListPath, missing);
    }

    /// <summary>The shot names in tests/parity/parity.capture.json, in manifest order.</summary>
    public static IReadOnlyList<string> ManifestShots()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindManifest()));
        return document.RootElement.EnumerateArray()
            .Where(step => step.TryGetProperty("shot", out _))
            .Select(step => step.GetProperty("shot").GetString() ?? throw new InvalidDataException("A shot entry has no name."))
            .ToList();
    }

    /// <summary>The shot name without its theme suffix: win-Mixer-dark gives win-Mixer.</summary>
    public static string BaseName(string shot) => shot[..shot.LastIndexOf('-')];

    private static string FindManifest() => Path.Combine(TestRepo.CheckoutRoot(), "Source", "tests", "parity", "parity.capture.json");

    private static void SaveWindow(Window window, string path)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
        frame.Save(path);
    }

    /// <summary>Renders one pane at 96 DPI, cropped to its own bounds, so a shot shows the pane and not the whole window.</summary>
    private static void SavePart(Control part, string path)
    {
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(part.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(part.Bounds.Height)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(part);
        bitmap.Save(path);
    }
}
