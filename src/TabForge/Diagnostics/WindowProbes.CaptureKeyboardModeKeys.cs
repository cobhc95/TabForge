using TabForge.Audio;
using System.Windows;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Diagnostics;

// Owns: the capture variants "keys" and "keys-wait" of the step "learn-playing": the selected track becomes a keyboard track with a two-hand riff (in memory only), the Keyboard mode pane plays it at a bar and
//   a fraction of it, and the keyboard view shows made-up play-along state: held right and extra keys, a score and a grade flash, or for "keys-wait" the cue and the awaited key.
// Does not own: step dispatch (WindowProbes.Capture.cs, WindowProbes.CaptureExtras.cs) or the view (KeyboardMode/KeyboardModeView.cs).
// Tests: none (diagnostics only; run through --capture).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private sealed class ProbeTransport : IKeyboardModeWaitHost
        {
            public bool IsPlaying { get; private set; } = true;
            public void Pause() => IsPlaying = false;
            public void Resume() => IsPlaying = true;
        }

        /// <summary>A MIDI device set with one example keyboard that never sends: captures show a connected device and never open the real ones.</summary>
        private sealed class ExampleMidiDevices : IMidiDeviceSet
        {
            private static readonly string[] Names = { "Example MIDI keyboard" };
            public event Action<int, int, int, long>? Message { add { } remove { } }
            public event Action<string, int, int, int, long>? DeviceMessage { add { } remove { } }
            public bool IsOpen { get; private set; }
            public IReadOnlyList<string> OpenNames => IsOpen ? Names : Array.Empty<string>();
            public IReadOnlyList<string> ListNames() => Names;
            public bool Rescan() => false;
            public int Open() { IsOpen = true; return 1; }
            public void Close() => IsOpen = false;
            public void Dispose() => Close();
        }

        /// <summary>Keyboard mode listens to the example device set (set before the first frame of a capture).</summary>
        private void UseExampleMidi() => _w.Window.Learn.HubOverride ??= new MidiInputHub(new ExampleMidiDevices());

        /// <summary>The MIDI input list of the Keyboard mode control bar as it shows when opened, rendered from the combo box's own drop-down without opening a popup (then <c>target: "menu"</c>).</summary>
        private void KeyboardModeMidiMenu()
        {
            if (_w.Window.KeyboardModePane.ControlBar is not KeyboardModeControlBar bar) throw new InvalidOperationException("the Keyboard mode pane has no control bar");
            var combo = bar.MidiBox;
            bar.FillMidi();
            combo.ApplyTemplate();
            var popup = combo.Template.FindName("PART_Popup", combo) as System.Windows.Controls.Primitives.Popup ?? throw new InvalidOperationException("the combo box has no drop-down");
            _menu = "learn-midi";
            _lastMenu = RenderHost(popup, () =>
            {
                var child = popup.Child as FrameworkElement ?? throw new InvalidOperationException("the drop-down has no content");
                popup.Child = null;
                child.MinWidth = combo.ActualWidth;
                return (child, () => popup.Child = child);
            });
        }

        /// <summary>The tool window "KeyboardModePopout": Keyboard mode on (run "learn-playing" with "keys" first) and the keyboard view in its own window, off-screen and not activated.</summary>
        private void KeyboardModePopoutShot()
        {
            var window = _w.Window;
            window.KeyboardMode.Enter();
            window.Learn.RequestPopout();
            if (Application.Current.Windows.OfType<KeyboardModePopoutWindow>().FirstOrDefault() is not { } popout) throw new InvalidOperationException("the pop-out needs a keyboard track (run learn-playing with \"keys\" first)");
            popout.ShowInTaskbar = false;
            popout.Left = CaptureOffscreen; popout.Top = CaptureOffscreen;
            _tools["KeyboardModePopout"] = popout;
        }

        /// <summary>Writes the riff over four bars from <paramref name="bar"/>, shows the Keyboard mode pane playing it and fills the keyboard view with made-up play-along state.</summary>
        private void KeyboardModeKeysPlaying(int bar, double fraction, bool waiting)
        {
            var track = _w._project.Tracks[_track];
            track.Kind = TrackKind.Keys;
            WriteKeysRiff(track, bar);
            var timeline = MidiTimelineBuilder.Build(_w._project, new PlaybackOptions());
            var span = timeline.Bars[Math.Clamp(bar, 0, timeline.Bars.Count - 1)];
            var ms = span.StartMs + fraction * (span.EndMs - span.StartMs);
            var source = KeyboardNoteSource.Build(timeline, _w._project, _track);
            var expected = source.Expected();
            var judge = new KeyboardModeJudge(expected);
            var feedback = new KeyboardFeedback();
            var score = new KeyboardModeScore();
            var now = ms / 1000;
            // Notes before now were played a little off the beat, one was missed; the last of them gives the grade flash.
            var past = expected.Where(e => e.OnsetSec < now - 0.25).ToList();
            for (var i = 0; i < past.Count; i++)
            {
                if (i == past.Count - 1) { judge.Advance(past[i].OnsetSec); score.Absorb(judge); feedback.Scan(judge, 0); }
                if (i % 7 == 5 && i < past.Count - 1) continue;
                var offset = (i % 3 - 1) * 0.03 + (i == past.Count - 1 ? 0.07 : 0);
                judge.Feed(new KeyboardModePlayed(past[i].Midi, true, past[i].OnsetSec + offset));
            }
            judge.Advance(now - 0.25);
            score.Absorb(judge);
            feedback.Scan(judge, Environment.TickCount64 + 3_600_000);   // an hour ahead: the flash stays fully visible for the shot
            KeyboardModeWaitMode? wait = null;
            if (waiting)
            {
                wait = new KeyboardModeWaitMode(new ProbeTransport()) { Enabled = true };
                var next = expected.Where(e => e.OnsetSec >= now - 0.25).Select(e => e.OnsetSec).DefaultIfEmpty(now).First();
                ms = next * 1000;
                wait.Step(judge, next, true);
            }
            else
            {
                foreach (var e in expected.Where(e => e.OnsetSec <= now && now < e.OnsetSec + Math.Max(0.1, e.DurationSec)))
                    feedback.Played(new KeyboardModePlayed(e.Midi, true, e.OnsetSec), true);
                feedback.Played(new KeyboardModePlayed(source.Highest - 14, true, now), false);   // a key that matches nothing
            }
            _w.Window.Learn.ProbePlay = (ms, timeline);
            if (_w.Window.KeyboardModePane.Keys is KeyboardModeView view) view.ProbePlayAlong(feedback, score, wait, judge);
        }

        /// <summary>Four bars with every length: quarter and half chords of both hands, a whole-bar chord, eighths and a sixteenth run, and a half note tied across the bar line into a whole-bar bar.</summary>
        private static void WriteKeysRiff(TrackModel track, int bar)
        {
            // (cell, denominator, tied, midis): a cell has one length, so a chord of both hands shares it. The first midi is on string 0 so a tie finds its origin.
            (int Cell, int Den, bool Tied, int[] Midis)[][] rhythm =
            {
                new[] { (0, 4, false, new[] { 72, 48, 52 }), (4, 8, false, new[] { 76 }), (6, 8, false, new[] { 79 }), (8, 2, false, new[] { 83, 55, 59, 86 }) },
                new[] { (0, 1, false, new[] { 74, 43, 47, 50, 77 }) },
                new[] { (0, 16, false, new[] { 72 }), (1, 16, false, new[] { 74 }), (2, 16, false, new[] { 76 }), (3, 16, false, new[] { 77 }), (4, 8, false, new[] { 81, 45 }), (6, 8, false, new[] { 77 }), (8, 2, false, new[] { 79, 45, 48 }) },
                new[] { (0, 2, true, new[] { 79 }), (8, 4, false, new[] { 72, 36, 43, 76 }), (12, 4, false, new[] { 74, 41 }) },
            };
            for (var b = 0; b < 4 && bar + b < track.Measures.Count; b++)
            {
                var cells = track.Measures[bar + b].Cells;
                foreach (var c in cells) { c.Notes.Clear(); c.IsRest = false; c.IsTied = false; }
                foreach (var (cell, den, tied, midis) in rhythm[b])
                {
                    cells[cell].DurationDenominator = den; cells[cell].IsTied = tied;
                    for (var i = 0; i < midis.Length; i++)
                    {
                        // A plausible fingering (thumb on the inner note of each hand), so the finger numbers show: 1 thumb to 5 little, stored as 0 to 4.
                        var right = midis[i] >= 60;
                        var rank = midis.Count(m => (m >= 60) == right && (right ? m < midis[i] : m > midis[i]));
                        cells[cell].Notes.Add(new TabNote { StringIndex = i, Fret = 0, MidiValue = midis[i], LeftHandFinger = Math.Min(4, rank * 2) });
                    }
                }
            }
        }
    }
}
