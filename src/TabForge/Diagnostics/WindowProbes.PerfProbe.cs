using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Window probes, resource probe: `TabForge.exe <song> --perf-follow <report.txt>` plays the song in each score
// follow style and records the process CPU and memory, so a change to following is measured, not guessed.
internal sealed partial class WindowProbes : MainWindow.ProbeAccess
{
    public void RunFollowPerfProbe(string reportPath)
    {
        var path = FilePathPolicy.OutputFile(reportPath, "performance report");
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var report = new System.Text.StringBuilder();
            try
            {
                await Task.Delay(2000);
                foreach (var (horizontal, continuous) in new[] { (false, false), (false, true), (true, false), (true, true) })
                {
                    SetHorizontalScoreView(horizontal);
                    _settings.Follow.ContinuousScroll = continuous;
                    _follow.ApplySettings(_settings.Follow);
                    await Task.Delay(800);
                    PlayFromStart();
                    await Task.Delay(1500); // warm-up
                    var process = Process.GetCurrentProcess();
                    process.Refresh();
                    var cpuBefore = process.TotalProcessorTime;
                    var clock = Stopwatch.StartNew();
                    await Task.Delay(8000);
                    process.Refresh();
                    var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
                StopPlayback();
                    report.AppendLine($"{(horizontal ? "horizontal" : "vertical  ")} {(continuous ? "continuous" : "page turn ")}: " +
                                      $"CPU {cpu:0.0}% of the machine, working set {process.WorkingSet64 / 1048576.0:0} MB");
                    await Task.Delay(500);
                }
            }
            catch (Exception ex) { report.AppendLine($"probe failed: {ex.GetBaseException().Message}"); }
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }

    /// <summary>`--probe-countin <report>`: turn count-in on through the real button handler, press Play the
    /// normal way, and record what the engine actually sent (count-in = channel-10 clicks before the music).</summary>
    public void RunCountInProbe(string reportPath)
    {
        var path = FilePathPolicy.OutputFile(reportPath, "count-in report");
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var report = new System.Text.StringBuilder();
            try
            {
                await Task.Delay(1500);
                if (int.TryParse(Environment.GetEnvironmentVariable("TF_PROBE_BAR"), out var probeBar)) Editor.SetPosition(probeBar, 0, Editor.SelectedString);
                if (!_countIn) CountIn_Click(Window, new RoutedEventArgs());
                report.AppendLine($"countIn={_countIn} onlyAtStart={_settings.Audio.CountInOnlyAtSongStart} bars={_settings.Audio.CountInBars} selectedBar={Editor.SelectedMeasure}");
                var options = BuildOptions();
                report.AppendLine($"options: CountIn={options.CountIn} CountInBars={options.CountInBars} Metronome={options.Metronome} StartBar={options.StartBar}");
                _midi.StartDiagnostics();
                TogglePlayback();
                await Task.Delay(2500);
                var log = _midi.DispatchLog;
                var first = log.FirstOrDefault(r => r.IsNoteOn && (r.Status & 0x0F) != 9);
                var clicks = log.Where(r => r.IsNoteOn && (r.Status & 0x0F) == 9).ToList();
                report.AppendLine($"channel-10 clicks={clicks.Count} firstClickMs={(clicks.Count > 0 ? clicks[0].StreamMs : -1):0} firstNoteMs={(first.Status != 0 ? first.StreamMs : -1):0} timelineCountInMs={_timeline?.CountInMs ?? -1:0}");
                StopPlayback();
            }
            catch (Exception ex) { report.AppendLine($"probe failed: {ex}"); }
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }
}