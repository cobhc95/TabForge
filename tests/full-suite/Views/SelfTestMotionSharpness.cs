using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Band;

namespace TabForge;

/// <summary>
/// Moving content stays crisp: the score scroll and caret, the timeline scroll and playhead, the Band lanes and a held Band row are drawn at
/// fractional offsets at four display scales (100% to 175%) and compared with a still picture moved by whole device pixels
/// (see MotionSharpnessProbe). The numbers go to the log as MOTION lines.
/// </summary>
public static partial class SelfTest
{
    private static readonly double[] MotionScales = { 1.0, 1.25, 1.5, 1.75 };
    private static readonly double[] MotionSteps = { 0.25, 0.5, 0.75, 1.3, 2.5, 7.37 };

    private static string F(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>The main window's score tree (scroller, centred page, editor) standing alone, so any display scale can be set on it.</summary>
    private static (ScrollViewer Scroll, Grid Page) MotionScoreRig(double scale, double viewW, double viewH, double pageW)
    {
        var editor = new TabEditorControl { Project = DemoSongFactory.Create(), SelectedTrackIndex = 0, Zoom = 1.0, PageWidthOverride = pageW };
        var grid = new Grid { Children = { editor } };
        var page = new Border { BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true, Child = grid };
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalContentAlignment = HorizontalAlignment.Center, Content = page };
        MotionSharpnessProbe.Stage(scroll, scale, viewW, viewH);
        return (scroll, grid);
    }

    private static void TestMotionSharpnessScore()
    {
        foreach (var scale in MotionScales)
            foreach (var vertical in new[] { true, false })
            {
                var (scroll, _) = MotionScoreRig(scale, 900, 500, vertical ? 880 : 1500);
                const double Start = 40;
                void Go(double o) { if (vertical) scroll.ScrollToVerticalOffset(o); else scroll.ScrollToHorizontalOffset(o); scroll.UpdateLayout(); }
                Go(Start);
                var still = MotionSharpnessProbe.Shoot(scroll, scale);
                var region = new Int32Rect(4, 4, still.PixelWidth - 8, still.PixelHeight - 8);
                double worstSharp = 1;
                foreach (var step in MotionSteps)
                {
                    Go(Start + step);
                    var shift = (int)(Math.Round((Start + step) * scale) - Math.Round(Start * scale));
                    var (_, sharp) = MotionSharpnessProbe.Best(MotionSharpnessProbe.Shoot(scroll, scale), still, vertical ? 0 : shift, vertical ? shift : 0, vertical, region);
                    worstSharp = Math.Min(worstSharp, sharp);
                }
                Check($"motion: the score scroll stays sharp ({(vertical ? "vertical" : "horizontal")}, x{scale:0.00})", worstSharp >= 0.98, F(worstSharp));
                Log.Add($"  MOTION score scroll {(vertical ? "vertical" : "horizontal")} x{scale:0.00}: worst sharp {F(worstSharp)}");
            }
    }

    private static void TestMotionSharpnessBand()
    {
        foreach (var scale in MotionScales)
            foreach (var vertical in new[] { false, true })
            {
                var host = new FakeBandHost(BandUnevenSong(80));
                host.Settings.Timeline.Band = new BandSettings { LaneLayout = vertical ? BandChoices.Vertical : BandChoices.Horizontal };
                host.Settings.Follow.Mode = FollowModes.Jump;
                using var band = new BandViewController(host);
                MotionSharpnessProbe.Stage(band.View, scale, 1000, 800);
                band.Tick();
                MotionSharpnessProbe.Stage(band.View, scale, 1000, 800);
                var lane = band.View.Rows[0].Lane;
                var line = VisualDescendants<Rectangle>(lane).First(r => r.Width == 2);
                var origin = lane.TransformToAncestor(band.View).Transform(new Point(0, 0));
                var region = new Int32Rect((int)(origin.X * scale) + 4, (int)(origin.Y * scale) + 4, (int)(lane.ActualWidth * scale) - 8, (int)(lane.ActualHeight * scale) - 8);
                double worstSharp = 1;
                int partial = 0, lineSeen = 0;
                BitmapSource? still = null;
                double stillSlide = 0;
                foreach (var step in new[] { 0.0, 1, 2, 3, 5, 8, 13 })
                {
                    // Horizontal: the strip stands on whole DIPs (what playback gives); vertical: a fractional glide between two systems.
                    var pos = vertical ? 1 + step * 0.037 : 300 + step;
                    lane.Place(20, 0.4, pos);
                    line.Visibility = Visibility.Collapsed;
                    MotionSharpnessProbe.Stage(band.View, scale, 1000, 800);
                    var shot = MotionSharpnessProbe.Shoot(band.View, scale);
                    var slide = vertical ? lane.SlideY : -pos;
                    if (still is null) { still = shot; stillSlide = slide; }
                    else
                    {
                        var shift = -(int)(Math.Round(slide * scale) - Math.Round(stillSlide * scale));
                        worstSharp = Math.Min(worstSharp, MotionSharpnessProbe.Best(shot, still, vertical ? 0 : -shift, vertical ? shift : 0, vertical, region).Sharp);
                    }
                    line.Visibility = Visibility.Visible;
                    if (!lane.PlayheadVisible) continue;
                    lineSeen++;
                    partial = Math.Max(partial, MotionSharpnessProbe.PartialColumns(MotionSharpnessProbe.Shoot(band.View, scale), shot, region.Y + 10));
                }
                Check($"motion: the Band strip and its playhead stay sharp ({(vertical ? "vertical" : "horizontal")}, x{scale:0.00})",
                    worstSharp >= 0.97 && partial == 0 && lineSeen > 0, $"sharp {F(worstSharp)}, soft playhead columns {partial}, frames with a line {lineSeen}");
                Log.Add($"  MOTION band {(vertical ? "vertical" : "horizontal")} x{scale:0.00}: worst sharp {F(worstSharp)}, soft playhead columns {partial} ({lineSeen} frames)");
            }
    }

    /// <summary>The score's playhead on its own: soft edge columns with and without pixel snapping.</summary>
    private static void TestMotionSharpnessPlayhead()
    {
        foreach (var scale in MotionScales)
        {
            var overlay = new PlayheadOverlay();
            MotionSharpnessProbe.Stage(overlay, scale, 400, 200);
            var line = "";
            foreach (var snap in new[] { false, true })
            {
                overlay.SnapToPixels = snap;
                overlay.SetGeometry(null);
                var blank = MotionSharpnessProbe.Shoot(overlay, scale);
                int worst = 0, soft = 0;
                var strip = new List<BitmapSource>();
                for (var i = 0; i < 40; i++)
                {
                    overlay.SetGeometry((100 + i * 0.37, 20, 180));
                    var shot = MotionSharpnessProbe.Shoot(overlay, scale);
                    if (i < 6) strip.Add(shot);
                    var partial = MotionSharpnessProbe.PartialColumns(shot, blank, (int)(100 * scale));
                    worst = Math.Max(worst, partial);
                    if (partial > 0) soft++;
                }
                if (Environment.GetEnvironmentVariable("TF_MOTION_PNG") is { Length: > 0 } folder)
                    MotionSharpnessProbe.SaveStrip(strip, new Int32Rect((int)(92 * scale), (int)(100 * scale), (int)(24 * scale), 12), 8, System.IO.Path.Combine(folder, $"playhead-{(snap ? "after" : "before")}-x{scale:0.00}.png"));
                line += $" {(snap ? "snapped" : "unsnapped")}: soft columns worst {worst}, soft frames {soft} of 40;";
                if (snap) Check($"motion: the score playhead is crisp at x{scale:0.00} in every frame", soft == 0, line);
            }
            Log.Add($"  MOTION score playhead x{scale:0.00}:{line}");
        }
    }

    /// <summary>The caret inside the real page tree (centred page, odd viewer widths, fractional scroll): hard edges there too.</summary>
    private static void TestMotionSharpnessScoreCaret()
    {
        foreach (var scale in MotionScales)
        {
            int soft = 0, frames = 0;
            foreach (var viewWidth in new[] { 901.0, 1000.0, 1013.0 })
            {
                var (scroll, grid) = MotionScoreRig(scale, viewWidth, 500, 700);
                var overlay = new PlayheadOverlay { SnapToPixels = true };
                grid.Children.Add(overlay);
                MotionSharpnessProbe.Stage(scroll, scale, viewWidth, 500);
                scroll.ScrollToVerticalOffset(13.3);
                scroll.UpdateLayout();
                var blank = MotionSharpnessProbe.Shoot(scroll, scale);
                for (var i = 0; i < 20; i++)
                {
                    overlay.SetGeometry((100 + i * 0.37, 30, 130));
                    frames++;
                    if (MotionSharpnessProbe.PartialColumns(MotionSharpnessProbe.Shoot(scroll, scale), blank, (int)(80 * scale)) > 0) soft++;
                }
            }
            Check($"motion: the caret in the scrolled, centred score page is crisp at x{scale:0.00}", soft == 0, $"{soft} of {frames} frames soft");
            Log.Add($"  MOTION score caret in page x{scale:0.00}: soft frames {soft} of {frames}");
        }
    }

    private static void TestMotionSharpnessTimeline()
    {
        var horizontalField = typeof(ArrangementPanel).GetField("_horizontal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var scale in MotionScales)
        {
            var panel = new ArrangementPanel();
            panel.Bind(BandUnevenSong(60), Array.Empty<Playback.MidiOutputDeviceInfo>());
            MotionSharpnessProbe.Stage(panel, scale, 700, 400);
            // The texture cache the main window gives the timeline (see MainWindow.ApplyGpuLayerCaches).
            panel.SetTimelineCache(new BitmapCache { RenderAtScale = Math.Max(1, scale), EnableClearType = true, SnapsToDevicePixels = true });
            MotionSharpnessProbe.Stage(panel, scale, 700, 400);
            var scroll = (ScrollViewer)horizontalField.GetValue(panel)!;
            // The playhead line without its shadow (the glow is soft by design): soft edge columns at fractional x.
            var line = VisualDescendants<Rectangle>(panel).First(r => r.Fill == Brushes.White && r.Effect is not null);
            line.Effect = null;
            int worst = 0, soft = 0;
            for (var i = 0; i < 24; i++)
            {
                panel.SetPlayhead(3, 0.05 + i * 0.011, true);
                MotionSharpnessProbe.Stage(panel, scale, 700, 400);
                line.Visibility = Visibility.Collapsed;
                var without = MotionSharpnessProbe.Shoot(panel, scale);
                line.Visibility = Visibility.Visible;
                var partial = MotionSharpnessProbe.PartialColumns(MotionSharpnessProbe.Shoot(panel, scale), without, (int)(200 * scale));
                worst = Math.Max(worst, partial);
                if (partial > 0) soft++;
            }
            Check($"motion: the timeline playhead is crisp at x{scale:0.00} in every frame", soft == 0, $"{soft} of 24 frames soft");
            // The scroll over the cached texture.
            panel.SetPlayhead(-1, 0);
            scroll.ScrollToHorizontalOffset(40);
            scroll.UpdateLayout();
            var still = MotionSharpnessProbe.Shoot(panel, scale);
            var region = new Int32Rect((int)(200 * scale), (int)(80 * scale), (int)(400 * scale), (int)(240 * scale));
            double worstSharp = 1;
            foreach (var step in MotionSteps)
            {
                scroll.ScrollToHorizontalOffset(40 + step);
                scroll.UpdateLayout();
                var shift = (int)(Math.Round((40 + step) * scale) - Math.Round(40 * scale));
                worstSharp = Math.Min(worstSharp, MotionSharpnessProbe.Best(MotionSharpnessProbe.Shoot(panel, scale), still, shift, 0, false, region).Sharp);
            }
            Check($"motion: the timeline scroll stays sharp (x{scale:0.00})", worstSharp >= 0.98, F(worstSharp));
            Log.Add($"  MOTION timeline x{scale:0.00}: playhead soft columns worst {worst}, soft frames {soft} of 24; scroll worst sharp {F(worstSharp)}");
        }
    }

    /// <summary>A Band row held by the pointer or gliding into its slot: a cached texture moved by fractional amounts, with the cache as it was and as BandReorder makes it.</summary>
    private static void TestMotionSharpnessRowDrag()
    {
        foreach (var scale in MotionScales)
        {
            var host = new FakeBandHost(BandUnevenSong(20));
            using var band = new BandViewController(host);
            MotionSharpnessProbe.Stage(band.View, scale, 1000, 800);
            band.Tick();
            MotionSharpnessProbe.Stage(band.View, scale, 1000, 800);
            var row = band.View.Rows[0];
            var origin = row.TransformToAncestor(band.View).Transform(new Point(0, 0));
            var region = new Int32Rect((int)(origin.X * scale) + 4, (int)(origin.Y * scale) + 24, (int)(row.ActualWidth * scale) - 8, (int)(row.ActualHeight * scale) - 40);
            var line = "";
            double dragSharp = 0;
            foreach (var (label, make) in new (string, Func<BitmapCache>)[] { ("plain cache", () => new BitmapCache(scale)), ("drag cache", () => BandReorder.DragCache(scale)) })
            {
                row.CacheMode = make();
                row.Shift.Y = 0;
                var still = MotionSharpnessProbe.Shoot(band.View, scale);
                double worstSharp = 1;
                foreach (var y in new[] { 0.3, 0.5, 0.7, 1.3, 2.5, 7.37 })
                {
                    row.Shift.Y = y;
                    var (_, sharp) = MotionSharpnessProbe.Best(MotionSharpnessProbe.Shoot(band.View, scale), still, 0, (int)Math.Round(y * scale), true, region);
                    worstSharp = Math.Min(worstSharp, sharp);
                }
                line += $" {label}: worst sharp {F(worstSharp)};";
                if (label == "drag cache") dragSharp = worstSharp;
            }
            row.CacheMode = null;
            row.Shift.Y = 0;
            Check($"motion: a held Band row stays sharp at x{scale:0.00}", dragSharp >= 0.98, line);
            Log.Add($"  MOTION band row drag x{scale:0.00}:{line}");
        }
    }
}
