using System.Collections.Generic;
using System.Linq;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Views.Meters;
using TabForge.Platform;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the horizontal stereo bars of the track meter: two bars stacked (left above right), filled left to right, clip end mark.
/// Tests: MeterBarPainter.DrawBarsHorizontal.
/// </summary>
public class TrackMeterHorizontalTests
{
    private sealed class Recorder : NullDrawContext
    {
        public readonly List<(UiColor? Fill, double X, double Y, double W, double H)> Rects = new();
        public override void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) => Rects.Add((fill, x, y, width, height));
    }

    private static readonly UiColor Track = new(1, 1, 1, 255), Green = new(2, 2, 2, 255), Hot = new(3, 3, 3, 255), Danger = new(4, 4, 4, 255), Text = new(5, 5, 5, 255);

    private static UiColor Ink(string key) => key switch { "TrackSliderTrack" => Track, "Meter" => Green, "MeterHot" => Hot, "Danger" => Danger, _ => Text };

    private static Recorder Draw(float left, float right, int clips)
    {
        var model = new MasterMeterModel(() => new MasterMeterSettings());
        model.Feed(new MasterMeterReading(left, right, 0, 0, 0, 0), 0);
        if (clips > 0) model.Feed(new MasterMeterReading(left, right, 0, 0, 0, clips), 10);
        var d = new Recorder();
        MeterBarPainter.DrawBarsHorizontal(d, Ink, false, model, 2, 5, ChromeMetrics.TrackMeterBarsWidth, ChromeMetrics.TrackMeterBarHeight, ChromeMetrics.TrackMeterGap,
            ChromeMetrics.TrackMeterCorner, ChromeMetrics.TrackMeterHoldHeight, ChromeMetrics.TrackMeterClipMark);
        return d;
    }

    [Fact]
    public void BarsAreStackedLeftAboveRightAndSmall()
    {
        var tracks = Draw(0.5f, 0.5f, 0).Rects.Where(r => r.Fill == Track).ToList();
        Assert.Equal(2, tracks.Count);
        Assert.True(tracks[0].Y < tracks[1].Y);
        Assert.Equal(tracks[0].X, tracks[1].X);
        Assert.Equal(ChromeMetrics.TrackMeterBarHeight, tracks[0].H);
        Assert.Equal(tracks[0].Y + ChromeMetrics.TrackMeterBarHeight + ChromeMetrics.TrackMeterGap, tracks[1].Y);
        Assert.True(tracks[0].W > tracks[0].H);
        Assert.True(2 * ChromeMetrics.TrackMeterBarHeight + ChromeMetrics.TrackMeterGap <= ChromeMetrics.TrackMeterHeight);
    }

    [Fact]
    public void BarsFillProportionallyToTheLevel()
    {
        var loud = Draw(1f, 0.1f, 0).Rects.Where(r => r.Fill == Green || r.Fill == Hot).ToList();
        var top = loud.Where(r => r.Y < 5 + ChromeMetrics.TrackMeterBarHeight).Sum(r => r.W);
        var bottom = loud.Where(r => r.Y >= 5 + ChromeMetrics.TrackMeterBarHeight).Sum(r => r.W);
        Assert.Equal(ChromeMetrics.TrackMeterBarsWidth, top, 3);
        Assert.True(bottom > 0 && bottom < top);
        Assert.DoesNotContain(Draw(0f, 0f, 0).Rects, r => r.Fill == Green || r.Fill == Hot);
    }

    [Fact]
    public void ClipMarkSitsAtTheRightEndOfBothBarsOnlyWhenLit()
    {
        Assert.DoesNotContain(Draw(0.5f, 0.5f, 0).Rects, r => r.Fill == Danger);
        var marks = Draw(0.5f, 0.5f, 3).Rects.Where(r => r.Fill == Danger).ToList();
        Assert.Equal(2, marks.Count);
        Assert.All(marks, m => Assert.Equal(2 + ChromeMetrics.TrackMeterBarsWidth - ChromeMetrics.TrackMeterClipMark, m.X));
    }
}
