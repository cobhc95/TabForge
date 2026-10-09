using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Band;

namespace TabForge;

/// <summary>Band lanes of different widths (one row's instrument hidden) share the start bar and the playhead's place.</summary>
public static partial class SelfTest
{
    private static void TestBandWideLaneSync()
    {
        foreach (var layout in new[] { BandChoices.Horizontal, BandChoices.Vertical })
        {
            var project = BandUnevenSong(80);
            var host = new FakeBandHost(project);
            host.Settings.Timeline.Band = new BandSettings { LaneLayout = layout };
            using var band = new BandViewController(host);
            BandStage(band, 1000, 900);
            band.Tick();
            band.ToggleInstrumentOf(0);
            BandStage(band, 1000, 900);
            var lanes = band.View.Rows.Select(r => r.Lane).ToList();
            var bad = "";
            Check(layout + " wide lane: the rows have different lane widths", Math.Abs(lanes[0].ActualWidth - lanes[1].ActualWidth) > 50, lanes[0].ActualWidth.ToString("0") + " vs " + lanes[1].ActualWidth.ToString("0"));
            foreach (var bar in new[] { 10, 33, 47, 70 })
            {
                band.Apply(bar, 0.4, bar * 2000.0, true, false);
                BandStage(band, 1000, 900);
                var a = lanes[0]; var b = lanes[1];
                if (layout == BandChoices.Horizontal) { if (Math.Abs(a.Offset - b.Offset) > 0.5 || Math.Abs(a.PlayheadAtLane.X - b.PlayheadAtLane.X) > 120) bad += $"bar {bar}: offset {a.Offset}/{b.Offset} x {a.PlayheadAtLane.X}/{b.PlayheadAtLane.X}; "; }
                else if (Math.Abs(a.PlayheadAtLane.Y - b.PlayheadAtLane.Y) > 1 || !a.PlayheadVisible || !b.PlayheadVisible) bad += $"bar {bar}: y {a.PlayheadAtLane.Y}/{b.PlayheadAtLane.Y}; ";
            }
            Check(layout + " wide lane: same first bar and playhead place whatever the lane width", bad == "", bad);
        }
    }
}
