using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Window probes, recording probe: `TabForge.exe <song> --probe-record <report.txt>` records MIDI into the song while it
// plays, without needing a keyboard: timed messages go through the same handler the MIDI driver uses. It then checks
// the MIDI clip, its lane, playback of the clip through the MIDI timeline, the conversion to notation, and a loop
// recording (takes on separate lanes, the newest playing). The window closes afterwards.
internal sealed partial class WindowProbes : MainWindow.ProbeAccess
{
    public void RunRecordingProbe(string reportPath)
    {
        var path = FilePathPolicy.OutputFile(reportPath, "recording probe report");
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var report = new System.Text.StringBuilder();
            var failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? " -> " + detail : "")}");
                if (!ok) failures++;
            }
            try
            {
                await Task.Delay(2000);
                var track = _project.Tracks[0];
                track.AudioInput = AudioInputs.Midi;
                ToggleArm(track);
                Check("a track can be armed with the MIDI input", track.RecordArm && AudioInputs.IsMidi(track.AudioInput));

                async Task Play(int pitch, int velocity, int holdMs)
                {
                    Recorder.OnMidiInput(0x90, pitch, velocity, Stopwatch.GetTimestamp());
                    await Task.Delay(holdMs);
                    Recorder.OnMidiInput(0x80, pitch, 0, Stopwatch.GetTimestamp());
                }

                // ---- a plain take ----
                ToggleRecording();
                Check("Record starts playback and recording", IsRecording && _midi.IsPlaying);
                await Task.Delay(600);
                await Play(64, 100, 250); await Task.Delay(150);
                await Play(59, 90, 250); await Task.Delay(150);
                await Play(55, 80, 250);
                await Task.Delay(300);
                Check("a live take is drawn while recording", Arrangement.LiveTakes.Any(t => t.Midi && t.Notes.Count >= 2), $"{Arrangement.LiveTakes.Count} take(s)");
                ToggleRecording();
                await Task.Delay(600);
                var clips = track.AudioClips.Where(c => c.IsMidi).ToList();
                Check("stopping creates one MIDI clip", clips.Count == 1, $"{clips.Count}");
                if (clips.Count == 1)
                {
                    var clip = clips[0];
                    Check("the clip holds the three notes played", clip.Notes!.Count == 3 && clip.Notes.Select(n => n.Pitch).OrderBy(p => p).SequenceEqual(new[] { 55, 59, 64 }),
                        string.Join(",", clip.Notes.Select(n => n.Pitch)));
                    Check("note lengths and velocities were kept", clip.Notes.All(n => n.LengthSec is > 0.15 and < 0.5) && clip.Notes.Any(n => n.Velocity == 100),
                        string.Join(" ", clip.Notes.Select(n => $"{n.LengthSec:0.00}s/v{n.Velocity}")));
                    Check("the clip sits on lane 0", clip.Lane == 0);
                    var timeline = TabForge.Playback.MidiTimelineBuilder.Build(_project, new TabForge.Playback.PlaybackOptions { RespectMuteSolo = false });
                    var withClip = timeline.Events.Count(e => e.IsNoteOn && e.TrackIndex == 0);
                    var without = TabForge.Playback.MidiTimelineBuilder.Build(_project, new TabForge.Playback.PlaybackOptions { RespectMuteSolo = false, SkipClips = true }).Events.Count(e => e.IsNoteOn && e.TrackIndex == 0);
                    Check("the MIDI clip plays with the song (three more notes on the track)", withClip - without == 3, $"{without} -> {withClip}");
                    Check("the take plays back at its own pitches", timeline.Events.Where(e => e.IsNoteOn && e.TrackIndex == 0).Select(e => e.Data1).Intersect(new[] { 55, 59, 64 }).Count() == 3);
                }

                // ---- MIDI clip into notation ----
                var before = _project.Tracks[0].Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count));
                if (clips.Count == 1)
                {
                    var written = MidiClipToTab.Write(_project, _project.Tracks[0], clips[0], sec => SongClock.BarAt(_project, sec));
                    var after = _project.Tracks[0].Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count));
                    Check("the clip converts to notation", written == 3, $"{written} notes written; track notes {before} -> {after}");
                }

                // ---- loop recording: two passes on separate lanes ----
                track.AudioClips.Clear(); track.Lanes.Clear();
                SetLoopActive(false);
                ApplyLoopRange(1, 1, 0, -1);   // bar 2 only
                SetLoopActive(true);
                ClipsChanged(true);
                StopPlayback();
                await Task.Delay(300);
                ToggleRecording();
                var recordLoop = Recorder.RecordLoop;
                var loopMs = (int)((recordLoop?.End - recordLoop?.Start) * 1000 ?? 1000);
                Check("the loop range is known while recording", recordLoop is not null, recordLoop is null ? "" : $"{recordLoop.Value.Start:0.00}-{recordLoop.Value.End:0.00} s");
                var songTimes = new List<string> { $"start bar {Editor.SelectedMeasure + 1}" };
                var real = Stopwatch.StartNew();
                string Now() { var s = SongClock.SecAt(Stopwatch.GetTimestamp()); return (double.IsNaN(s) ? "-" : $"{s:0.00}") + $" (real {real.ElapsedMilliseconds} ms, lap {Recorder.RecordLaps})"; }
                // One note per pass, following the song clock's loop wraps rather than wall time: the loop does not always play at
                // 100 % (the loop speed trainer starts slower, e.g. at 50 %), so a pass can last longer than the loop's song seconds.
                for (var pass = 0; pass < 2; pass++)
                {
                    if (pass > 0)
                    {
                        var lap = Recorder.RecordLaps;
                        var wait = Stopwatch.StartNew();
                        while (Recorder.RecordLaps == lap && wait.ElapsedMilliseconds < 20000) await Task.Delay(20);
                        await Task.Delay(150);
                    }
                    else await Task.Delay(Math.Max(200, loopMs / 3));
                    songTimes.Add($"note {pass + 1} @ {Now()} s");
                    await Play(60 + pass, 100, 120);
                }
                await Task.Delay(200);
                songTimes.Add($"stop @ {Now()} s");
                ToggleRecording();
                await Task.Delay(600);
                var takes = track.AudioClips.Where(c => c.IsMidi).ToList();
                Check("loop recording made one take per pass", takes.Count >= 2,
                    $"{takes.Count} take(s), lanes {string.Join(",", takes.Select(t => t.Lane))}; {string.Join(", ", songTimes)}");
                if (takes.Count >= 2)
                {
                    Check("each take is on its own lane", takes.Select(t => t.Lane).Distinct().Count() == takes.Count);
                    var newest = takes.Max(t => t.Lane);
                    Check("the newest take's lane plays, older MIDI lanes grey out", ClipLanes.Plays(track, newest) && takes.Where(t => t.Lane != newest).All(t => !ClipLanes.Plays(track, t.Lane)));
                }
                SetLoopActive(false);
            }
            catch (Exception ex) { report.AppendLine($"probe failed: {ex}"); failures++; }
            report.AppendLine(failures == 0 ? "recording probe: all checks passed" : $"recording probe: {failures} failure(s)");
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(failures == 0 ? 0 : 1);
        }));
    }
}
