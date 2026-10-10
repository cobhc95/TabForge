using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Audio;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

// Owns: the Keyboard mode practice checks: the practice track (the selected track skipped by the scheduler while a MIDI keyboard is heard, its Mute and the song untouched, the player's keys sent through
//   it, cleared when MIDI goes off or the controller is disposed) and the control bar (every control named and reachable, toggles shown with a check, each control changes its setting or calls the
//   transport, the bar wraps on a narrow width).
// Does not own: the listener and judge (TestKeyboardModeMidiListener), the playback gate itself (TestPracticeSilence) or the pop-out (TestKeyboardModePopout).
// Tests: TestKeyboardModePracticeTrack, TestKeyboardModeControlBar.
public static partial class SelfTest
{
    private static void TestKeyboardModePracticeTrack()
    {
        using var alive = KeepAlive();
        var dev = new FakeMidiDevice();
        var hub = new MidiInputHub(dev);
        var window = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
        try
        {
            ShowTestWindow(window);
            var host = (IPaneHost)window;
            var learn = host.Settings.Learn ??= new KeyboardModeSettings();
            var input = learn.MidiInput;
            var controller = window.Learn;
            controller.ListenerFactory = () => new KeyboardModeMidiListener(hub.CreateClient(), _ => 0.0, () => 0.0);
            var track = host.SelectedTrack!;
            var was = track.Kind;
            var document = window.OpenDocuments.First(d => ReferenceEquals(d.Project, host.Project));
            var engine = document.Playback.Engine;
            var dirty = document.Project.IsDirty;
            var index = host.Project.Tracks.IndexOf(track);
            track.Kind = TrackKind.Keys;
            learn.MidiInput = KeyboardModeSettings.MidiAny;
            controller.Tick();
            var practice = controller.Practice;
            Check("practice: while a MIDI keyboard is heard the selected track is skipped by the scheduler", practice?.Track == track && engine.PracticeSilence == index, $"{engine.PracticeSilence} vs {index}");
            Check("practice: the track's mute and the song stay untouched", !track.Mute && document.Project.IsDirty == dirty);
            practice!.Play(0x90, 60, 100); practice.Play(0x80, 60, 0);
            Check("practice: the player's keys are played through the track", practice.Sent == 2, practice.Sent.ToString());
            learn.MidiInput = KeyboardModeSettings.MidiOff;
            controller.Tick();
            Check("practice: MIDI input off gives the track back exactly", engine.PracticeSilence == -1 && practice.Track is null && !track.Mute);
            practice.Play(0x90, 60, 100);
            Check("practice: nothing is played through it once given back", practice.Sent == 2);
            learn.MidiInput = KeyboardModeSettings.MidiAny;
            controller.Tick();
            controller.Dispose();
            Check("practice: disposing the controller gives the track back", engine.PracticeSilence == -1 && !track.Mute);
            track.Kind = was;
            learn.MidiInput = input;
        }
        finally { foreach (var s in window.OpenDocuments.ToList()) s.MarkClean(); window.Close(); }
    }

    private static void TestKeyboardModeControlBar()
    {
        using var alive = KeepAlive();
        var window = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
        try
        {
            ShowTestWindow(window);
            var learn = ((IPaneHost)window).Settings.Learn ??= new KeyboardModeSettings();
            var saved = (learn.WaitForNotes, learn.Hands, learn.LookAheadSeconds, learn.MidiInput, learn.ShowNoteNames);
            bool playing = false, loop = false;
            var speed = 1.0;
            int plays = 0, stops = 0;
            var controls = new KeyboardModeControls(window.Learn, new KeyboardModeTransport(() => { plays++; playing = !playing; }, () => stops++, () => playing, () => speed, v => speed = v, () => loop, () => loop = !loop));
            var bar = new KeyboardModeControlBar(controls);
            var row = (WrapPanel)bar.Child;
            var items = row.Children.OfType<Control>().ToList();
            Check("control bar: every control has a name and takes keyboard focus", items.Count >= 11 && items.All(c => AutomationProperties.GetName(c).Length > 0 && c.Focusable),
                string.Join(", ", items.Where(c => AutomationProperties.GetName(c).Length == 0 || !c.Focusable).Select(c => c.GetType().Name)));
            Button B(string name) => items.OfType<Button>().First(b => AutomationProperties.GetName(b) == name);
            ComboBox C(string name) => items.OfType<ComboBox>().First(b => AutomationProperties.GetName(b) == name);
            void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Click(B("Play"));
            Check("control bar: play calls the window's play / pause and shows Pause", plays == 1 && AutomationProperties.GetName(items[0]) == "Pause" && ((string)((Button)items[0]).Content).Contains("Pause"));
            Click(B("Stop"));
            Check("control bar: stop calls the window's stop", stops == 1);
            var wait = learn.WaitForNotes;
            Click(B("Wait for me"));
            Check("control bar: Wait for me toggles the setting and shows a check when on", learn.WaitForNotes != wait && ((string)B("Wait for me").Content).StartsWith("✓") == learn.WaitForNotes);
            Click(B("Loop"));
            Check("control bar: Loop calls the transport's loop and shows a check", loop && ((string)B("Loop").Content).StartsWith("✓") && AutomationProperties.GetItemStatus(B("Loop")) == "on");
            C("Speed").SelectedIndex = Array.IndexOf(KeyboardModeControls.SpeedPresets, 0.75);
            Check("control bar: a speed preset sets the transport's speed", Math.Abs(speed - 0.75) < 1e-9, speed.ToString());
            C("Hands").SelectedIndex = (int)KeyboardHandsFilter.Left;
            Check("control bar: Hands sets the practised hand", learn.Hands == KeyboardHands.NameOf(KeyboardHandsFilter.Left), learn.Hands);
            var look = controls.LookAheadSeconds;
            Click(B("Look-ahead longer: shorter notes"));
            Check("control bar: + lengthens the look-ahead (shorter notes) and shows it", controls.LookAheadSeconds == Math.Min(KeyboardModeSettings.MaxLookAheadSeconds, look + 1));
            var midi = C("MIDI in");
            var tags = midi.Items.OfType<ComboBoxItem>().Select(i => (string)i.Tag).ToList();
            Check("control bar: the MIDI list offers any available device first and Off last", tags.First() == KeyboardModeSettings.MidiAny && tags.Last() == KeyboardModeSettings.MidiOff, string.Join("|", tags));
            midi.SelectedIndex = tags.Count - 1;
            Check("control bar: choosing Off turns play along off", learn.MidiInput == KeyboardModeSettings.MidiOff && !learn.PlayAlongKeyboard);
            var names = learn.ShowNoteNames;
            Click(B("Note names"));
            Check("control bar: Note names toggles the setting", learn.ShowNoteNames != names);

            bar.Measure(new Size(2400, double.PositiveInfinity));
            var one = bar.DesiredSize.Height;
            bar.Measure(new Size(420, double.PositiveInfinity));
            Check("control bar: a narrow width wraps the controls onto more rows (nothing is cut off)", bar.DesiredSize.Height > one * 1.8 && bar.DesiredSize.Width <= 420, $"{one:0} -> {bar.DesiredSize.Height:0}");
            (learn.WaitForNotes, learn.Hands, learn.LookAheadSeconds, learn.MidiInput, learn.ShowNoteNames) = saved;
        }
        finally { foreach (var s in window.OpenDocuments.ToList()) s.MarkClean(); window.Close(); }
    }
}
