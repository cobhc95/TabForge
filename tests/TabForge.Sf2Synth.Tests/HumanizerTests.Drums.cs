using System;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Humanize;
using Xunit;

namespace TabForge.Sf2Synth.Tests;

// Owns: the drum-family checks: crash always under snare on flat input, backbeat weight, successive-crash fatigue and
//   already-human drums untouched.
// Does not own: the melodic checks (HumanizerTests.cs), the parts (HumanizerTests.Songs.cs).
// Tests: Humanizer drum branch, HumanTable drum constants.
public sealed partial class HumanizerTests
{
    private static Part Drums(int bars = 8, double bpm = 120)
    {
        var w = new Writer(bpm);
        for (var b = 0; b < bars; b++)
        {
            w.Note(b, 0, 0.25, 36, vel: 100);
            w.Note(b, 0, 0.25, 42, vel: 100);
            w.Note(b, 0.5, 0.25, 42, vel: 100);
            w.Note(b, 1, 0.25, 38, vel: 100);
            w.Note(b, 1, 0.25, 42, vel: 100);
            w.Note(b, 1.5, 0.25, 42, vel: 100);
            w.Note(b, 2, 0.25, 36, vel: 100);
            w.Note(b, 2, 0.25, 42, vel: 100);
            w.Note(b, 2.5, 0.25, 42, vel: 100);
            w.Note(b, 3, 0.25, 38, vel: 100);
            w.Note(b, 3, 0.25, 42, vel: 100);
            w.Note(b, 3.5, 0.25, 42, vel: 100);
            if (b % 4 == 0) w.Note(b, 0, 0.25, 49, vel: 100);
        }
        return new(HumanTable.DrumProgram, w.Notes);
    }

    private static Part TwoCrashes()
    {
        var w = new Writer(120);
        // Written at 70 so both crashes sit below the crash ceiling and the -1.5 dB fatigue step is not masked by the clamp.
        w.Note(0, 0, 0.25, 42, vel: 70);
        w.Note(0, 0, 0.25, 49, vel: 70);
        w.Note(0, 2, 0.25, 49, vel: 70); // same bar/phrase, later beat: only fatigue separates them
        w.Note(1, 0, 0.25, 38, vel: 70);
        return new(HumanTable.DrumProgram, w.Notes);
    }

    [Fact]
    public void DrumsAreHumanizedWithCrashUnderSnare()
    {
        var part = Drums();
        var (outp, _) = Play(part, On);
        var crash = part.Notes.Select((n, i) => (n.Key, outp[i].Velocity)).Where(x => x.Key == 49).Select(x => x.Velocity).ToArray();
        var snare = part.Notes.Select((n, i) => (n.Key, outp[i].Velocity)).Where(x => x.Key is 38 or 40).Select(x => x.Velocity).ToArray();
        output.WriteLine($"drum max crash {crash.Max()}, max snare {snare.Max()}");
        Assert.True(crash.Max() < snare.Max(), $"crash {crash.Max()} >= snare {snare.Max()}");
        Assert.NotEqual(part.Notes.Select(n => n.Velocity), outp.Select(o => o.Velocity));
        var backbeat = part.Notes.Select((n, i) => (n, outp[i].Velocity)).Where(x => x.n.Key is 38 or 40 && (int)Math.Floor(x.n.Beat) % 2 == 1).Select(x => x.Velocity).Average();
        output.WriteLine($"snare 2&4 mean {backbeat:F1}");
        Assert.True(backbeat > 100, $"backbeat {backbeat:F1} <= 100");
        Assert.All(outp, o => Assert.InRange(o.Velocity, 1, 127));
        Assert.All(outp, o => Assert.True(o.OffMs > o.OnMs));
    }

    [Fact]
    public void SnareOnTwoAndFourGetsTheBoost()
    {
        var w = new Writer(120);
        for (var b = 0; b < 8; b++)
        {
            w.Note(b, 0, 0.25, 38, vel: 90);
            w.Note(b, 1, 0.25, 38, vel: 90);
            w.Note(b, 2, 0.25, 38, vel: 90);
            w.Note(b, 3, 0.25, 38, vel: 90);
        }
        var (outp, _) = Play(new Part(HumanTable.DrumProgram, w.Notes), On);
        var on24 = w.Notes.Select((n, i) => (n, outp[i].Velocity)).Where(x => (int)x.n.Beat == 1 || (int)x.n.Beat == 3).Select(x => x.Velocity).ToArray();
        var on13 = w.Notes.Select((n, i) => (n, outp[i].Velocity)).Where(x => (int)x.n.Beat == 0 || (int)x.n.Beat == 2).Select(x => x.Velocity).ToArray();
        output.WriteLine($"snare 2&4 mean {on24.Average():F1}, 1&3 mean {on13.Average():F1}");
        Assert.True(on24.Average() > on13.Average(), $"2&4 {on24.Average()} <= 1&3 {on13.Average()}");
    }

    [Fact]
    public void ASecondCrashInThePhraseIsQuietAndLater()
    {
        var part = TwoCrashes();
        var (outp, _) = Play(part, On);
        var crashes = part.Notes.Select((n, i) => (n, outp[i])).Where(x => x.n.Key == 49).OrderBy(x => x.n.OnMs).ToList();
        var (first, second) = (crashes[0].Item2, crashes[1].Item2);
        var db = 40 * Math.Log10((double)second.Velocity / first.Velocity);
        output.WriteLine($"crash1 vel {first.Velocity} at {first.OnMs:F1} ms, crash2 vel {second.Velocity} at {second.OnMs:F1} ms, delta {db:F2} dB");
        Assert.InRange(db, -2.5, -0.5);
        Assert.True(second.OnMs - first.OnMs > 0);
    }
}
