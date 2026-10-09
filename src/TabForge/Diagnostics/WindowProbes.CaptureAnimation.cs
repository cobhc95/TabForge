using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the capture step "frames" (an animated sequence written as numbered PNGs plus <prefix>.frames.json with the time of each frame).
//   {"frames":"name","action":"playback|track-drag|section-drag|area-move|record|clip-edit|type-notes","p":[...],"count":40,"interval":50,"hold":8,
//    "target":"window","region":[x,y,w,h]}
// Each frame applies the action at progress t (0..1) through the simulation seams (no pressed mouse button needed), waits, and renders the target.
// Does not own: single shots (WindowProbes.CaptureShots.cs) or the GIF assembly (done outside the app from the PNGs).
// Tests: none (diagnostics only).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private async Task FramesAsync(string prefix, Dictionary<string, JsonElement> args)
        {
            string Str(string k, string d) => args.TryGetValue(k, out var v) ? v.GetString() ?? d : d;
            int Int(string k, int d) => args.TryGetValue(k, out var v) ? v.GetInt32() : d;
            var action = Str("action", "");
            var count = Int("count", 40);
            var interval = Int("interval", 50);
            var hold = Int("hold", 8);
            var p = args.TryGetValue("p", out var pv) && pv.ValueKind == JsonValueKind.Array ? pv.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0).ToArray() : Array.Empty<double>();
            if (Int("zoomout", 0) > 0) { _w.Arrangement.SimulateZoomOut(Int("zoomout", 0)); await _w.Settle(500); }
            var target = Str("target", "window");
            var element = Resolve(target);
            ScrubHardwareNames();
            JsonElement? region = args.TryGetValue("region", out var rg) ? rg : null;
            var frame = FrameAction(action, p, args);
            var writes = new List<Task>();
            var times = new List<long>();
            var clock = Stopwatch.StartNew();
            var total = count + hold;
            for (var i = 0; i < total; i++)
            {
                var t = Math.Clamp(i / (double)Math.Max(1, count - 1), 0, 1);
                if (i < count) frame.Step(i, t); else frame.Hold(i - count);
                await _w.Settle(interval);
                RenderTreeFresh(element);
                var bitmap = Render(element, element.ActualWidth, element.ActualHeight, region);
                times.Add(clock.ElapsedMilliseconds);
                var path = FilePathPolicy.OutputFile(Path.Combine(_out, $"{prefix}_{i:D3}.png"), "animation frame", ".png");
                writes.Add(Task.Run(() =>
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(path);
                    encoder.Save(file);
                }));
            }
            frame.End();
            await Task.WhenAll(writes);
            File.WriteAllText(Path.Combine(_out, prefix + ".frames.json"), JsonSerializer.Serialize(times), new UTF8Encoding(false));
            Log($"frames {prefix}: {total} frames in {clock.ElapsedMilliseconds} ms ({action})");
        }

        private sealed record FrameScript(Action<int, double> Step, Action<int> Hold, Action End);

        private static double Ease(double t) => t * t * (3 - 2 * t);

        private FrameScript FrameAction(string action, double[] p, Dictionary<string, JsonElement> args)
        {
            var w = _w;
            var panel = w.Arrangement;
            switch (action)
            {
                case "playback":   // p = [from, to] as bar + fraction (1-based, e.g. 5.0 .. 7.5)
                {
                    var project = w._project;
                    void At(double pos)
                    {
                        var bar = (int)Math.Floor(pos);
                        var frac = pos - bar;
                        var start = w.SongClock.BarStartSec(project, bar - 1);
                        var end = w.SongClock.BarStartSec(project, bar);
                        w._playheadMs = (start + frac * (end - start)) * 1000;
                        w._isPlayingVisual = true;
                        SetPlayhead(bar, frac);
                        w.RefreshInstrument();
                    }
                    return new FrameScript((_, t) => At(p[0] + (p[1] - p[0]) * t), _ => { }, () => w._isPlayingVisual = false);
                }
                case "track-drag":   // p = [from, to] (0-based track rows)
                {
                    int from = (int)p[0], to = (int)p[1];
                    var y0 = panel.TrackRowCentreY(from);
                    var y1 = panel.TrackRowCentreY(to);
                    var started = false;
                    return new FrameScript(
                        (i, t) =>
                        {
                            if (!started) { panel.SimulateTrackDragStart(from); started = true; }
                            panel.SimulateTrackDragMove(y0 + (y1 - y0) * Ease(t));
                        },
                        hold => { if (hold == 0) panel.SimulateTrackDragEnd(); }, () => { });
                }
                case "section-drag":   // p = [section index, section index to land before] (0-based)
                {
                    var hits = panel.TimelineForTest.SectionHits();
                    int from = (int)p[0], to = (int)p[1];
                    var x0 = hits[from].Bounds.X + hits[from].Bounds.Width / 2;
                    var x1 = to < hits.Count ? hits[to].Bounds.X + 4 : hits[^1].Bounds.Right;
                    var started = false;
                    return new FrameScript(
                        (i, t) =>
                        {
                            if (!started) { panel.SimulateSectionDragStart(from, x0); started = true; }
                            panel.SimulateSectionDragMove(x0 + (x1 - x0) * Ease(t));
                        },
                        hold => { if (hold == 0) panel.SimulateSectionDragEnd(); }, () => { });
                }
                case "area-move":   // p = [first bar, last bar, insert before bar] (1-based); the bars must be selected
                {
                    var tl = panel.TimelineForTest;
                    int first = (int)p[0] - 1, last = (int)p[1] - 1, drop = (int)p[2] - 1;
                    var x0 = (tl.XOfBar(first) + tl.XOfBar(last + 1)) / 2;
                    var x1 = tl.XOfBar(drop);
                    var begun = false;
                    return new FrameScript(
                        (i, t) =>
                        {
                            if (!begun) { panel.BeginAreaMove(first, last); begun = true; }
                            tl.AreaMove.PointerMoved(x0 + (x1 - x0) * Ease(t));
                        },
                        hold => { if (hold == 0) tl.AreaMove.Finish(tl.AreaMove.Target); }, () => { });
                }
                case "record":   // p = [start bar (1-based), seconds]: arms a fresh audio track and grows a take with incoming input
                {
                    w.Window.AddAudioTrack();
                    var track = w._project.Tracks[^1];
                    _cleanup.Add(() => { w._project.Tracks.Remove(track); w.RefreshTracks(); w.RefreshArrangement(); });
                    track.RecordArm = true;
                    w.RefreshTracks(); w.RefreshArrangement();
                    var start = w.SongClock.BarStartSec(w._project, (int)p[0] - 1);
                    var take = new LiveTake { Track = track, StartSec = start, EndSec = start };
                    panel.LiveTakes.Add(take);
                    var rng = new Random(7);
                    return new FrameScript((i, t) =>
                    {
                        take.EndSec = start + p[1] * t;
                        var env = 0.35 + 0.55 * Math.Abs(Math.Sin(t * 9)) * (0.6 + 0.4 * Math.Sin(t * 31));
                        for (var k = 0; k < 3; k++) take.Peaks.Add((float)Math.Clamp(env * (0.6 + 0.4 * rng.NextDouble()), 0.02, 1));
                        panel.RefreshLiveTakes();
                        SetPlayhead((int)p[0], 0);
                    }, _ => { }, () => { panel.LiveTakes.Clear(); panel.RefreshLiveTakes(); track.RecordArm = false; });
                }
                case "clip-edit":   // needs "audio-track" first: split the clip, then drag the fade handles out
                {
                    var track = w._project.Tracks[_track];
                    var clip = track.AudioClips[0];
                    var split = false;
                    return new FrameScript((i, t) =>
                    {
                        w.Arrangement.SelectedClip = clip;
                        if (t >= 0.3 && !split)
                        {
                            ClipSplitGlue.Split(track, clip, clip.StartSec + clip.SourceLengthSec * 0.55);
                            split = true;
                        }
                        if (split)
                        {
                            var u = Ease(Math.Clamp((t - 0.3) / 0.7, 0, 1));
                            ClipSplitGlue.SetFades(clip, 1.5 * u, 0);
                            if (track.AudioClips.Count > 1) ClipSplitGlue.SetFades(track.AudioClips[^1], 0, 1.8 * u);
                        }
                        w.RefreshArrangement();
                        w.Arrangement.SelectedClip = clip;
                    }, _ => { }, () => { });
                }
                case "type-notes":   // p = [bar (1-based), duration denominator]; args "notes": [[string, fret], ...] typed one per two frames, the cursor advancing after each
                {
                    var notes = args["notes"].EnumerateArray().Select(e => (s: e[0].GetInt32(), f: e[1].GetInt32())).ToArray();
                    var begun = false;
                    return new FrameScript((i, t) =>
                    {
                        if (!begun)
                        {
                            w.Editor.SetPosition((int)p[0] - 1, 0, notes[0].s, seekPlayback: false);
                            if (p.Length > 1) w.Editor.Effects.SetDuration((int)p[1]);
                            begun = true;
                        }
                        if (i % 2 != 0 || i / 2 >= notes.Length) return;
                        var (s, f) = notes[i / 2];
                        w.Editor.SetPosition(w.Editor.SelectedMeasure, w.Editor.SelectedCell, s, seekPlayback: false);
                        foreach (var digit in f.ToString()) w.Editor.Effects.EnterFret(digit - '0', autoAdvance: false);
                        w.Editor.MoveBeat(1);
                    }, _ => { }, () => { });
                }
                case "mixer":   // p = [track]: the open Mixer window's fader moves down and up, and pans
                {
                    var mixer = _tools.TryGetValue("Mixer", out var mw) ? mw as MixerWindow : null;
                    var track = w._project.Tracks[(int)p[0]];
                    var volume = track.Volume;
                    bool muteHit = false, soloHit = false;
                    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
                    {
                        for (var k = 0; k < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); k++)
                        {
                            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, k);
                            if (c is T hit) yield return hit;
                            foreach (var d in Descendants<T>(c)) yield return d;
                        }
                    }
                    void Press(string name)   // a real click on the track's strip button, raised the way a mouse click is
                    {
                        if (mixer is null) return;
                        var strip = Descendants<FrameworkElement>(mixer).FirstOrDefault(e => System.Windows.Automation.AutomationProperties.GetName(e).StartsWith($"Mixer strip: {track.Name}"));
                        var button = strip is null ? null : Descendants<System.Windows.Controls.Button>(strip).FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
                        button?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    return new FrameScript((i, t) =>
                    {
                        track.Volume = (int)Math.Round(volume * (1 - 0.45 * Math.Sin(Math.Clamp(t / 0.6, 0, 1) * Math.PI)));
                        track.Pan = (int)Math.Round(64 + 50 * Math.Sin(t * Math.PI * 2));
                        mixer?.SyncValues();
                        if (!muteHit && t >= 0.55) { muteHit = true; Press("Mute"); }
                        if (!soloHit && t >= 0.8) { soloHit = true; Press("Solo"); }
                    }, _ => { }, () => { });
                }
                default: throw new InvalidOperationException($"unknown frames action '{action}'");
            }
        }
    }
}
