using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: finishing an offline video export: finalising that takes long but makes progress succeeds, off the UI thread with a "Finishing video" status; an encoder that
//   truly hangs fails after the stall allowance with its unfinished file kept; the producer writes every frame when duration x fps is fractional or exact.
//   A real export through the flow at 1080p60 and 4K60 with the hardware and the software encoder finishes.
// Does not own: cancelling and the hardware fallback (VideoExportCancelTests), the live encoders (LiveVideoRecordTests).
// Tests: itself.
public sealed class VideoExportFinishTests
{
    private const int Rate = 48000, Fps = 60;

    [Theory]
    [InlineData(432000, 540)]   // exactly 540 frames of sound
    [InlineData(431680, 540)]   // 539.6 frames: the last, partial frame is still drawn
    [InlineData(431999, 540)]
    [InlineData(432001, 541)]
    public void FramesCoverTheSoundWhateverTheFraction(long audioFrames, int expected) => Assert.Equal(expected, VideoExportFlow.FramesFor(audioFrames, Fps));

    [AvaloniaTheory]
    [InlineData(540)]
    [InlineData(540 - 1)]
    public async Task EveryFrameAndEverySampleReachesTheEncoder(int frames)
    {
        var fake = new FakeEncoder { FinishMs = 0 };
        var (request, wav, path) = Request(fake, frames);
        try
        {
            await VideoExportFlow.EncodeAsync(request, wav, path, frames, null, CancellationToken.None);
            Assert.Equal(frames, fake.FrameCount);
            Assert.Equal((long)frames * Rate / Fps, fake.AudioSamples / 2);
            Assert.True(fake.Finished);
        }
        finally { File.Delete(wav); }
    }

    [AvaloniaFact]
    public async Task ASlowFinishThatMakesProgressSucceedsOffTheUiThread()
    {
        var fake = new FakeEncoder { FinishMs = 15_000, FinishBeats = true };
        var (request, wav, path) = Request(fake, 10);
        var texts = new List<string>();
        var ui = Environment.CurrentManagedThreadId;
        try
        {
            await VideoExportFlow.EncodeAsync(request, wav, path, 10, new Collect(texts), CancellationToken.None);
            Assert.True(fake.Finished);
            Assert.NotEqual(ui, fake.FinishThread);
            Assert.Contains(texts, t => t.StartsWith("Finishing video"));
        }
        finally { File.Delete(wav); }
    }

    [AvaloniaFact]
    public async Task AFinishThatNeverReturnsFailsAndKeepsTheFile()
    {
        var fake = new FakeEncoder { FinishMs = -1 };
        var kept = Path.Combine(Path.GetTempPath(), "tf-kept-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(kept, new byte[] { 1, 2, 3 });
        try
        {
            var e = await Assert.ThrowsAsync<VideoEncoderException>(() => VideoEncoderFinisher.FinishAsync(fake, TimeSpan.FromMilliseconds(600), kept, null, TimeSpan.FromMilliseconds(50)));
            Assert.Equal(kept, e.KeptFile);
            Assert.Contains(kept, e.Message);
            Assert.NotNull(fake.AbortedFor);
            Assert.True(File.Exists(kept));
        }
        finally { fake.Release(); File.Delete(kept); }
    }

    [AvaloniaTheory]
    [InlineData("Auto")]
    [InlineData("Software")]
    public async Task ARealMediaFoundation4kExportFinishesOffTheUiThread(string encoderChoice)
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "claude", "tf-real-4k-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var mp4 = Path.Combine(dir, "real4k60-" + encoderChoice + ".mp4");
        const int w = 3840, h = 2160, frames = 120;
        var stall = VideoEncoderFinisher.StallAllowance(w, h, Fps);
        using var encoder = VideoEncoderFactory.Open(mp4, w, h, Fps, new VideoAudioFormat(Rate, 2), options: new VideoEncoderOptions(encoderChoice) { FinalizeTimeout = stall });
        var pixels = new byte[w * h * 4];
        var audio = new float[(Rate / Fps) * 2];
        var writing = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
        {
            for (var p = 0; p < pixels.Length; p += 4 * 997) pixels[p] = (byte)(i * 7 + p);   // a changing picture
            encoder.WriteFrame(pixels, TimeSpan.FromTicks(i * 10_000_000L / Fps));
            encoder.WriteAudio(audio);
        }
        var written = writing.ElapsedMilliseconds;
        var finishing = System.Diagnostics.Stopwatch.StartNew();
        await VideoEncoderFinisher.FinishAsync(encoder, stall, mp4, null);
        Assert.True(new FileInfo(mp4).Length > 10_000);
        File.WriteAllText(Path.Combine(dir, "result-" + encoderChoice + ".txt"), $"{encoder.EncoderName}; {frames} frames written in {written} ms; finish {finishing.ElapsedMilliseconds} ms; {new FileInfo(mp4).Length} bytes");
    }

    // The real export path (flow, Media Foundation, finisher) at both sizes with both encoders: nothing regressed and no finish stalls here.
    [AvaloniaTheory]
    [InlineData(1920, 1080, "Hardware")]
    [InlineData(1920, 1080, "Software")]
    [InlineData(3840, 2160, "Hardware")]
    [InlineData(3840, 2160, "Software")]
    public async Task ARealExportThroughTheFlowFinishes(int w, int h, string encoderChoice)
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "claude", "tf-real-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        const int frames = 120;
        var (request, wav, _) = Request(null, frames, w, h, encoderChoice);
        var mp4 = Path.Combine(dir, $"real{h}p60-{encoderChoice}.mp4");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var written = await VideoExportFlow.EncodeAsync(request, wav, mp4, frames, null, CancellationToken.None);
            Assert.Equal(mp4, written);   // no stall, so no software redo
            Assert.True(new FileInfo(mp4).Length > 10_000);
            File.WriteAllText(Path.Combine(dir, "result.txt"), $"{w}x{h} {encoderChoice}: {frames} frames in {clock.ElapsedMilliseconds} ms, {new FileInfo(mp4).Length} bytes");
        }
        finally { File.Delete(wav); }
    }

    [Fact]
    public void TheAllowanceGrowsWithTheWork()
    {
        Assert.True(VideoEncoderFinisher.StallAllowance(3840, 2160, 60) > VideoEncoderFinisher.StallAllowance(1920, 1080, 30));
        Assert.True(VideoEncoderFinisher.StallAllowance(1920, 1080, 30) >= TimeSpan.FromSeconds(30));
    }

    /// <summary>An export request over <paramref name="frames"/> frames of silence; a null <paramref name="fake"/> uses the real encoder (and full-size frames).</summary>
    internal static (VideoExportRequest, string Wav, string Path) Request(FakeEncoder? fake, int frames, int w = 3840, int h = 2160, string encoder = "Auto")
    {
        var project = GuitarProImporter.Import(TestRepo.DemoSong());
        var wav = Path.Combine(Path.GetTempPath(), "tf-finish-" + Guid.NewGuid().ToString("N") + ".wav");
        WriteWav(wav, (long)frames * Rate / Fps);
        var request = new VideoExportRequest
        {
            Project = project, Settings = new AppSettings(), Engine = null!, Timeline = RenderSpecBuilder.Compile(project), StartMs = 0, EndMs = frames * 1000.0 / Fps,
            Spec = new VideoViewSpec { Width = w, Height = h }, Path = "unused.mp4", Fps = Fps, Ui = AvaloniaUiThread.ForApplication(), Encoder = encoder,
            OpenFrames = () => new FakeFrames(fake is null ? w * h * 4 : 4), OpenEncoder = fake is null ? null : (_, _) => fake, FinishPoll = TimeSpan.FromMilliseconds(100),
        };
        return (request, wav, "unused-staging.mp4");
    }

    private static void WriteWav(string path, long frames)
    {
        using var w = new BinaryWriter(File.Create(path));
        var bytes = (int)(frames * 8);
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)3); w.Write((short)2); w.Write(Rate); w.Write(Rate * 8); w.Write((short)8); w.Write((short)32);
        w.Write("data"u8); w.Write(bytes); w.Write(new byte[bytes]);
    }

    internal sealed class Collect : IProgress<VideoExportProgress>
    {
        private readonly List<string> _texts;
        public Collect(List<string> texts) => _texts = texts;
        public void Report(VideoExportProgress value) { lock (_texts) _texts.Add(value.Text); }
    }

    private sealed class FakeFrames(int size) : IVideoFrameSource
    {
        private readonly byte[] _pixels = new byte[size];
        public VideoFrameTimings Timings { get; } = new();
        public ReadOnlySpan<byte> Render(double ms) => _pixels;
        public void Dispose() { }
    }

    internal sealed class FakeEncoder : IVideoEncoder
    {
        private readonly ManualResetEventSlim _release = new();
        private long _beat;
        public int FinishMs;              // < 0: never returns until released
        public bool FinishBeats;
        public int FrameCount, FinishThread;
        public long AudioSamples;
        public volatile bool Finished;
        public volatile string? AbortedFor;
        public bool Hardware;

        public long DroppedFrames => 0;
        public bool UsesHardware => Hardware;
        public string EncoderName => "fake";
        public IReadOnlyList<string> Notes => Array.Empty<string>();
        public long Heartbeat => Interlocked.Read(ref _beat);
        public bool WriteFrame(ReadOnlySpan<byte> bgra, TimeSpan timestamp) { FrameCount++; return true; }
        public void WriteAudio(ReadOnlySpan<float> samples) => AudioSamples += samples.Length;
        public void Abort(string why) { AbortedFor = why; _release.Set(); }
        public void Release() => _release.Set();

        public void Finish()
        {
            FinishThread = Environment.CurrentManagedThreadId;
            if (FinishMs < 0) { _release.Wait(); return; }
            var until = Environment.TickCount64 + FinishMs;
            while (Environment.TickCount64 < until) { Thread.Sleep(100); if (FinishBeats) Interlocked.Increment(ref _beat); }
            Finished = true;
        }

        public void Dispose() { }
    }
}
