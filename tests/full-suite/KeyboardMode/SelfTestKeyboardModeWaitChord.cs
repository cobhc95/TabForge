using TabForge.KeyboardMode;

namespace TabForge;

// Owns: the wait mode chord and transport checks: a chord whose keys come apart, wrong extra keys, a release and press again, two chords in a row, repeated pitches needing a new press, and
//   the transport staying as it was when wait mode is turned on or off, with a hold resumed when it ends other than by a chord. Fake host and clock, never the real transport.
// Does not own: the basic pause / resume / skip checks (SelfTestKeyboardModeWait.cs) or the judge windows (SelfTestKeyboardModeTolerance.cs).
// Tests: TestKeyboardModeWaitChord, TestKeyboardModeWaitTransport.
public static partial class SelfTest
{
    private static void TestKeyboardModeWaitChord()
    {
        long clock = 0;
        KeyboardModeJudge Judge() => new(new[]
        {
            new KeyboardModeExpected(60, 1.0, 0.4), new KeyboardModeExpected(64, 1.0, 0.4), new KeyboardModeExpected(67, 1.0, 0.4),
            new KeyboardModeExpected(60, 2.0, 0.4), new KeyboardModeExpected(64, 2.0, 0.4), new KeyboardModeExpected(67, 2.0, 0.4),
            new KeyboardModeExpected(72, 3.0, 0.05), new KeyboardModeExpected(72, 3.1, 0.05),
        });
        KeyboardModePlayed On(int midi) => new(midi, true, double.NaN);
        KeyboardModePlayed Off(int midi) => new(midi, false, double.NaN);
        (KeyboardModeWaitMode Wait, FakeWaitHost Host, KeyboardModeJudge Judge) New()
        {
            var h = new FakeWaitHost();
            return (new KeyboardModeWaitMode(h) { Enabled = true, Now = () => clock }, h, Judge());
        }

        // One key of the chord is dropped, the others held; the missing key comes much later and the chord advances. Nothing was reset by the wait.
        var (w, host, j) = New();
        w.Step(j, 1.0, true);
        w.Press(On(60)); w.Press(On(64)); w.Step(j, 1.0, true);
        clock += 4000; w.Step(j, 1.0, true);
        Check("chord: two of three keys keep waiting however long it takes", w.IsWaiting && host.Resumes == 0);
        w.Press(On(67)); w.Step(j, 1.0, true);
        Check("chord: the dropped key pressed later completes the chord", !w.IsWaiting && host.Resumes == 1 && j.Results.Take(3).All(r => r.IsHit));

        // Wrong extra keys never block and never advance.
        (w, host, j) = New();
        w.Step(j, 1.0, true);
        w.Press(On(61)); w.Press(On(62)); w.Press(On(61)); w.Step(j, 1.0, true);
        Check("chord: wrong keys alone do not advance", w.IsWaiting && host.Resumes == 0);
        w.Press(On(60)); w.Press(On(63)); w.Press(On(64)); w.Press(On(67)); w.Step(j, 1.0, true);
        Check("chord: correct keys among wrong ones advance", !w.IsWaiting && host.Resumes == 1);

        // Releasing and pressing again keeps what was made; a note-off before the others arrive undoes nothing.
        (w, host, j) = New();
        w.Step(j, 1.0, true);
        w.Press(On(60)); w.Press(Off(60)); w.Press(On(60)); w.Press(Off(64)); w.Press(On(64)); w.Step(j, 1.0, true);
        w.Press(Off(60)); w.Press(Off(64)); w.Step(j, 1.0, true);
        Check("chord: released keys stay satisfied", w.IsWaiting && host.Resumes == 0);
        w.Press(On(67)); w.Step(j, 1.0, true);
        Check("chord: the last key completes it after the others were released", !w.IsWaiting && host.Resumes == 1);

        // Stuck: pressing the same notes again always works.
        (w, host, j) = New();
        w.Step(j, 1.0, true); w.Press(On(60)); w.Press(On(61)); w.Step(j, 1.0, true);
        for (var i = 0; i < 3; i++) { w.Press(On(60)); w.Press(On(61)); w.Step(j, 1.0, true); }
        Check("stuck: pressing known keys again does not break the wait", w.IsWaiting);
        w.Press(On(64)); w.Press(On(60)); w.Press(On(67)); w.Step(j, 1.0, true);
        Check("stuck: pressing the required notes again advances", !w.IsWaiting && host.Resumes == 1);

        // Two chords in a row: keys still held from the first do not satisfy the second.
        (w, host, j) = New();
        w.Step(j, 1.0, true);
        w.Press(On(60)); w.Press(On(64)); w.Press(On(67)); w.Step(j, 1.0, true);
        clock += 500; w.Step(j, 1.5, true); w.Step(j, 2.0, true); clock += 10; w.Step(j, 2.0, true);
        Check("chords: the second chord waits though its keys are still held from the first", w.IsWaiting && host.Pauses == 2);
        w.Press(On(60)); w.Press(On(64)); w.Step(j, 2.0, true);
        Check("chords: a repeated pitch needs a new press, the rest of the chord is still missing", w.IsWaiting && host.Resumes == 1);
        w.Press(On(67)); w.Step(j, 2.0, true);
        Check("chords: new presses of the second chord advance it", !w.IsWaiting && host.Resumes == 2);

        // A chord played just ahead of its pause counts.
        (w, host, j) = New();
        w.Step(j, 1.0, true);
        w.Press(On(60)); w.Press(On(64)); w.Press(On(67)); w.Step(j, 1.0, true);
        clock += 200; w.Press(On(60)); w.Press(On(64)); w.Press(On(67));
        clock += 100; w.Step(j, 2.0, true); w.Step(j, 2.0, true);
        Check("chords: the next chord played ahead of its pause counts", !w.IsWaiting && host.Pauses == 2 && host.Resumes == 2);

        // Rapid repeated notes: each needs its own press.
        (w, host, _) = New();
        j = new KeyboardModeJudge(new[] { new KeyboardModeExpected(72, 3.0, 0.05), new KeyboardModeExpected(72, 3.1, 0.05) });
        w.Step(j, 3.0, true); w.Press(On(72)); w.Step(j, 3.0, true);
        Check("rapid: the first of two quick repeats resumes", host.Resumes == 1);
        w.Step(j, 3.05, true); w.Step(j, 3.1, true);
        Check("rapid: the second quick repeat waits for its own press", w.IsWaiting && host.Pauses == 2);
        w.Press(On(72)); w.Step(j, 3.1, true);
        Check("rapid: the new press resumes it", !w.IsWaiting && host.Resumes == 2);
    }

    private static void TestKeyboardModeWaitTransport()
    {
        long clock = 0;
        KeyboardModeJudge Judge() => new(new[] { new KeyboardModeExpected(60, 1.0, 0.4), new KeyboardModeExpected(64, 2.0, 0.4) });
        (KeyboardModeWaitMode Wait, FakeWaitHost Host) New(bool on)
        {
            var h = new FakeWaitHost();
            return (new KeyboardModeWaitMode(h) { Enabled = on, Now = () => clock }, h);
        }

        // On while playing, a chord just passed: no hold for it, the song plays on.
        var (w, host) = New(false);
        var j = Judge();
        w.Step(j, 1.4, true);
        w.Enabled = true;
        w.Step(j, 1.5, true);
        Check("transport: turning wait on while playing does not pause for a chord already passed", host.Pauses == 0 && host.Playing && !w.IsWaiting);
        w.Step(j, 2.0, true);
        Check("transport: it holds for the next chord when due", host.Pauses == 1 && !host.Playing);
        w.Press(new KeyboardModePlayed(64, true, double.NaN)); w.Step(j, 2.0, true);
        Check("transport: and resumes by itself when satisfied", host.Playing && host.Resumes == 1);

        // On or off while stopped: nothing happens to the transport.
        (w, host) = New(false); host.Playing = false; j = Judge();
        w.Enabled = true; w.Step(j, 0, false); w.Enabled = false;
        Check("transport: turning wait on and off while stopped touches nothing", host.Pauses == 0 && host.Resumes == 0 && !host.Playing);

        // Off during a hold plays on; a seek or hands change (a new judge) during a hold plays on; a stop does not resume.
        (w, host) = New(true); j = Judge();
        w.Step(j, 1.0, true);
        w.Enabled = false;
        Check("transport: turning wait off during a hold resumes the song", host.Playing && host.Resumes == 1 && !w.IsWaiting);
        w.Enabled = true; w.Step(j, 1.0, true);
        Check("transport: turned on again it holds for the due chord", !host.Playing && w.IsWaiting);
        var j2 = Judge();
        w.Step(j2, 0.2, true);
        Check("transport: a new run (hands, seek, loop) during a hold resumes the song", host.Playing && host.Resumes == 2);
        w.Step(j2, 1.0, true);
        Check("transport: the new run holds again when its chord is due", !host.Playing && w.IsWaiting);
        w.Step(j2, 1.0, false);
        Check("transport: a stop during a hold resumes nothing", host.Resumes == 2 && !w.IsWaiting);

        // A hold the player ended by pressing play is not paused again.
        (w, host) = New(true); j = Judge();
        w.Step(j, 1.0, true); host.Playing = true; w.Step(j, 1.1, true); w.Enabled = false;
        Check("transport: a hold ended by hand leaves the song playing without another resume", host.Playing && host.Resumes == 0);
    }
}
