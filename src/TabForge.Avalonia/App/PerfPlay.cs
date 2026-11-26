using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TabForge.Avalonia.Transport;

namespace TabForge.Avalonia;

// Owns: the internal playback benchmark (TABFORGE_INTERNAL=1, --perf-play <seconds> --profile <scratch> <song>): the real app and engine, the
//     window off screen, the start-up song played for the given time, then frame, GC and memory figures written to <scratch>\perf-play.txt and exit.
//     PERF_HIDE=TypeName,... hides those controls first, to attribute a cost to a view.
// Does not own: CPU sampling of the processes (the caller measures them), the window, the playback.
// Threads: UI thread.
internal static class PerfPlay
{
    public const string Argument = "--perf-play";

    /// <summary>The benchmark length in seconds when this run is one, else null.</summary>
    public static int? Seconds(string[] args)
    {
        if (Environment.GetEnvironmentVariable("TABFORGE_INTERNAL") != "1") return null;
        var at = Array.IndexOf(args, Argument);
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var s) ? s : null;
    }

    /// <summary>Keeps the benchmark window off every screen and out of the taskbar.</summary>
    public static void Prepare(MainWindow window)
    {
        window.WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.Manual;
        window.Position = new global::Avalonia.PixelPoint(-32000, -32000);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    /// <summary>Called once the start-up song is open: settles, plays for <paramref name="seconds"/>, writes the figures and shuts the app down.</summary>
    public static async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, AvaloniaPlaybackSessions sessions, int seconds)
    {
        var lines = new System.Collections.Generic.List<string>();
        try
        {
            await Task.Delay(4000);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if (Environment.GetEnvironmentVariable("PERF_HIDE") is { Length: > 0 } hide)
            {
                var names = hide.Split(',');
                foreach (var v in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<global::Avalonia.Controls.Control>().ToList())
                    if (names.Contains(v.GetType().Name)) { v.IsVisible = false; lines.Add("hid " + v.GetType().FullName); }
            }
            var session = sessions.For(window.Songs.Active) ?? throw new InvalidOperationException("no playback session");
            var frames = 0;
            var frameWatch = new Stopwatch();
            var gaps = new System.Collections.Generic.List<double>(seconds * 300);
            var last = 0.0;
            session.PlayheadFrame += () =>
            {
                frames++;
                var now = frameWatch.Elapsed.TotalMilliseconds;
                if (frames > 1) gaps.Add(now - last);
                last = now;
            };
            var proc = Process.GetCurrentProcess();
            var gc0 = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            session.PlayOrPause();
            await Task.Delay(1500);   // past the start
            frames = 0; gaps.Clear(); frameWatch.Restart();
            proc.Refresh();
            var cpu0 = proc.TotalProcessorTime;
            var allocStart = GC.GetTotalAllocatedBytes();
            File.WriteAllText(Path.Combine(TabForge.Services.UserPaths.Local, "perf-play.started"), DateTime.Now.ToString("O"));
            await Task.Delay(seconds * 1000);
            var elapsed = frameWatch.Elapsed.TotalSeconds;
            proc.Refresh();
            var cpu = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            gaps.Sort();
            double P(double q) => gaps.Count == 0 ? 0 : gaps[Math.Min(gaps.Count - 1, (int)(q * gaps.Count))];
            lines.Add($"playing={session.IsPlaying} seconds={elapsed:F1}");
            lines.Add($"ui_cpu_ms={cpu:F0} ui_cpu_pct_of_one_core={cpu / (elapsed * 10):F2} ui_cpu_pct_total={cpu / (elapsed * 10) / Environment.ProcessorCount:F3}");
            lines.Add($"cpu_ms_per_frame={cpu / Math.Max(1, frames):F2} frames={frames} fps={frames / elapsed:F1} gap_p50={P(0.5):F2} gap_p99={P(0.99):F2} gap_max={(gaps.Count > 0 ? gaps[^1] : 0):F2} gaps_over_20ms={gaps.Count(g => g > 20)}");
            lines.Add($"alloc_mb_per_s={(GC.GetTotalAllocatedBytes() - allocStart) / 1048576.0 / elapsed:F2} gc0={GC.CollectionCount(0) - gc0.Item1} gc1={GC.CollectionCount(1) - gc0.Item2} gc2={GC.CollectionCount(2) - gc0.Item3}");
            lines.Add($"ws_mb={proc.WorkingSet64 / 1048576.0:F1} private_mb={proc.PrivateMemorySize64 / 1048576.0:F1} managed_heap_mb={GC.GetTotalMemory(false) / 1048576.0:F1} gc_committed_mb={GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576.0:F1}");
            session.Stop();
        }
        catch (Exception ex) { lines.Add("error: " + ex); }
        try { File.WriteAllLines(Path.Combine(TabForge.Services.UserPaths.Local, "perf-play.txt"), lines); }
        catch (IOException ex) { Console.Error.WriteLine("perf-play: " + ex.Message); }
        Dispatcher.UIThread.Post(() => desktop.Shutdown(0));
    }
}
