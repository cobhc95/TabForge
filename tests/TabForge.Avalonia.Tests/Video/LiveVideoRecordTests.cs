using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Video;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Services.Features;
using TabForge.Services.Video;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Record video checks of the Avalonia window: the title-bar camera button runs the recording command (not the export window), a
/// recording started and stopped saves an MP4 with frames and sound, the red recording dot follows the recording state, and the window frame
/// capture reads back the window's own pixels. Tests: Video/MainWindowLiveVideo, Video/AvaloniaWindowFrameCapture, Shell/TitleBarView.
/// </summary>
public class LiveVideoRecordTests
{
    private const int FrameWidth = 64, FrameHeight = 64;
    private const int SampleRate = 48000, ChunkFrames = 480;   // a 10 ms stereo chunk
    private const int MinimumGrabs = 10;                       // frames the fake capture must give before the stop
    private const int MaximumAudioFrames = SampleRate * 10;    // the most sound the test feeds before it gives up

    /// <summary>A recorder host with no window: a fake capture and the audio the test feeds in.</summary>
    private sealed class FakeRecordHost : ILiveVideoRecordHost
    {
        private readonly List<(string Message, string? Folder)> _notices = new();
        private Action<int, long, float[], int>? _audio;
        private long _grabs;

        public FakeRecordHost(string folder) => Settings = new LiveVideoSettings { Folder = folder, Fps = 30 };

        public LiveVideoSettings Settings { get; }
        public int OutputSampleRate { get; init; }
        public bool GrabThrows { get; init; }
        public bool CaptureDisposed { get; private set; }
        public bool MasterTapOn { get; private set; }
        public long Grabs => Interlocked.Read(ref _grabs);
        public List<(string Message, string? Folder)> Notices { get { lock (_notices) return new(_notices); } }

        public void SetMasterTap(bool on) => MasterTapOn = on;
        public event Action<int, long, float[], int> MasterAudio { add => _audio += value; remove => _audio -= value; }
        public ScreenRect? RegionRect(string region) => new ScreenRect(0, 0, FrameWidth, FrameHeight);
        public IVideoCapture BeginCapture(int width, int height, Func<ScreenRect?> region) => new SolidCapture(this);
        public void ShowRecording(TimeSpan? elapsed) { }
        public void Notify(string message, string? openFolder) { lock (_notices) _notices.Add((message, openFolder)); }
        public void Post(Action action) => action();
        public IDisposable Every(TimeSpan period, Action tick) => new NoTimer();

        /// <summary>One master tap chunk, as the engine's reader thread would deliver it.</summary>
        public void Feed(long firstFrame, float[] samples) => _audio?.Invoke(SampleRate, firstFrame, samples, ChunkFrames);

        private void CountGrab() => Interlocked.Increment(ref _grabs);

        private sealed class SolidCapture : IVideoCapture
        {
            private readonly FakeRecordHost _host;
            public SolidCapture(FakeRecordHost host) => _host = host;
            public bool Grab(byte[] bgra)
            {
                if (_host.GrabThrows) throw new InvalidOperationException("grab failed");
                Array.Fill(bgra, (byte)0x80);
                _host.CountGrab();
                return true;
            }
            public void Dispose() => _host.CaptureDisposed = true;
        }

        private sealed class NoTimer : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>A window chrome that records nothing (the title bar only needs the interface).</summary>
    private sealed class NullChrome : IWindowChrome
    {
        public WindowChromeState State { get; set; }
        public event EventHandler? StateChanged { add { } remove { } }
        public void BeginMove() { }
        public void BeginResize(ResizeEdge edge) { }
        public void Minimize() { }
        public void ToggleMaximise() { }
        public void Close() { }
    }

    [Fact]
    public async Task RecordingWritesAnMp4WithFramesAndSoundWhenStopped()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-video-" + Guid.NewGuid().ToString("N"));
        try
        {
            var host = new FakeRecordHost(folder);
            var controller = new LiveVideoRecordController(host);
            controller.Toggle();
            Assert.True(host.MasterTapOn);
            var chunk = new float[ChunkFrames * 2];
            for (long first = 0; host.Grabs < MinimumGrabs && first < MaximumAudioFrames; first += ChunkFrames)
            {
                host.Feed(first, chunk);
                await Task.Delay(10);
            }
            controller.Toggle();
            await controller.Finalizing;

            Assert.False(host.MasterTapOn);
            var notice = Assert.Single(host.Notices);
            Assert.StartsWith("Recording saved to ", notice.Message);
            Assert.Equal(folder, notice.Folder);
            var file = Assert.Single(Directory.GetFiles(folder, "*.mp4"));
            Assert.True(new FileInfo(file).Length > 1000, "the MP4 holds frames and sound");
            Assert.Empty(Directory.GetFiles(folder, "*.tmp.mp4"));   // the staging file was moved, not left behind
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnEngineRateTheMp4CannotTakeIsRefusedBeforeTheStart()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-video-" + Guid.NewGuid().ToString("N"));
        var host = new FakeRecordHost(folder) { OutputSampleRate = 96000 };
        var controller = new LiveVideoRecordController(host);
        controller.Toggle();

        Assert.False(controller.IsRecording);
        Assert.False(host.MasterTapOn);
        var notice = Assert.Single(host.Notices);
        Assert.Contains("96 kHz", notice.Message);
        Assert.Null(notice.Folder);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task AFailingGrabEndsTheRecordingAndReleasesTheCapture()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-video-" + Guid.NewGuid().ToString("N"));
        try
        {
            var host = new FakeRecordHost(folder) { GrabThrows = true };
            var controller = new LiveVideoRecordController(host) { TuneOptions = o => o with { HardwareProbe = VideoEncoderOptions.ProbeFail } };   // no self-test: the encoder opens at once
            controller.Toggle();
            var chunk = new float[ChunkFrames * 2];
            for (long first = 0; first < SampleRate; first += ChunkFrames) { host.Feed(first, chunk); await Task.Delay(5); }
            controller.Stop();
            await controller.Finalizing;

            Assert.True(host.CaptureDisposed);
            Assert.StartsWith("The video could not be saved: grab failed", Assert.Single(host.Notices).Message);
            Assert.Empty(Directory.GetFiles(folder, "*.mp4"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task RecordAsync(LiveVideoRecordController controller, FakeRecordHost host)
    {
        controller.Toggle();
        var chunk = new float[ChunkFrames * 2];
        for (long first = 0; host.Grabs < MinimumGrabs && first < MaximumAudioFrames; first += ChunkFrames)
        {
            host.Feed(first, chunk);
            await Task.Delay(10);
        }
        controller.Toggle();
        await controller.Finalizing;
    }

    [Fact]
    public async Task AHungFinishShowsAFailureNoticeWithinTheWatchdogAndKeepsTheUnfinishedFile()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-video-" + Guid.NewGuid().ToString("N"));
        try
        {
            var host = new FakeRecordHost(folder);
            host.Settings.HardwareProbe = VideoEncoderOptions.ProbePass;
            var controller = new LiveVideoRecordController(host)
            {
                FinishTimeout = TimeSpan.FromSeconds(2),
                TuneOptions = o => o with { SimulateFinalizeHang = true, FinalizeTimeout = TimeSpan.FromSeconds(60) },
            };
            await RecordAsync(controller, host);

            var notice = Assert.Single(host.Notices);
            Assert.StartsWith("The video could not be saved: ", notice.Message);
            Assert.Contains("errors.log", notice.Message);
            Assert.Null(notice.Folder);
            Assert.DoesNotContain(Directory.GetFiles(folder, "*.mp4"), f => !System.IO.Path.GetFileName(f).StartsWith('.'));   // nothing was put in place
            var kept = Assert.Single(Directory.GetFiles(folder, "*.tmp.mp4"));   // the unfinished file stays, and the notice says where
            Assert.Contains(kept, notice.Message);
            Assert.Equal(VideoEncoderOptions.ProbePass, host.Settings.HardwareProbe);   // a watchdog abort says nothing about the hardware encoder
        }
        finally
        {
            for (var i = 0; i < 40 && Directory.Exists(folder); i++)
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { await Task.Delay(50); }   // the abandoned encoder is still letting go of its file
            }
        }
    }

    [Fact]
    public void AnAbandonedEncoderReturnsInTimeLogsItAndCannotKeepTheAppOpen()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-enc-" + Guid.NewGuid().ToString("N") + ".mp4");
        var options = new VideoEncoderOptions(VideoEncoderChoices.Software) { SimulateFinalizeHang = true, FinalizeTimeout = TimeSpan.FromSeconds(1) };
        var encoder = VideoEncoder.Open(path, 1920, 1080, 30, new VideoAudioFormat(48000, 2), live: true, options: options);
        var worker = (Thread)typeof(VideoEncoder).GetField("_worker", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(encoder)!;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.Throws<VideoEncoderException>(encoder.Finish);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Contains(encoder.Notes, n => n.StartsWith("abandoned: ") && n.Contains(path));
            Assert.True(worker.IsBackground);
        }
        finally
        {
            worker.Join(5000);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task AStopWhileTheEncoderIsStillOpeningLeavesNoOrphanEncoder()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = System.IO.Path.Combine(folder, "late.mp4");
            var session = LiveVideoSession.Start(path, 1920, 1080, 30, _ => false, new VideoEncoderOptions());   // probe unknown: the first-run self-test runs before the open
            session.OnAudio(SampleRate, 0, new float[ChunkFrames * 2], ChunkFrames);
            session.Abort("test");
            Assert.ThrowsAny<VideoEncoderException>(session.Finish);
            Assert.False(File.Exists(path));   // abort and commit exclude each other
            var gone = false;
            for (var i = 0; i < 100 && !gone; i++)   // a late open closes its encoder: the kept file is not locked
            {
                try { foreach (var f in Directory.GetFiles(folder)) File.Delete(f); gone = true; }
                catch (IOException) { await Task.Delay(50); }
            }
            Assert.True(gone);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AFailedHardwareSelfTestIsForgottenAfterAWeek()
    {
        var failed = new LiveVideoSettings { HardwareProbe = VideoEncoderOptions.ProbeFail, HardwareProbeAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Equal(VideoEncoderOptions.ProbeFail, VideoEncoderOptions.ProbeToUse(failed, failed.HardwareProbeAt.AddDays(6)));
        Assert.Equal("", VideoEncoderOptions.ProbeToUse(failed, failed.HardwareProbeAt.AddDays(8)));   // tried again
        failed.HardwareProbe = VideoEncoderOptions.ProbePass;
        Assert.Equal(VideoEncoderOptions.ProbePass, VideoEncoderOptions.ProbeToUse(failed, failed.HardwareProbeAt.AddDays(80)));
    }

    [Fact]
    public async Task AHardwareEncoderFailureFallsBackToSoftwareAndRemembersIt()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-video-" + Guid.NewGuid().ToString("N"));
        try
        {
            var host = new FakeRecordHost(folder);
            var controller = new LiveVideoRecordController(host) { TuneOptions = o => o with { SimulateHardwareFailure = true } };
            await RecordAsync(controller, host);

            Assert.StartsWith("Recording saved to ", Assert.Single(host.Notices).Message);
            var file = Assert.Single(Directory.GetFiles(folder, "*.mp4"));
            Assert.True(new FileInfo(file).Length > 1000);
            Assert.Equal(VideoEncoderOptions.ProbeFail, host.Settings.HardwareProbe);
            Assert.Empty(Directory.GetFiles(folder, "*.tmp.mp4"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TitleBarRecordButtonRunsTheRecordingCommandNotTheExportWindow()
    {
        var window = new Window();
        var ran = new List<string>();
        var commands = new MainWindowCommands(window, () => { });
        commands.Add(FeatureHotkeyIds.RecordVideo, () => ran.Add(FeatureHotkeyIds.RecordVideo));
        commands.Add(FeatureHotkeyIds.ExportVideo, () => ran.Add(FeatureHotkeyIds.ExportVideo));
        var bar = TitleBarView.For(window, new TabStripView(new TabForge.Services.TabSettings()), commands);
        window.Content = new DockPanel { Children = { bar } };
        window.Show();

        var record = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Record");
        record.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new[] { FeatureHotkeyIds.RecordVideo }, ran);
        window.Close();
    }

    [AvaloniaFact]
    public void RecordingDotFollowsTheRecordingState()
    {
        var bar = new TitleBarView(new TabStripView(new TabForge.Services.TabSettings()), new NullChrome(), () => { }, () => { });
        var window = new Window { Width = 900, Height = 300, Content = new DockPanel { LastChildFill = false, Children = { bar } } };
        window.Show();
        var dot = bar.GetLogicalDescendants().OfType<Ellipse>().Single(e => e.Name == "VideoRecDot");

        Assert.False(dot.IsVisible);
        bar.ShowRecording(true);
        Assert.True(dot.IsVisible);
        bar.ShowRecording(false);
        Assert.False(dot.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void WindowFrameCaptureReadsTheWindowsPixels()
    {
        var window = new Window { Width = FrameWidth, Height = FrameHeight, Content = new Border { Background = Brushes.Red } };
        window.Show();
        window.UpdateLayout();
        var capture = new AvaloniaWindowFrameCapture(FrameWidth, FrameHeight, 30, () => window.Content as Visual,
            () => new ScreenRect(0, 0, FrameWidth, FrameHeight), AvaloniaUiThread.ForApplication());
        var bgra = new byte[FrameWidth * FrameHeight * 4];
        capture.RenderNow();

        Assert.True(capture.Grab(bgra));
        var centre = (FrameHeight / 2 * FrameWidth + FrameWidth / 2) * 4;
        Assert.Equal(255, bgra[centre + 2]);   // red
        Assert.Equal(0, bgra[centre]);         // blue
        capture.Dispose();
        window.Close();
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(120, 60)]
    [InlineData(17, 60)]
    public void LiveCaptureRateIsThirtyOrSixtyFps(int setting, int expected)
    {
        Assert.Equal(60, LiveVideoChoices.MaxCaptureFps);
        Assert.Equal(expected, LiveVideoChoices.CaptureFps(setting));
    }

    [Fact]
    public void FrameSlotsAreFloorOfAudioClockTimesFps()
    {
        Assert.Equal(60, VideoFrameSlots.SlotOf(1000, 60));
        Assert.Equal(0, VideoFrameSlots.SlotOf(16, 60));
        Assert.Equal(3, VideoFrameSlots.SlotOf(50, 60));
        Assert.Equal(30, VideoFrameSlots.SlotOf(1000, 30));
    }

    [Fact]
    public void FramesAtZeroSixteenFiftyLandInSlotsZeroZeroThree()
    {
        var slots = new VideoFrameSlots(60);
        Assert.True(slots.TryNext(0, out var s0, out _)); slots.Commit(s0);
        Assert.False(slots.TryNext(16, out _, out _));   // same slot 0: not taken again
        Assert.True(slots.TryNext(50, out var s3, out var fill));
        Assert.Equal(0, s0);
        Assert.Equal(3, s3);
        Assert.Equal(1, fill);   // slots 1 and 2 repeat the previous frame
    }

    [Fact]
    public void FirstFrameAtFiftyMsFillsSlotsZeroToThree()
    {
        var slots = new VideoFrameSlots(60);
        Assert.False(slots.HasFrame);
        Assert.True(slots.TryNext(50, out var slot, out var firstFill));
        Assert.Equal(0, firstFill);   // the session writes the first frame into slots firstFill..slot = 0,1,2,3
        Assert.Equal(3, slot);
    }

    [Fact]
    public void FallbackStrideTakesEverySecondSlot()
    {
        var slots = new VideoFrameSlots(60) { Stride = 2 };
        Assert.True(slots.TryNext(50, out var slot, out _));
        Assert.Equal(2, slot);
    }

    [Theory]
    [InlineData(120, 60)]
    [InlineData(30, 30)]
    [InlineData(45, 60)]
    public void OldFpsValuesMapToThirtyOrSixty(int saved, int expected)
    {
        Assert.Equal(expected, LiveVideoChoices.NormalizeFps(saved));
        Assert.Equal(expected, LiveVideoChoices.FpsOf(saved + " fps"));
        Assert.Equal(2, LiveVideoChoices.FpsLabels.Length);
        Assert.Equal(60, new LiveVideoSettings().Fps);
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(55, false)]
    public void RateBelowEightyFivePercentForThreeSecondsFallsBack(int perSecond, bool fires)
    {
        var monitor = new CaptureRateMonitor(60);
        var fired = 0;
        long now = 0;
        monitor.Tick(now);
        for (var sec = 0; sec < 6; sec++)
        {
            for (var i = 0; i < perSecond; i++) monitor.OnFrame();
            now += 1000;
            if (monitor.Tick(now)) fired++;
            if (sec == 1 && fires) Assert.Equal(0, fired);   // not yet 3 seconds
        }
        Assert.Equal(fires ? 1 : 0, fired);
    }

    [AvaloniaFact]
    public void WindowFrameCaptureIsCappedAndSkipsARenderNobodyTook()
    {
        var window = new Window { Width = FrameWidth, Height = FrameHeight, Content = new Border { Background = Brushes.Red } };
        window.Show();
        window.UpdateLayout();
        var capture = new AvaloniaWindowFrameCapture(FrameWidth, FrameHeight, 120, () => window.Content as Visual,
            () => new ScreenRect(0, 0, FrameWidth, FrameHeight), AvaloniaUiThread.ForApplication());
        try
        {
            Assert.Equal(LiveVideoChoices.MaxCaptureFps, capture.FrameRate);
            var bgra = new byte[FrameWidth * FrameHeight * 4];
            capture.RenderNow();
            Assert.Equal(1, capture.Renders);
            capture.RenderNow();   // the capture thread has not taken the frame: skipped, not re-rendered
            Assert.Equal(1, capture.Renders);
            Assert.True(capture.Grab(bgra));
            capture.RenderNow();
            Assert.Equal(2, capture.Renders);
        }
        finally
        {
            capture.Dispose();
            window.Close();
        }
    }
}
