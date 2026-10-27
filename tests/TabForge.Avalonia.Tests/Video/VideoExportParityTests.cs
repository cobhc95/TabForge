using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using NAudio.Wave;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Video;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Views.Score;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: File > Export video on the Avalonia side: the frame source keeps the BGRA contract (size, opaque, the paper in B,G,R order, the same
/// frame for the same time, a moving playhead), and the shared flow driven by AvaloniaVideoExporter writes a valid MP4 of the first two bars
/// whose frames cover the rendered sound (tail included) and whose sound is the phase-2 render of the same range; a cancelled export leaves a
/// file already at the target as it was. Tests: Video/AvaloniaVideoFrameSource, Views/AvaloniaVideoExporter, Core/Services/Video/VideoExportFlow.
/// </summary>
public class VideoExportParityTests
{
    private const int SmallWidth = 640, SmallHeight = 360, CancelFps = 30, Rate = 48000;
    private const double CoverToleranceSeconds = 0.001;   // the MP4's 100 ns clock against the sample count
    private const double MinSnrDb = 20;   // AAC-LC is perceptual, not a waveform codec: a dense mix fitted at best lag and gain lands near 24 dB at 192 kbps (a pure chord reaches 30); a lag, gain or channel fault falls far below 20
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(5);

    private readonly ITestOutputHelper _out;
    public VideoExportParityTests(ITestOutputHelper output) { _out = output; AlphaTabWarmUp.EnsureReady(); }

    private static SongProject Demo() => GuitarProImporter.Import(TestRepo.DemoSong());

    [AvaloniaFact]
    public void FramesKeepTheBgraContract()
    {
        var project = Demo();
        var timeline = RenderSpecBuilder.Compile(project);
        var spec = new VideoViewSpec { Layout = VideoLayout.ScoreOnly, Tracks = new[] { 0 }, Dark = true, Width = SmallWidth, Height = SmallHeight };
        using var a = new AvaloniaVideoFrameSource(project, timeline, spec);
        using var b = new AvaloniaVideoFrameSource(project, timeline, spec);
        var first = a.Render(0).ToArray();
        Assert.Equal(SmallWidth * SmallHeight * 4, first.Length);
        Assert.All(Enumerable.Range(0, first.Length / 4), i => Assert.Equal(255, first[i * 4 + 3]));
        var paper = new ScoreGeometryProbe(project, new AvaloniaDrawContext()).Appearance.DarkPaperColor;
        Assert.Equal(new[] { paper.B, paper.G, paper.R }, first[..3]);   // top-left is paper: blue, green, red
        Assert.Equal(Hash(first), Hash(b.Render(0)));
        Assert.NotEqual(Hash(first), Hash(a.Render(1000)));   // the playhead moved
    }

    [AvaloniaTheory]
    [InlineData(30)]
    [InlineData(60)]
    public async Task ExportWritesTheRenderedSoundUnderEnoughFrames(int fps)
    {
        var project = Demo();
        var timeline = RenderSpecBuilder.Compile(project);
        var (start, end) = RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomBars, 0, 0, 1, -1, 0, 0);
        var folder = Path.Combine(Path.GetTempPath(), "tf-video-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust()) { EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe"), WarmIdle = TimeSpan.Zero };
        var plugins = new PluginSettings { Driver = "Null", Device = "", SampleRate = Rate };
        var settings = new AppSettings();
        try
        {
            var spec = new VideoViewSpec { Layout = VideoLayout.ScoreOnly, Tracks = new[] { 0 }, Dark = true };
            var mp4 = Path.Combine(folder, "song.mp4");
            var plan = new VideoExportPlan(timeline, start, end, spec, mp4, fps);
            var ui = AvaloniaUiThread.ForApplication();
            var result = await Within(VideoExportFlow.RunAsync(AvaloniaVideoExporter.Request(project, settings, client, plan, ui, null, _ => Task.FromResult(true), plugins), null, CancellationToken.None));

            var wav = Path.Combine(folder, "song.wav");   // the phase-2 render of the same range, with the settings the flow renders with
            var render = new RenderSettings { Format = 2, TailMode = 0, TailMs = 0, SampleRate = Rate, Mono = false, SafetyLimiter = settings.Render.SafetyLimiter };
            await Within(RenderJob.RunAsync(new RenderRequest { Project = project, Plugins = plugins, MasterPercent = settings.Audio.MasterVolume, Settings = render, Timeline = timeline, StartMs = start, EndMs = end, MasterFile = wav }, null, CancellationToken.None, client));
            var source = ReadLeft(wav);

            var back = Mp4ReadBack.Read(mp4, fps);
            var snr = Mp4ReadBack.FitSnr(source, back.Left, Rate);
            _out.WriteLine($"result {result.Frames} frames, mp4 {back.VideoFrames} frames, video {back.VideoEnd:0.000} s, audio {back.AudioEnd:0.000} s, render {source.Length / (double)Rate:0.000} s, SNR {snr:0.0} dB");
            Assert.Equal(VideoExportFlow.FramesFor(source.Length, fps), result.Frames);
            Assert.True(Math.Abs(back.VideoFrames - result.Frames) <= 1, $"{back.VideoFrames} frames of {result.Frames}");
            var range = (end - start) / 1000.0;
            Assert.True(Math.Abs(back.VideoEnd - range) <= 1.0 / fps + CoverToleranceSeconds, $"video {back.VideoEnd:0.000} s against the range {range:0.000} s (no frozen tail)");
            Assert.True(Math.Abs(back.AudioEnd - back.VideoEnd) <= 1.0 / fps + CoverToleranceSeconds, "audio and video end together");
            Assert.True(back.VideoEnd >= source.Length / (double)Rate - CoverToleranceSeconds, "the frames cover the rendered sound");
            Assert.True(snr >= MinSnrDb, $"SNR {snr:0.0} dB against the render");
        }
        finally
        {
            try { client.Sync(Array.Empty<TrackModel>(), plugins); } catch (InvalidOperationException) { }
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task ACancelledExportLeavesTheExistingTargetAlone()
    {
        var project = Demo();
        var timeline = RenderSpecBuilder.Compile(project);
        var (start, end) = RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomBars, 0, 0, 1, -1, 0, 0);
        var target = Path.Combine(Path.GetTempPath(), "tf-video-keep-" + Guid.NewGuid().ToString("N") + ".mp4");
        var before = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(target, before);
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust()) { EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe"), WarmIdle = TimeSpan.Zero };
        try
        {
            var plan = new VideoExportPlan(timeline, start, end, new VideoViewSpec { Layout = VideoLayout.ScoreOnly }, target, CancelFps);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var request = AvaloniaVideoExporter.Request(project, new AppSettings(), client, plan, AvaloniaUiThread.ForApplication(), null, null, new PluginSettings { Driver = "Null" });
            await Assert.ThrowsAnyAsync<Exception>(() => VideoExportFlow.RunAsync(request, null, cancelled.Token));
            Assert.Equal(before, File.ReadAllBytes(target));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "." + Path.GetFileNameWithoutExtension(target) + "*"));   // no staging file left
        }
        finally { File.Delete(target); }
    }

    private static async Task<T> Within<T>(Task<T> job)
    {
        Assert.Same(job, await Task.WhenAny(job, Task.Delay(ExportTimeout)));
        return await job;
    }

    private static float[] ReadLeft(string wav)
    {
        using var reader = new AudioFileReader(wav);
        var all = new float[reader.Length / sizeof(float)];
        var read = reader.Read(all, 0, all.Length);
        var left = new float[read / 2];
        for (var i = 0; i < left.Length; i++) left[i] = all[i * 2];
        return left;
    }

    private static string Hash(ReadOnlySpan<byte> pixels) => Convert.ToHexString(SHA256.HashData(pixels));
}
