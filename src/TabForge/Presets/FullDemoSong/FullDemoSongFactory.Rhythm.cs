using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Models;

namespace TabForge.Presets;

// Owns: the rhythm section of the demo song: the two rhythm guitars, the bass and the sub drop, with their riffs and per-bar dynamics.
// Does not own: the structure (FullDemoSongFactory.Skeleton.cs) or the drum and lead parts (FullDemoSongFactory.Drums.cs, FullDemoSongFactory.LeadKeys.cs).
// Tests: TestFullDemoSong.

// Owner (c1): Rhythm Gtr L (7-string drop A), Rhythm Gtr R (8-string, L plus the numbered deltas), Bass (5-string, BassFollow plus
// the overrides) and the Sub Drop, plan sections 3.1-3.4. Every bar goes through Riff (slot sums asserted); chug jitter only via Hv.
internal static partial class FullDemoSongFactory
{
    // ------------------------------------------------------------------ named patterns (plan 3.1, Rhythm L)

    private const string RiffM1 = "16:O> 16:O 8:s4:2> 16:O 16:O 8:s4:0 16:O 16:O 8:s5:3 8:O 8:s4:2";
    private const string RiffM2 = "16:O> 16:O 8:s4:3> 16:O 16:O 8:s4:2 16:O 16:O 8:s5:3 8:P1> 8:X";
    private const string RiffM2ph = "16:O> 16:O 8:s4:3> 16:O 16:O 8:s4:2 16:O 16:O 8:s5:3 4:s5:1+ph=13+vib";
    private const string RiffVHead = "16:P0> 16:O 16:O 16:O 8:P1> 16:O 16:O 16:O 16:O 8:P3>";
    private const string RiffV = RiffVHead + " 8t:O 8t:O 8t:s7:1>";
    private const string RiffVb = RiffVHead + " 8t:s7:3> 8t:s7:1 8t:O";
    private const string RiffVe1 = RiffVHead + " 4:P6>>";
    private const string RiffDHead = "16:P0> 16:O 16:O 16:P1> 16:O 16:O 16:O> 16:O 16:O 16:P3> 16:O 16:O";
    private const string RiffD = RiffDHead + " 8:P1> 16:O 16:X";
    private const string RiffDph = RiffDHead + " 4:s5:3+ph=15+vib";
    private const string RiffDend = "8:P0> 8:X> 8:P1> 8:X> 4:P3> 16:O 16:O 16:O 16:O";
    private const string RiffBuild ="16:O 16:O 16:O 16:O 16:O 16:O 16:O 16:O 16:O 16:O 16:O 16:O 8:P7> 8:P8>";
    private const string RiffStab48 = "8:P0>+st 8:r 16:O 16:O 8:P1>+st 8:r 8:P3>+st 8:P1>+st 8:r";
    private const string RiffStab49 = "8:P0>+st 8:r 16:O 16:O 8:P6>>+st 4:r 16:O 16:O 8:r";
    private const string RiffStab50 = "8:P0>+st 8:X> 4:r 4:r 16:O 16:O 16:O 16:O";
    private const string RiffB7 = "16:O> 16:O 8:P1 16:O> 16:O 8:P3 16:O> 16:O 16:O 16:O 8:P6>";
    private const string RiffB7d = "16:O> 16:O 8:P1 16:O> 16:O 8:P3 8(2:3):P6> 8(2:3):P5>";
    private const string RiffC7Bar87 = "16:O> O P1 O O P3 X O> O P1 O O P3 X O> O";
    private const string RiffC7Bar88 = "16:P1 O O P3 X O> O P1 O O P3 X O> O P1 O";
    private const string RiffC7Bar89 = "16:O P3 X O> O P1 O O P3 X O> O P1 O O P3";
    private const string RiffBD1 = "8:P0>> 16:O 16:r 16:r 16:O 8:O 4..:P1> 16:O";
    private const string RiffBD2 = "8:O> 16:O 16:O 8:r 8:s5:3+ph=15+vib 4:P6> 8:O 8:X";
    private const string RiffBounce = "16:O> O r O O> O r O P1> O r O P3> O X O";
    private const string RiffBounce100 = "16:O> O r O O> O r O P1> O r O 4:P0>>";
    private const string RiffWindUp ="16:P0> 16:O 16:O 16:O 16:P0> 16:O 16:O 16:O 16:P0> 16:O 16:O 16:O 16:P0> 16:O 16:O 16:O";

    private static string ChordM(int n) => $"4.:P{n}> 16:O{n} 16:O{n} 8:P{n} 8:O{n} 8:O{n} 8:P{n}";
    private static string RiffP(string shape) => $"4.:{shape}>+lr 4.:{shape}+lr 4:{shape}+lr";
    private static string RiffC(int n) => $"2.:P{n}> 16:O{n} 16:O{n} 8:O{n}";
    private static string RiffC2(int n) => $"8:P{n}> 8:O{n} 8:O{n} 8:O{n} 8:P{n} 8:O{n} 8:O{n} 8:O{n}";
    private static string ChEnd(int n) => $"2:P{n}> 4:P{n} 4:P{n}+sod";

    private const string ShapeAm = "[s7:0 s5:0 s3:2]";
    private static readonly string[] RhyPreShapes = { ShapeAm, "Q1", "Q3", "Q0" };                                   // Am F G E
    private static readonly string[] RhyPreShapesR2 = { "[s5:12 s3:14]", "[s5:8 s3:10]", "[s5:10 s3:12]", "[s5:7 s3:9]" };  // R delta 2
    private static readonly int[] RhyChordsA = { 8, 3, 10, 0, 8, 3, 10 };   // F C G Am F C G
    private static readonly int[] RhyChordsB = { 10, 5, 12, 2, 10, 5, 12 }; // G D A Bm G D A (B minor)

    private static readonly Regex PowerChordRx = new(@"(^|:)P(\d+)", RegexOptions.CultureInvariant);

    /// <summary>Base velocity of a rhythm-section bar (plan 2.3).</summary>
    private static int RhythmDyn(int bar) => bar switch
    {
        <= 5 => Dyn.mp,
        <= 10 => Dyn.mf,
        <= 18 => Dyn.ff,
        <= 31 => Dyn.f,
        <= 39 => PreDyn(bar - 32),
        <= 47 => Dyn.ff,
        <= 50 => Dyn.fff,
        <= 62 => Dyn.f,
        <= 70 => PreDyn(bar - 63),
        <= 78 => Dyn.ff,
        <= 82 => Dyn.mf,
        <= 90 => Dyn.f,
        <= 101 => Dyn.ff,
        <= 109 => Dyn.f,
        <= 137 => Dyn.ff,
        <= 140 => Dyn.f,
        141 or 143 => Dyn.fff,
        _ => Dyn.ff,
    };

    /// <summary>Pre-chorus: mf, rising one step every two bars to ff.</summary>
    private static int PreDyn(int k) => k switch { <= 1 => Dyn.mf, <= 3 => Dyn.f, _ => Dyn.ff };

    /// <summary>Bars where Rhythm R adds the octave s4:n+2 to every power chord (R delta 1).</summary>
    private static bool OctaveChorus(int bar) => bar is (>= 40 and <= 47) or (>= 71 and <= 78) or (>= 102 and <= 117) or (>= 119 and <= 134);

    /// <summary>Rhythm L (right = false) or R (right = true) tokens of a bar, or null for an empty / simile bar (plan 3.2).</summary>
    private static string? RhythmTokens(int bar, bool right)
    {
        var tokens = bar switch
        {
            <= 9 => null,
            10 => "2:r 8:r 16:O 16:O",
            11 or 13 => RiffM1,
            12 => RiffM2,
            14 => RiffM2ph,
            15 => ChordM(8),
            16 => ChordM(3),
            17 => ChordM(10),
            18 => "2:P7> 4:P7 8:O 8:X",
            19 or 51 or 55 => RiffV,
            20 or 52 => RiffVb,
            21 => right ? "16:P0> 16:O+g 16:O 16:O 8:P1> 16:O 16:O 16:O 16:O 8:P3> 8t:O 8t:O 8t:s7:1>" : RiffV,   // R delta 3
            22 or 56 => RiffVe1,
            23 => "4.:P8> 8:O 4:P6> 16:O 16:O 16:O 16:O",
            24 or 26 or 28 or 30 => RiffD,
            25 or 29 or 57 or 58 => null,                                                                        // simile
            27 => RiffDph,
            31 => RiffDend,
            >= 32 and <= 35 => RiffP(RhyPreShapes[bar - 32]),
            >= 36 and <= 38 => RiffP(RhyPreShapes[bar - 36]),
            39 => RiffBuild,
            40 => "2.:P8>+sib 16:O8 16:O8 8:O8",                                                                    // RiffC(8), slide in from below
            >= 41 and <= 46 => RiffC(RhyChordsA[bar - 40]),
            47 or 78 or 109 or 117 => ChEnd(7),
            48 => RiffStab48,
            49 => right ? "8:P0>+st 8:r 16:O 16:O 8:P6>>+st 4:r 16:O+g 16:O+g 8:r" : RiffStab49,                // R delta 3
            50 => RiffStab50,
            53 => right ? RiffVHead + " 8t:s5:3+sh=15 8t:O 8t:s7:1>" : RiffV,                                    // R delta 4
            54 => "16:P0> 16:O 16:O 16:O 8:P1> 16:O 16:O 4:P5> 4:P6>+ss",
            59 => "1:P0+trem16",
            60 => "1:P1+trem16",
            61 => "1:P3+trem16",
            62 => "2:P6>+trem16 4:P5+trem16 4:X",
            >= 63 and <= 66 => RiffP(right ? RhyPreShapesR2[bar - 63] : RhyPreShapes[bar - 63]),                   // R delta 2
            >= 67 and <= 69 => RiffP(right ? RhyPreShapesR2[bar - 67] : RhyPreShapes[bar - 67]),
            70 => right ? RiffBuild[..RiffBuild.LastIndexOf(' ')] + " 8:[s6:0 s5:0]+psu" : RiffBuild,                        // R delta 5
            >= 71 and <= 77 => RiffC(RhyChordsA[bar - 71]),
            >= 79 and <= 82 => "1:r",
            >= 83 and <= 85 => RiffB7,
            86 => RiffB7d,
            87 => RiffC7Bar87,
            88 => RiffC7Bar88,
            89 => RiffC7Bar89,
            90 => "16:P0> 16:O 16:O 16:O 8:P1> 8:P3> 4:[s7:0 s6:0]+psd 4:r",
            91 or 93 or 95 or 97 => RiffBD1,
            92 or 96 => RiffBD2,
            94 => right ? "8:O> 8:O 2:s8:0 4:r" : "8:O> 8:O 2:s7:0 4:r",                                        // R delta 7
            98 => right ? "4:P0>> 4:P1>> 4:P3>> 4:[s8:0 s7:6 s6:6]>>" : "4:P0>> 4:P1>> 4:P3>> 4:P6>>",          // R delta 9
            99 => RiffBounce,
            100 => RiffBounce100,
            101 => RiffWindUp,
            >= 102 and <= 108 => RiffC(RhyChordsA[bar - 102]),
            >= 110 and <= 116 => RiffC2(RhyChordsA[bar - 110]),
            118 => "8:P10> 8:P12> 4:r",
            >= 119 and <= 125 => RiffC(RhyChordsB[bar - 119]),
            126 or 134 => ChEnd(9),
            >= 127 and <= 133 => RiffC2(RhyChordsB[bar - 127]),
            135 or 137 => RiffM1,
            136 or 138 => RiffM2,
            139 => "2:P2> 2:P10>",
            140 => "2:P12> 2:P9>",
            141 => "4:P2>>+ferm 4:r 2:r",
            142 => "1:r",
            143 => "16:O2> 16:O2 16:O2 16:P3> 16:O2 16:O2 16:P3> 16:O2 8:P5> 8:P3> 4:r",
            144 => "1:[s7:2 s6:2 s5:2 s4:4]>>+lr+fout",
            _ => null,
        };
        if (tokens is null || !right || !OctaveChorus(bar)) return tokens;
        // R delta 1: every power chord also gets the octave s4:n+2.
        return PowerChordRx.Replace(tokens, m =>
        {
            var n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            return $"{m.Groups[1].Value}[s7:{n} s6:{n} s5:{n} s4:{n + 2}]";
        });
    }

    /// <summary>Bass bars that override BassFollow (plan 3.3), or null to follow Rhythm L an octave down.</summary>
    private static string? BassTokens(int bar)
    {
        static string Roots(string root) => $"8:{root}> 8:{root} 8:{root} 8:{root} 8:{root} 8:{root} 8:{root} 8:{root}";
        static string RootA(int fret) => fret switch { 8 => "s4:1", 3 => "s3:3", 10 => "s4:3", 0 => "s3:0", _ => "s4:0" };
        static string RootB(int fret) => fret switch { 10 => "s4:3", 5 => "s3:5", 12 => "s3:0", 2 => "s3:2", _ => "s4:2" };
        return bar switch
        {
            6 => "1:s3:0+fin",
            7 => "1:s4:1",
            8 => "1:s3:3",
            9 => "1:s4:0",
            10 => "2.:s4:0+tie",
            >= 32 and <= 38 => $"1:{new[] { "s5:0", "s4:1", "s4:3", "s4:0", "s5:0", "s4:1", "s4:3" }[bar - 32]}+lr",
            >= 40 and <= 46 => Roots(RootA(RhyChordsA[bar - 40])),
            >= 71 and <= 77 => Roots(RootA(RhyChordsA[bar - 71])),
            >= 102 and <= 108 => Roots(RootA(RhyChordsA[bar - 102])),
            >= 110 and <= 116 => Roots(RootA(RhyChordsA[bar - 110])),
            47 or 78 or 109 or 117 => "2:s4:0> 4:s4:0 4:s4:0+sod",
            126 or 134 => "2:s4:2> 4:s4:2 4:s4:2+sod",
            >= 119 and <= 125 => Roots(RootB(RhyChordsB[bar - 119])),
            >= 127 and <= 133 => Roots(RootB(RhyChordsB[bar - 127])),
            79 => "8:s2:0+slap 8:s1:7+pop 8:s2:0+slap 8:s2:0+ds 8:s2:3+slap 8:s1:7+pop 8:s1:5+slap 8:s1:2+slap",
            80 => "8:s2:2+slap 8:s1:9+pop 8:s2:2+slap 8:s2:2+ds 8:s2:5+slap 8:s1:9+pop 8:s1:7+slap 8:s1:4+slap",
            81 => "8:s2:3+slap 8:s1:10+pop 8:s2:3+slap 8:s2:3+ds 8:s2:7+slap 8:s1:10+pop 8:s1:9+slap 8:s1:5+slap",
            82 => "4:s4:0+slap 4:s1:12+nh 4:s2:2+slap 8:s2:2+ds 8:s1:1",
            141 => "4:s5:2>>+ferm 4:r 2:r",
            142 => "1:r",
            144 => "1:s5:2+lr+fout",
            _ => null,
        };
    }

    /// <summary>Sub Drop bars (plan 3.4); written an octave up (Transpose -12). Everything else stays empty.</summary>
    private static string? SubTokens(int bar) => bar switch
    {
        11 => "1:s4:12>",
        91 => "2:s4:12>> 2:s4:12+tie",
        119 => "1:s4:14>",
        141 => "4:s4:14>>+ferm 4:r 2:r",
        142 => "1:r",
        143 => "2:s4:14>> 2:r",
        _ => null,
    };

    private static void BuildRhythm(SongProject song)
    {
        var rhyL = Track(song, RhyL);
        var rhyR = Track(song, RhyR);
        var bass = Track(song, Bass);
        var sub = Track(song, Sub);

        for (var bar = 1; bar <= BarCount; bar++)
        {
            var vel = RhythmDyn(bar);
            var transpose = bar is >= 135 and <= 138 ? 2 : 0;   // outro riff in B (O -> O2, P1 -> P3)
            foreach (var (t, right) in new[] { (rhyL, false), (rhyR, true) })
            {
                var tokens = RhythmTokens(bar, right);
                if (tokens is null) continue;
                Riff(t, bar, tokens, vel, transposeFrets: transpose, velOffset: right ? -2 : 0);
                GuitarDetails(t, bar, right, vel + (right ? -2 : 0));
            }

            var bassTokens = BassTokens(bar);
            if (bassTokens is not null) Riff(bass, bar, bassTokens, vel);
            else if (bar >= 10 && RhythmTokens(bar, false) is not null) BassFollow(rhyL, bass, bar, vel);
            BassDetails(bass, bar);

            if (SubTokens(bar) is { } subTokens)
            {
                Riff(sub, bar, subTokens, bar is 91 or 141 or 143 ? Dyn.fff : Dyn.ff);
                SubDetails(sub, bar);
            }
        }
    }

    /// <summary>The per-beat extras of a guitar bar that the grammar does not spell: beat text, pick strokes (L only), beams,
    /// sound duration, velocity ramps, whammy curves and fermatas on rests.</summary>
    private static void GuitarDetails(TrackModel t, int bar, bool right, int vel)
    {
        var beats = RhyBeats(t, bar);
        TabCell At(double slot) => beats.First(b => Math.Abs(b.Onset - slot) < 1e-6).Cell;

        if (!right)
        {
            var text = bar switch
            {
                19 => (0.0, "Riff V"), 24 => (0, "Riff D (3+3+3+3+4)"), 50 => (2, "gang: HEY!"), 83 => (0, "7/8: 2+2+3"),
                87 => (0, "7 over 4"), 90 => (8, "pick scrape"), 91 => (0, "gang: RISE!"), 94 => (4, "dive bomb"),
                _ => (-1.0, (string?)null),
            };
            if (text.Item2 is not null) At(text.Item1).Text = text.Item2;
            // N30 pick strokes: 19 all down, 24 alternate by 16th position
            if (bar is 19 or 24)
                foreach (var (onset, cell) in beats)
                    foreach (var n in cell.Notes)
                        n.Techniques.Add(bar == 19 || (int)Math.Round(onset) % 2 == 0 ? "PickDown" : "PickUp");
        }

        // B27 beams: the djent grouping, the 7/8 3-group and the 7-over-4 cell starts
        if (bar is 24 or 26 or 27 or 28 or 30)
            foreach (var s in new[] { 3, 6, 9 }) At(s).BreakSecondaryBeamBefore = true;
        if (bar is >= 83 and <= 86)
            foreach (var (onset, cell) in beats) if (onset >= 8 - 1e-6) cell.BeamMode = BeamMode.Force;
        var breaks = bar switch { 87 => new[] { 7, 14 }, 88 => new[] { 5, 12 }, 89 => new[] { 3, 10 }, _ => Array.Empty<int>() };
        foreach (var s in breaks) At(s).BeamMode = BeamMode.Break;

        // B29: stab cells ring 60 %
        if (bar is >= 48 and <= 50)
            foreach (var (_, cell) in beats) if (cell.Staccato) cell.SoundDurationPercent = 60;

        // velocity ramps: the build (39, 70) 80 -> 112 over the 12 chugs; the wind-up (101) 80 -> 127 over all 16
        if (bar is 39 or 70 or 101)
        {
            var count = bar == 101 ? 16 : 12;
            var top = bar == 101 ? 127 : 112;
            var off = right ? -2 : 0;
            for (var k = 0; k < count; k++)
            {
                var (onset, cell) = beats[k];
                var centre = (int)Math.Round(80 + (top - 80.0) * k / (count - 1)) + off;
                foreach (var n in cell.Notes) n.Velocity = Hv(t.Name, bar, onset, n.Fret, centre, 3, SectionOf(bar), Lane.Chug);
            }
        }
        // 91: the first chord is fff
        if (bar == 91) foreach (var n in At(0).Notes) n.Velocity = Dyn.fff + (right ? -2 : 0);

        // N12 whammy: L 92 gargle, L 94 dive bomb; R 92 predive-dive, R 94 E1 dive, R 96 predive
        switch (bar, right)
        {
            case (92, false): Whammy(At(6), At(6).Notes[0], "TremBar", (0, 0), (30, -2), (60, 0)); break;
            case (94, false): Whammy(At(4), At(4).Notes[0], "TremBarDive", (0, 0), (20, -8), (45, -24), (60, -24)); break;
            case (92, true): Whammy(At(6), At(6).Notes[0], "TremBarPrediveDive", (0, -4), (20, -4), (60, -16)); break;
            case (94, true): Whammy(At(4), At(4).Notes[0], "TremBarDive", (0, 0), (40, -8), (60, -8)); break;
            case (96, true): Whammy(At(6), At(6).Notes[0], "TremBarPredive", (0, -6), (15, 0), (60, 0)); break;
        }

        if (bar == 142) At(0).Fermata = true;
    }

    /// <summary>BassFollow (plan 3.3): the lowest note of each Rhythm L attack an octave down, same rhythm; keeps palm mute (with chug
    /// jitter), dead, accent, staccato and the shift slide; harmonics and whammy become a plain root; beam overrides are copied.</summary>
    private static void BassFollow(TrackModel gtr, TrackModel bass, int bar, int vel)
    {
        var m = bass.Measures[bar - 1];
        EnsureSlots(m);
        var sec = SectionOf(bar);
        foreach (var (onset, g) in RhyBeats(gtr, bar))
        {
            var c = Put(m, onset, g.DurationDenominator, g.Dots, g.TupletNumerator, g.TupletDenominator);
            c.Accent = g.Accent;
            c.Staccato = g.Staccato;
            c.Fermata = g.Fermata;
            c.BeamMode = g.BeamMode;
            c.BreakSecondaryBeamBefore = g.BreakSecondaryBeamBefore;
            var low = g.Notes.Where(n => !n.IsGraceNote).OrderByDescending(n => n.StringIndex).FirstOrDefault();
            if (low is null) { c.IsRest = true; continue; }
            var (s, fret) = (low.StringIndex + 1) switch
            {
                8 => (4, 0),
                var gs => (gs - 2, low.Fret),   // s7 -> s5, s6 -> s4, s5 -> s3, s4 -> s2, s3 -> s1
            };
            var note = Fret(bass, s, fret, vel);
            if (TechniqueNames.HasPalmMute(low.Techniques))
            {
                note.Techniques.Add("PalmMute");
                note.Velocity = Hv(bass.Name, bar, onset, fret, vel, 3, sec, Lane.Chug);
            }
            if (low.Dead) note.Dead = true;
            if (low.Techniques.Contains("ShiftSlide")) note.Techniques.Add("ShiftSlide");
            c.Notes.Add(note);
        }
    }

    private static void BassDetails(TrackModel bass, int bar)
    {
        var beats = RhyBeats(bass, bar);
        if (bar is >= 79 and <= 82) foreach (var (_, c) in beats) c.SoundDurationPercent = 80;
        if (bar == 142) beats[0].Cell.Fermata = true;
    }

    private static void SubDetails(TrackModel sub, int bar)
    {
        var beats = RhyBeats(sub, bar);
        TabCell At(double slot) => beats.First(b => Math.Abs(b.Onset - slot) < 1e-6).Cell;
        switch (bar)
        {
            case 11: Whammy(At(0), At(0).Notes[0], "TremBarDive", (0, 0), (40, -24), (60, -24)); break;
            case 91:
                Whammy(At(0), At(0).Notes[0], "TremBarDive", (0, 0), (30, -24), (60, -24));
                Whammy(At(8), At(8).Notes[0], "TremBarHold", (0, -24), (60, -24));
                break;
            case 119: Whammy(At(0), At(0).Notes[0], "TremBarDive", (0, 0), (40, -24), (60, -24)); break;
            case 142: At(0).Fermata = true; break;
            case 143: Whammy(At(0), At(0).Notes[0], "TremBarDive", (0, 0), (50, -24), (60, -24)); break;
        }
    }

    /// <summary>Voice-1 beats of a bar with their onsets, in time order.</summary>
    private static List<(double Onset, TabCell Cell)> RhyBeats(TrackModel t, int bar)
    {
        var cells = t.Measures[bar - 1].Cells;
        return Beats(cells).Select(b => (b.Onset, cells[b.Index])).ToList();
    }
}
