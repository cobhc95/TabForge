using System.Windows;
using System.Windows.Controls;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

/// <summary>
/// One visual scale for the score and the instrument pane: "Fit width" sizes the score from the window, and the fretboard
/// never draws its numbers much larger than the score's tab digits, however tall or wide its pane is.
/// </summary>
public static partial class SelfTest
{
    private static void TestScoreScaleMatchesFretboard()
    {
        Check("score scale: fit width is 100% on a small or medium window", ScoreZoomController.FitZoom(900) == 1.0 && ScoreZoomController.FitZoom(1500) == 1.0);
        Check("score scale: fit width grows on a large window", ScoreZoomController.FitZoom(2000) >= 1.2, $"{ScoreZoomController.FitZoom(2000)}");
        Check("score scale: fit width stops at 150%", ScoreZoomController.FitZoom(5000) == 1.5);

        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var state = InstrumentVisualizer.Build(p, p.Tracks[0], tl, 0, false, false, 4, false, false, null);
        state.NumberScale = FretNumberSizes.Scale(FretNumberSizes.Large);
        using var alive = KeepAlive();
        // Window widths 1280 / 1920 / 2560: the score viewport and the fretboard pane are about the window minus the side panel.
        foreach (var (window, paneHeight) in new[] { (1280.0, 220.0), (1920.0, 600.0), (2560.0, 900.0) })
        {
            var viewport = window - 420;
            foreach (var zoom in new[] { ScoreZoomController.FitZoom(viewport), 1.5 })
            {
                var panel = new InstrumentPanel { ContentScale = zoom };
                var host = new Border { Width = viewport, Height = paneHeight, Child = panel };
                host.Measure(new Size(viewport, paneHeight)); host.Arrange(new Rect(0, 0, viewport, paneHeight));
                panel.SetState(state);
                host.UpdateLayout(); PumpUi();
                var fretPx = 13.5 * state.NumberScale * panel.DrawScale;
                var tabPx = 11.0 * zoom;
                var ratio = fretPx / tabPx;
                var tag = $"{window:0} wide, pane {paneHeight:0}, zoom {zoom:0.0#}";
                Log.Add($"  info  score scale {tag}: draw scale {panel.DrawScale:0.00}, fret numbers {fretPx:0.0}px, tab digits {tabPx:0.0}px, max pane {panel.MaximumHeight:0}");
                // Small panes may draw below the score's size (MinScale keeps it readable); never far above it.
                Check($"score scale {tag}: fret numbers stay in the score's size family", ratio is > 0.6 and < 1.3, $"{ratio:0.00}");
                if (paneHeight >= 600)
                    Check($"score scale {tag}: a tall pane draws at the score's size, not larger", ratio is > 0.95 and < 1.3, $"{ratio:0.00}");
                Check($"score scale {tag}: the pane's maximum follows the scale", panel.MaximumHeight <= InstrumentPanel.MaximumPaneHeight(state, viewport, panel.NaturalHeight) + 0.5);
            }
        }
    }
}
