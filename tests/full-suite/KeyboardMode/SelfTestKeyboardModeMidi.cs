using TabForge.Audio;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

// Owns: the Keyboard mode MIDI keyboard checks: the shared input (two clients, one set of devices), the listener turning stamps into song time and queueing them, the session judging them
//   (hits, early and late, extras, sustain, latency, paused clock, loop wrap, seek), no subscription left after dispose, and the controller choosing keyboard or guitar and honouring the setting
//   in a real window. Fake device and fake clock, never a real MIDI device.
// Does not own: the judge and score themselves (SelfTestKeyboardModeJudge.cs).
// Tests: TestKeyboardModeMidiListener.
public static partial class SelfTest
{
    private sealed class FakeMidiDevice : IMidiInputSource
    {
        public int Devices = 1, OpenCalls, CloseCalls;
        public event Action<int, int, int, long>? Message;
        public bool IsOpen { get; private set; }
        public int Subscribers => Message?.GetInvocationList().Length ?? 0;
        public int Open() { OpenCalls++; if (Devices > 0) IsOpen = true; return IsOpen ? Devices : 0; }
        public void Close() { CloseCalls++; IsOpen = false; }
        public void Dispose() => Close();
        public void Fire(int status, int d1, int d2, long stamp) => Message?.Invoke(status, d1, d2, stamp);
    }

    private static void TestKeyboardModeMidiListener()
    {
        // Thru: a program change is not passed on; bend, pedal and pressure are.
        var thruDev = new FakeMidiDevice();
        var thruListener = new KeyboardModeMidiListener(new MidiInputHub(thruDev).CreateClient(), _ => 0.0, () => 0.0);
        var thru = new List<int>();
        thruListener.Thru += (s, _, _) => thru.Add(s & 0xF0);
        thruListener.Start();
        foreach (var s in new[] { 0x90, 0xB0, 0xC0, 0xD0, 0xE0 }) thruDev.Fire(s, 1, 1, 1);
        thruListener.Dispose();
        Check("listener: Thru passes note, controller, pressure and bend but never a program change", thru.SequenceEqual(new[] { 0x90, 0xB0, 0xD0, 0xE0 }), string.Join(",", thru));
        // The hub: two clients share one set of devices, the last one closes it.
        var dev = new FakeMidiDevice();
        var hub = new MidiInputHub(dev);
        var rec = hub.CreateClient();
        var learn = hub.CreateClient();
        var heardRec = new List<int>(); var heardLearn = new List<int>();
        rec.Message += (s, _, _, _) => heardRec.Add(s);
        learn.Message += (s, _, _, _) => heardLearn.Add(s);
        dev.Fire(0x90, 60, 100, 1);
        Check("hub: a client that is not open hears nothing", heardRec.Count == 0 && heardLearn.Count == 0);
        Check("hub: the first client opens the devices", rec.Open() == 1 && rec.IsOpen && dev.IsOpen && hub.HeldCount == 1);
        Check("hub: a second client does not reopen a handle: still one set of devices open", learn.Open() == 1 && dev.IsOpen && hub.HeldCount == 2 && dev.Subscribers == 1);
        dev.Fire(0x90, 60, 100, 2);
        Check("hub: both clients hear a message", heardRec.Count == 1 && heardLearn.Count == 1);
        learn.Close();
        Check("hub: the other client leaving keeps the devices open for the recording", dev.IsOpen && dev.CloseCalls == 0 && hub.HeldCount == 1);
        dev.Fire(0x90, 62, 100, 3);
        Check("hub: the client that left hears no more", heardRec.Count == 2 && heardLearn.Count == 1);
        rec.Close(); rec.Close();
        Check("hub: the last client closes the devices once (close is idempotent)", !dev.IsOpen && dev.CloseCalls == 1 && hub.HeldCount == 0 && !rec.IsOpen);
        var none = new FakeMidiDevice { Devices = 0 };
        var lone = new MidiInputHub(none).CreateClient();
        Check("hub: no device found: open returns 0 and the client is not open (a retry opens again)", lone.Open() == 0 && !lone.IsOpen && lone.Open() == 0 && none.OpenCalls == 2);

        // A keyboard song: C4 at 0, E4 at 500, a chord C4 + G4 at 1000 ms (120 bpm, 4/4).
        var song = KeysSong();
        Beat(song, 0, 0, 0, 4, 60);
        Beat(song, 0, 0, 4, 4, 64);
        var chord = Beat(song, 0, 0, 8, 4, 60, 1, 0);
        chord.Notes.Add(new TabNote { StringIndex = 2, Fret = 0, MidiValue = 67 });
        var source = KeysSourceOf(song);

        // The fake clock: a stamp is milliseconds on the song clock; paused gives NaN.
        var paused = false; var latency = 0.0;
        (FakeMidiDevice Dev, KeyboardModeMidiListener Listener, KeyboardModeKeyboardSession Session) NewRun()
        {
            var d = new FakeMidiDevice();
            var listener = new KeyboardModeMidiListener(new MidiInputHub(d).CreateClient(), stamp => paused ? double.NaN : stamp / 1000.0, () => latency);
            var session = new KeyboardModeKeyboardSession(listener);
            session.SetSource(source, true);
            return (d, listener, session);
        }
        void Frame(KeyboardModeKeyboardSession s, double ms, bool jumped = false, int wraps = 0, KeyboardModeLoop? loop = null) => s.Update(ms, true, false, loop, wraps, jumped, latency);

        // Perfect hits, a chord, release and sustain.
        var a = NewRun();
        Check("listener: starting opens and subscribes the device once", a.Listener.IsListening && a.Dev.IsOpen && a.Dev.Subscribers == 1);
        Frame(a.Session, 0);
        a.Dev.Fire(0x90, 60, 90, 10); a.Dev.Fire(0x80, 60, 0, 300);
        a.Dev.Fire(0x90, 64, 90, 495); a.Dev.Fire(0x90, 60, 90, 1000); a.Dev.Fire(0x90, 67, 90, 1020);
        Frame(a.Session, 1100);
        Check("session: a run starts with every note of the song expected", a.Session.Judge!.Results.Count == 4 && a.Session.Runs == 1);
        Check("session: perfect presses of notes and a chord are hits", a.Session.Score.Hits == 4 && a.Session.Score.Perfects == 4 && a.Session.Score.Misses == 0, $"{a.Session.Score.Hits}/{a.Session.Score.Perfects}");
        Check("session: a note-off ends the hold of the key", a.Session.Judge.Results[0].HeldSec is > 0.25 and < 0.35, a.Session.Judge.Results[0].HeldSec.ToString());
        a.Dev.Fire(0x90, 60, 0, 1500);   // note-on with velocity 0 is a release
        Frame(a.Session, 1600);
        Check("session: note-on with velocity 0 releases the key", a.Session.Judge.Results[2].HeldSec is > 0.4 and < 0.6, a.Session.Judge.Results[2].HeldSec.ToString());

        // Early, late, extra keys and a miss.
        var b = NewRun();
        Frame(b.Session, 0);
        b.Dev.Fire(0x90, 60, 90, 0); b.Dev.Fire(0x90, 64, 90, 400);   // E4 100 ms early: good
        b.Dev.Fire(0x90, 61, 90, 700);                                 // a key that is not in the song: extra
        b.Dev.Fire(0x90, 60, 90, 1100);                                // C4 of the chord 100 ms late: good; G4 never played: miss
        Frame(b.Session, 1400);
        var r = b.Session.Judge!.Results;
        Check("session: early and late presses are good hits", r[1].IsEarly && r[2].IsLate && b.Session.Score.Hits == 3 && b.Session.Score.Perfects == 1, $"{b.Session.Score.Hits}");
        Check("session: an extra key is counted but changes no accuracy", b.Session.Score.Extras == 1);
        Check("session: a key never played is a miss once its window passed and the chord is partial", b.Session.Score.Misses == 1 && b.Session.Score.PartialChords == 1);

        // Latency: heard 100 ms after the stamp, so a press stamped at 600 belongs at 500.
        latency = 0.1;
        var c = NewRun();
        Frame(c.Session, 0);
        c.Dev.Fire(0x90, 60, 90, 100); c.Dev.Fire(0x90, 64, 90, 600);
        Frame(c.Session, 700);
        Check("session: the output latency is taken off the press time", c.Session.Score.Perfects == 2, $"{c.Session.Score.Perfects}");
        latency = 0;

        // A paused or stopped clock gives no time: the key is ignored, never a miss or an extra.
        var d2 = NewRun();
        Frame(d2.Session, 0);
        paused = true;
        d2.Dev.Fire(0x90, 60, 90, 10);
        paused = false;
        Frame(d2.Session, 100);
        Check("listener: a key while the clock is paused is dropped", d2.Session.Score.Extras == 0 && d2.Session.Score.Hits == 0 && d2.Session.Judge!.Results[0].Grade == KeyboardModeGrade.Pending);
        d2.Dev.Fire(0xB0, 64, 127, 50);   // a controller message is not a key
        d2.Dev.Fire(0x91, 60, 90, 20);    // another channel is a key too
        Frame(d2.Session, 150);
        Check("listener: only note messages are kept (any channel)", d2.Session.Score.Hits == 1);

        // A loop wrap, a seek and a restart each begin a fresh run with a fresh score.
        var loop = new KeyboardModeLoop(0, 1000);
        var e = NewRun();
        Frame(e.Session, 0, loop: loop);
        e.Dev.Fire(0x90, 60, 90, 0); e.Dev.Fire(0x90, 64, 90, 500);
        Frame(e.Session, 900, loop: loop);
        Check("loop: the run holds only the notes inside the loop", e.Session.Judge!.Results.Count == 2 && e.Session.Score.Hits == 2);
        Frame(e.Session, 20, wraps: 1, loop: loop);
        Check("loop: a wrap starts a fresh run and zero score", e.Session.Runs == 2 && e.Session.Score.Hits == 0 && e.Session.Judge!.Results.Count == 2);
        Frame(e.Session, 600, jumped: true);
        Check("seek: a jump starts a fresh run from the new position (earlier notes are not expected)", e.Session.Runs == 3 && e.Session.Judge!.Results.Count == 2 && e.Session.Judge.Results[0].Expected.OnsetSec >= 1.0);
        e.Session.Update(0, false, false, null, 0, false, 0);
        Frame(e.Session, 0);
        Check("restart: running again after a stop is a fresh run", e.Session.Runs == 4 && e.Session.Judge!.Results.Count == 4);

        // The leak check: stopping leaves nothing subscribed or open, and a late key queues nothing.
        a.Session.Stop();
        Check("leak: stop unsubscribes and closes the device", !a.Dev.IsOpen && a.Dev.Subscribers == 1 && !a.Listener.IsListening);
        a.Dev.Fire(0x90, 60, 90, 2000);
        var queued = 0; a.Listener.Drain(_ => queued++);
        Check("leak: a key after stop is not queued", queued == 0);
        a.Session.SetSource(source, true); a.Session.SetSource(null, true);
        Check("leak: a track that is not a keyboard track stops the listener", !a.Dev.IsOpen && !a.Listener.IsListening);
        foreach (var run in new[] { b, c, d2, e }) run.Session.Dispose();
        Check("leak: dispose of every session closes every device", new[] { b, c, d2, e }.All(x => !x.Dev.IsOpen && !x.Listener.IsListening));
        var flood = NewRun();
        for (var i = 0; i < 2000; i++) flood.Dev.Fire(0x90, 60, 90, i);
        var kept = 0; flood.Listener.Drain(_ => kept++);
        Check("listener: a stalled UI keeps a bounded queue", kept is > 0 and <= 512, kept.ToString());
        flood.Session.Dispose();

        TestKeyboardModeMidiWindow();
    }

    /// <summary>The controller of a real window: track with notes plus the setting subscribes, a track without notes does not, and dispose releases.</summary>
    private static void TestKeyboardModeMidiWindow()
    {
        using var alive = KeepAlive();
        var dev = new FakeMidiDevice();
        var hub = new MidiInputHub(dev);
        var window = new MainWindow(Audio.AudioEngineClient.Instance, new Shell.AppOptions());
        try
        {
            ShowTestWindow(window);
            var host = (IPaneHost)window;
            var learn = host.Settings.Learn ??= new KeyboardModeSettings();
            var controller = window.Learn;
            var made = 0;
            controller.ListenerFactory = () => { made++; return new KeyboardModeMidiListener(hub.CreateClient(), _ => 0.0, () => 0.0); };
            var track = host.SelectedTrack!;
            var was = track.Kind;
            Check("learn setting: play along listens to any available MIDI device by default", new KeyboardModeSettings() is { PlayAlongKeyboard: true, MidiInput: KeyboardModeSettings.MidiAny });
            learn.MidiInput = KeyboardModeSettings.MidiOff;
            Check("learn setting: MIDI input Off is play along off", !learn.PlayAlongKeyboard);

            track.Kind = TrackKind.Keys;
            controller.Tick();
            Check("learn keyboard: setting off: no listener is made and nothing subscribes", made == 0 && !dev.IsOpen && hub.HeldCount == 0 && controller.KeyboardSession is null);
            learn.PlayAlongKeyboard = true;
            controller.Tick();
            Check("learn keyboard: a keyboard track with the setting on subscribes once", made == 1 && dev.IsOpen && hub.HeldCount == 1 && controller.KeyboardSession!.IsListening);
            controller.Tick(); controller.Tick();
            Check("learn keyboard: more frames do not subscribe again", made == 1 && hub.HeldCount == 1 && dev.Subscribers == 1);
            Check("learn keyboard: the score is exposed read-only and starts at zero", controller.Score.Judged == 0 && controller.Score.Hits == 0);
            track.Kind = TrackKind.Audio;
            controller.Tick();
            Check("learn keyboard: a track without notes leaves the input", hub.HeldCount == 0 && !dev.IsOpen && !controller.KeyboardSession!.IsListening);
            track.Kind = TrackKind.Keys;
            controller.Tick();
            Check("learn keyboard: back on a track with notes it listens again", hub.HeldCount == 1 && made == 1);
            learn.PlayAlongKeyboard = false;
            controller.Tick();
            Check("learn keyboard: turning the setting off leaves the input", hub.HeldCount == 0 && !dev.IsOpen);
            learn.PlayAlongKeyboard = true;
            controller.Tick();
            controller.Dispose();
            Check("learn keyboard: disposing the controller leaves nothing subscribed", hub.HeldCount == 0 && !dev.IsOpen, $"{hub.HeldCount} {dev.IsOpen}");
            track.Kind = was;
            learn.PlayAlongKeyboard = false;
        }
        finally { foreach (var s in window.OpenDocuments.ToList()) s.MarkClean(); window.Close(); }
    }
}
