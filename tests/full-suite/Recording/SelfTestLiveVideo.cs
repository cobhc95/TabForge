using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.Controllers;
using TabForge.Services;
using TabForge.Services.Video;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge;

/// <summary>
/// Live video recording (part of <see cref="SelfTest"/>): the engine's master output tap carries what is audible in order and without gaps;
/// a 2 s headless record (null output, tap, live session) gives an MP4 with an audio stream and frames; the controller handles start, stop,
/// stop at the end of the song and a second start.
/// </summary>
public static partial class SelfTest
{
    private static void StartTestEngine(SharedBlock shared, string wav)
    {
        EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        EH.Configure(NullConfig(48000, 256, manual: false));
        EH.LoadChain(0, "song", false, new List<PluginSpec>());
        EH.SetClips(0, new List<ClipSpec> { new(wav, 0, 0, 8, 0, 0, 1) }, owner: 1);
        EH.Command(EngineCommand.SetTrackMix, w => { w.Write(0); w.Write(100); w.Write(64); });
        EH.Collect();
        // Plays from here on; the disk thread can lag, so wait (bounded) until the clip is heard before the test taps it.
        EH.SetPlaying(1, true, 0.0);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && shared.Peak(0) <= 0.01f) { EH.Collect(); Thread.Sleep(2); }
    }

    private static void StopTestEngine(SharedBlock shared, string wav)
    {
        EH.Detach();
        SettleDisk();
        shared.Dispose();
        try { File.Delete(wav); } catch (IOException) { }
    }

    /// <summary>The disk thread answers a seek after the song has moved on: the clip still starts (the stale frames are skipped) instead of being re-sought forever.</summary>
    private static void TestClipPlayerCatchesUpAfterSeek()
    {
        var wav = WriteTestWav(4);
        try
        {
            using var player = new TabForge.AudioEngine.Audio.ClipPlayer(new ClipSpec(wav, 0, 0, 4, 0, 0, 1), 48000);
            var l = new float[256]; var r = new float[256];
            player.Mix(l, r, 256, 0.0, true);       // asks the disk thread for the clip start
            player.Service();                        // the disk thread answers (a first block of the file)
            Array.Clear(l); Array.Clear(r);
            player.Mix(l, r, 256, 0.1, true);        // the song is already 0.1 s on: well past the 256-frame window
            var peak = 0f;
            for (var i = 0; i < l.Length; i++) peak = Math.Max(peak, Math.Abs(l[i]));
            Check("clip player: a seek answered late still plays (the playhead moved on while the disk thread worked)", peak > 0.01f, $"peak {peak:0.###}");
        }
        finally { try { File.Delete(wav); } catch (IOException) { } }
    }

    private static void TestMasterTapProtocol()
    {
        Check("master tap: wire values are fixed (command 41, event 24, chunk bound 8192 frames)",
            (byte)EngineCommand.SetMasterTap == 41 && (byte)EngineEvent.MasterAudio == 24 && MasterTapLimits.MaxChunkFrames == 8192);
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var wav = WriteTestWav(8);
        try
        {
            StartTestEngine(shared, wav);
            var chunks = new List<(long First, int Frames)>();
            var gate = new object();
            var peak = 0f; var finite = true;
            EH.Tap.Sink = (rate, first, data, frames) =>
            {
                lock (gate)
                {
                    chunks.Add((first, frames));
                    for (var i = 0; i < frames * 2; i++) { if (!float.IsFinite(data[i])) finite = false; peak = Math.Max(peak, Math.Abs(data[i])); }
                }
            };
            Thread.Sleep(100);
            Check("master tap: off by default, nothing is sent while idle", chunks.Count == 0 && !EH.Tap.On);
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(true));
            EH.Collect();
            Thread.Sleep(700);
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(false));
            EH.Collect();
            Thread.Sleep(100);
            long next = 0; var contiguous = true; long total;
            lock (gate)
            {
                foreach (var (first, frames) in chunks) { if (first != next) contiguous = false; next = first + frames; }
                total = next;
            }
            Check("master tap: chunks start at frame 0 and follow each other without a gap", chunks.Count > 3 && contiguous && chunks[0].First == 0, $"{chunks.Count} chunks, {total} frames, contiguous {contiguous}");
            Check("master tap: about 0.7 s of audio arrived in real time, finite, and the clip is audible", total is > 20000 and < 48000 && finite && peak > 0.01f, $"frames {total}, peak {peak:0.###}, finite {finite}");
            Check("master tap: nothing was lost", EH.Tap.Lost == 0);
            lock (gate) chunks.Clear();
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(true));
            EH.Collect();
            Thread.Sleep(200);
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(false));
            EH.Collect();
            Thread.Sleep(100);
            lock (gate) Check("master tap: switching it on again restarts the frame count at 0", chunks.Count > 0 && chunks[0].First == 0, $"first {(chunks.Count > 0 ? chunks[0].First : -1)}");
        }
        finally { StopTestEngine(shared, wav); }
    }

    /// <summary>A restart waits for the previous drain thread. A sink still stalled on the old run's block cannot forward that block, or the
    /// old run's next block, after the new run has started: the new run's frames are numbered from 0 with no gap and carry only its own level.</summary>
    private static void TestMasterTapRestartWaitsForOldRun()
    {
        const float oldLevel = 0.25f, newLevel = 0.5f;
        var tap = new TabForge.AudioEngine.Mixing.MasterTap();
        var gate = new object();
        var stalled = new ManualResetEventSlim(false);
        var calls = new List<(int Phase, long First, int Frames, float Peak)>();
        var phase = 0;
        tap.Sink = (_, first, data, frames) =>
        {
            var peak = 0f;
            for (var i = 0; i < frames * 2; i++) peak = Math.Max(peak, Math.Abs(data[i]));
            lock (gate) calls.Add((phase, first, frames, peak));
            if (first == 0 && frames == 1000 && !stalled.IsSet) { stalled.Set(); Thread.Sleep(700); }
        };
        float[] Block(float level, int frames) { var b = new float[frames * 2]; Array.Fill(b, level); return b; }
        try
        {
            tap.Enable(true, 48000);
            tap.Write(Block(oldLevel, 1000), 0, 1000);      // the old run's first block: its sink stalls on it
            var reached = stalled.Wait(2000);
            tap.Write(Block(oldLevel, 1000), 0, 1000);      // the old run's second block waits in the ring
            tap.Enable(false, 48000);
            tap.Enable(true, 48000);                        // restart while the old drain thread is still in the stalled sink
            lock (gate) phase = 1;
            tap.Write(Block(newLevel, 3000), 0, 3000);      // the new run
            Thread.Sleep(1000);
            tap.Enable(false, 48000);
            Thread.Sleep(100);
            List<(int Phase, long First, int Frames, float Peak)> seen;
            lock (gate) seen = new List<(int, long, int, float)>(calls);
            var newRun = seen.FindAll(c => c.Phase == 1);
            long next = 0; var contiguous = newRun.Count > 0 && newRun[0].First == 0;
            foreach (var c in newRun) { if (c.First != next) contiguous = false; next = c.First + c.Frames; }
            var oldAfterRestart = newRun.FindAll(c => c.Peak < 0.4f).Count;
            var oldBeforeRestart = 0; long oldFrames = 0;
            foreach (var c in seen) if (c.Phase == 0) { oldFrames += c.Frames; if (Math.Abs(c.Peak - oldLevel) < 0.05f) oldBeforeRestart++; }
            Check("master tap restart: the stalled sink was reached (the test stalls the old run mid-block)", reached, $"reached {reached}");
            Check("master tap restart: no block of the old run is forwarded after the restart",
                oldAfterRestart == 0, $"{oldAfterRestart} old-run chunks after the restart; new-run chunks {newRun.Count}");
            Check("master tap restart: the new run's frames are numbered from 0 with no gap and all arrive",
                contiguous && next == 3000, $"first {(newRun.Count > 0 ? newRun[0].First : -1)}, contiguous {contiguous}, frames {next}");
            Check("master tap restart: the old run's backlog is forwarded before its numbering ends",
                oldFrames == 2000 && oldBeforeRestart == 2, $"old frames {oldFrames}, old chunks at old level {oldBeforeRestart}");
        }
        finally { tap.Enable(false, 48000); }
    }

    private sealed class FakeVideoHost : ILiveVideoRecordHost
    {
        public LiveVideoSettings Settings { get; } = new() { Resolution = LiveVideoChoices.P1080, Fps = 30 };
        public bool IsPlaying { get; set; }
        public int Starts, Stops, TapOn, TapOff, Opened, Closed;
        public readonly List<(string Message, string? Folder)> Notices = new();
        public readonly List<TimeSpan?> Shown = new();
        public event Action<int, long, float[], int>? MasterAudio;
        public void Raise(int rate, long first, float[] data, int frames) => MasterAudio?.Invoke(rate, first, data, frames);
        public void StartPlayback() { Starts++; IsPlaying = true; }
        public void StopPlayback() { Stops++; IsPlaying = false; }
        public void SetMasterTap(bool on) { if (on) TapOn++; else TapOff++; }
        public ScreenRect? RegionRect(string region) => new(0, 0, 640, 360);
        public IVideoCapture BeginCapture(int width, int height, Func<ScreenRect?> region) { Opened++; return new Gradient(this); }
        public void ShowRecording(TimeSpan? elapsed) => Shown.Add(elapsed);
        public void Notify(string message, string? openFolder) { lock (Notices) Notices.Add((message, openFolder)); }
        public void Post(Action action) => action();
        public IDisposable Every(TimeSpan period, Action tick) => new Nothing();

        private sealed class Nothing : IDisposable { public void Dispose() { } }
        private sealed class Gradient : IVideoCapture
        {
            private readonly FakeVideoHost _host; private int _n;
            public Gradient(FakeVideoHost host) => _host = host;
            public bool Grab(byte[] bgra) { for (var i = 0; i < bgra.Length; i += 4096) bgra[i] = (byte)(_n++); return true; }
            public void Dispose() => _host.Closed++;
        }
    }

    /// <summary>Feeds the controller's host real-time sine chunks (5 ms each) from a thread, like the engine's tap.</summary>
    private static Thread FeedAudio(FakeVideoHost host, Func<bool> running)
    {
        var thread = new Thread(() =>
        {
            long first = 0; var chunk = new float[480 * 2]; var generation = host.TapOn;
            while (running())
            {
                if (host.TapOn != generation) { generation = host.TapOn; first = 0; }   // the engine restarts the count when the tap is switched on
                for (var i = 0; i < 480; i++) chunk[i * 2] = chunk[i * 2 + 1] = 0.2f * MathF.Sin((first + i) * 0.058f);
                host.Raise(48000, first, (float[])chunk.Clone(), 480);
                first += 480;
                Thread.Sleep(10);
            }
        }) { IsBackground = true };
        thread.Start();
        return thread;
    }

    private static void TestLiveVideoRecord()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tf-livevideo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var wav = WriteTestWav(8);
        try
        {
            // The screen grabber: a 1600 x 800 region of the desktop (about a score pane) scaled into a 1080p and a 4K frame; the cost per frame is reported.
            foreach (var (w, h) in new[] { (1920, 1080), (3840, 2160) })
            {
                using var grabber = new ScreenRegionGrabber(w, h, () => new ScreenRect(0, 0, 1600, 800));
                var pixels = new byte[w * h * 4];
                var ok = grabber.Grab(pixels);   // the first call creates the GDI objects
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < 20; i++) ok &= grabber.Grab(pixels);
                Check($"live video: the screen grabber fills a {w}x{h} frame ({watch.Elapsed.TotalMilliseconds / 20:0.0} ms per frame)", ok);
            }

            // 2 s through the real engine tap (null output) into a live session and a 1080p30 MP4.
            StartTestEngine(shared, wav);
            var path = Path.Combine(dir, "engine.mp4");
            var frame = 0;
            var session = LiveVideoSession.Start(path, 1920, 1080, 30, buffer => { buffer[0] = (byte)frame++; return true; });
            EH.Tap.Sink = (rate, first, data, frames) => session.OnAudio(rate, first, data, frames);
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(true));
            EH.Collect();
            Thread.Sleep(2000);
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(false));
            EH.Collect();
            Thread.Sleep(50);
            var seconds = session.Elapsed.TotalSeconds;
            session.Finish();
            var (frames, audio) = ReadBackMp4(path);
            Check("live video: a 2 s headless record gives an MP4 with an audio stream and frames", File.Exists(path) && audio && frames >= 30, $"frames {frames}, audio {audio}, audio seconds {seconds:0.00}, dropped {session.DroppedFrames}");
            Check("live video: the audio clock covers the 2 s", seconds is > 1.5 and < 2.6, $"{seconds:0.00} s");
            Check("live video: Finish without any audio is a clear error and saves nothing", ThrowsNoAudio(Path.Combine(dir, "silent.mp4")));
            EH.Tap.Sink = null;

            // The controller's state machine with a fake window.
            var host = new FakeVideoHost();
            host.Settings.Folder = dir;
            var controller = new LiveVideoRecordController(host);
            var running = true;
            var feeder = FeedAudio(host, () => running);
            controller.Toggle();
            Check("live video: Record video starts playback, the tap and the capture", controller.IsRecording && host.Starts == 1 && host.TapOn == 1 && host.Opened == 1 && host.IsPlaying);
            Thread.Sleep(700);
            controller.Toggle();
            controller.Finalizing.Wait(20000);
            Check("live video: pressing it again stops playback and the tap, closes the capture and saves the MP4",
                !controller.IsRecording && host.Stops == 1 && host.TapOff == 1 && host.Closed == 1 && host.Notices.Count == 1 && host.Notices[0].Folder == dir && Directory.GetFiles(dir, "TabForge *.mp4").Length == 1,
                $"stops {host.Stops}, notices {string.Join("|", host.Notices.ConvertAll(n => n.Message))}");

            controller.Toggle();
            Check("live video: a second start records again into a new file", controller.IsRecording && host.Starts == 2 && host.TapOn == 2);
            Thread.Sleep(500);
            controller.OnTransportStopped();   // the song ended or Stop was pressed
            controller.Finalizing.Wait(20000);
            Check("live video: the end of the song stops the recording without stopping playback again", !controller.IsRecording && host.Stops == 1 && host.TapOff == 2 && host.Notices.Count == 2);
            controller.OnTransportStopped();
            controller.Stop();
            Check("live video: stopping while idle does nothing", !controller.IsRecording && host.TapOff == 2 && host.Notices.Count == 2);
            running = false; feeder.Join(2000);
            var saved = Directory.GetFiles(dir, "TabForge *.mp4");
            var (second, secondAudio) = saved.Length > 1 ? ReadBackMp4(saved[1]) : (0, false);
            Check("live video: both recordings are readable MP4 files with sound", saved.Length == 2 && secondAudio && second > 3, $"files {saved.Length}, frames of the second {second}");
        }
        finally
        {
            StopTestEngine(shared, wav);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// What a live recording shows: the whole window by default, the whole region scaled into the frame (never cropped), a portrait frame for a
    /// region taller than wide (a portrait monitor), and screen rectangles in physical pixels at the display's scaling.
    /// </summary>
    private static void TestLiveVideoRegionFit()
    {
        Check("live video region: the default records the whole window", new LiveVideoSettings().Region == LiveVideoChoices.Window && LiveVideoChoices.NormalizeRegion(null) == LiveVideoChoices.Window);

        // A maximised window on a 2160 x 3840 portrait monitor at 175 %, and a landscape window at 150 %.
        var portrait = new ScreenRect(0, 0, 2160, 3786);
        var landscape = new ScreenRect(-2880, 0, 2880, 1620);
        Check("live video region: a portrait region records a portrait frame",
            LiveVideoChoices.SizeOf(LiveVideoChoices.P1080, portrait) == (1080, 1920) && LiveVideoChoices.SizeOf(LiveVideoChoices.P4K, portrait) == (2160, 3840)
            && LiveVideoChoices.SizeOf(LiveVideoChoices.P1080, landscape) == (1920, 1080) && LiveVideoChoices.SizeOf(LiveVideoChoices.P1080, null) == (1920, 1080));

        foreach (var (fw, fh, r) in new[] { (1920, 1080, portrait), (1080, 1920, portrait), (1920, 1080, landscape), (1920, 1080, new ScreenRect(0, 0, 1600, 800)), (1920, 1080, new ScreenRect(5, 7, 1371, 2160)), (3840, 2160, new ScreenRect(0, 0, 640, 360)) })
        {
            var d = ScreenRegionGrabber.Fit(fw, fh, r.Width, r.Height);
            var inside = d.X >= 0 && d.Y >= 0 && d.X + d.Width <= fw && d.Y + d.Height <= fh;
            var fills = d.Width == fw || d.Height == fh;
            var aspect = Math.Abs(d.Width * (long)r.Height - d.Height * (long)r.Width) <= Math.Max(r.Width, r.Height);
            var centred = Math.Abs(fw - d.Width - 2 * d.X) <= 1 && Math.Abs(fh - d.Height - 2 * d.Y) <= 1;
            Check($"live video region: {r.Width}x{r.Height} fits whole into {fw}x{fh}", inside && fills && aspect && centred, $"{d}");
        }

        // A real window: the region rectangle matches the window's client area in physical pixels at this display's scaling.
        var root = new System.Windows.Controls.Grid();
        var window = new System.Windows.Window { Content = root, Width = 640, Height = 360, Left = 40, Top = 40, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rect = Views.Video.VideoRecordUi.ScreenRectOf(root);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            GetClientRect(hwnd, out var client); var origin = new Win32Point(); ClientToScreen(hwnd, ref origin);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
            Check($"live video region: the window rectangle is the client area in physical pixels (scale {dpi:0.##})",
                rect is { } q && Math.Abs(q.X - origin.X) <= 1 && Math.Abs(q.Y - origin.Y) <= 1 && Math.Abs(q.Width - client.Right) <= 1 && Math.Abs(q.Height - client.Bottom) <= 1,
                $"region {rect}, client {origin.X},{origin.Y} {client.Right}x{client.Bottom}");
        }
        finally { window.Close(); }

        // The encoder takes the portrait sizes.
        foreach (var (w, h) in new[] { (1080, 1920), (2160, 3840) })
        {
            var path = Path.Combine(Path.GetTempPath(), $"tf-portrait-{Guid.NewGuid():N}.mp4");
            try
            {
                var hardware = EncodeTestClip(path, w, h, 60, 0.5, live: false, out _);
                var (frames, audio) = ReadBackMp4(path);
                Check($"live video region: a {w}x{h} portrait clip encodes and reads back (hardware encoder: {hardware})", Math.Abs(frames - 30) <= 1 && audio, $"frames {frames}, audio {audio}");
            }
            finally { try { File.Delete(path); } catch (IOException) { } }
        }
    }

    private struct Win32Point { public int X, Y; }
    private struct Win32Rect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Win32Rect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Win32Point point);

    private static bool ThrowsNoAudio(string path)
    {
        var session = LiveVideoSession.Start(path, 1920, 1080, 30, _ => true);
        try { session.Finish(); return false; }
        catch (VideoEncoderException e) { return e.Message.Contains("No audio") && !File.Exists(path); }
    }
}
