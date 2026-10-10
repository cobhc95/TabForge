using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Services.Video;

namespace TabForge;

// Owns: the Desktop Duplication grabber check: a four-colour test window on its own thread is grabbed at 1080p and 4K (upright on a rotated display, cost per frame) and a live session; skips both when DXGI is unavailable (a remote session).
// Does not own: the GDI grabber or the live session (SelfTestLiveVideo).
public static partial class SelfTest
{
    private static void TestDxgiGrabber()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var rect = new ScreenRect(120, 120, 640, 360);
        var thread = new Thread(() =>
        {
            // Quadrants: red (flickering, so the desktop keeps changing), blue, green, white; they show the picture is upright on a rotated display.
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 2 };
            var red = new System.Windows.Controls.Border { Background = Brushes.Red };
            foreach (var quadrant in new[] { red, new System.Windows.Controls.Border { Background = Brushes.Blue }, new System.Windows.Controls.Border { Background = Brushes.Lime }, new System.Windows.Controls.Border { Background = Brushes.White } })
                grid.Children.Add(quadrant);
            var window = new Window { Left = 100, Top = 100, Width = 640, Height = 360, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Content = grid };
            dispatcher = Dispatcher.CurrentDispatcher;
            var flip = false;
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Normal, (_, _) => red.Background = (flip = !flip) ? Brushes.Red : new SolidColorBrush(Color.FromRgb(250, 0, 0)), dispatcher);
            window.Show(); timer.Start();
            var a = grid.PointToScreen(new Point(0, 0)); var b = grid.PointToScreen(new Point(grid.ActualWidth, grid.ActualHeight));
            rect = new ScreenRect((int)a.X + 2, (int)a.Y + 2, (int)(b.X - a.X) - 4, (int)(b.Y - a.Y) - 4);   // the window in physical pixels
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        ready.Wait(); Thread.Sleep(400);
        var available = true;
        try
        {
            foreach (var (w, h) in new[] { (1920, 1080), (3840, 2160), (480, 270) })   // the last is under half the region: halved on the GPU first
            {
                using var grabber = new DxgiRegionGrabber(w, h, () => rect);
                var pixels = new byte[w * h * 4];
                bool ok;
                try { ok = grabber.Grab(pixels); }
                catch (DxgiUnavailableException ex) { available = false; Check($"dxgi grabber: skipped at {w}x{h}, duplication unavailable", true, ex.Message); continue; }
                (byte B, byte G, byte R) At(int fx, int fy) { var i = (fy * w + fx) * 4; return (pixels[i], pixels[i + 1], pixels[i + 2]); }
                var (tl, tr, bl, br) = (At(w / 4, h / 4), At(3 * w / 4, h / 4), At(w / 4, 3 * h / 4), At(3 * w / 4, 3 * h / 4));
                var upright = tl.R > 200 && tl.B < 40 && tr.B > 200 && tr.R < 40 && bl.G > 200 && bl.R < 40 && bl.B < 40 && br.R > 200 && br.G > 200 && br.B > 200;
                Check($"dxgi grabber: a {w}x{h} frame shows the test window upright (red, blue / green, white)", ok && upright, $"region {rect}, tl {tl} tr {tr} bl {bl} br {br}");
                var spent = TimeSpan.Zero; var composed0 = grabber.ComposedFrames;
                for (var i = 0; i < 120; i++)
                {
                    var t0 = Stopwatch.GetTimestamp(); grabber.Grab(pixels); spent += Stopwatch.GetElapsedTime(t0);
                    Thread.Sleep(8);
                }
                var composed = grabber.ComposedFrames - composed0;
                Check($"dxgi grabber: {w}x{h} takes {spent.TotalMilliseconds / Math.Max(1, composed):0.0} ms per composed frame", composed > 5, $"{composed} composed of 120 ticks");
            }

            // The whole primary screen (a maximised window; portrait on a portrait display): the cost per composed frame.
            if (available)
            {
                var screen = new ScreenRect(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
                var (w, h) = LiveVideoChoices.SizeOf(LiveVideoChoices.P1080, screen);
                using var grabber = new DxgiRegionGrabber(w, h, () => screen);
                var pixels = new byte[w * h * 4];
                var ok = grabber.Grab(pixels);
                var spent = TimeSpan.Zero; var composed0 = grabber.ComposedFrames;
                for (var i = 0; i < 60; i++) { var t0 = Stopwatch.GetTimestamp(); ok &= grabber.Grab(pixels); spent += Stopwatch.GetElapsedTime(t0); Thread.Sleep(8); }
                var composed = grabber.ComposedFrames - composed0;
                Check($"dxgi grabber: the whole {screen.Width}x{screen.Height} screen into {w}x{h} takes {spent.TotalMilliseconds / Math.Max(1, composed):0.0} ms per composed frame", ok && composed > 5, $"{composed} composed of 60 ticks");
            }

            if (!available) return;   // no duplication (a remote session): the app records through GDI instead
            // End to end: 3 s into a live session with synthetic audio; the achieved rate is frames written over the audio seconds.
            foreach (var (w, h, fps) in new[] { (1920, 1080, 60), (1920, 1080, 120), (3840, 2160, 60), (3840, 2160, 120) })
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tf-dxgi-{Guid.NewGuid():N}.mp4");
                try
                {
                    using var grabber = new DxgiRegionGrabber(w, h, () => rect);
                    long grabTicks = 0; var grabCalls = 0;
                    var session = LiveVideoSession.Start(path, w, h, fps, b => { var t0 = Stopwatch.GetTimestamp(); var r = grabber.Grab(b); grabTicks += Stopwatch.GetTimestamp() - t0; grabCalls++; return r; });
                    var chunk = new float[960]; long first = 0; var watch = Stopwatch.StartNew();
                    while (watch.Elapsed.TotalSeconds < 3) { session.OnAudio(48000, first, chunk, 480); first += 480; Thread.Sleep(10); }
                    var seconds = session.Elapsed.TotalSeconds; var frames = session.FramesWritten; var dropped = session.DroppedFrames;
                    session.Finish();
                    Check($"dxgi grabber: live {w}x{h} at {fps} fps reaches {frames / seconds:0.0} fps, encoder dropped {dropped}, grab {Stopwatch.GetElapsedTime(0, grabTicks).TotalMilliseconds / Math.Max(1, grabCalls):0.0} ms avg over {grabCalls} calls, {grabber.ComposedFrames} composed", frames > 20, $"{frames} frames in {seconds:0.0} s");
                }
                finally { try { System.IO.File.Delete(path); } catch (System.IO.IOException) { } }
            }
        }
        catch (DxgiUnavailableException ex) { Check("dxgi grabber: skipped, desktop duplication unavailable here (no interactive desktop, or refused)", true, ex.Message); }   // not a defect of the grabber
        finally { dispatcher?.InvokeShutdown(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
