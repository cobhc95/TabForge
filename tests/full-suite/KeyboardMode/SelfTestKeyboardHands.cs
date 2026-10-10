using System.Diagnostics;
using TabForge.KeyboardMode.Hands;

namespace TabForge;

// Owns: the hand assigner checks: empty input, one-hand passages high and low, melody over bass, a wide chord split without crossing, a four-octave arpeggio,
//   contrary motion that never crosses, repeated notes, a guitar strum, unsorted input, determinism, speed on a 5000-note part, and the accuracy on the evaluation set.
// Does not own: the note source (SelfTestKeyboardModeKeyboard.cs). The evaluation pieces are HandEvalSet below.
// Tests: TestKeyboardHands, TestKeyboardHandsAccuracy.
public static partial class SelfTest
{
    /// <summary>A line of equal notes of <paramref name="lenMs"/> from <paramref name="startMs"/>.</summary>
    private static List<HandNote> HandLine(long startMs, long lenMs, params int[] pitches) =>
        pitches.Select((p, i) => new HandNote(p, startMs + i * lenMs, lenMs)).ToList();

    private static string HandsText(IReadOnlyList<HandNote> notes, Hand[] hands) => string.Join(" ", notes.Select((n, i) => $"{n.Pitch}{(hands[i] == Hand.Left ? "L" : "R")}"));

    private static void TestKeyboardHands()
    {
        Check("hands: no notes give no hands", HandAssigner.Assign(Array.Empty<HandNote>()).Length == 0);

        // One-hand passages stay in one hand: a high scale right, a low scale left, a tune around middle C right.
        var high = HandLine(0, 200, 72, 74, 76, 77, 79, 81, 83, 84, 83, 81, 79, 77, 76, 74, 72);
        Check("hands: a high scale is all right hand", HandAssigner.Assign(high).All(h => h == Hand.Right), HandsText(high, HandAssigner.Assign(high)));
        var low = HandLine(0, 200, 36, 38, 40, 41, 43, 45, 47, 48, 47, 45, 43, 41, 40, 38, 36);
        Check("hands: a low scale is all left hand", HandAssigner.Assign(low).All(h => h == Hand.Left), HandsText(low, HandAssigner.Assign(low)));
        var tune = HandLine(0, 250, 64, 64, 65, 67, 67, 65, 64, 62, 60, 60, 62, 64, 64, 62, 62);
        Check("hands: a tune around middle C stays in one hand", HandAssigner.Assign(tune).Distinct().Count() == 1, HandsText(tune, HandAssigner.Assign(tune)));

        // Melody over bass: whole-note bass and an eighth-note melody.
        var song = HandLine(0, 2000, 36, 41, 43, 36).Concat(HandLine(0, 250, 72, 74, 76, 77, 79, 77, 76, 74, 72, 71, 72, 74, 76, 74, 72, 71, 72, 76, 79, 84, 79, 76, 74, 72, 72, 72, 72, 72, 72, 72, 72, 72)).ToList();
        var songHands = HandAssigner.Assign(song);
        Check("hands: the bass is left and the melody right", songHands.Take(4).All(h => h == Hand.Left) && songHands.Skip(4).All(h => h == Hand.Right), HandsText(song, songHands));
        Check("hands: repeated notes stay in one hand", songHands.Skip(song.Count - 8).Distinct().Count() == 1);

        // A wide chord (two octaves) is split between the hands, lower notes left, and neither hand stretches past a tenth.
        var wide = new[] { 48, 52, 55, 60, 64, 67, 72 }.Select(p => new HandNote(p, 0, 1000)).ToList();
        var wideHands = HandAssigner.Assign(wide);
        var wl = wide.Where((_, i) => wideHands[i] == Hand.Left).Select(n => n.Pitch).ToList();
        var wr = wide.Where((_, i) => wideHands[i] == Hand.Right).Select(n => n.Pitch).ToList();
        Check("hands: a two-octave chord uses both hands without crossing", wl.Count > 0 && wr.Count > 0 && wl.Max() < wr.Min() && wl.Max() - wl.Min() <= 16 && wr.Max() - wr.Min() <= 16, HandsText(wide, wideHands));

        // A fast four-octave arpeggio up and down: the left hand takes the bottom, the right the top, and each sweep hands over once.
        int[] up = { 36, 43, 48, 52, 55, 60, 64, 67, 72, 76, 79, 84 };
        var arp = HandLine(0, 100, up.Concat(up.Reverse().Skip(1)).ToArray());
        var arpHands = HandAssigner.Assign(arp);
        var changes = Enumerable.Range(1, arp.Count - 1).Count(i => arpHands[i] != arpHands[i - 1]);
        Check("hands: an arpeggio over four octaves goes left to right and back", arpHands[0] == Hand.Left && arpHands[11] == Hand.Right && arpHands[^1] == Hand.Left && changes == 2, HandsText(arp, arpHands));

        // Contrary motion: the lines approach and the left hand never sits above the right at the same moment.
        var rise = HandLine(0, 250, 48, 50, 52, 53, 55, 57, 59, 60, 62, 64);
        var fall = HandLine(0, 250, 84, 83, 81, 79, 77, 76, 74, 72, 71, 69);
        var both = rise.Concat(fall).ToList();
        var bothHands = HandAssigner.Assign(both);
        var crossed = both.Where((_, i) => bothHands[i] == Hand.Left).Any(l => both.Where((_, j) => bothHands[j] == Hand.Right).Any(r => r.StartMs == l.StartMs && r.Pitch < l.Pitch));
        Check("hands: approaching lines never cross", !crossed && bothHands.Take(10).All(h => h == Hand.Left) && bothHands.Skip(10).All(h => h == Hand.Right), HandsText(both, bothHands));

        // A guitar's open E strum (two octaves, six notes): split, at most five per hand, lows left.
        var strum = Enumerable.Range(0, 4).SelectMany(b => new[] { 40, 47, 52, 56, 59, 64 }.Select(p => new HandNote(p, b * 500, 400))).ToList();
        var strumHands = HandAssigner.Assign(strum);
        Check("hands: a six-string strum is split between the hands, lows left", Enumerable.Range(0, 4).All(b => strumHands[b * 6] == Hand.Left && strumHands[b * 6 + 5] == Hand.Right), HandsText(strum, strumHands));

        // Unsorted input gives the same hand per note as sorted input, and the same input always gives the same answer.
        var piece = HandEvalSet.Load().Notes.Take(240).ToList();
        var sorted = HandAssigner.Assign(piece);
        var rng = new Random(7);
        var perm = Enumerable.Range(0, piece.Count).OrderBy(_ => rng.Next()).ToArray();
        var shuffledHands = HandAssigner.Assign(perm.Select(i => piece[i]).ToList());
        Check("hands: unsorted input is assigned like sorted input", perm.Select((src, at) => shuffledHands[at] == sorted[src]).All(x => x));
        Check("hands: the result is deterministic", HandAssigner.Assign(piece).SequenceEqual(sorted));
        Check("hands: the default options are the fitted constants", HandAssigner.Assign(piece, new HandAssignerOptions()).SequenceEqual(sorted) && HandAssignerOptions.Default == new HandAssignerOptions());
        Check("hands: options change the result (no register top for the left hand moves notes)", !HandAssigner.Assign(piece, new HandAssignerOptions { LeftTop = 20, RegisterWeight = 50 }).SequenceEqual(sorted));

        // Speed: 5000 notes (melody, bass and chords) in well under 50 ms.
        var big = new List<HandNote>(5000);
        var r2 = new Random(3);
        for (var i = 0; big.Count < 5000; i++)
        {
            big.Add(new HandNote(36 + r2.Next(12), i * 250, 500));
            big.Add(new HandNote(60 + r2.Next(24), i * 250, 200));
            if (i % 4 == 0) { big.Add(new HandNote(55, i * 250, 900)); big.Add(new HandNote(59, i * 250, 900)); big.Add(new HandNote(62, i * 250, 900)); }
        }
        HandAssigner.Assign(big);
        var best = long.MaxValue;
        for (var run = 0; run < 3; run++) { var sw = Stopwatch.StartNew(); HandAssigner.Assign(big); best = Math.Min(best, sw.ElapsedMilliseconds); }
        Log.Add($"  info  hands: {big.Count} notes in {best} ms");
        Check("hands: a 5000-note part takes under 50 ms", best < 50, $"{best} ms");
    }

    private static void TestKeyboardHandsAccuracy()
    {
        // Measured: the fitted model 97.8 % (1182 notes), 95.9 % when each piece is left out of the fit; the "below middle C is left" rule 85.4 %.
        var (notes, hands, _) = HandEvalSet.Load();
        var got = HandAssigner.Assign(notes);
        var accuracy = Enumerable.Range(0, notes.Count).Count(i => got[i] == hands[i]) / (double)notes.Count;
        var baseline = Enumerable.Range(0, notes.Count).Count(i => (notes[i].Pitch < 60 ? Hand.Left : Hand.Right) == hands[i]) / (double)notes.Count;
        Log.Add($"  info  hands: evaluation {notes.Count} notes, model {accuracy:P1}, middle-C split {baseline:P1}");
        Check("hands: the evaluation set has its notes", notes.Count > 1000, notes.Count.ToString());
        Check("hands: at least 97 % of the evaluation notes get their written hand", accuracy >= 0.97, $"{accuracy:P1}");
        Check("hands: the model beats the middle-C split by 10 points", accuracy - baseline >= 0.10, $"{accuracy:P1} vs {baseline:P1}");
    }
}

// Owns: the hand-assignment evaluation set: short piano textures with the hand of every note as written on the grand staff (upper staff right, lower staff left),
//   modelled on public-domain openings (a C major prelude in broken chords, a 3/8 bagatelle, an Alberti-bass sonatina, an E minor prelude, a slow 3/4 piece with
//   bass and chord, a chorale, a waltz) and plain drills (scales in octaves, a right-hand-only tune, a left-hand-only bass line), plus the demo song's piano part.
//   The pitches are written from memory, so a phrase may differ from a printed edition; the hand split is what is measured.
// Does not own: the assigner (src/TabForge/KeyboardMode/Hands) or the accuracy check (TestKeyboardHandsAccuracy above).
// Tests: TestKeyboardHandsAccuracy.
// Format: "piece <name> <ms per unit>", then voice lines named by hand letter (R or L) and voice number: tokens "<units>:<note>", "<units>:[<notes>]" or "<units>:r"; lines of one voice
//   continue each other, "(...)N" repeats, "|" is a bar line and ignored.
public static class HandEvalSet
{
    public const string Text = """
piece prelude-c 200
L1 (8:C4)2 | (8:C4)2 | (8:B3)2 | (8:C4)2 | (8:C4)2 | (8:C4)2 | (8:B3)2 | (8:B3)2 | (8:A3)2 | (8:D3)2 | (8:G3)2 | (8:G3)2 | (8:F3)2 | (8:F3)2 | (8:E3)2
L2 (1:r 7:E4)2 | (1:r 7:D4)2 | (1:r 7:D4)2 | (1:r 7:E4)2 | (1:r 7:E4)2 | (1:r 7:D4)2 | (1:r 7:D4)2 | (1:r 7:C4)2 | (1:r 7:C4)2 | (1:r 7:A3)2 | (1:r 7:B3)2 | (1:r 7:Bb3)2 | (1:r 7:A3)2 | (1:r 7:Ab3)2 | (1:r 7:G3)2
R1 (2:r 1:G4 1:C5 1:E5 1:G4 1:C5 1:E5)2 | (2:r 1:A4 1:D5 1:F5 1:A4 1:D5 1:F5)2 | (2:r 1:G4 1:D5 1:F5 1:G4 1:D5 1:F5)2 | (2:r 1:G4 1:C5 1:E5 1:G4 1:C5 1:E5)2
R1 (2:r 1:A4 1:E5 1:A5 1:A4 1:E5 1:A5)2 | (2:r 1:F#4 1:A4 1:D5 1:F#4 1:A4 1:D5)2 | (2:r 1:G4 1:D5 1:G5 1:G4 1:D5 1:G5)2 | (2:r 1:E4 1:G4 1:C5 1:E4 1:G4 1:C5)2
R1 (2:r 1:E4 1:G4 1:C5 1:E4 1:G4 1:C5)2 | (2:r 1:D4 1:F#4 1:C5 1:D4 1:F#4 1:C5)2 | (2:r 1:D4 1:G4 1:B4 1:D4 1:G4 1:B4)2 | (2:r 1:E4 1:G4 1:C#5 1:E4 1:G4 1:C#5)2
R1 (2:r 1:D4 1:A4 1:D5 1:D4 1:A4 1:D5)2 | (2:r 1:D4 1:F4 1:B4 1:D4 1:F4 1:B4)2 | (2:r 1:C4 1:G4 1:C5 1:C4 1:G4 1:C5)2

piece bagatelle-a 125
R1 1:E5 1:D#5 | 1:E5 1:D#5 1:E5 1:B4 1:D5 1:C5 | 2:A4 1:r 1:C4 1:E4 1:A4 | 2:B4 1:r 1:E4 1:G#4 1:B4 | 2:C5 1:r 1:E4 1:E5 1:D#5
R1 1:E5 1:D#5 1:E5 1:B4 1:D5 1:C5 | 2:A4 1:r 1:C4 1:E4 1:A4 | 2:B4 1:r 1:E4 1:C5 1:B4 | 4:A4 2:r
L1 2:r | 6:r | 1:A2 1:E3 1:A3 3:r | 1:E2 1:E3 1:G#3 3:r | 1:A2 1:E3 1:A3 3:r
L1 6:r | 1:A2 1:E3 1:A3 3:r | 1:E2 1:E3 1:G#3 3:r | 1:A2 1:E3 1:A3 3:r

piece sonatina-c 150
R1 8:C5 4:E5 4:G5 | 6:B4 1:C5 1:D5 8:C5 | 8:A5 4:G5 4:C6 | 4:G5 2:F5 2:E5 8:F5 | 8:E5 4:D5 4:C5 | 4:B4 4:G4 8:C5
L1 2:C4 2:G4 2:E4 2:G4 2:C4 2:G4 2:E4 2:G4 | 2:D4 2:G4 2:F4 2:G4 2:C4 2:G4 2:E4 2:G4 | 2:C4 2:A4 2:F4 2:A4 2:C4 2:G4 2:E4 2:G4
L1 2:B3 2:G4 2:D4 2:G4 2:C4 2:G4 2:E4 2:G4 | 2:C4 2:G4 2:E4 2:G4 2:B3 2:G4 2:D4 2:G4 | 2:G3 2:G4 2:F4 2:G4 8:[C4 E4]

piece prelude-em 300
R1 12:B4 4:C5 | 16:B4 | 12:B4 4:C5 | 8:B4 8:A4 | 12:A4 4:B4 | 16:A4
L1 (2:[G3 B3 E4])8 | (2:[G3 B3 E4])4 (2:[G3 B3 D#4])4 | (2:[F#3 A3 D#4])8 | (2:[F#3 A3 D4])4 (2:[F3 A3 D4])4 | (2:[F3 A3 C4])8 | (2:[E3 A3 C4])8

piece slow-34 260
L1 (4:G2 8:[B3 D4 F#4] 4:D2 8:[A3 C#4 F#4])6
R1 12:r | 12:r | 12:r | 12:r | 4:r 4:F#5 4:A5 | 4:G5 4:F#5 4:C#5 | 4:B4 4:C#5 4:D5 | 12:A4 | 4:r 4:F#5 4:A5 | 4:G5 4:F#5 4:C#5 | 4:B4 4:C#5 4:D5 | 12:E5

piece chorale-g 400
R1 2:[D4 G4] 2:[D4 G4] 2:[D4 F#4] 2:[C4 E4] 4:[B3 D4] 2:[D4 G4] 2:[D4 A4] 2:[D4 B4] 2:[E4 C5] 2:[D4 B4] 2:[C4 A4] 4:[B3 G4]
L1 2:[G2 B3] 2:[G2 B3] 2:[D3 A3] 2:[C3 G3] 4:[G2 G3] 2:[G2 B3] 2:[F#2 A3] 2:[G2 G3] 2:[C3 G3] 2:[G2 G3] 2:[D3 F#3] 4:[G2 D3]

piece waltz-c 180
L1 (4:C3 4:[E3 G3 C4] 4:[E3 G3 C4] | 4:G2 4:[F3 G3 B3] 4:[F3 G3 B3])2 | 4:A2 4:[E3 A3 C4] 4:[E3 A3 C4] | 4:F2 4:[F3 A3 C4] 4:[F3 A3 C4] | 4:G2 4:[F3 G3 B3] 4:[F3 G3 B3] | 4:C3 4:[E3 G3 C4] 4:r
R1 6:E5 2:D5 2:C5 2:E5 | 8:D5 4:G4 | 6:F5 2:E5 2:D5 2:F5 | 8:E5 4:C5 | 2:C5 2:E5 2:A5 2:G5 2:E5 2:C5 | 6:A4 2:C5 4:F5 | 4:D5 4:B4 4:G4 | 12:C5

piece octave-scales 130
R1 2:C4 2:D4 2:E4 2:F4 2:G4 2:A4 2:B4 2:C5 2:D5 2:E5 2:F5 2:G5 2:A5 2:B5 4:C6 2:B5 2:A5 2:G5 2:F5 2:E5 2:D5 2:C5 2:B4 2:A4 2:G4 2:F4 2:E4 2:D4 4:C4
L1 2:C3 2:D3 2:E3 2:F3 2:G3 2:A3 2:B3 2:C4 2:D4 2:E4 2:F4 2:G4 2:A4 2:B4 4:C5 2:B4 2:A4 2:G4 2:F4 2:E4 2:D4 2:C4 2:B3 2:A3 2:G3 2:F3 2:E3 2:D3 4:C3

piece tune-rh-only 250
R1 2:E4 2:E4 2:F4 2:G4 | 2:G4 2:F4 2:E4 2:D4 | 2:C4 2:C4 2:D4 2:E4 | 3:E4 1:D4 4:D4 | 2:E4 2:E4 2:F4 2:G4 | 2:G4 2:F4 2:E4 2:D4 | 2:C4 2:C4 2:D4 2:E4 | 3:D4 1:C4 4:C4

piece bass-lh-only 250
L1 2:A2 2:C3 2:E3 2:A3 | 2:G2 2:B2 2:D3 2:G3 | 2:F2 2:A2 2:C3 2:F3 | 2:E2 2:G#2 2:B2 2:E3 | 2:A2 2:E3 2:A3 2:E3 | 2:D3 2:F3 2:A3 2:F3 | 2:E3 2:B2 2:G#2 2:E2 | 8:A2

piece walking-bass 240
L1 2:C3 2:E3 2:G3 2:A3 | 2:D3 2:F3 2:A3 2:C4 | 2:G2 2:B2 2:D3 2:F3 | 2:C3 2:G2 2:A2 2:B2 | 2:C3 2:E3 2:G3 2:E3 | 2:F2 2:A2 2:C3 2:A2 | 2:G2 2:D3 2:B2 2:G2 | 8:C3
R1 4:[E4 G4] 2:C5 2:E5 | 6:[F4 A4] 2:D5 | 4:[F4 B4] 4:D5 | 8:[E4 G4 C5] | 2:G5 2:E5 2:C5 2:G4 | 4:[A4 C5] 4:F5 | 4:[F4 B4 D5] 4:G5 | 8:[E4 G4 C5]

piece demo-piano 500
R1 6:E6 2:D6 4:C6 4:E6 | 8:D6 8:r | 6:E6 2:D6 4:C6 4:G6 | 8:B5 8:G#5
R1 16:[G4 B4 D5] | 16:[F#4 A4 D5] | 16:[E4 A4 C#5] | 16:[F#4 B4 D5] | 16:[G4 B4 D5] | 16:[F#4 A4 D5] | 16:[E4 A4 C#5] | 16:[F#4 A#4 C#5]
L1 16:A2 | 16:F2 | 16:C3 | 16:E2 | 8:G2 8:G2 | 8:D2 8:D2 | 8:A2 8:A2 | 8:B2 8:B2 | 8:G2 8:G2 | 8:D2 8:D2 | 8:A2 8:A2 | 8:F#2 8:F#2

piece wide-chords 600
L1 4:[C2 C3] 4:[G1 G2] 4:[A1 A2] 4:[F1 F2] | 4:[C2 G2 C3] 4:[G1 D2 G2] 4:[F1 C2 F2] 4:[C2 G2 E3]
R1 4:[E4 G4 C5 E5] 4:[D4 G4 B4 D5] 4:[C4 E4 A4 C5] 4:[C4 F4 A4 C5] | 4:[G4 C5 E5 G5] 4:[G4 B4 D5 G5] 4:[A4 C5 F5 A5] 4:[G4 C5 E5 G5]

piece invention-c 160
R1 1:r 1:C4 1:D4 1:E4 1:F4 1:D4 1:E4 1:C4 2:G4 2:C5 2:B4 2:C5 | 1:D5 1:G4 1:A4 1:B4 1:C5 1:A4 1:B4 1:G4 2:D5 2:G5 2:F5 2:G5 | 1:E5 1:A5 1:G5 1:F5 1:E5 1:G5 1:F5 1:A5 1:G5 1:F5 1:E5 1:D5 1:C5 1:E5 1:D5 1:F5
L1 8:r 1:r 1:C3 1:D3 1:E3 1:F3 1:D3 1:E3 1:C3 | 4:G3 4:G2 8:r | 4:r 2:C3 2:B2 2:C3 2:D3 2:E3 2:G3

piece minuet-g 220
R1 4:D5 2:G4 2:A4 2:B4 2:C5 | 4:D5 4:G4 4:G4 | 4:E5 2:C5 2:D5 2:E5 2:F#5 | 4:G5 4:G4 4:G4 | 4:C5 2:D5 2:C5 2:B4 2:A4 | 4:B4 2:C5 2:B4 2:A4 2:G4 | 4:F#4 2:G4 2:A4 2:B4 2:G4 | 12:A4
L1 8:G3 4:A3 | 12:B3 | 12:C4 | 12:B3 | 12:A3 | 12:G3 | 4:D4 4:B3 4:G3 | 8:D4 4:D3

piece nocturne-eb 200
L1 (1:Eb2 1:Bb2 1:G3 1:Bb2 1:G3 1:Bb2)2 | (1:Ab1 1:Ab2 1:C3 1:Eb3 1:C3 1:Ab2)2 | (1:Bb1 1:F2 1:D3 1:Ab3 1:D3 1:F2)2 | (1:Eb2 1:Bb2 1:G3 1:Bb2 1:G3 1:Bb2)2
R1 6:Bb4 4:G5 2:F5 | 6:G5 4:F5 2:Eb5 | 6:D5 2:Eb5 2:F5 2:D5 | 12:Eb5

piece broken-lh 220
L1 (1:C2 1:G2 1:E3 1:G2)2 | (1:F2 1:C3 1:A3 1:C3)2 | (1:G2 1:D3 1:B3 1:D3)2 | (1:C2 1:G2 1:E3 1:G2)2
R1 4:E5 4:G5 | 4:F5 4:A5 | 4:D5 2:B4 2:G5 | 8:C5
""";

    /// <summary>The notes of every piece (piece offsets keep the pieces apart in time) and the written hand of each.</summary>
    public static (List<HandNote> Notes, List<Hand> Hands, List<string> Piece) Load()
    {
        var notes = new List<HandNote>(); var hands = new List<Hand>(); var piece = new List<string>();
        foreach (var block in Text.Split("piece ", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var head = lines[0].Split(' ');
            var unit = long.Parse(head[1], System.Globalization.CultureInfo.InvariantCulture);
            var cursor = new Dictionary<string, long>();
            var offset = notes.Count == 0 ? 0 : notes.Max(n => n.StartMs + n.DurationMs) + 5000;
            foreach (var line in lines.Skip(1))
            {
                var voice = line[..2];
                var t = cursor.GetValueOrDefault(voice);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Expand(line[2..]), @"(\d+):(\[[^\]]*\]|\S+)"))
                {
                    var len = long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * unit;
                    foreach (var name in m.Groups[2].Value.Trim('[', ']').Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (name == "r") continue;
                        notes.Add(new HandNote(Midi(name), offset + t, len));
                        hands.Add(voice[0] == 'L' ? Hand.Left : Hand.Right);
                        piece.Add(head[0]);
                    }
                    t += len;
                }
                cursor[voice] = t;
            }
        }
        return (notes, hands, piece);
    }

    private static string Expand(string s)
    {
        var rx = new System.Text.RegularExpressions.Regex(@"\(([^()]*)\)(\d+)");
        while (rx.IsMatch(s)) s = rx.Replace(s, m => string.Join(' ', Enumerable.Repeat(m.Groups[1].Value, int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))));
        return s;
    }

    /// <summary>"C4" is 60, "F#3" 54, "Bb3" 58.</summary>
    public static int Midi(string name)
    {
        var step = "C D EF G A B".IndexOf(name[0]);
        var i = 1; var acc = 0;
        if (name[i] == '#') { acc = 1; i++; } else if (name[i] == 'b') { acc = -1; i++; }
        return (int.Parse(name[i..], System.Globalization.CultureInfo.InvariantCulture) + 1) * 12 + step + acc;
    }
}
