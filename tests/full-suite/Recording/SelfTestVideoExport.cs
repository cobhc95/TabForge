using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Views.Video;

namespace TabForge;

/// <summary>
/// The video export: the first two bars of the demo song at 1080p30 in all four layouts (the focus and score-and-band ones in the light theme). Each MP4 reads back with the
/// expected number of frames (within 1) and an audio stream, and a frame from the middle differs from the first (the cursor moves).
/// Set TABFORGE_VIDEO_FRAMES to a folder to keep the first, middle and last frame of each run as PNG files.
/// </summary>
public static partial class SelfTest
{
    private static void TestVideoExport()
    {
        var bytes = FuzzFindSample("TabForge Demo - Ashen Meridian.gp5") ?? FuzzFindSample("TabForge Demo - Ashen Meridian.gp");
        if (bytes is null) { Check("video export: the demo song is present", false); return; }
        var project = GuitarProImporter.ImportBytes(bytes, "demo.gp5");
        var timeline = RenderSpecBuilder.Compile(project);
        var (start, end) = RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomBars, 0, 0, 1, -1, 0, 0);
        var keep = Environment.GetEnvironmentVariable("TABFORGE_VIDEO_FRAMES");
        if (!string.IsNullOrEmpty(keep)) KeepVideoStills(project, timeline, (start + end) / 2, keep);
        CheckVideoExportProgress(timeline);
        var client = AudioEngineClient.Instance;
        var previousWarm = client.WarmIdle;
        client.WarmIdle = TimeSpan.Zero;
        try
        {
            foreach (var layout in new[] { VideoLayout.ScoreOnly, VideoLayout.Band, VideoLayout.Focus, VideoLayout.ScoreAndBand })
            {
                var path = Path.Combine(Path.GetTempPath(), $"tf-videoexport-{Guid.NewGuid():N}.mp4");
                var frames = (int)Math.Round((end - start) / 1000.0 * 30);
                byte[]? first = null, middle = null, last = null;
                try
                {
                    var request = new VideoExportRequest
                    {
                        Project = project, Settings = new AppSettings(), Engine = client, Timeline = timeline, StartMs = start, EndMs = end, Path = path, Fps = 30,
                        Plugins = new PluginSettings { Driver = TabForge.AudioEngine.Output.AudioOutputFactory.Null, Device = "", SampleRate = 48000 },
                        Spec = new VideoViewSpec { Layout = layout, Tracks = new[] { 0, 1 }, Dark = layout is VideoLayout.ScoreOnly or VideoLayout.Band },   // the other two run in the light theme
                        TimingsTap = t => { if (Environment.GetEnvironmentVariable("TABFORGE_VIDEO_PERF") is { Length: > 0 } f) File.AppendAllText(f, VideoPerfLine(layout, t) + Environment.NewLine); },
                        FrameTap = (i, pixels) =>
                        {
                            if (Environment.GetEnvironmentVariable("TABFORGE_VIDEO_DUMP") is { Length: > 0 } dump && i % 9 == 0) { Directory.CreateDirectory(dump); File.WriteAllBytes(Path.Combine(dump, $"{layout}-{i:000}.bin"), pixels.ToArray()); }
                            if (i == 0) first = pixels.ToArray();
                            if (i == frames / 2) middle = pixels.ToArray();
                            if (i == frames - 1) last = pixels.ToArray();
                        },
                    };
                    var task = VideoExportFlow.RunAsync(request, null, CancellationToken.None);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromMinutes(10))
                    {
                        Thread.Sleep(5);
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    }
                    var result = task.IsCompleted ? task.GetAwaiter().GetResult() : null;
                    Check($"video export {layout}: finished with {frames} frames", result is { Frames: var n } && n == frames, result is null ? "timed out" : $"{result.Frames}");
                    var (read, audio) = File.Exists(path) ? ReadBackMp4(path) : (0, false);
                    Check($"video export {layout}: the MP4 reads back with the expected frames (within 1) and an audio stream", Math.Abs(read - frames) <= 1 && audio, $"frames {read} of {frames}, audio {audio}");
                    Check($"video export {layout}: a frame from the middle differs from the first (the cursor moves)", first is not null && middle is not null && !first.AsSpan().SequenceEqual(middle), "identical");
                    if (!string.IsNullOrEmpty(keep) && first is not null && middle is not null && last is not null)
                        foreach (var (name, pixels) in new[] { ("first", first), ("middle", middle), ("last", last) })
                        {
                            Directory.CreateDirectory(keep);
                            var bitmap = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Pbgra32, null, pixels, 1920 * 4);
                            var png = new PngBitmapEncoder();
                            png.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(keep, $"video-{layout}-{name}.png"));
                            png.Save(file);
                        }
                }
                finally { try { File.Delete(path); } catch (IOException) { } }
            }
        }
        finally { client.WarmIdle = previousWarm; }
    }

    /// <summary>One line per export: ms per frame in each drawing phase, the hand-off to the encoder (UI-blocked time, including queue waits) and the bake count.</summary>
    private static string VideoPerfLine(VideoLayout layout, VideoFrameTimings t)
    {
        var n = Math.Max(1, t.Frames);
        return $"{layout}: {t.Frames} frames, ms/frame layout {t.Layout / n:0.00} bake {t.Bake / n:0.00} instrument {t.Instrument / n:0.00} band {t.Band / n:0.00} " +
               $"raster {t.Render / n:0.00} copy {t.Copy / n:0.00} compose {t.Compose / n:0.00} hand-off {t.Encode / n:0.00} total {(t.Layout + t.Bake + t.Instrument + t.Band + t.Render + t.Copy + t.Compose + t.Encode) / n:0.00} bakes {t.Bakes}";
    }

    /// <summary>The progress mapping of a range that starts mid-song: it runs 0 to 1 over the range's own frames, never from the song's start.</summary>
    private static void CheckVideoExportProgress(TabForge.Playback.ScoreTimeline timeline)
    {
        var (start, end) = RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomBars, 4, 0, 5, -1, 0, 0);
        var frames = (int)Math.Round((end - start) / 1000.0 * 30);
        var first = VideoExportProgress.Frame(0, frames, 0).Fraction;
        var last = VideoExportProgress.Frame(frames - 1, frames, 1).Fraction;
        Check("video export progress: the range starts mid-song", start > 0 && frames > 8, $"start {start}, frames {frames}");
        Check("video export progress: audio runs from 0 up to where the frames start", VideoExportProgress.Audio(0).Fraction == 0 && Math.Abs(VideoExportProgress.Audio(1).Fraction - first) < 1e-9, "audio does not meet the frames");
        Check("video export progress: the frames end just below 1", first < 0.15 && last < 1 && last > 0.9, $"{first:0.000} .. {last:0.000}");
        Check("video export progress: the frame text counts the range and shows an ETA", VideoExportProgress.Frame(450, 900, 20).Text == "Frame 450 / 900, about 0:20 left", VideoExportProgress.Frame(450, 900, 20).Text);
    }

    /// <summary>Keeps one still per layout and theme (no audio, no encoding) for a visual check.</summary>
    private static void KeepVideoStills(TabForge.Models.SongProject project, TabForge.Playback.ScoreTimeline timeline, double ms, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var layout in new[] { VideoLayout.ScoreOnly, VideoLayout.Band, VideoLayout.Focus, VideoLayout.ScoreAndBand })
            foreach (var dark in new[] { true, false })
            {
                using var source = new VideoFrameSource(project, timeline, new AppSettings(), new VideoViewSpec { Layout = layout, Tracks = new[] { 0, 1 }, Dark = dark });
                var pixels = source.Render(ms).ToArray();
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Pbgra32, null, pixels, 1920 * 4)));
                using var file = File.Create(Path.Combine(folder, $"still-{layout}-{(dark ? "dark" : "light")}.png"));
                png.Save(file);
            }
    }
}
