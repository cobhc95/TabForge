using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Video;
using TabForge.Services.Video;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the UI-thread cost checks of the window frame capture: it renders at the frame's size (not the window's), the capture thread's grab
/// allocates nothing in steady state, and the UI-thread time per captured frame is reported. Tests: Video/AvaloniaWindowFrameCapture.
/// </summary>
public class LiveVideoCaptureCostTests
{
    private const int WindowWidth = 1920, WindowHeight = 1080;
    private const int SmallWidth = 480, SmallHeight = 270, Frames = 30, SmallRecordBytes = 256;
    private readonly ITestOutputHelper _output;

    public LiveVideoCaptureCostTests(ITestOutputHelper output) => _output = output;

    private static Window ShowWindow()
    {
        var window = new Window { Width = WindowWidth, Height = WindowHeight, Content = new Border { Background = Brushes.Red } };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static AvaloniaWindowFrameCapture Make(Window window, int width, int height) =>
        new(width, height, 30, () => window.Content as Visual, () => new ScreenRect(0, 0, WindowWidth, WindowHeight), AvaloniaUiThread.ForApplication());

    [AvaloniaFact]
    public void CaptureRendersAtTheFrameSizeAndGrabAllocatesNoFrameBuffer()
    {
        var window = ShowWindow();
        var capture = Make(window, SmallWidth, SmallHeight);
        try
        {
            var bgra = new byte[SmallWidth * SmallHeight * 4];
            capture.RenderNow();
            Assert.True(capture.Grab(bgra));
            var centre = (SmallHeight / 2 * SmallWidth + SmallWidth / 2) * 4;
            Assert.Equal(255, bgra[centre + 2]);   // the red window, scaled to the frame
            Assert.Equal(SmallWidth * SmallHeight * 4, bgra.Length);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Frames; i++) Assert.True(capture.Grab(bgra));
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < Frames * SmallRecordBytes);   // only the region records, never a frame buffer
        }
        finally { capture.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public void CaptureSkipsTheRenderWhileTheEncoderHasNotTakenTheFrame()
    {
        var window = ShowWindow();
        var capture = Make(window, SmallWidth, SmallHeight);
        try
        {
            for (var i = 0; i < Frames; i++) capture.RenderNow();   // nobody grabs: the UI never re-renders
            Assert.Equal(1, capture.Renders);
        }
        finally { capture.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public void ReportsUiThreadMillisecondsPerCapturedFrame()
    {
        var window = ShowWindow();
        try
        {
            foreach (var (w, h) in new[] { (WindowWidth, WindowHeight), (SmallWidth, SmallHeight) })
            {
                var capture = Make(window, w, h);
                var bgra = new byte[w * h * 4];
                capture.RenderNow(); capture.Grab(bgra);
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < Frames; i++) { capture.RenderNow(); capture.Grab(bgra); }
                _output.WriteLine($"frame {w}x{h}: {watch.Elapsed.TotalMilliseconds / Frames:F2} ms per captured frame (render + copy + grab)");
                capture.Dispose();
            }
        }
        finally { window.Close(); }
    }
}
