using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the H11-icon preload: the three icon sets load on the UI thread (IsReady / Preload /
/// IconArtPreloader), Draw never throws with or without art (placeholder until ready), and unknown keys stay empty.
/// Does not own: the art itself (TrackIconArt, ToolIconArt, TransportIconArt) or any paint-pixel assertion.
/// Tests: this class.
/// </summary>
public class IconArtPreloadTests
{
    [AvaloniaFact]
    public void PreloadOnUiThreadMakesAllSetsReady()
    {
        IconArtPreloader.Preload();
        Assert.True(TrackIconArt.IsReady);
        Assert.True(ToolIconArt.IsReady);
        Assert.True(TransportIconArt.IsReady);
        Assert.True(IconArtPreloader.IsReady);
    }

    [AvaloniaFact]
    public void PreloadIsIdempotentAndKnownKeysExist()
    {
        TrackIconArt.Preload();
        TransportIconArt.Preload();
        ToolIconArt.PreloadAll();
        TrackIconArt.Preload();
        TransportIconArt.Preload();
        ToolIconArt.PreloadAll();
        Assert.True(TrackIconArt.IsReady);
        Assert.True(ToolIconArt.IsReady);
        Assert.True(TransportIconArt.IsReady);
        Assert.True(TrackIconArt.Has(TrackIconKeys.AudioKey));
        Assert.False(TrackIconArt.Has("no such icon"));
        Assert.False(TransportIconArt.Has("no such icon"));
    }

    [AvaloniaFact]
    public void DrawNeverThrowsWithOrWithoutArt()
    {
        var d = new NullDrawContext();
        var ink = new UiColor(255, 10, 20, 30);
        var accent = new UiColor(255, 200, 100, 0);
        // Unknown keys are placeholders (no art, no throw) both before and after the preload.
        TrackIconArt.Draw(d, "no such icon", 0, 0, 16, ink, accent);
        TransportIconArt.Draw(d, "no such icon", 0, 0, 16, light: false);
        ToolIconArt.Draw(d, "no/such/icon", 0, 0, 16, ink, light: false);

        TrackIconArt.Preload();
        TransportIconArt.Preload();
        ToolIconArt.PreloadAll();
        TrackIconArt.Draw(d, TrackIconKeys.AudioKey, 0, 0, 16, ink, accent);
        foreach (var name in ToolIconNames().Take(3)) ToolIconArt.Draw(d, name, 0, 0, 16, ink, light: false);
    }

    private static System.Collections.Generic.IEnumerable<string> ToolIconNames()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons");
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root, "*.svg", SearchOption.AllDirectories)
            .Select(file =>
            {
                var name = Path.GetRelativePath(root, file);
                return name[..^".svg".Length].Replace(Path.DirectorySeparatorChar, '/');
            }).ToList();
    }
}
