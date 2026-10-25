using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Video;
using TabForge.AudioEngine.Mixing;
using TabForge.Platform;
using TabForge.Services.Video;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the principle parity of live video between the hosts: the same 2 s chord goes through the master tap and the Core session once with
/// the Avalonia host's capture (the window render) and once with the WPF host's capture (the desktop grabber of VideoRecordUi.BeginCapture).
/// Each MP4 must hold about 2 s of frames, its picture and sound must end within one frame of each other, and its decoded sound must match
/// what the tap handed over (SNR fit as in SelfTestLiveAudioFidelity). Tests: Core/Services/Video/LiveVideoSession, AudioEngine/Mixing/MasterTap.
/// </summary>
[Collection(TimingCollection.Name)]   // two real-time 2 s recordings: run alone so scheduler contention cannot stretch one capture past the other
public class LiveVideoParityTests
{
    private const int Width = 1920, Height = 1080, Fps = 30, Rate = 48000, Block = 256;
    private const double Seconds = 2.0;
    private const double MinSnrDb = 30;      // AAC keeps the chord at this or better (the fidelity self test's MP4 bar)
    private const int MaxLagFrames = 4096;   // the encoder's priming is shorter than this
    private const int FitFrames = Rate / 2;  // half a second compared, from the middle of the run

    private readonly ITestOutputHelper _out;
    public LiveVideoParityTests(ITestOutputHelper output) => _out = output;

    private static float Chord(long n)
    {
        var v = 0.0;
        for (var k = 1; k <= 12; k++) v += Math.Sin(2 * Math.PI * 100 * k * n / Rate + k) / k;
        return (float)(0.05 * v);
    }

    [AvaloniaFact]
    public async Task BothHostsCapturesRecordTheSameValidTwoSeconds()
    {
        var window = new Window { Width = 320, Height = 180, Content = new Border { Background = Brushes.SteelBlue } };
        window.Show();
        var region = new ScreenRect(0, 0, 320, 180);
        var avalonia = await Record("avalonia", () => new AvaloniaWindowFrameCapture(Width, Height, Fps, () => window.Content as Visual, () => region, AvaloniaUiThread.ForApplication()));
        var wpf = await Record("wpf", () => new DesktopCapture(new DesktopRegionGrabber(Width, Height, () => region, new BlackScreen())));
        window.Close();
        Assert.True(Math.Abs(avalonia.VideoEnd - wpf.VideoEnd) <= 1.0 / Fps, $"durations {avalonia.VideoEnd:0.000} s and {wpf.VideoEnd:0.000} s");
    }

    private sealed record Result(double VideoEnd, double AudioEnd, int Frames, double Snr);

    private async Task<Result> Record(string name, Func<IVideoCapture> open)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-parity-{name}-{Guid.NewGuid():N}.mp4");
        var tapped = new List<float>();
        using var capture = open();
        var session = LiveVideoSession.Start(path, Width, Height, Fps, capture.Grab);
        var tap = new MasterTap { Sink = (rate, first, data, frames) => { lock (tapped) for (var i = 0; i < frames; i++) tapped.Add(data[i * 2]); session.OnAudio(rate, first, (float[])data.Clone(), frames); } };
        try
        {
            tap.Enable(true, Rate);
            var feeder = Task.Run(() => Feed(tap));   // the audio callback: real-time blocks of the chord
            while (!feeder.IsCompleted) await Task.Delay(5);   // the UI thread stays free for the window capture's timer
            await feeder;
            tap.Enable(false, Rate);
            await Task.Delay(50);
            session.Finish();
            var mp4 = Mp4ReadBack.Read(path, Fps);
            float[] source; lock (tapped) source = tapped.ToArray();
            var snr = Mp4ReadBack.FitSnr(source, mp4.Left, Rate);
            var result = new Result(mp4.VideoEnd, mp4.AudioEnd, mp4.VideoFrames, snr);
            _out.WriteLine($"{name}: {result}");
            Assert.True(mp4.VideoFrames >= (Seconds - 0.2) * Fps, $"{name}: {mp4.VideoFrames} frames");
            Assert.True(Math.Abs(mp4.VideoEnd - Seconds) <= 0.1, $"{name}: video {mp4.VideoEnd:0.000} s");
            Assert.True(Math.Abs(mp4.VideoEnd - mp4.AudioEnd) <= 1.0 / Fps, $"{name}: video ends {mp4.VideoEnd:0.000} s, audio {mp4.AudioEnd:0.000} s");
            Assert.True(snr >= MinSnrDb, $"{name}: SNR {snr:0.0} dB against the tap");
            return result;
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static void Feed(MasterTap tap)
    {
        var block = new float[Block * 2];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (long n = 0; n < Seconds * Rate; n += Block)
        {
            for (var i = 0; i < Block; i++) block[2 * i] = block[2 * i + 1] = Chord(n + i);
            tap.Write(block, 0, Block);
            var due = (n + Block) * 1000.0 / Rate;
            while (clock.Elapsed.TotalMilliseconds < due) Thread.Sleep(1);
        }
    }

    /// <summary>The WPF host's capture (VideoRecordUi.Capture) around the Core desktop grabber.</summary>
    private sealed class DesktopCapture : IVideoCapture
    {
        private readonly DesktopRegionGrabber _grabber;
        public DesktopCapture(DesktopRegionGrabber grabber) => _grabber = grabber;
        public bool Grab(byte[] bgra) => _grabber.Grab(bgra);
        public void Dispose() => _grabber.Dispose();
    }

    /// <summary>The GDI fallback for a machine without desktop duplication: a black frame (the WPF adapter is not reachable from here).</summary>
    private sealed class BlackScreen : IScreenCapture
    {
        public IScreenFrame? OpenFrame(int width, int height) => new Frame();
        private sealed class Frame : IScreenFrame
        {
            public void Clear() { }
            public bool StretchFromScreen(ScreenRect source, ScreenRect destination) => true;
            public void CopyPixelsTo(byte[] bgra) => Array.Clear(bgra);
            public void Dispose() { }
        }
    }

}
