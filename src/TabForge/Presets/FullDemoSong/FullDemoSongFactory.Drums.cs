using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

// Owns: the drum part of the demo song in every bar: fills, ramps, rolls, beam groups and the hit velocities.
// Does not own: the structure and marks (FullDemoSongFactory.Skeleton.cs) or the rhythm and lead parts (FullDemoSongFactory.Rhythm.cs, FullDemoSongFactory.LeadKeys.cs).
// Tests: TestFullDemoSong.

// Owner (b): the drum part, plan section 3.9, every one of the 144 bars. All velocities come from the Grid / Fill / Roll helpers
// (which humanise through Hv) or from Hv directly; there is no other source of variation, so every build is identical.
internal static partial class FullDemoSongFactory
{
    // ---- named grooves (plan 3.9 bar map). Lanes: K S SS CL H R C CH SP T1..T4; one character per 16th.
    private const string D16 = "dddddddddddddddd";
    private const string Back2and4 = "....x.......x...";
    private const string BackOn3 = "........x.......";

    /// <summary>Source bars that end in a fill or a ramp: the next bar's downbeat crash is fff (plan 3.9 "Fill ramp").</summary>
    internal static readonly int[] DrumFillBars =
        { 14, 18, 22, 23, 27, 31, 39, 43, 47, 50, 54, 58, 62, 70, 73, 78, 82, 90, 101, 105, 109, 113, 117, 122, 126, 130, 134, 138, 140 };

    /// <summary>Owner (b): the drum part, plan section 3.9 (Grid / Fill / Roll / Hv).</summary>
    private static void BuildDrums(SongProject song)
    {
        var d = Track(song, Drums);
        void G(int bar, params (string, string)[] lanes) => Grid(d, bar, SectionOf(bar), lanes);
        // the lead hand leaves the hats / ride for the crash on a section downbeat
        static string NoOne(string p) => "." + p[1..];

        // ---- 1–10 Intro: Embers on the TR-808 (the kit change itself is the skeleton's mix table: Program 25 at 1, 0 at 11)
        var pickup = Put(d.Measures[0], 0, 4);
        pickup.IsRest = true;
        pickup.Text = "808 kit";                                                            // B17
        // 2 stays empty (S47); bar 3 holds only the reverse-cymbal swell (below): a fade-in is a channel-wide expression ramp, so nothing else may sound during it
        for (var bar = 4; bar <= 5; bar++) G(bar, ("K", "x.........x....."));                                  // 808a
        for (var bar = 6; bar <= 7; bar++) G(bar, ("K", "x.........x....."), ("CL", "........x......."), ("H", "X.x.X.x.X.x.X.x."));  // 808b
        // 808c: straight 8ths, then the trap rolls: 6 × 16(6:4) and 5 × 16(5:4), each 60 → 90
        G(8, ("K", "x.........x.x..."), ("CL", "........x......."), ("H", "X.x.X.x........."));
        Roll(d, 8, 8, 6, 6, 4, 16, 42, 60, 90);
        Roll(d, 8, 12, 5, 5, 4, 16, 42, 60, 90);
        // 808d: odd-tuplet hat rolls 7:4, 9:8, 10:8, three 16ths and four 64ths, one continuous swell 60 → 95, the last 64th opens
        G(9, ("K", "x..............."), ("CL", "........x......."));
        var rolls = new (double Start, int Count, int TupN, int TupD, int Den)[] { (0, 7, 7, 4, 16), (4, 9, 9, 8, 32), (8, 10, 10, 8, 32), (12, 3, 0, 0, 16), (15, 4, 0, 0, 64) };
        var total = rolls.Sum(r => r.Count) - 1;
        var done = 0;
        foreach (var (start, count, tn, td, den) in rolls)
        {
            int V(int k) => (int)Math.Round(60 + 35.0 * k / total);
            Roll(d, 9, start, count, tn, td, den, 42, V(done), V(done + count - 1));
            done += count;
        }
        var lastHat = d.Measures[8].Cells.Single(c => Math.Abs((c.RhythmicPosition ?? -1) - 15.75) < 1e-6).Notes.Single(n => n.MidiValue == 42);
        var open = Drum(46, lastHat.Velocity);
        lastHat.MidiValue = open.MidiValue; lastHat.Fret = open.Fret; lastHat.StringIndex = open.StringIndex;
        // 3 (A7-A07): the reverse-cymbal swell on its own, a half note on beats 3-4 after a half rest, rising into the 808 kick of bar 4 (fade in, N24)
        Put(d.Measures[2], 0, 2).IsRest = true;
        var swell = Put(d.Measures[2], 8, 2);
        var reverse = Drum(49, Hv(d.Name, 3, 8, 49, 120, 4, Section.Swell, Lane.Crash));
        reverse.Techniques.Add("FadeIn");                                                   // N24
        swell.Notes.Add(reverse);
        // 10 (3/4) Swell: the 808 snare accelerates 16ths → 16(6:4) → 32(11:8) into the band
        G(10, ("K", "p..........."));
        Roll(d, 10, 0, 4, 0, 0, 16, 38, 60, 80);
        Roll(d, 10, 4, 6, 6, 4, 16, 38, 80, 100);
        Roll(d, 10, 8, 11, 11, 8, 32, 38, 100, 124);

        // ---- 11–18 Intro: Meridian (standard kit)
        const string halfK1 = "xx..xx..xx..x...", halfK2 = "xx..xx..xx..x.x.", chinaHalf = "....X.......X...";
        G(11, ("K", "X" + halfK1[1..]), ("S", BackOn3), ("CH", chinaHalf), ("C", "X..............."));   // HalfM
        G(12, ("K", halfK2), ("S", BackOn3), ("CH", chinaHalf));
        G(13, ("K", halfK1), ("S", BackOn3), ("CH", chinaHalf));
        G(14, ("K", halfK2), ("S", BackOn3), ("CH", chinaHalf));
        Fill(d, 14, FillKind.P4);
        const string back16K = "x.....x.x.....x.", back16S = "....x..g....x...", ride8 = "X.x.X.x.X.x.X.x.";
        G(15, ("K", back16K), ("S", back16S), ("R", NoOne(ride8)), ("C", "X..............."));             // Back16
        for (var bar = 16; bar <= 17; bar++) G(bar, ("K", back16K), ("S", back16S), ("R", ride8));
        Fill(d, 18, FillKind.F16, flam: true);

        // ---- 19–31 Verse 1
        const string rideQ = "X...X...X...X...";
        G(19, ("K", D16), ("S", Back2and4), ("R", NoOne(rideQ)), ("C", "X..............."));              // DBV
        for (var bar = 20; bar <= 21; bar++) G(bar, ("K", D16), ("S", Back2and4), ("R", rideQ));
        G(22, ("K", "dddddddddddd...."), ("S", "....x.......rrrr"), ("R", "X...X...X......."));             // ending 1
        G(23, ("K", "x...x...x...x..."), ("S", "rrrrrrrr........"), ("T2", "........rr......"),              // ending 2
              ("T3", "..........rr...."), ("T4", "............rrrX"));
        const string djentK = "x..x..x..x..x.x.", hatQ = "X...X...X...X...";
        G(24, ("K", djentK), ("S", BackOn3), ("H", NoOne(hatQ)), ("C", "X..............."));             // DjentD
        foreach (var bar in new[] { 26, 28, 30 }) G(bar, ("K", djentK), ("S", BackOn3), ("H", hatQ));
        G(27, ("K", "x..x..x..x......"), ("S", "........x...rrrr"), ("H", "X...X...X......."));
        G(31, ("K", djentK), ("S", BackOn3), ("H", hatQ));
        Fill(d, 31, FillKind.P8, flam: true);

        // ---- 32–39 Pre-chorus 1: the build (crash quarters over a kick that doubles every two bars)
        void Build1(int bar) => G(bar, ("K", "x.......x......."), ("C", "X.......x......."));
        void Build2(int bar) => G(bar, ("K", "x...x...x...x..."), ("C", "X...x...x...x..."), ("H", "..o...o...o...o."));
        void Build3(int bar) => G(bar, ("K", "x.x.x.x.x.x.x.x."), ("C", "X...x...x...x..."), ("H", "..o...o...o...o."));
        Build1(32); Build1(33); Build2(34); Build2(35); Build3(36); Build3(37);
        G(38, ("K", D16), ("C", "X...x...x...x..."));                                                  // Build4
        Fill(d, 39, FillKind.Roll39);

        // ---- 40–47 Chorus 1 (half-time, crashes alternating 49 / 57)
        void ChorusHT(int bar, bool downbeat)
        {
            var lanes = new List<(string, string)> { ("K", "x..x..x.x.....x."), ("S", BackOn3), ("C", "X...2...x...2...") };
            if (downbeat) lanes.Add(("CH", "X..............."));
            G(bar, lanes.ToArray());
        }
        for (var bar = 40; bar <= 46; bar++) ChorusHT(bar, bar is 40 or 44);
        Fill(d, 43, FillKind.P4);
        G(45, ("SP", "..............x."));
        Fill(d, 47, FillKind.F16, flam: true);

        // ---- 48–50 Post-chorus stabs (fff, heavy accents)
        G(48, ("K", "X...XXX...X.X..."), ("S", "......X...X.X..."), ("CH", "X.....X...X.X..."));
        G(49, ("K", "x...xxx.....xx.."), ("S", "......x........."), ("CH", "X.....X........."));
        G(50, ("K", "x.x.........dddd"), ("CH", "X.X............."), ("S", "............rrrr"));

        // ---- 51–62 Verse 2: ride-bell groove with ghosts, then the blast
        const string v2K = "x.x...x.x.x...x.", v2S = "....x..g.g..x...", v2R = "b.x.X.x.X.x.X.x.";
        G(51, ("K", v2K), ("S", v2S), ("R", NoOne(v2R)), ("C", "X..............."));
        foreach (var bar in new[] { 52, 53, 54, 55, 56, 57 }) G(bar, ("K", v2K), ("S", v2S), ("R", v2R));
        Fill(d, 54, FillKind.P4);
        Fill(d, 58, FillKind.F16);
        const string blastK = "b.b.b.b.b.b.b.b.", blastS = ".b.b.b.b.b.b.b.b";
        G(59, ("K", blastK), ("S", blastS), ("CH", hatQ), ("C", "X..............."));
        for (var bar = 60; bar <= 61; bar++) G(bar, ("K", blastK), ("S", blastS), ("CH", hatQ));
        G(62, ("K", blastK[..12] + "...."), ("S", blastS[..12] + "...."), ("CH", "X...X...X......."),
              ("T1", "............r..."), ("T2", ".............r.."), ("T3", "..............r."), ("T4", "...............X"));

        // ---- 63–70 Pre-chorus 2
        Build1(63); Build1(64); Build2(65); Build2(66);
        for (var bar = 67; bar <= 69; bar++) G(bar, ("K", D16), ("S", Back2and4), ("C", "X...x...x...x..."));
        G(70, ("K", D16[..8] + "........"), ("S", "....x..........."), ("C", "X...x..........."));
        Fill(d, 70, FillKind.F6);

        // ---- 71–78 Chorus 2: half-time, then double bass
        for (var bar = 71; bar <= 74; bar++) ChorusHT(bar, bar == 71);
        Fill(d, 73, FillKind.P4);
        for (var bar = 75; bar <= 77; bar++) G(bar, ("K", D16), ("S", Back2and4), ("C", "X...2...x...2..."));   // ChorusDB
        G(78, ("K", "ddddddddddddX..."), ("S", Back2and4), ("C", "X...2...x...c..."));                         // the choke on beat 4

        // ---- 79–82 Interlude: Glass (swung jazz breath: ride, cross-stick, feathered kick, hat foot)
        const string swR = "X...X.x.X...X.x.", swSS = "....x.......x...", swH = "....p.......p...", swK = "p.......p.......", swS = "..........g.....";
        for (var bar = 79; bar <= 81; bar++) G(bar, ("R", swR), ("SS", swSS), ("H", swH), ("K", swK), ("S", swS));
        G(82, ("R", swR[..12] + "...."), ("SS", swSS), ("H", swH), ("K", swK), ("S", "..........g.rrrr"));
        ReRamp(d, 82, 12, 16, 80, 110);

        // ---- 83–90 Bridge: 7/8 (2+2+3), then the 7-over-4 polymeter, then the mid-bar drop into a beat of silence
        const string b7K = "xxx.xxx.xxxxx.", b7S = "........x.....", b7CH = "X...X...X.....";
        G(83, ("K", b7K), ("S", b7S), ("CH", b7CH), ("C", "X............."));
        for (var bar = 84; bar <= 85; bar++) G(bar, ("K", b7K), ("S", b7S), ("CH", b7CH));
        G(86, ("K", "xxx.xxx.x..x.."), ("S", "........x..x.."), ("CH", "X...X...X..X.."));
        for (var bar = 83; bar <= 86; bar++)                                                  // B27: the closing 3-group (slots 8–13) beamed as one
            BeamGroups(d, bar, BeamMode.Force, Beats(d.Measures[bar - 1].Cells).Select(b => b.Onset).Where(o => o > 8 && o < 14).Select(o => (int)o).ToArray());
        var polyStarts = new Dictionary<int, int[]> { [87] = new[] { 0, 7, 14 }, [88] = new[] { 5, 12 }, [89] = new[] { 3, 10 } };
        foreach (var (bar, starts) in polyStarts)
        {
            var k = D16.ToCharArray(); var ch = new string('.', 16).ToCharArray();
            foreach (var s in starts) { k[s] = 'X'; ch[s] = 'X'; }
            G(bar, ("K", new string(k)), ("CH", new string(ch)), ("S", BackOn3), ("R", "b...b...b...b..."));   // Poly7
            BeamGroups(d, bar, BeamMode.Break, starts.Where(s => s > 0).ToArray());                        // B27: beams follow the 7-cell
        }
        G(90, ("K", "dddddddd........"), ("CH", "X..............."), ("S", "........f......."), ("T4", "........X......."), ("C", "........c......."));

        // ---- 91–101 Breakdown: Meridian Falls (112, half-time stomp, china), Bounce, Wind-up
        G(91, ("K", "X.X..XX.X......X"), ("S", "........f......."), ("CH", "X...X.......X..."), ("C", "X..............."));   // Stomp
        G(92, ("K", "X.XX..X.X...X.X."), ("S", BackOn3), ("CH", chinaHalf));
        G(93, ("K", "X.X..XX.X......X"), ("S", BackOn3), ("CH", "X...X.......X..."));
        G(94, ("K", "X.X.X..........."), ("CH", "X..............."), ("C", "....X..........."));
        G(95, ("K", D16), ("S", BackOn3), ("CH", chinaHalf), ("C", "X..............."));                   // StompDB
        for (var bar = 96; bar <= 97; bar++) G(bar, ("K", D16), ("S", BackOn3), ("CH", chinaHalf));
        G(98, ("K", hatQ), ("S", hatQ), ("CH", hatQ));
        for (var bar = 99; bar <= 100; bar++) G(bar, ("K", "x..xx..xx..xx..x"), ("S", BackOn3), ("CH", hatQ));   // Bounce
        G(101, ("K", D16), ("S", "rrrrrrrrrrrr...."), ("T1", "............r..."), ("T2", ".............r.."),       // WindUp
               ("T3", "..............r."), ("T4", "...............X"));
        ReRamp(d, 101, 0, 16, 80, 127);

        // ---- 102–117 Solo
        const string s1K = "x..x..x.x.x...x.", s1S = "....x..g....x..g";
        G(102, ("K", s1K), ("S", s1S), ("R", NoOne(ride8)), ("C", "X..............."));                   // GrooveS1
        for (var bar = 103; bar <= 108; bar++) G(bar, ("K", s1K), ("S", s1S), ("R", ride8));
        Fill(d, 105, FillKind.P4);
        Fill(d, 109, FillKind.F16, flam: true);
        G(110, ("K", D16), ("S", Back2and4), ("R", NoOne(ride8)), ("C", "X..............."));               // GrooveS2
        for (var bar = 111; bar <= 115; bar++) G(bar, ("K", D16), ("S", Back2and4), ("R", ride8));
        Fill(d, 113, FillKind.P4);
        G(116, ("K", D16), ("S", Back2and4), ("C", "X...X...X...X..."));
        Fill(d, 117, FillKind.F16);                                                          // the D.S. lands on bar 36's crash

        // ---- 118 Coda: Lift (2/4), 119–134 Final Chorus
        G(118, ("K", "x.x....."), ("C", "X.X....."), ("S", "..f....."));
        for (var bar = 119; bar <= 126; bar++) ChorusHT(bar, bar == 119);
        Fill(d, 122, FillKind.P4);
        Fill(d, 126, FillKind.P8);
        for (var bar = 127; bar <= 133; bar++) G(bar, ("K", D16), ("S", Back2and4), ("C", "X.x.X.x.X.x.X.x."));   // crash 8ths
        Fill(d, 130, FillKind.P4);
        Fill(d, 134, FillKind.F16, flam: true);

        // ---- 135–140 Outro
        G(135, ("K", D16), ("S", BackOn3), ("CH", chinaHalf), ("C", "X..............."));                   // OutroDB
        for (var bar = 136; bar <= 138; bar++) G(bar, ("K", D16), ("S", BackOn3), ("CH", chinaHalf));
        Fill(d, 138, FillKind.P4);
        ChorusHT(139, false);
        Fill(d, 140, FillKind.FTrip8);

        // ---- 141–144 Fake-out
        G(141, ("K", "X..............."), ("S", "X..............."), ("CH", "X..............."), ("C", "c..............."));
        CellAt(d, 141, 0)!.Fermata = true;                                                    // B08
        var hold = Put(d.Measures[141], 0, 1);
        hold.IsRest = true; hold.Fermata = true; hold.Text = "hold for the feedback";           // B08, B17 (free-time bar)
        G(143, ("K", "xxxxxxxxX.X....."), ("S", "........X.X....."), ("CH", "X.......X.X....."));
        G(144, ("K", "X..............."), ("T4", "X..............."), ("C", "X..............."));
        foreach (var n in CellAt(d, 144, 0)!.Notes.Where(n => n.MidiValue == 49)) n.Techniques.Add("FadeOut");   // N25 ring-out

        // ---- the crash after every fill is fff; then the simile bars (S20) get an exact copy so a clean .gp still sounds right
        foreach (var bar in DrumFillBars.Select(b => b + 1).Where(b => b <= BarCount))
            foreach (var n in CellAt(d, bar, 0)?.Notes.Where(n => !n.IsGraceNote && n.MidiValue is 49 or 57) ?? Enumerable.Empty<TabNote>())
                n.Velocity = Hv(d.Name, bar, 0, n.MidiValue, Dyn.fff, 4, SectionOf(bar), Lane.Crash);
        foreach (var bar in new[] { 25, 29 })
            d.Measures[bar - 1].Cells = d.Measures[bar - 2].Cells.Select(c => c.Clone()).ToList();
    }

    /// <summary>The voice-1 beat cell starting at <paramref name="slot"/> of a drum bar (null when none starts there).</summary>
    private static TabCell? CellAt(TrackModel d, int bar, double slot)
    {
        var cells = d.Measures[bar - 1].Cells;
        foreach (var (index, onset) in Beats(cells))
            if (Math.Abs(onset - slot) < 1e-6) return cells[index];
        return null;
    }

    /// <summary>Re-ramps the snare and tom hits starting in [from, to) of a drum bar to v0 → v1 in time order (±3 jitter, via Hv).</summary>
    private static void ReRamp(TrackModel d, int bar, double from, double to, int v0, int v1)
    {
        var cells = d.Measures[bar - 1].Cells;
        var hits = Beats(cells).Where(b => b.Onset >= from - 1e-9 && b.Onset < to - 1e-9)
            .SelectMany(b => cells[b.Index].Notes.Where(n => !n.IsGraceNote && n.MidiValue is 38 or 50 or 48 or 45 or 43).Select(n => (b.Onset, Note: n)))
            .ToList();
        for (var k = 0; k < hits.Count; k++)
        {
            var centre = hits.Count == 1 ? v1 : (int)Math.Round(v0 + (v1 - v0) * (double)k / (hits.Count - 1));
            hits[k].Note.Velocity = Hv(d.Name, bar, hits[k].Onset, hits[k].Note.MidiValue, centre, 3, SectionOf(bar), Lane.FillRamp);
        }
    }

    /// <summary>Sets a beam override on the voice-1 beats that start at the given slots (B27).</summary>
    private static void BeamGroups(TrackModel d, int bar, BeamMode mode, params int[] slots)
    {
        foreach (var s in slots)
            if (CellAt(d, bar, s) is { } c) c.BeamMode = mode;
    }
}
