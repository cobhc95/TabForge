using TabForge.Audio;
using TabForge.KeyboardMode;

namespace TabForge;

// Owns: the timing tolerance checks: the Strict / Normal / Relaxed windows and their symmetry, a chord pressed spread over 140 ms, the Relaxed default and the settings value, and a tolerance
//   change starting a fresh run. Headless judge; the session check uses a fake device.
// Does not own: the strict judge edges (SelfTestKeyboardModeJudge.cs).
// Tests: TestKeyboardModeTolerance.
public static partial class SelfTest
{
    private static void TestKeyboardModeTolerance()
    {
        KeyboardModeResult? Press(string name, double at) => new KeyboardModeJudge(new[] { Ex(60, 1.0) }, KeyboardModeTolerances.Of(name)).Feed(new KeyboardModePlayed(60, true, at));

        Check("tolerance: the default setting is Relaxed", new KeyboardModeSettings().TimingTolerance == KeyboardModeSettings.TimingRelaxed && KeyboardModeSettings.NormalizeTiming("nonsense") == KeyboardModeSettings.TimingRelaxed);
        Check("tolerance: Strict is the judge's own default windows", KeyboardModeTolerances.Of("Strict") == KeyboardModeTolerances.Default && KeyboardModeTolerances.Of("Strict") is { PerfectMs: 60, GoodMs: 150 });
        Check("tolerance: an unknown name is Relaxed", KeyboardModeTolerances.Of(null) is { PerfectMs: 150, GoodMs: 300 } && KeyboardModeTolerances.Of("Relaxed") is { PerfectMs: 150, GoodMs: 300 });

        Check("relaxed: 150 ms early is the best grade", Press("Relaxed", 0.850)?.Grade == KeyboardModeGrade.Perfect);
        Check("relaxed: 150 ms late is the best grade", Press("Relaxed", 1.150)?.Grade == KeyboardModeGrade.Perfect);
        Check("relaxed: 300 ms early is a lower-grade hit", Press("Relaxed", 0.700) is { Grade: KeyboardModeGrade.Good, IsEarly: true });
        Check("relaxed: 300 ms late is a lower-grade hit", Press("Relaxed", 1.300) is { Grade: KeyboardModeGrade.Good, IsLate: true });
        Check("relaxed: 301 ms either way is no hit", Press("Relaxed", 1.301) is null && Press("Relaxed", 0.699) is null);
        Check("normal: 100 ms is the best grade, 220 ms a hit, 221 ms none", Press("Normal", 1.100)?.Grade == KeyboardModeGrade.Perfect && Press("Normal", 0.780)?.Grade == KeyboardModeGrade.Good && Press("Normal", 1.221) is null);
        Check("strict: 151 ms is no hit", Press("Strict", 1.151) is null && Press("Strict", 0.849) is null);

        // A chord pressed spread over 140 ms: every note counts; a repeated pitch is judged to the nearer note.
        var chord = new KeyboardModeJudge(new[] { Ex(60, 1.0), Ex(64, 1.0), Ex(67, 1.0) }, KeyboardModeTolerances.Of("Relaxed"));
        chord.Feed(new KeyboardModePlayed(60, true, 0.93)); chord.Feed(new KeyboardModePlayed(64, true, 1.00)); chord.Feed(new KeyboardModePlayed(67, true, 1.07));
        Check("relaxed: a chord spread over 140 ms is hit whole", chord.Results.All(r => r.IsHit) && chord.GroupState(0) == (3, 0, true) && !chord.IsPartial(0));
        var repeats = new KeyboardModeJudge(new[] { Ex(60, 1.0), Ex(60, 1.25) }, KeyboardModeTolerances.Of("Relaxed"));
        repeats.Feed(new KeyboardModePlayed(60, true, 1.2)); repeats.Feed(new KeyboardModePlayed(60, true, 1.0));
        Check("relaxed: quick repeats go to the nearer note", repeats.Results.All(r => r.IsHit) && repeats.Results[0].PlayedOnSec == 1.0 && repeats.Results[1].PlayedOnSec == 1.2);
        var miss = new KeyboardModeJudge(new[] { Ex(60, 1.0) }, KeyboardModeTolerances.Of("Relaxed"));
        miss.Advance(1.3);
        Check("relaxed: a note is not a miss until 300 ms after", !miss.Results[0].IsResolved);
        miss.Advance(1.31);
        Check("relaxed: it is a miss after that", miss.Results[0].Grade == KeyboardModeGrade.Miss);

        // The session judges with the tolerance given and starts a fresh run when it changes.
        var song = KeysSong();
        Beat(song, 0, 0, 4, 4, 60);
        var dev = new FakeMidiDevice();
        var listener = new KeyboardModeMidiListener(new MidiInputHub(dev).CreateClient(), stamp => stamp / 1000.0, () => 0);
        using var session = new KeyboardModeKeyboardSession(listener);
        session.SetSource(KeysSourceOf(song), true);
        session.Update(0, true, false, null, 0, false, 0);
        var runs = session.Runs;
        session.Tolerances = KeyboardModeTolerances.Of("Relaxed");
        session.Update(10, true, false, null, 0, false, 0);
        Check("session: a tolerance change starts a fresh run with those windows", session.Runs == runs + 1 && session.Tolerances.GoodMs == 300);
        session.Tolerances = KeyboardModeTolerances.Of("Relaxed");
        session.Update(20, true, false, null, 0, false, 0);
        Check("session: the same tolerance again does not restart the run", session.Runs == runs + 1);
    }
}
