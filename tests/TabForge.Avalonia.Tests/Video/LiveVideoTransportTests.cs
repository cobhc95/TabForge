using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Platform;
using TabForge.Services.Video;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Record-video-never-touches-transport contract: starting, stopping, saving or a capture
// error never starts, stops or pauses playback, and a user play/stop/pause mid-capture just keeps
// recording. Tests: Core/Services/Video/LiveVideoRecordController.
public class LiveVideoTransportTests
{
    private const int FrameWidth = 64, FrameHeight = 64;
    private const int SampleRate = 48000, ChunkFrames = 480;
    private const int MinimumGrabs = 10;

    /// <summary>Stand-in for the song transport: the controller has no handle to it, so it must never change.</summary>
    private sealed class FakeTransport
    {
        public bool IsPlaying { get; private set; }
        public int Bar { get; private set; }
        public void Play(int bar) { IsPlaying = true; Bar = bar; }
        public void Stop() { IsPlaying = false; }
    }

    private sealed class FakeHost : ILiveVideoRecordHost
    {
        private readonly List<(string Message, string? Folder)> _notices = new();
        private readonly List<Action> _ticks = new();
        private Action<int, long, float[], int>? _audio;
        private long _grabs;

        public FakeHost(string folder) => Settings = new LiveVideoSettings { Folder = folder, Fps = 30 };

        public FakeTransport Transport { get; } = new();
        public LiveVideoSettings Settings { get; }
        public int OutputSampleRate => 0;
        public bool GrabThrows { get; init; }
        public long Grabs => Interlocked.Read(ref _grabs);
        public IReadOnlyList<Action> Ticks { get { lock (_ticks) return _ticks.ToList(); } }
        public List<(string Message, string? Folder)> Notices { get { lock (_notices) return _notices.ToList(); } }

        public void SetMasterTap(bool on) { }
        public event Action<int, long, float[], int> MasterAudio { add => _audio += value; remove => _audio -= value; }
        public ScreenRect? RegionRect(string region) => new ScreenRect(0, 0, FrameWidth, FrameHeight);
        public IVideoCapture BeginCapture(int width, int height, Func<ScreenRect?> region) => new SolidCapture(this);
        public void ShowRecording(TimeSpan? elapsed) { }
        public void Notify(string message, string? openFolder) { lock (_notices) _notices.Add((message, openFolder)); }
        public void Post(Action action) => action();
        public IDisposable Every(TimeSpan period, Action tick) { lock (_ticks) _ticks.Add(tick); return new NoTimer(); }

        public void Feed(long firstFrame, float[] samples) => _audio?.Invoke(SampleRate, firstFrame, samples, ChunkFrames);
        public void RunTicks() { foreach (var tick in Ticks) tick(); }

        private void CountGrab() => Interlocked.Increment(ref _grabs);

        private sealed class SolidCapture : IVideoCapture
        {
            private readonly FakeHost _host;
            public SolidCapture(FakeHost host) => _host = host;
            public bool Grab(byte[] bgra)
            {
                if (_host.GrabThrows) throw new InvalidOperationException("grab failed");
                Array.Fill(bgra, (byte)0x80);
                _host.CountGrab();
                return true;
            }
            public void Dispose() { }
        }

        private sealed class NoTimer : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "tf-video-transport-" + Guid.NewGuid().ToString("N"));

    private static void DeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }

    private static async Task FeedUntilGrabs(FakeHost host, int minimumGrabs = MinimumGrabs)
    {
        var chunk = new float[ChunkFrames * 2];
        for (long first = 0; host.Grabs < minimumGrabs && first < SampleRate * 10; first += ChunkFrames)
        {
            host.Feed(first, chunk);
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task StoppedPlusStartVideo_StaysStoppedAndRecords()
    {
        // V1: starting video must not start playback. Old code called host.StartPlayback() here.
        Assert.Null(typeof(ILiveVideoRecordHost).GetMethod("StartPlayback"));
        var folder = NewFolder();
        try
        {
            var host = new FakeHost(folder);
            var controller = new LiveVideoRecordController(host);
            Assert.False(host.Transport.IsPlaying);

            controller.Toggle();

            Assert.True(controller.IsRecording);
            Assert.False(host.Transport.IsPlaying);
            Assert.Equal(0, host.Transport.Bar);
            controller.Stop();
            await controller.Finalizing;
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task PlayingAtBar5PlusStartVideo_StillPlayingFromBar5()
    {
        // V1: starting video while playing keeps the position. Old code skipped its
        // StartPlayback call while playing, so this guards the position, not the call.
        var folder = NewFolder();
        try
        {
            var host = new FakeHost(folder);
            host.Transport.Play(5);
            var controller = new LiveVideoRecordController(host);

            controller.Toggle();

            Assert.True(controller.IsRecording);
            Assert.True(host.Transport.IsPlaying);
            Assert.Equal(5, host.Transport.Bar);
            controller.Stop();
            await controller.Finalizing;
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task PlayingPlusStopVideo_StillPlayingAndFileSaved()
    {
        // V2: stopping/saving video never stops playback. Old code called host.StopPlayback().
        Assert.Null(typeof(ILiveVideoRecordHost).GetMethod("StopPlayback"));
        var folder = NewFolder();
        try
        {
            var host = new FakeHost(folder);
            host.Transport.Play(5);
            var controller = new LiveVideoRecordController(host);
            controller.Toggle();
            await FeedUntilGrabs(host);

            controller.Toggle();
            await controller.Finalizing;

            Assert.False(controller.IsRecording);
            Assert.True(host.Transport.IsPlaying);
            Assert.Equal(5, host.Transport.Bar);
            var notice = Assert.Single(host.Notices);
            Assert.StartsWith("Recording saved to ", notice.Message);
            Assert.Equal(folder, notice.Folder);
            Assert.NotEmpty(Directory.GetFiles(folder, "*.mp4"));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task RecordingPlusUserStop_PlaybackStopsButVideoKeepsRecording()
    {
        // V3: user play/stop/pause mid-capture is just captured, never ends the video.
        // Old wiring ended the video via controller.OnTransportStopped(); that hook is gone.
        Assert.Null(typeof(LiveVideoRecordController).GetMethod("OnTransportStopped"));
        var folder = NewFolder();
        try
        {
            var host = new FakeHost(folder);
            host.Transport.Play(5);
            var controller = new LiveVideoRecordController(host);
            controller.Toggle();
            Assert.True(controller.IsRecording);

            host.Transport.Stop();

            Assert.False(host.Transport.IsPlaying);
            Assert.True(controller.IsRecording);
            controller.Stop();
            await controller.Finalizing;
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task CaptureError_EndsVideoWithNoticeButLeavesPlayback()
    {
        // V2 error path: a capture failure ends the video with a notice, playback untouched.
        // Old Tick called StopRecording(stopPlayback: true), stopping a playing song.
        var folder = NewFolder();
        try
        {
            var host = new FakeHost(folder) { GrabThrows = true };
            host.Transport.Play(5);
            var controller = new LiveVideoRecordController(host) { TuneOptions = o => o with { HardwareProbe = VideoEncoderOptions.ProbeFail } };   // no self-test: the encoder opens at once
            controller.Toggle();
            Assert.True(controller.IsRecording);

            var chunk = new float[ChunkFrames * 2];
            for (long first = 0; first < SampleRate; first += ChunkFrames) { host.Feed(first, chunk); await Task.Delay(5); }
            for (var i = 0; i < 50 && controller.IsRecording; i++) { host.RunTicks(); await Task.Delay(10); }

            Assert.False(controller.IsRecording);
            Assert.True(host.Transport.IsPlaying);
            Assert.Equal(5, host.Transport.Bar);
            await controller.Finalizing;
            Assert.StartsWith("The video could not be saved: grab failed", Assert.Single(host.Notices).Message);
            Assert.True(host.Transport.IsPlaying);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }
}
