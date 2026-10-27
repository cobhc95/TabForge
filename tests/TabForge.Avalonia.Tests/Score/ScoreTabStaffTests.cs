using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Testing;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof that the default score draws the TAB staff (one horizontal line per string) under the notation staff.
/// Does not own: the drawing itself (Core ScoreRenderer) or the notation menu (MainWindowNotationMenuTests).
/// Tests: ScoreGeometryProbe, ScoreRenderer.
/// </summary>
public class ScoreTabStaffTests
{
    private const double LineTolerance = 0.01;

    [AvaloniaFact]
    public void DefaultScoreDrawsEveryTabStringLineUnderTheStaff()
    {
        var song = DemoSongFactory.Create();
        var draw = new AvaloniaDrawContext();
        var probe = new ScoreGeometryProbe(song, draw);
        List<DrawRecord> records;
        using (var bitmap = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96)))
        {
            using var dc = bitmap.CreateDrawingContext();
            records = probe.Draw(draw.Use(dc), 4);
        }

        var strings = song.Tracks[0].StringTunings.Count;
        var tabTop = probe.TabTop(0);
        var stringLines = records.Count(r => r.Kind == "Line" && Math.Abs(r.Values[1] - r.Values[3]) < LineTolerance
            && Enumerable.Range(0, strings).Any(s => Math.Abs(r.Values[1] - (tabTop + s * probe.StringGap)) < LineTolerance));
        Assert.Equal(strings, stringLines);
    }
}
