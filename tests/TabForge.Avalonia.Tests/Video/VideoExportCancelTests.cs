using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services.Video;
using TabForge.Avalonia.Views;
using Xunit;
using Fake = TabForge.Avalonia.Tests.VideoExportFinishTests.FakeEncoder;

namespace TabForge.Avalonia.Tests;

// Owns: what the user can rely on when an export's encoder misbehaves: Cancel export returns at once even while the encoder never finishes
//   (the dialog is usable again), a stalled hardware encoder is given up on and the video redone with the software encoder, live recording and
//   export run the same finalize sequence, export uses live recording's hardware rule, and the working files live in the app temp folder.
// Does not own: finishing that takes long but moves (VideoExportFinishTests), the live recorder (LiveVideoRecordTests).
// Tests: itself.
public sealed class VideoExportCancelTests
{
    private const int Frames = 10;

    [AvaloniaFact]
    public async Task CancelWhileTheEncoderNeverFinishesReturnsWithinASecondAndTheDialogWorksAgain()
    {
        var fake = new Fake { FinishMs = -1 };
        var (request, wav, path) = VideoExportFinishTests.Request(fake, Frames);
        var window = new VideoExportWindow(DemoSongFactory.Create(), new VideoSettings(), hasSelection: false,
            run: async (_, progress, cancel) => { await VideoExportFlow.EncodeAsync(request, wav, path, Frames, progress, cancel); return new VideoExportResult(Frames, 1, 1); },
            saveFile: _ => Path.Combine(Path.GetTempPath(), "tf-cancel-unused.mp4"));
        try
        {
            window.Show();
            var export = window.ExportPressed();
            for (var i = 0; i < 200 && fake.FinishThread == 0; i++) await Task.Delay(20);
            Assert.NotEqual(0, fake.FinishThread);   // finishing: the encoder now never returns by itself
            Assert.False(ButtonOf(window, VideoExportPresenter.ExportButton).IsEnabled);
            var clock = Stopwatch.StartNew();
            ButtonOf(window, "Close").RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Assert.Same(export, await Task.WhenAny(export, Task.Delay(1000)));
            Assert.True(clock.ElapsedMilliseconds < 1000, $"cancel took {clock.ElapsedMilliseconds} ms");
            Assert.True(ButtonOf(window, VideoExportPresenter.ExportButton).IsEnabled);
            Assert.Equal(VideoExportPresenter.CloseButton, ButtonOf(window, "Close").Content);
            Assert.NotNull(fake.AbortedFor);
        }
        finally { fake.Release(); window.Close(); File.Delete(wav); }
    }

    [AvaloniaFact]
    public async Task AStalledHardwareEncoderIsRedoneWithTheSoftwareEncoder()
    {
        var stalled = new Fake { FinishMs = -1, Hardware = true };
        var software = new Fake { FinishMs = 0 };
        var opened = new List<string>();
        var (request, wav, _) = VideoExportFinishTests.Request(stalled, Frames, encoder: VideoEncoderChoices.Hardware);
        request = With(request, (_, options) => { lock (opened) { opened.Add(options.Encoder); return opened.Count == 1 ? stalled : software; } });
        var texts = new List<string>();
        var path = Path.Combine(VideoExportFlow.StagingFolder, "tf-fallback-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var written = await VideoExportFlow.EncodeAsync(request, wav, path, Frames, new VideoExportFinishTests.Collect(texts), CancellationToken.None);
            Assert.Equal(new[] { VideoEncoderChoices.Hardware, VideoEncoderChoices.Software }, opened);
            Assert.NotNull(stalled.AbortedFor);
            Assert.True(software.Finished);
            Assert.Equal(Frames, software.FrameCount);
            Assert.NotEqual(path, written);
            Assert.Contains(texts, t => t.StartsWith(VideoExportFlow.HardwareStalledText));
            Assert.Equal(VideoEncoderOptions.ProbeFail, request.Settings.LiveVideo.HardwareProbe);   // remembered for live recording and the next export
        }
        finally { stalled.Release(); File.Delete(wav); }
    }

    [Fact]
    public void ExportUsesLiveRecordingsHardwareRule()
    {
        var auto = new VideoEncoderOptions();
        Assert.Equal(VideoEncoderChoices.Software, VideoEncoderFactory.ForExport(auto, HostOs.Windows).Encoder);   // not tested yet: software
        Assert.Equal(VideoEncoderChoices.Software, VideoEncoderFactory.ForExport(auto with { HardwareProbe = VideoEncoderOptions.ProbeFail }, HostOs.Windows).Encoder);
        Assert.Equal(VideoEncoderChoices.Auto, VideoEncoderFactory.ForExport(auto with { HardwareProbe = VideoEncoderOptions.ProbePass }, HostOs.Windows).Encoder);
        Assert.Equal(VideoEncoderChoices.Hardware, VideoEncoderFactory.ForExport(new VideoEncoderOptions(VideoEncoderChoices.Hardware), HostOs.Windows).Encoder);
        Assert.Equal(VideoEncoderChoices.Auto, VideoEncoderFactory.ForExport(auto, HostOs.Linux).Encoder);   // ffmpeg keeps its own Auto elsewhere
    }

    [Fact]
    public void TheExportsWorkingFilesAreInTheAppTempFolder()
    {
        var target = Path.Combine(AppFolders.Current.Videos, "Song.mp4");
        var staging = VideoExportFlow.StagingPath(target, "abcd1234");
        Assert.StartsWith(Path.Combine(AppFolders.Current.Temp, "TabForge"), staging);
        Assert.NotEqual(Path.GetDirectoryName(target), Path.GetDirectoryName(staging));
        Assert.EndsWith(".mp4", staging);
    }

    [Fact]
    public void LiveRecordingAndExportRunTheSameFinalizeSequence()
    {
        if (!OperatingSystem.IsWindows()) return;
        var live = FinalizeSteps(live: true);
        var export = FinalizeSteps(live: false);
        Assert.Equal(VideoEncoder.FinishSteps, live);
        Assert.Equal(live, export);
    }

    // The finalize step names a real (software) encoder notes in one mode.
    private static List<string> FinalizeSteps(bool live)
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-steps-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var encoder = VideoEncoder.Open(path, 1920, 1080, 30, new VideoAudioFormat(48000, 2), live, new VideoEncoderOptions(VideoEncoderChoices.Software));
            var frame = new byte[1920 * 1080 * 4];
            for (var i = 0; i < 5; i++)
            {
                encoder.WriteFrame(frame, TimeSpan.FromTicks(i * 10_000_000L / 30));
                encoder.WriteAudio(new float[1600 * 2]);
                if (live) Thread.Sleep(5);
            }
            encoder.Finish();
            var step = new Regex("^finalize step '([^']+)'");
            return encoder.Notes.Select(n => step.Match(n)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        }
        finally { File.Delete(path); }
    }

    private static VideoExportRequest With(VideoExportRequest r, Func<string, VideoEncoderOptions, IVideoEncoder> open) => new()
    {
        Project = r.Project, Settings = r.Settings, Engine = r.Engine, Timeline = r.Timeline, StartMs = r.StartMs, EndMs = r.EndMs, Spec = r.Spec, Path = r.Path,
        Fps = r.Fps, Ui = r.Ui, Encoder = r.Encoder, OpenFrames = r.OpenFrames, OpenEncoder = open, FinishPoll = TimeSpan.FromMilliseconds(50),
        HardwareStall = TimeSpan.FromMilliseconds(400),
    };

    private static Button ButtonOf(Window window, string text) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text || b.Name == text);
}
