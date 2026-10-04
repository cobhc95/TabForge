using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using Trace = TabForge.Services.Trace;

namespace TabForge.Diagnostics;

// Owns: `--speed-audit <report.md>` (with --profile <scratch folder>): drives the real off-screen main window on the demo song plus a long
//   generated audio clip, times every common action (time to dispatcher idle, synchronous UI work, worst frame gap, "ui" trace lines) in the
//   stopped and playing states, and writes a markdown table. Master volume is 0 for the run; the song is a copy in the report folder.
// Does not own: any behaviour; the actions go through the window's own commands and the simulation seams (private ones through reflection).
// Tests: none (diagnostics only).
internal sealed partial class WindowProbes
{
    /// <summary>Runs the speed audit after the window is up, writes <paramref name="reportPath"/>, then exits.</summary>
    public void RunSpeedAudit(string reportPath)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var exit = 0;
            var full = Path.GetFullPath(reportPath);
            var runner = new CaptureRun(this, Path.GetDirectoryName(full)!);
            try { await runner.SpeedAuditAsync(full); }
            catch (Exception ex) { exit = 1; runner.Log($"FAILED: {ex.GetBaseException()}"); }
            finally
            {
                try { runner.Finish(); } catch (Exception ex) { Debug.WriteLine($"Speed audit clean-up failed: {ex}"); }
                DialogHost.Capture = null;
                ContextMenuCapture = null;
                _confirmOnClose = false;
                Application.Current.Shutdown(exit);
            }
        }));
    }

    private sealed partial class CaptureRun
    {
        private sealed record SpeedRow(string Action, string State, List<double> Idle, List<double> Sync, List<double> Gap, Dictionary<string, double> Traces, string Note);

        private readonly List<SpeedRow> _speed = new();
        private double _gapMax;
        private long _lastFrame;
        private bool _timing;
        private TaskCompletionSource? _nextFrame;
        private string _state = "stopped";
        private AudioClip? _longClip;
        private int _audioTrack = -1;

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static object? Call(object target, string name, params object?[] args)
        {
            var method = target.GetType().GetMethods(Any).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length)
                         ?? throw new MissingMethodException(target.GetType().Name, name);
            return method.Invoke(target, args);
        }

        private static object? Get(object target, string field) =>
            (target.GetType().GetField(field, Any) ?? throw new MissingFieldException(target.GetType().Name, field)).GetValue(target);

        private bool Hotkey(string id) => Call(_w.Window, "RunHotkey", id) is true;

        private DocumentSession Doc => _w._documents.Active;

        private void OnFrame(object? sender, EventArgs e)
        {
            var now = Stopwatch.GetTimestamp();
            if (_timing && _lastFrame != 0) _gapMax = Math.Max(_gapMax, Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
            _lastFrame = now;
            _nextFrame?.TrySetResult();
        }

        public async Task SpeedAuditAsync(string reportPath)
        {
            if (!UserPaths.IsProfile || UserPaths.ProfileIsRealUserFolder)
                throw new InvalidOperationException("--speed-audit needs --profile <scratch folder> (never the real user folder).");
            _name = Path.GetFileNameWithoutExtension(reportPath);
            await _w.Settle(1200);
            SetSize(new Size(1600, 1000));
            ApplyTheme("dark");
            _w._settings.Audio.MasterVolume = 0;   // the playing state is measured silently
            var demo = FindDemo();
            var gp5 = Path.Combine(Path.GetDirectoryName(demo)!, Path.GetFileNameWithoutExtension(demo) + ".gp5");
            var copy = Path.Combine(_out, "speed-audit-song" + Path.GetExtension(File.Exists(gp5) ? gp5 : demo));
            File.Copy(File.Exists(gp5) ? gp5 : demo, copy, overwrite: true);
            OpenSong(copy);
            await _w.Settle(1500);
            AddLongAudioTrack(180);
            await _w.Settle(1500);
            DialogHost.Capture = _ => false;              // any modal question answers "cancel" unless an action says otherwise
            ContextMenuCapture = m => { m.ApplyTemplate(); m.Measure(new Size(800, 1400)); };   // menus are built and measured, never opened
            CompositionTarget.Rendering += OnFrame;       // keeps frames coming, so a gap means the UI thread was busy
            try
            {
                await StoppedAndPlayingAsync("stopped");
                _state = "playing";
                await EnsurePlayingAsync();
                await StoppedAndPlayingAsync("playing");
                _w.StopPlayback();
                _state = "stopped";
                await _w.Settle(500);
                await StoppedOnlyAsync();
            }
            finally { CompositionTarget.Rendering -= OnFrame; }
            File.WriteAllText(reportPath, SpeedReport(), new UTF8Encoding(false));
        }

        private void AddLongAudioTrack(int seconds)
        {
            _w.Window.AddAudioTrack();
            var track = _w._project.Tracks[^1];
            var path = Path.Combine(_out, "speed-audit-clip.wav");
            WriteExampleWav(path, seconds);
            _longClip = new AudioClip { Name = "Long clip", File = path, StartSec = _w.SongClock.BarStartSec(_w._project, 1), SourceLengthSec = seconds, FileLengthSec = seconds };
            track.AudioClips.Add(_longClip);
            _audioTrack = _w._project.Tracks.IndexOf(track);
            _w.RefreshTracks(); _w.RefreshArrangement();
            _w.TrackMixerGrid.SelectedIndex = 0;
        }

        private async Task EnsurePlayingAsync()
        {
            if (_state != "playing" || _w._isPlayingVisual) return;
            _w.PlayFromStart();
            await _w.Settle(600);
        }

        // ---------- measurement ----------

        private async Task TimeAsync(string action, Func<object?> act, Func<Task>? after = null, int reps = 5, string note = "")
        {
            // TABFORGE_SPEED_ONLY="open Preferences;Delete bars" times only the actions whose names contain one of the fragments.
            var only = Environment.GetEnvironmentVariable("TABFORGE_SPEED_ONLY");
            if (!string.IsNullOrWhiteSpace(only) && !only.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(f => action.Contains(f.Trim(), StringComparison.OrdinalIgnoreCase))) return;
            var row = new SpeedRow(action, _state, new(), new(), new(), new(), note);
            var trace = Trace.PathFor("ui");
            for (var i = 0; i < reps; i++)
            {
                await EnsurePlayingAsync();
                await _w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(60);
                var traceStart = File.Exists(trace) ? new FileInfo(trace).Length : 0;
                _gapMax = 0; _lastFrame = Stopwatch.GetTimestamp(); _timing = true;
                var t0 = Stopwatch.GetTimestamp();
                double sync, idle;
                try
                {
                    var result = act();
                    sync = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    if (result is Task task) await task;
                    if (result is false && i == 0) row = row with { Note = (row.Note + " command not handled").Trim() };
                    await _w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    idle = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    // A short tail catches debounced work (timers) and the frame that shows the result.
                    _nextFrame = new TaskCompletionSource();
                    await Task.WhenAny(_nextFrame.Task, Task.Delay(250));
                    await Task.Delay(200);
                    await _w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }
                catch (Exception ex)
                {
                    _timing = false;
                    Log($"{action} ({_state}) rep {i}: {ex.GetBaseException().Message}");
                    _speed.Add(row with { Note = "FAILED: " + ex.GetBaseException().Message });
                    return;
                }
                _timing = false;
                row.Sync.Add(sync); row.Idle.Add(idle); row.Gap.Add(_gapMax);
                ReadTrace(trace, traceStart, row.Traces);
                if (after is not null) { try { await after(); } catch (Exception ex) { Log($"{action} after: {ex.GetBaseException().Message}"); } }
            }
            _speed.Add(row);
        }

        private static void ReadTrace(string path, long start, Dictionary<string, double> into)
        {
            if (!File.Exists(path)) return;
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length <= start) return;
                stream.Seek(start, SeekOrigin.Begin);
                text = new StreamReader(stream).ReadToEnd();
            }
            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length > 13) line = line[13..];   // "HH:mm:ss.fff "
                var m = System.Text.RegularExpressions.Regex.Match(line, @"(\d+(?:\.\d)?) ms");
                var ms = m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                var key = System.Text.RegularExpressions.Regex.Replace(line, @"\d+(?:\.\d)? ms", "# ms");
                if (key.StartsWith("FRAME gap", StringComparison.Ordinal)) continue;   // measured directly
                into[key] = Math.Max(into.GetValueOrDefault(key), ms);
            }
        }

        private static double Median(List<double> v) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }

        private string SpeedReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Speed audit (raw)");
            sb.AppendLine();
            sb.AppendLine($"Song: demo copy plus a 180 s audio clip; window 1600x1000 off-screen; ui trace {(Trace.IsOn("ui") ? "on" : "OFF (set TABFORGE_TRACE=ui)")}; {Environment.ProcessorCount} logical CPUs.");
            sb.AppendLine("Idle = action start to dispatcher ApplicationIdle; sync = the call itself; gap = worst gap between rendered frames until 450 ms after idle.");
            sb.AppendLine();
            sb.AppendLine("| Action | State | Idle median ms | Idle worst ms | Sync median ms | Worst frame gap ms | Flag | Note |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---|---|");
            foreach (var r in _speed)
            {
                var flag = (r.Idle.Count > 0 && Median(r.Idle) > 50 ? "IDLE " : "") + (r.Gap.Count > 0 && r.Gap.Max() > 33 ? "GAP" : "");
                sb.AppendLine($"| {r.Action} | {r.State} | {Median(r.Idle):0.0} | {(r.Idle.Count > 0 ? r.Idle.Max() : 0):0.0} | {Median(r.Sync):0.0} | {(r.Gap.Count > 0 ? r.Gap.Max() : 0):0} | {flag.Trim()} | {r.Note} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Trace lines per action (slowest first, max ms over the repetitions)");
            foreach (var r in _speed.Where(r => r.Traces.Count > 0))
            {
                sb.AppendLine($"- **{r.Action}** ({r.State})");
                foreach (var (line, ms) in r.Traces.OrderByDescending(p => p.Value).Take(6)) sb.AppendLine($"  - {ms:0.0} ms: `{line}`");
            }
            sb.AppendLine();
            sb.AppendLine("## Log");
            sb.AppendLine("```");
            sb.Append(_log);
            sb.AppendLine("```");
            return sb.ToString();
        }
    }
}
