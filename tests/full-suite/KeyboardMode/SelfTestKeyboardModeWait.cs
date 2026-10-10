using TabForge.Audio;
using TabForge.KeyboardMode;
using TabForge.Models;

namespace TabForge;

// Owns: the Learn wait mode checks: pause at the chord's onset, resume exactly once when every note was pressed (one by one, wrong keys, held keys, early press), skip, re-arm on a new judge
//   (loop wrap, seek, track change), the setting off or a dispose while paused (the held song plays on), a stalled or very fast run, and the session feeding pitch-only presses during a pause.
//   Fake host, fake device and fake clock, never the real transport.
// Does not own: the judge (SelfTestKeyboardModeJudge.cs) or the listener (SelfTestKeyboardModeMidi.cs).
// Tests: TestKeyboardModeWaitMode.
public static partial class SelfTest
{
    private sealed class FakeWaitHost : IKeyboardModeWaitHost
    {
        public bool Playing = true;
        public int Pauses, Resumes;
        public bool IsPlaying => Playing;
        public void Pause() { Pauses++; Playing = false; }
        public void Resume() { Resumes++; Playing = true; }
    }

    private static void TestKeyboardModeWaitMode()
    {
        KeyboardModeJudge Judge() => new(new[] { new KeyboardModeExpected(60, 1.0, 0.4), new KeyboardModeExpected(64, 1.0, 0.4), new KeyboardModeExpected(67, 1.0, 0.4), new KeyboardModeExpected(72, 2.0, 0.3), new KeyboardModeExpected(60, 2.5, 0.3) });
        KeyboardModePlayed On(int midi, double t = double.NaN) => new(midi, true, t);
        long clock = 0;
        (KeyboardModeWaitMode Wait, FakeWaitHost Host) New() { var h = new FakeWaitHost(); return (new KeyboardModeWaitMode(h) { Enabled = true, Now = () => clock }, h); }

        // Pause at the onset, once; a wrong key and a partial chord keep waiting; the last note resumes once.
        var (w, host) = New();
        var j = Judge();
        w.Step(j, 0.5, true);
        Check("wait: nothing pauses before the chord reaches the line", host.Pauses == 0 && !w.IsWaiting);
        w.Step(j, 1.0, true); w.Step(j, 1.02, true); w.Step(j, 1.1, true);
        Check("wait: the chord at the line pauses the song once", host.Pauses == 1 && w.IsWaiting && !host.Playing);
        w.Press(On(61)); w.Step(j, 1.0, true);
        Check("wait: a wrong key does not resume or advance", w.IsWaiting && host.Resumes == 0 && !j.Results[0].IsResolved);
        w.Press(On(60)); w.Step(j, 1.0, true); w.Press(On(64)); w.Step(j, 1.0, true);
        Check("wait: a partial chord keeps waiting", w.IsWaiting && host.Resumes == 0);
        w.Press(On(67)); w.Step(j, 1.0, true); w.Step(j, 1.0, true); w.Step(j, 1.05, true);
        Check("wait: the last note resumes exactly once", !w.IsWaiting && host.Resumes == 1 && host.Pauses == 1 && host.Playing);
        Check("wait: the chord counts as played on its onset", j.Results.Take(3).All(r => r.Grade == KeyboardModeGrade.Perfect && r.OffsetMs == 0));
        w.Step(j, 1.5, true);
        Check("wait: the next chord is not awaited early", host.Pauses == 1);
        w.Step(j, 2.0, true);
        Check("wait: the next chord pauses in turn", host.Pauses == 2 && w.IsWaiting);

        // A release is not a press; a key pressed and released counts; held keys from before the wait need a fresh press.
        w.Press(new KeyboardModePlayed(72, false, double.NaN)); w.Step(j, 2.0, true);
        Check("wait: a release does not count as a press", w.IsWaiting);
        w.Press(On(72)); w.Press(new KeyboardModePlayed(72, false, double.NaN)); w.Step(j, 2.0, true);
        Check("wait: a key pressed and released again counts", !w.IsWaiting && host.Resumes == 2);
        w.Press(On(60)); clock += 5000; w.Step(j, 2.5, true); w.Step(j, 2.5, true);   // pressed long before the wait began: stale
        Check("wait: a press from long before the wait does not count", w.IsWaiting && host.Resumes == 2);
        w.Press(On(60)); w.Step(j, 2.5, true);
        Check("wait: the fresh press resumes", !w.IsWaiting && host.Resumes == 3 && j.Results[4].IsHit);

        // A note the judge already took before the line (played early) counts; a fully early chord never pauses.
        (w, host) = New(); j = Judge();
        j.Feed(new KeyboardModePlayed(60, true, 0.95));
        w.Step(j, 1.0, true); w.Press(On(64)); w.Step(j, 1.0, true); w.Press(On(67)); w.Step(j, 1.0, true);
        Check("wait: a note played just before the line stays played; the rest resumes", host.Pauses == 1 && host.Resumes == 1 && j.Results[0].OffsetMs == -50);
        (w, host) = New(); j = Judge();
        foreach (var m in new[] { 60, 64, 67 }) j.Feed(new KeyboardModePlayed(m, true, 0.98));
        w.Step(j, 1.0, true); w.Step(j, 1.2, true);
        Check("wait: a chord already played before the line is not paused for", host.Pauses == 0);

        // Skip gives up the chord (misses) and resumes; with nothing awaited it does nothing.
        (w, host) = New(); j = Judge();
        Check("skip: nothing is awaited, nothing happens", !w.Skip() && host.Resumes == 0);
        w.Step(j, 1.0, true); w.Press(On(60)); w.Step(j, 1.0, true);
        Check("skip: gives up the awaited chord and resumes once", w.Skip() && host.Resumes == 1 && host.Playing && j.Results.Take(3).All(r => r.Grade == KeyboardModeGrade.Miss || r.Grade == KeyboardModeGrade.Perfect) && j.Results[1].Grade == KeyboardModeGrade.Miss);
        w.Step(j, 1.5, true);
        Check("skip: the song goes on to the next chord", host.Pauses == 1);

        // A new judge (loop wrap, seek, track change) re-arms: the song stays paused, the wait is dropped, and the new chord pauses once.
        (w, host) = New(); j = Judge();
        w.Step(j, 1.0, true);
        var j2 = Judge();
        w.Step(j2, 0.2, true);
        Check("rearm: a new judge drops the wait and plays the song on", !w.IsWaiting && host.Resumes == 1 && host.Playing);
        w.Step(j2, 0.9, true); w.Step(j2, 1.0, true); w.Step(j2, 1.0, true);
        Check("rearm: the new run waits for its chord once", host.Pauses == 2 && w.IsWaiting);
        w.Rearm();
        Check("rearm: a dispose or a hidden pane plays the held song on", !w.IsWaiting && host.Resumes == 2 && host.Playing);

        // The player resumes by hand: the same chord is not awaited again.
        (w, host) = New(); j = Judge();
        w.Step(j, 1.0, true);
        host.Playing = true;
        w.Step(j, 1.1, true); w.Step(j, 1.2, true);
        Check("manual: resuming with the transport ends the wait for that chord", !w.IsWaiting && host.Pauses == 1 && host.Resumes == 0);
        j.Advance(1.9);   // the chord the player moved on from is a miss by now
        w.Step(j, 2.0, true);
        Check("manual: the next chord is awaited", host.Pauses == 2);

        // The setting off while waiting: stay paused, no auto-play; turned on again it works on the next playing frame.
        (w, host) = New(); j = Judge();
        w.Step(j, 1.0, true);
        w.Enabled = false;
        w.Press(On(60)); w.Step(j, 1.0, true);
        Check("off: turning it off while waiting plays the held song on once", !w.IsWaiting && host.Resumes == 1 && host.Playing);
        w.Enabled = true;
        w.Step(j, 1.0, true);
        Check("off: turned on again it waits for the chord", w.IsWaiting && host.Pauses == 2);
        w.Step(j, 1.0, false);
        Check("stop: a stopped song drops the wait without a resume", !w.IsWaiting && host.Resumes == 1);

        // Stalled frames and tempo extremes: chords 50 ms apart, the frame time far ahead; each chord pauses and resumes once.
        (w, host) = New();
        var fast = new KeyboardModeJudge(Enumerable.Range(0, 8).Select(i => new KeyboardModeExpected(60 + i, 1.0 + i * 0.05, 0.03)));
        for (var i = 0; i < 8; i++)
        {
            w.Step(fast, 5.0, true);
            Check("fast: chord " + i + " pauses when due, though the frame is late", w.IsWaiting && host.Pauses == i + 1, $"{host.Pauses}");
            w.Press(On(60 + i)); w.Step(fast, 5.0, true);
        }
        Check("fast: every pause had one resume", host.Pauses == 8 && host.Resumes == 8 && fast.Results.All(r => r.IsHit));
        (w, host) = New(); j = Judge();
        w.Step(j, 1.0, true); w.Step(j, 0.1, true); w.Step(j, 9.0, true);
        Check("speed: the song time moving while waiting changes nothing", w.IsWaiting && host.Pauses == 1 && host.Resumes == 0);
        (w, host) = New(); w.Enabled = false; j = Judge(); w.Step(j, 5, true);
        Check("off: nothing pauses when off", host.Pauses == 0);

        // The session: a press while the clock is paused reaches wait mode (NaN time) and is scored as a hit on the onset; wait off drops it as before.
        var song = KeysSong();
        Beat(song, 0, 0, 4, 4, 60);
        Beat(song, 0, 0, 8, 4, 64);
        var source = KeysSourceOf(song);
        var clockPaused = false;
        var dev = new FakeMidiDevice();
        var listener = new KeyboardModeMidiListener(new MidiInputHub(dev).CreateClient(), stamp => clockPaused ? double.NaN : stamp / 1000.0, () => 0);
        var session = new KeyboardModeKeyboardSession(listener);
        var sh = new FakeWaitHost();
        var wait = new KeyboardModeWaitMode(sh) { Enabled = true };
        session.Wait = wait;
        session.SetSource(source, true);
        void Frame(double ms, bool jumped = false) => session.Update(ms, true, !sh.Playing, null, 0, jumped, 0);
        Frame(0);
        dev.Fire(0x90, 60, 90, 495);
        Frame(100);
        Check("session: an early press is judged as usual and no pause comes before the line", session.Score.Hits == 1 && sh.Pauses == 0);
        Frame(1000);
        Check("session: the next note at the line pauses the song", sh.Pauses == 1 && wait.IsWaiting);
        clockPaused = true;
        dev.Fire(0x90, 64, 90, 1020);
        Frame(1000);
        Check("session: a press during the pause resumes and is a hit on the onset", sh.Resumes == 1 && session.Score.Hits == 2 && session.Score.Perfects == 2 && session.Score.Misses == 0, $"{sh.Resumes}/{session.Score.Hits}/{session.Score.Misses}");
        clockPaused = false;
        session.SetSource(source, true);   // same source: no change
        session.SetSource(null, false);
        session.SetSource(source, true);   // a track change away and back: a fresh run
        Frame(0); Frame(600);
        Check("session: a fresh run waits for its first chord", sh.Pauses == 2 && wait.IsWaiting && !sh.Playing);
        session.SetSource(null, false);
        Check("session: a track change while waiting drops the wait and plays the song on", !wait.IsWaiting && sh.Resumes == 2 && sh.Playing);
        session.SetSource(source, true);
        sh.Playing = true; Frame(0); Frame(600);
        Check("session: waiting again", sh.Pauses == 3 && wait.IsWaiting);
        Frame(0, jumped: true);
        Check("session: a seek (fresh run) drops the wait and plays the song on", !wait.IsWaiting && sh.Resumes == 3 && sh.Playing);
        session.Dispose();
        Check("session: dispose is unsubscribed, not waiting and resumes nothing more", sh.Resumes == 3 && !listener.IsListening && !wait.IsWaiting);
    }
}
