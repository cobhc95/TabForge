using TabForge.KeyboardMode;

namespace TabForge;

// Owns: the Keyboard mode judge and score checks: hit windows and their exact edges, early and late, misses, extras, chords and partial hits, sustain, the latency sign, NaN times,
//   determinism with a randomised ordered event stream, and the score arithmetic. Headless: no WPF, no device.
// Does not own: the keyboard note source (SelfTestKeyboardModeKeyboard.cs).
// Tests: TestKeyboardModeJudge, TestKeyboardModeScore.
public static partial class SelfTest
{
    private static KeyboardModeExpected Ex(int midi, double onset, double dur = 0.5) => new(midi, onset, dur);

    /// <summary>The result of one press at <paramref name="at"/> seconds on a note of key 60 due at 1.0 s.</summary>
    private static (KeyboardModeResult? Hit, KeyboardModeJudge Judge) KeyboardModePress(double at)
    {
        var j = new KeyboardModeJudge(new[] { Ex(60, 1.0) });
        var hit = j.Feed(new KeyboardModePlayed(60, true, at));
        return (hit, j);
    }

    private static void TestKeyboardModeJudge()
    {
        // Windows: perfect within 60 ms, good within 150 ms, both edges inclusive; 151 ms is no hit.
        Check("judge: a press on the onset is perfect", KeyboardModePress(1.0).Hit is { Grade: KeyboardModeGrade.Perfect, OffsetMs: 0 });
        Check("judge: exactly 60 ms late is still perfect", KeyboardModePress(1.060).Hit?.Grade == KeyboardModeGrade.Perfect);
        Check("judge: exactly 60 ms early is still perfect", KeyboardModePress(0.940).Hit?.Grade == KeyboardModeGrade.Perfect);
        var late = KeyboardModePress(1.061).Hit;
        Check("judge: 61 ms late is a good hit marked late", late is { Grade: KeyboardModeGrade.Good, IsLate: true, IsEarly: false } && Math.Abs(late.OffsetMs - 61) < 1e-6, late?.OffsetMs.ToString());
        var early = KeyboardModePress(0.850).Hit;
        Check("judge: exactly 150 ms early is a good hit marked early", early is { Grade: KeyboardModeGrade.Good, IsEarly: true, IsLate: false });
        Check("judge: exactly 150 ms late is a good hit", KeyboardModePress(1.150).Hit?.Grade == KeyboardModeGrade.Good);
        var (over, overJudge) = KeyboardModePress(1.151);
        Check("judge: 151 ms late is no hit and the press is an extra", over is null && overJudge.Extras.Count == 1 && !overJudge.Results[0].IsResolved);
        Check("judge: 151 ms early is no hit", KeyboardModePress(0.849).Hit is null);
        Check("judge: a custom window is honoured", new KeyboardModeJudge(new[] { Ex(60, 1.0) }, new KeyboardModeTolerances { PerfectMs = 20, GoodMs = 40 }).Feed(new KeyboardModePlayed(60, true, 1.030))?.Grade == KeyboardModeGrade.Good);

        // Misses come when the good window has passed, not before.
        var j = new KeyboardModeJudge(new[] { Ex(60, 1.0), Ex(62, 2.0) });
        j.Advance(1.150);
        Check("judge: the note is not a miss while a press could still hit it (150 ms after)", !j.Results[0].IsResolved);
        j.Advance(1.1501);
        Check("judge: the note is a miss once the window has passed", j.Results[0].Grade == KeyboardModeGrade.Miss && !j.Results[1].IsResolved && j.ResolvedPrefix == 1);
        Check("judge: a press after the miss is an extra", j.Feed(new KeyboardModePlayed(60, true, 1.2)) is null && j.Extras.Count == 1);
        j.Finish(5);
        Check("judge: finishing misses what is left", j.Results.All(r => r.Grade == KeyboardModeGrade.Miss) && j.ResolvedPrefix == 2);

        // Extras: a wrong key, a second press of the same key and a press far from any note.
        var x = new KeyboardModeJudge(new[] { Ex(60, 1.0) });
        Check("judge: a wrong key is an extra and leaves the note pending", x.Feed(new KeyboardModePlayed(61, true, 1.0)) is null && x.Extras.Count == 1 && !x.Results[0].IsResolved);
        x.Feed(new KeyboardModePlayed(60, true, 1.0));
        Check("judge: a second press of the same key is an extra", x.Feed(new KeyboardModePlayed(60, true, 1.02)) is null && x.Extras.Count == 2);
        Check("judge: a note-off alone is not an extra", x.Feed(new KeyboardModePlayed(70, false, 1.1)) is null && x.Extras.Count == 2);

        // The nearest note wins; a tie goes to the earlier; the other note stays free.
        var two = new KeyboardModeJudge(new[] { Ex(60, 1.0), Ex(60, 1.1) });
        var near = two.Feed(new KeyboardModePlayed(60, true, 1.04));
        Check("judge: a press takes the nearer of two notes of its key", near?.Index == 0 && !two.Results[1].IsResolved);
        Check("judge: the next press takes the other note", two.Feed(new KeyboardModePlayed(60, true, 1.1))?.Index == 1);
        var tie = new KeyboardModeJudge(new[] { Ex(60, 1.0), Ex(60, 1.1) });
        Check("judge: equally near goes to the earlier note", tie.Feed(new KeyboardModePlayed(60, true, 1.05))?.Index == 0);

        // Chords: onsets within 40 ms of the first are one group; hitting only some is a partial hit.
        var ch = new KeyboardModeJudge(new[] { Ex(60, 2.0), Ex(64, 2.01), Ex(67, 2.04), Ex(72, 2.041), Ex(48, 3.0) });
        Check("judge: notes within 40 ms of the chord's first are one group (40 in, 41 out)", ch.Results[0].Group == 0 && ch.Results[2].Group == 0 && ch.Results[3].Group == 1 && ch.Results[4].Group == 2 && ch.GroupStarts.SequenceEqual(new[] { 0, 3, 4 }), string.Join(",", ch.Results.Select(r => r.Group)));
        ch.Feed(new KeyboardModePlayed(60, true, 2.0));
        ch.Feed(new KeyboardModePlayed(64, true, 2.02));
        Check("judge: a chord with a note still open is not complete", ch.GroupState(0) is { Hits: 2, Misses: 0, Complete: false } && !ch.IsPartial(0));
        ch.Advance(2.3);
        Check("judge: a chord with one note missed is a partial hit", ch.GroupState(0) is { Hits: 2, Misses: 1, Complete: true } && ch.IsPartial(0));
        Check("judge: a chord with all notes missed is not partial", ch.GroupState(1) is { Hits: 0, Misses: 1, Complete: true } && !ch.IsPartial(1));
        Check("judge: a later chord is judged on its own", !ch.Results[4].IsResolved);

        // Sustain: the key must be held for the share of the note's length; a short note is never judged on it.
        var s = new KeyboardModeJudge(new[] { Ex(60, 1.0, 1.0), Ex(62, 3.0, 1.0), Ex(64, 5.0, 0.1), Ex(65, 7.0, 1.0) });
        s.Feed(new KeyboardModePlayed(60, true, 1.0)); s.Feed(new KeyboardModePlayed(60, false, 1.7));
        s.Feed(new KeyboardModePlayed(62, true, 3.0)); s.Feed(new KeyboardModePlayed(62, false, 3.3));
        s.Feed(new KeyboardModePlayed(64, true, 5.0)); s.Feed(new KeyboardModePlayed(64, false, 5.01));
        s.Feed(new KeyboardModePlayed(65, true, 7.0));
        Check("judge: held for 70% of its length counts as sustained", s.Results[0].Sustained == true && Math.Abs(s.Results[0].HeldSec - 0.7) < 1e-9);
        Check("judge: released after 30% is a short hold", s.Results[1].Sustained == false);
        Check("judge: a note under 120 ms is never a short hold", s.Results[2].Sustained == true);
        Check("judge: a key still down has no sustain result yet", s.Results[3].Sustained is null);
        s.Finish(7.9);
        Check("judge: finishing releases a held key at that time (90% held)", s.Results[3].Sustained == true && Math.Abs(s.Results[3].HeldSec - 0.9) < 1e-9);
        var re = new KeyboardModeJudge(new[] { Ex(60, 1.0, 1.0), Ex(60, 2.0, 1.0) });
        re.Feed(new KeyboardModePlayed(60, true, 1.0)); re.Feed(new KeyboardModePlayed(60, true, 2.0));
        Check("judge: pressing a held key again ends the old hold at that moment", re.Results[0].HeldSec == 1.0 && re.Results[0].Sustained == true && re.Results[1].Sustained is null);

        // Latency: the player hears the song late by the latency, so the press belongs earlier.
        Check("judge: compensation subtracts the latency", Math.Abs(KeyboardModeJudge.Compensate(1.1, 0.1) - 1.0) < 1e-12 && Math.Abs(KeyboardModeJudge.Compensate(10.0, 0.25) - 9.75) < 1e-12);
        var lat = new KeyboardModeJudge(new[] { Ex(60, 1.0) });
        var compensated = lat.Feed(new KeyboardModePlayed(60, true, KeyboardModeJudge.Compensate(1.1, 0.1)));
        Check("judge: a press 100 ms after the onset on the clock with 100 ms latency is perfect", compensated?.Grade == KeyboardModeGrade.Perfect && Math.Abs(compensated.OffsetMs) < 1e-6);
        Check("judge: the same press with the wrong sign would not even hit", new KeyboardModeJudge(new[] { Ex(60, 1.0) }).Feed(new KeyboardModePlayed(60, true, 1.1 + 0.1)) is null);

        // A time the clock could not tell is not judged and never a miss.
        var n = new KeyboardModeJudge(new[] { Ex(60, 1.0) });
        n.Advance(double.NaN);
        Check("judge: NaN times are ignored (not an extra, not a miss)", n.Feed(new KeyboardModePlayed(60, true, double.NaN)) is null && n.Extras.Count == 0 && !n.Results[0].IsResolved);
        Check("judge: an empty expected list judges nothing", new KeyboardModeJudge(Array.Empty<KeyboardModeExpected>()).Feed(new KeyboardModePlayed(60, true, 1)) is null);

        KeyboardModeJudgeDeterminism();
    }

    // A randomised but ordered stream, judged twice with different Advance cadences: the verdict must not depend on how often Advance runs, and must be repeatable.
    private static void KeyboardModeJudgeDeterminism()
    {
        var rng = new Random(20260610);
        var expected = new List<KeyboardModeExpected>();
        var t = 0.5;
        for (var i = 0; i < 300; i++)
        {
            t += 0.05 + rng.NextDouble() * 0.4;
            expected.Add(Ex(48 + rng.Next(0, 24), t, 0.1 + rng.NextDouble()));
            if (rng.Next(4) == 0) expected.Add(Ex(48 + rng.Next(0, 24), t + rng.NextDouble() * 0.05, 0.3));
        }
        var played = new List<KeyboardModePlayed>();
        foreach (var e in expected)
        {
            var roll = rng.NextDouble();
            if (roll < 0.15) continue;
            var key = roll < 0.25 ? e.Midi + 1 : e.Midi;
            var at = e.OnsetSec + (rng.NextDouble() - 0.5) * 0.4;
            played.Add(new KeyboardModePlayed(key, true, at));
            played.Add(new KeyboardModePlayed(key, false, at + e.DurationSec * (0.2 + rng.NextDouble())));
        }
        played.Sort((a, b) => a.TimeSec.CompareTo(b.TimeSec));
        var end = t + 3;

        string Run(bool advanceOften, bool shuffledInput, out int fed, out int hits)
        {
            var judge = new KeyboardModeJudge(shuffledInput ? expected.OrderByDescending(e => e.OnsetSec).ToList() : expected);
            fed = 0;
            hits = 0;
            foreach (var e in played)
            {
                if (advanceOften) judge.Advance(e.TimeSec);
                if (e.On) fed++;
                if (judge.Feed(e) is not null) hits++;
            }
            judge.Finish(end);
            return string.Join(";", judge.Results.Select(r => $"{r.Expected.Midi}@{r.Expected.OnsetSec:F4}:{r.Grade}:{r.OffsetMs:F3}:{r.Sustained}")) + "|" + judge.Extras.Count;
        }
        var a = Run(true, false, out var fed1, out var hits1);
        var b = Run(true, false, out _, out _);
        var c = Run(false, false, out _, out _);
        var d = Run(true, true, out _, out _);
        Check("judge: the same stream gives the same verdict twice", a == b);
        Check("judge: how often Advance runs does not change the verdict", a == c);
        Check("judge: the order the expected notes are given in does not change the verdict", a == d);
        var judge2 = new KeyboardModeJudge(expected);
        foreach (var e in played) judge2.Feed(e);
        judge2.Finish(end);
        var hitCount = judge2.Results.Count(r => r.IsHit);
        Check("judge: every press is a hit or an extra and every note is hit at most once", hitCount + judge2.Extras.Count == fed1 && hitCount == hits1 && hitCount > 100 && judge2.Extras.Count > 10, $"{hitCount}+{judge2.Extras.Count} vs {fed1}");
        Check("judge: after finishing every note is a hit or a miss", judge2.Results.All(r => r.IsResolved) && judge2.ResolvedPrefix == expected.Count);
        Check("judge: a hit is never farther than the good window", judge2.Results.Where(r => r.IsHit).All(r => Math.Abs(r.OffsetMs) <= 150 + 1e-6));
    }

    private static void TestKeyboardModeScore()
    {
        var notes = Enumerable.Range(0, 5).Select(i => Ex(60 + i, 1.0 + i)).ToArray();
        var j = new KeyboardModeJudge(notes);
        var score = new KeyboardModeScore();
        Check("score: nothing judged is 0%", score.Judged == 0 && score.AccuracyPercent == 0 && score.Streak == 0);
        j.Feed(new KeyboardModePlayed(60, true, 1.0));    // perfect
        j.Feed(new KeyboardModePlayed(61, true, 2.1));    // good
        j.Advance(3.2);                            // note 3 missed
        j.Feed(new KeyboardModePlayed(63, true, 4.0));
        j.Feed(new KeyboardModePlayed(64, true, 5.0));
        j.Feed(new KeyboardModePlayed(99, true, 5.0));    // extra
        score.Absorb(j);
        Check("score: hits, perfects, misses and extras add up", score.Hits == 4 && score.Perfects == 3 && score.Misses == 1 && score.Extras == 1 && score.Judged == 5, $"{score.Hits}/{score.Perfects}/{score.Misses}/{score.Extras}");
        Check("score: accuracy is hits over judged notes (80%)", Math.Abs(score.AccuracyPercent - 80) < 1e-9);
        Check("score: a miss resets the streak, the best is kept", score.Streak == 2 && score.BestStreak == 2);
        score.Absorb(j);
        Check("score: absorbing again counts nothing twice", score.Hits == 4 && score.Misses == 1 && score.Judged == 5);

        // A later hit waits for an earlier note still open, so the streak follows the score order, not the order of presses.
        var order = new KeyboardModeJudge(new[] { Ex(60, 1.0), Ex(62, 1.1), Ex(64, 1.2) });
        var s2 = new KeyboardModeScore();
        order.Feed(new KeyboardModePlayed(62, true, 1.1));
        order.Feed(new KeyboardModePlayed(64, true, 1.2));
        s2.Absorb(order);
        Check("score: a hit behind a note still open is not counted yet", s2.Judged == 0 && s2.Streak == 0);
        order.Advance(1.5);
        s2.Absorb(order);
        Check("score: once the first note is a miss the later hits follow it in order (streak 2)", s2.Misses == 1 && s2.Hits == 2 && s2.Streak == 2 && s2.BestStreak == 2);

        // Partial chords and short holds.
        var ch = new KeyboardModeJudge(new[] { Ex(60, 1.0, 1.0), Ex(64, 1.0, 1.0), Ex(67, 3.0, 0.5), Ex(71, 3.0, 0.5) });
        var s3 = new KeyboardModeScore();
        ch.Feed(new KeyboardModePlayed(60, true, 1.0)); ch.Feed(new KeyboardModePlayed(60, false, 1.2));
        ch.Advance(2.5);
        s3.Absorb(ch);
        Check("score: a chord with one note missed counts as a partial chord", s3.PartialChords == 1 && s3.Hits == 1 && s3.Misses == 1, $"{s3.PartialChords} {s3.Hits}");
        ch.Feed(new KeyboardModePlayed(67, true, 3.0)); ch.Feed(new KeyboardModePlayed(71, true, 3.0));
        ch.Finish(9);
        s3.Absorb(ch);
        Check("score: the other chord, hit whole, is not partial and a key let go early is a short hold", s3.PartialChords == 1 && s3.Hits == 3 && s3.ShortHolds == 1, $"{s3.PartialChords} {s3.Hits} {s3.ShortHolds}");
        s3.Reset();
        Check("score: reset clears everything", s3.Hits == 0 && s3.Misses == 0 && s3.Streak == 0 && s3.BestStreak == 0 && s3.PartialChords == 0 && s3.Extras == 0);

        // Arithmetic of a whole song: 3 of 8 gives 37.5%.
        var eight = new KeyboardModeJudge(Enumerable.Range(0, 8).Select(i => Ex(60, 1.0 + i)).ToArray());
        for (var i = 0; i < 3; i++) eight.Feed(new KeyboardModePlayed(60, true, 1.0 + i));
        eight.Finish(20);
        var s4 = new KeyboardModeScore();
        s4.Absorb(eight);
        Check("score: 3 hits of 8 notes is 37.5% with a best streak of 3", Math.Abs(s4.AccuracyPercent - 37.5) < 1e-9 && s4.BestStreak == 3 && s4.Streak == 0);
    }
}
