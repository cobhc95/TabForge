using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// A long audio clip zoomed in draws only the visible span of its waveform (part of <see cref="SelfTest"/>): the columns built stay
/// within the viewport plus margins, the visible pixels equal the full draw, and the render cost is reported.
/// </summary>
public static partial class SelfTest
{
    private static void TestClipWaveformSpan()
    {
        var temp = Path.Combine(Path.GetTempPath(), "TabForge-span-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(temp, new byte[64]);
        var savedOpen = WaveformCache.OpenOverride;
        try
        {
            WaveformCache.ClearAll();
            WaveformCache.OpenOverride = _ => new FakeMediaSource(300, 8000L * 300);
            var song = DropSong(bars: 150);
            var clip = MoveClip("Long", 0, 300, 0);
            clip.File = temp;
            song.Tracks[0].AudioClips.Add(clip);
            ClipLanes.Ensure(song.Tracks[0], 1);
            var timeline = new TrackTimeline
            {
                Project = song, MeasureWidth = 400, Media = TestMediaContext(new AppSettings(), null),
                BarStartSec = b => b * 2.0, BarOfSec = s => ((int)Math.Floor(s / 2), s / 2 - Math.Floor(s / 2)),
            };
            _ = WaveformCache.Get(clip.File, timeline.Media);
            WaveformCache.WaitIdle(5000);
            var width = Math.Max(timeline.TotalWidth, 200);
            var height = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowsHeight(song) + 10;
            timeline.Measure(new Size(width, height));
            timeline.Arrange(new Rect(0, 0, width, height));
            double Render(out byte[] pixels)
            {
                timeline.InvalidateVisual();
                timeline.InvalidateMeasure();
                timeline.Measure(new Size(width, height));
                timeline.Arrange(new Rect(0, 0, width, height));
                timeline.UpdateLayout();
                var bmp = new RenderTargetBitmap(1000, (int)height, 96, 96, PixelFormats.Pbgra32);
                var sw = Stopwatch.StartNew();
                bmp.Render(timeline);
                sw.Stop();
                pixels = new byte[1000 * (int)height * 4];
                bmp.CopyPixels(new Int32Rect(0, 0, 1000, (int)height), pixels, 1000 * 4, 0);
                return sw.Elapsed.TotalMilliseconds;
            }
            timeline.SetViewport(0, 0);
            var full = Render(out var fullPixels);
            var fullCols = timeline.WaveColumnsBuilt;
            timeline.SetViewport(0, 1000);
            timeline.InvalidateVisual();
            var span = Render(out var spanPixels);
            var spanCols = timeline.WaveColumnsBuilt;
            Console.WriteLine($"clip waveform span: whole clip {full:0.0} ms ({fullCols} columns), visible span {span:0.0} ms ({spanCols} columns), timeline {width:0} px");
            Check("clip waveform: the whole-clip draw builds a column per pixel of the clip", fullCols > 10000, $"{fullCols}");
            Check("clip waveform: zoomed in, columns built stay within 3 x the viewport width", spanCols <= 3002, $"{spanCols}");
            Check("clip waveform: the visible pixels equal the whole-clip draw", fullPixels.AsSpan().SequenceEqual(spanPixels));
            timeline.SetViewport(width * 0.4, 1000);
            _ = Render(out _);
            Check("clip waveform: a scrolled viewport still builds only its own span", timeline.WaveColumnsBuilt <= 3002, $"{timeline.WaveColumnsBuilt}");
        }
        finally
        {
            WaveformCache.OpenOverride = savedOpen;
            WaveformCache.ClearAll();
            try { File.Delete(temp); } catch (IOException) { }
        }
    }
}
