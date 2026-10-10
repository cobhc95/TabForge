using System.Linq;
using TabForge.Models;

namespace TabForge.Presets;

// Owns: the lead, lead harmony, clean, pad and piano parts of the demo song: their notes and beats, lyrics, chord names, octave shifts, voices and stem overrides.
// Does not own: the structure and mix-table points (FullDemoSongFactory.Skeleton.cs) or the rhythm section (FullDemoSongFactory.Rhythm.cs).
// Tests: TestFullDemoSong.

// Owner (c2): Lead, Lead Harmony, Clean, Pad and Piano (plan 3.5–3.8), with lyrics (B20), chord names (B18), octave shifts (B25),
// voices (B23) and stem overrides (B26). Structure and mix-table points belong to the skeleton; this file only writes notes and beats.
//
// Harmonic pitches (GuitarProImporter.HarmonicMidi): Natural sounds the node of the string; Artificial/Pinch sound fret + node read as an
// interval (12 = octave, 24/5 = two octaves); Tap sounds open string + node + 12; Feedback sounds fret + 12. The nodes below are chosen so
// the notes sound what the plan intends: the glass melody an octave up (ah=12), the tap harmonic G5 (th=3), the feedback B4 (open B, node 12).
internal static partial class FullDemoSongFactory
{
    /// <summary>The chorus hook (plan 3.5): Lead, lyrics chorus 1 / chorus 2 (one syllable per note), harmony in 3rds and in 6ths.</summary>
    internal static readonly (string Lead, string Ch1, string Ch2, string Thirds, string Sixths)[] Hook =
    {
        ("4.:s1:12 8:s1:10 4:s1:8 4:s2:10", "Hold the last light", "Sing through the smoke", "4.:s1:8 8:s2:12 4:s2:10 4:s2:6", "4.:s2:8 8:s2:6 4:s2:5 4:s3:5"),
        ("2:s2:8+tn 4:s1:12 4:s1:10", "as it fades", "till it clears", "2:s2:5+tn 4:s1:8 4:s2:12", "2:s3:4+tn 4:s2:8 4:s2:6"),
        ("4.:s1:10 8:s1:8 4:s2:12 4:s1:10", "we are the ash", "we are the spark", "4.:s2:12 8:s2:10 4:s2:8 4:s2:12", "4.:s2:6 8:s2:5 4:s2:3 4:s2:6"),
        ("2..:s1:8+vib 8:s2:12", "of the", "in the", "2..:s2:10+vib 8:s2:8", "2..:s2:5+vib 8:s2:3"),
        ("4.:s1:12 8:s1:10 4:s1:8 4:s1:17", "sun that burned us", "dark that re- mains", "4.:s1:8 8:s2:12 4:s2:10 4:s1:13", "4.:s2:8 8:s2:6 4:s2:5 4:s1:8"),
        ("2:s1:15+vib 4:s1:12 4:s1:8", "down to the", "when the world", "2:s1:12+vib 4:s1:8 4:s2:10", "2:s2:12+vib 4:s2:8 4:s2:5"),
        ("4.:s1:10 8:s1:12 4:s1:10 4:s2:12", "end of the long", "falls to the cold", "4.:s2:12 8:s1:8 4:s2:12 4:s2:8", "4.:s2:6 8:s2:8 4:s2:6 4:s2:3"),
        ("2:s2:12+vib 4:s2:9 4:s2:12+vib", "mer- i- dian", "mer- i- dian", "2:s2:9 4:s2:5 4:s2:9+vib", "2:s2:3 4:s3:4 4:s2:3+vib"),
    };

    private static void BuildLeadAndKeys(SongProject song)
    {
        var lead = Track(song, Lead);
        var harm = Track(song, LeadHarm);
        BuildLeadLines(lead, harm);
        BuildSolo(lead, harm);
        BuildClean(Track(song, Clean));
        BuildPad(Track(song, Pad));
        BuildPiano(Track(song, Piano));
    }

    // ------------------------------------------------------------------ small tools

    /// <summary>Beat cells of a bar and voice in time order (notes, rests and annotations).</summary>
    private static List<TabCell> BeatCells(TrackModel t, int bar, int voice = 0)
    {
        var m = t.Measures[bar - 1];
        var cells = voice == 0 ? m.Cells : m.Voice2Cells;
        return Beats(cells).Select(b => cells[b.Index]).ToList();
    }

    /// <summary>The cells with notes of a bar and voice in time order.</summary>
    private static List<TabCell> NoteCellsOf(TrackModel t, int bar, int voice = 0) =>
        BeatCells(t, bar, voice).Where(c => c.Notes.Any(n => !n.IsGraceNote)).ToList();

    /// <summary>The beat of a bar and voice that starts at <paramref name="slot"/>.</summary>
    private static TabCell BeatAt(TrackModel t, int bar, double slot, int voice = 0)
    {
        var m = t.Measures[bar - 1];
        var cells = voice == 0 ? m.Cells : m.Voice2Cells;
        foreach (var (index, onset) in Beats(cells))
            if (Math.Abs(onset - slot) < 1e-6) return cells[index];
        throw new InvalidOperationException($"Demo song: track '{t.Name}' bar {bar} voice {voice + 1}: no beat at slot {slot}.");
    }

    /// <summary>The first principal note of a cell.</summary>
    private static TabNote Main(TabCell c) => c.Notes.First(n => !n.IsGraceNote);

    /// <summary>One lyric syllable per note cell (voice 1). Throws when the syllables and notes do not match.</summary>
    private static void Lyrics(TrackModel t, int bar, string[] words)
    {
        var cells = NoteCellsOf(t, bar);
        if (words.Length != cells.Count)
            throw new InvalidOperationException($"Demo song: track '{t.Name}' bar {bar}: {words.Length} syllables for {cells.Count} notes.");
        for (var i = 0; i < cells.Count; i++) cells[i].Lyrics = words[i];
    }

    /// <summary>Voice 2 with exactly the bar's slots (CellsForVoice would create 16 cells, too many for a 3/4 bar).</summary>
    private static void PrepareVoice2(TrackModel t, int bar)
    {
        var m = t.Measures[bar - 1];
        if (m.Voice2Cells.Count == 0) m.Voice2Cells = Enumerable.Range(0, SlotsOf(m)).Select(_ => new TabCell()).ToList();
    }

    private static void Stems(TrackModel t, int bar, int voice, StemDirection dir)
    {
        foreach (var c in BeatCells(t, bar, voice)) c.StemDirection = dir;
    }

    private static void Octave(TrackModel t, int bar, int voice, int semitones)
    {
        foreach (var c in BeatCells(t, bar, voice)) c.OctaveShiftSemitones = semitones;
    }

    /// <summary>Pre-chorus dynamic (plan 2.3: mf → ff, one step every two bars).</summary>
    private static int PreVel(int bar)
    {
        var k = bar - (bar >= 63 ? 63 : 32);
        return k < 2 ? Dyn.mf : k < 4 ? Dyn.f : Dyn.ff;
    }

    // ------------------------------------------------------------------ Lead and Lead Harmony outside the solo (plan 3.5)

    private static void BuildLeadLines(TrackModel lead, TrackModel harm)
    {
        // Intro: Meridian, the motif announced (ff).
        Riff(lead, 15, "4.:s1:12 8:s1:10 4:s1:8 4:s1:12", Dyn.ff);
        Riff(lead, 16, "2:s1:10+vib 4:s1:8 4:s2:8", Dyn.ff);
        Riff(lead, 17, "4.:s1:10 8:s1:8 4:s2:12 4:s1:10", Dyn.ff);
        Riff(lead, 18, "2:s2:12+vib 2:s2:9+wv", Dyn.ff);

        // Chorus 1 (40-47): harmony in 3rds, lead f, lyrics of chorus 1.
        WriteChorus(lead, harm, 40, Dyn.f, sixths: false, transpose: 0, lyricLine: 1);
        BeatAt(lead, 41, 0).Tenuto = true;                                         // B12 (already in the hook, stated for the record)
        var c44 = BeatAt(lead, 44, 12);                                            // N33 GraceBefore + legato slide into A5
        Grace(c44, lead, 1, 15, before: true, "GraceBefore", 0.5, Dyn.f - 10);
        c44.Notes[0].Techniques.Add("LegatoSlide");
        c44.Notes[0].SlideTargetMidi = Main(c44).MidiValue;

        // Verse 2 answer phrase (f).
        Riff(lead, 51, "1:s2:13+fin+tn", Dyn.f);
        Riff(lead, 52, "2:s2:11+vib 2:s2:10+ls", Dyn.f);                           // A4 slides legato into bar 53's C5
        Riff(lead, 53, "2:s2:13 4:s2:11 4:s2:10", Dyn.f);
        Riff(lead, 54, "2.:s3:14+wv 4:r", Dyn.f);

        // Pre-chorus 2: the wah filter lead, then the tremolo build 8 → 16 → 32 → 64.
        Riff(lead, 63, "1:s1:12+wc", PreVel(63));
        Riff(lead, 64, "1:s1:13+wo", PreVel(64));
        Riff(lead, 65, "1:s1:15+wc", PreVel(65));
        Riff(lead, 66, "2:s1:12+wo 2:s1:16+wc", PreVel(66));
        Riff(lead, 67, "2:s1:12+trem8 2:s1:15+trem8", PreVel(67));
        Riff(lead, 68, "2:s1:13+trem16 2:s1:17+trem16", PreVel(68));
        Riff(lead, 69, "2:s1:15+trem32 4:s1:13+trem32 4:s1:12+trem32", PreVel(69));
        Riff(lead, 70, "2:s1:12+trem64 2:s1:16+trem64+vib", PreVel(70));

        // Chorus 2 (71-78): harmony in 6ths, ff, lyrics of chorus 2.
        WriteChorus(lead, harm, 71, Dyn.ff, sixths: true, transpose: 0, lyricLine: 2);
        Main(BeatAt(lead, 71, 0)).Techniques.Add("SlideInAbove");                  // N07
        var c75 = BeatAt(lead, 75, 12);                                            // N33 GraceBend: G5 bent a tone to A5
        Grace(c75, lead, 1, 15, before: true, "GraceBend", 0.5, Dyn.ff - 10);
        Bend(c75.Notes[0], "Bend", (0, 0), (30, 4), (60, 4));
        var c78 = BeatAt(lead, 78, 12);                                            // N12 dip on the last "-dian"
        Whammy(c78, Main(c78), "TremBarDip", (0, 0), (20, -4), (40, 0), (60, 0));

        // Breakdown: the gang vocal as an annotation-only beat (B32).
        var rise = Put(lead.Measures[90], 0, 1);
        rise.Lyrics = "RISE!";
        rise.Text = "gang vocal";

        // Final chorus (119-134): a whole step up (B minor), 6ths; 127-134 an octave up (8va).
        WriteChorus(lead, harm, 119, Dyn.ff, sixths: true, transpose: 2, lyricLine: 1);
        var c123 = BeatAt(lead, 123, 12);                                          // N33 GraceOnBeat: A5 → B5
        Grace(c123, lead, 1, 17, before: false, "GraceOnBeat", 0.5, Dyn.ff - 10);
        WriteChorus(lead, harm, 127, Dyn.ff, sixths: true, transpose: 2, lyricLine: 3);
        for (var bar = 127; bar <= 134; bar++) Octave(lead, bar, 0, 12);            // B25 8va (not folded into MidiValue, D-4)
        BeatAt(lead, 127, 0).Text = "octave pedal +1 oct";

        // Outro and fake-out (lead ff → f, harmony in 6ths below).
        Riff(lead, 135, "4.:s1:14 8:s1:12 4:s1:10 4:s1:14", Dyn.ff);
        Riff(lead, 136, "2:s1:12+vib 2:r", Dyn.ff);
        Riff(lead, 137, "4.:s1:14 8:s1:12 4:s1:10 4:s1:14", Dyn.ff);
        Riff(lead, 138, "2:s1:12 2:s1:10+wv", Dyn.f);
        Riff(lead, 139, "1:s1:14+tn", Dyn.f);
        Riff(lead, 140, "1:s1:19", Dyn.f);
        var c140 = BeatAt(lead, 140, 0);
        Whammy(c140, Main(c140), "TremBarWide", (0, 0), (10, -2), (20, 0), (30, -2), (40, 0), (50, -2), (60, 0));
        Riff(lead, 141, "4:s1:19>>+ferm 4:r 2:r", Dyn.fff);
        Riff(lead, 142, "1:s2:0+fb=12+fin", Dyn.f);                               // feedback on the open B, node 12: sounds B4
        BeatAt(lead, 142, 0).Text = "(feedback)";

        Riff(harm, 135, "4.:s1:5 8:s2:8 4:s2:7 4:s1:5", Dyn.ff);
        Riff(harm, 136, "2:s2:8 2:r", Dyn.ff);
        Riff(harm, 137, "4.:s1:5 8:s2:8 4:s2:7 4:s1:5", Dyn.ff);
        Riff(harm, 138, "2:s2:8 2:s2:7", Dyn.f);
        Riff(harm, 139, "1:s1:5", Dyn.f);
        Riff(harm, 140, "1:s1:10+vib", Dyn.f);
        Riff(harm, 141, "4:s1:10>>+ferm 4:r 2:r", Dyn.fff);
    }

    /// <summary>Eight chorus bars: the hook on the Lead with lyrics (line 1, 2, or 3 = both lines), the harmony in 3rds or 6ths.</summary>
    private static void WriteChorus(TrackModel lead, TrackModel harm, int first, int vel, bool sixths, int transpose, int lyricLine)
    {
        for (var k = 0; k < 8; k++)
        {
            var bar = first + k;
            var h = Hook[k];
            Riff(lead, bar, h.Lead, vel, transposeFrets: transpose);
            Riff(harm, bar, sixths ? h.Sixths : h.Thirds, vel, transposeFrets: transpose);
            var one = h.Ch1.Split(' ');
            var two = h.Ch2.Split(' ');
            Lyrics(lead, bar, lyricLine switch
            {
                1 => one,
                2 => two,
                _ => one.Select((w, i) => w + "\n" + two[i]).ToArray(),        // two lyric lines: chorus 1 over chorus 2
            });
        }
    }

    // ------------------------------------------------------------------ the solo, 102-117 (plan 3.5; SG 3.2 arc)

    private static void BuildSolo(TrackModel lead, TrackModel harm)
    {
        const int A = Dyn.f, B = Dyn.ff;   // first half f, second half (double time) ff

        // 102-105: call and response on motif M, bends that cry and answer.
        Riff(lead, 102, "4.:s1:12 8:s1:10 2:s1:8+vib", A);                                     // call: E5 D5 C5
        Riff(lead, 103, "2:s2:15 4:s2:13+wv 4:r", A);                                         // D5 → E5 → D5, then air
        Bend(Main(BeatAt(lead, 103, 0)), "BendRelease", (0, 0), (12, 4), (36, 4), (48, 0), (60, 0));
        Riff(lead, 104, "4.:s1:15 8:s1:13 2:s1:10", A);                                       // response a 3rd higher: E5 cries to D5
        Bend(Main(BeatAt(lead, 104, 8)), "PrebendRelease", (0, 4), (20, 4), (40, 0), (60, 0));
        Riff(lead, 105, "4:s1:10 4:s1:8 2:s2:10+wv", A);                                      // ends on the root
        Bend(Main(BeatAt(lead, 105, 0)), "Prebend", (0, 4), (60, 4));

        // 106-109: development: a triplet sequence, a legato slide into a pinch, the legato lift, harmonic-minor pull-offs.
        Riff(lead, 106, "8t:s1:12 s1:10 s1:8 s1:10 s1:8 s2:10 s1:8 s2:10 s2:8 8:s3:9+ss 8:s3:12", A);
        Riff(lead, 107, "8:s3:10+ls 4.:s3:12+ph=24+wv 4:r 16:s2:8 16:s2:10 16:s2:12 16:s1:8", A);
        Main(BeatAt(lead, 107, 0)).SlideTargetMidi = lead.PitchOf(2, 12);                     // the slide lands on the fretted G4, not the squeal
        Riff(lead, 108, "16(5:4):s3:7+h s3:9+h s3:10 s2:8+h s2:10 s2:12 s1:8+h s1:10+h s1:12+h s1:13 4:s1:15 4:s1:15+tie", A);
        Bend(Main(BeatAt(lead, 108, 8)), "Bend", (0, 0), (15, 4), (60, 4));
        Bend(Main(BeatAt(lead, 108, 12)), "Release", (0, 4), (30, 0), (60, 0));
        Riff(lead, 109, "4:s1:16 4:s1:12+vib 16(7:4):s1:10+h s1:8+h s1:7 s2:10+h s2:9+h s2:6+h s2:5 4:s2:9+vib", A);
        Bend(Main(BeatAt(lead, 109, 0)), "PrebendRelease", (0, 2), (24, 2), (48, 0), (60, 0));

        // 110-111: second wind: tapped arpeggios (F, then C) and a tap harmonic.
        const string tapF = "s1:13+tap+h s1:5+h s1:8";
        Riff(lead, 110, "16t:" + string.Join(' ', Enumerable.Repeat(tapF, 6)) + " s1:13+tap+h s1:5+h s1:8 s1:12+ltap+h s1:8+h s1:5", B);
        BeatAt(lead, 110, 0).Text = "tap";
        const string tapC = "s1:15+tap+h s1:8+h s1:12";
        Riff(lead, 111, "16t:" + string.Join(' ', Enumerable.Repeat(tapC, 4)) + " 2:s1:3+th=3", B);   // tap harmonic sounds G5

        // 112-113: the harmony joins, tremolo-picked, then trills.
        Riff(lead, 112, "2:s1:10+trem32 4:s1:12+trem32 4:s1:10+trem32", B);
        Riff(harm, 112, "2:s2:12+trem32 4:s1:8+trem32 4:s2:12+trem32", B);
        BeatAt(lead, 112, 0).Text = "harmony";
        Riff(lead, 113, "2:s1:8+trem32 2:s1:12", B);
        Riff(harm, 113, "2:s2:10+trem32 2:s1:8", B);
        Trill(Main(BeatAt(lead, 113, 8)), lead.PitchOf(0, 13));
        Trill(Main(BeatAt(harm, 113, 8)), harm.PitchOf(0, 10));

        // 114-115: sweeps (sextuplets, pick direction marked), a harmonic-minor 9-tuplet and a 13-tuplet run down.
        Riff(lead, 114, "16(6:4):s5:8+pd s4:7+pd s3:5+pd s2:6+pd s1:5+pd+h s1:8 s1:8+pu+h s1:5 s2:6+pu s3:5+pu s4:7+pu s5:8+pu "
                        + "32(9:8):s2:5 s2:6 s2:9 s2:10 s1:7 s1:8 s1:10 s1:12 s1:13 4:s1:17+wv", B);
        Riff(lead, 115, "16(6:4):s5:15+pd s4:14+pd s3:12+pd s2:13+pd s1:12+pd+h s1:15 s1:15+pu+h s1:12 s2:13+pu s3:12+pu s4:14+pu s5:15+pu "
                        + "32(13:8):s1:15 s1:13 s1:12 s1:10 s1:8 s2:12 s2:10 s2:8 s2:6 s2:5 s3:7 s3:5 s3:4 8:s2:8+sou 8:r", B);

        // 116: the peak, a unison bend held against the harmony.
        Riff(lead, 116, "2:[s1:19+wv s2:22] 2:[s1:19+tie+wv s2:22+tie]", B);
        Bend(BeatAt(lead, 116, 0).Notes.First(n => n.StringIndex == 1), "Custom", (0, 0), (10, 4), (25, 4), (35, 3), (45, 4), (60, 4));
        Bend(BeatAt(lead, 116, 8).Notes.First(n => n.StringIndex == 1), "Hold", (0, 4), (60, 4));
        Riff(harm, 116, "1:s1:15+wv", B);

        // 117: a gargle, then a pinch squeal (E5) dived into the D.S.
        Riff(lead, 117, "4:s2:9 2.:s4:2+ph=24", B);
        Bend(Main(BeatAt(lead, 117, 0)), "PrebendBend", (0, 2), (15, 2), (30, 6), (60, 6));
        var dive = BeatAt(lead, 117, 4);
        Whammy(dive, Main(dive), "TremBarCustom", (0, 0), (8, 4), (16, -4), (24, 4), (36, -12), (60, -24));
        dive.Text = "dive!";
    }

    private static void Trill(TabNote n, int target)
    {
        n.Techniques.Add("Trill");
        n.TrillTargetMidi = target;
        n.TrillDurationDenominator = 16;
    }

    // ------------------------------------------------------------------ Clean guitar, capo 5 (plan 3.6)

    private static readonly Dictionary<string, string> CleanShapesA = new()
    {
        ["F"] = "[s5:3 s4:2 s3:0 s2:1 s1:0]", ["C"] = "[s6:3 s5:2 s4:0 s3:0 s2:3 s1:3]", ["G"] = "[s4:0 s3:2 s2:3 s1:2]",
        ["Am"] = "[s6:0 s5:2 s4:2 s3:0 s2:0 s1:0]", ["E7"] = "[s5:2 s4:1 s3:2 s2:0 s1:2]",
    };

    private static readonly Dictionary<string, string> CleanShapesB = new()
    {
        ["G"] = "[s4:0 s3:2 s2:3 s1:2]", ["D"] = "[s5:0 s4:2 s3:2 s2:2 s1:0]", ["A"] = "[s6:0 s5:2 s4:2 s3:1 s2:0 s1:0]",
        ["Bm"] = "[s6:2 s5:4 s4:4 s3:2 s2:2 s1:2]", ["F#"] = "[s5:4 s4:6 s3:6 s2:6 s1:4]",
    };

    private static readonly string[] ChorusChordsA = { "F", "C", "G", "Am", "F", "C", "G", "E7" };
    private static readonly string[] ChorusChordsB = { "G", "D", "A", "Bm", "G", "D", "A", "F#" };

    private static string LetRing(string token) => token.EndsWith(":r", StringComparison.Ordinal) ? token : token + "+lr";

    private static void BuildClean(TrackModel clean)
    {
        // Intro: Embers. Voice 1 = the arpeggio (stems up, let ring), voice 2 = the bass (stems down; held by its own length, so no
        // second "let ring" line is printed over the first).
        Riff(clean, 1, "8:s2:0 8:s1:0", Dyn.p);
        const string am = "8:s4:2 8:s3:0 8:s2:0 8:s1:2 8:s2:0 8:s3:0 8:s1:2 8:s2:0";
        const string fm = "8:s3:0 8:s2:0 8:s1:0 8:s2:0 8:s3:0 8:s2:0 8:s1:0 8:s2:0";
        for (var bar = 2; bar <= 10; bar++) PrepareVoice2(clean, bar);
        foreach (var (bar, vel) in new[] { (2, Dyn.p), (6, Dyn.mf) })
        {
            Riff(clean, bar, am, vel, swap: LetRing);
            Riff(clean, bar, "2.:s6:0 4:s5:2", vel, voice: 1);
            Riff(clean, bar + 1, fm, vel, swap: LetRing);
            Riff(clean, bar + 1, "2.:s5:3 4:s4:2", vel, voice: 1);
            EchoGrid(clean, bar + 2, bar == 2 ? Dyn.mp : Dyn.mf);
            Riff(clean, bar + 2, "2.:s6:3 4:s5:2", bar == 2 ? Dyn.mp : Dyn.mf, voice: 1);
        }
        Riff(clean, 5, "4:s3:12+nh 4:s2:12+nh 4:s1:12+nh 8:s4:7+nh 8:s3:7+nh", Dyn.mp, swap: LetRing);   // Am11 in harmonics (nodes 12 and 7)
        Riff(clean, 5, "1:s6:0", Dyn.mp, voice: 1);
        // Bar 9: the E7 roll without the s5 note, which the bass voice holds (two voices on one string would collide).
        Riff(clean, 9, "2:[s4:1 s3:2 s2:0 s1:2]+au 4:s2:12+nh 4:s2:5+nh", Dyn.mf, swap: LetRing);         // E5 and E6 (node 5)
        Riff(clean, 9, "1:s5:2", Dyn.mf, voice: 1);
        Riff(clean, 10, "2.:r", Dyn.mf);
        Riff(clean, 10, "2.:s5:2+tie", Dyn.mf, voice: 1);                                         // tied across the barline
        for (var bar = 2; bar <= 10; bar++) { Stems(clean, bar, 0, StemDirection.Up); Stems(clean, bar, 1, StemDirection.Down); }
        BeatAt(clean, 2, 0).Text = "let ring, fingerstyle";
        string[] names = { "Am(add9)", "Fmaj7", "C(add9)", "Am11", "Am(add9)", "Fmaj7", "C(add9)", "E7", "E7" };
        for (var bar = 2; bar <= 10; bar++) BeatCells(clean, bar)[0].ChordName = names[bar - 2];

        // N35 fingering, bars 2-3: p on the bass voice, i on s4/s3, m on s2, a on s1; left hand per the Em and Cmaj7 shapes.
        foreach (var bar in new[] { 2, 3 })
        {
            foreach (var n in BeatCells(clean, bar).SelectMany(c => c.Notes))
                n.RightHandFinger = n.StringIndex switch { 0 => 3, 1 => 2, _ => 1 };
            foreach (var n in BeatCells(clean, bar, 1).SelectMany(c => c.Notes)) n.RightHandFinger = 0;
        }
        foreach (var n in BeatCells(clean, 2).Concat(BeatCells(clean, 2, 1)).SelectMany(c => c.Notes))
            n.LeftHandFinger = (n.StringIndex, n.Fret) switch { (4, 2) => 1, (3, 2) => 2, (0, 2) => 4, _ => null };
        foreach (var n in BeatCells(clean, 3, 1).SelectMany(c => c.Notes))
            n.LeftHandFinger = (n.StringIndex, n.Fret) switch { (4, 3) => 3, (3, 2) => 2, _ => null };

        // Choruses: the half-time strum (brush 0.33 slot per string), chord names.
        foreach (var (first, vel) in new[] { (40, Dyn.ff), (71, Dyn.ff) })
            for (var k = 0; k < 8; k++) Strum(clean, first + k, CleanShapesA[ChorusChordsA[k]], ChorusChordsA[k], vel, 0.33);
        for (var k = 0; k < 16; k++) Strum(clean, 119 + k, CleanShapesB[ChorusChordsB[k % 8]], ChorusChordsB[k % 8], Dyn.ff, 0.5);

        // Interlude: Glass (swung): the motif in artificial harmonics an octave up, then a rasgueado and a rolled E7(b9).
        Riff(clean, 79, "4.:s2:5+ah=12 8:s2:3+ah=12 4:s2:1+ah=12 4:s2:5+ah=12", Dyn.mp);          // A5 G5 F5 A5
        Riff(clean, 80, "2:s2:3+ah=12 4:s2:0+ah=12 4:s2:3+ah=12", Dyn.mp);                        // G5 E5 G5
        Riff(clean, 81, "4.:s1:3+ah=12 8:s1:1+ah=12 2:s1:0+ah=12", Dyn.mp);                       // C6 Bb5 A5
        Riff(clean, 82, "4:[s5:2 s4:1 s3:2 s2:0 s1:2]+rasg+lr 4:r 2:[s5:2 s4:1 s3:2 s2:0 s1:2]+ad+lr", Dyn.mp);
        string[] glass = { "Dm7", "Em7", "Fmaj7", "E7(b9)" };
        for (var k = 0; k < 4; k++) BeatCells(clean, 79 + k)[0].ChordName = glass[k];
    }

    /// <summary>The echo grid (plan 3.6 bar 4): the C(add9) arpeggio on even 16ths, a ghosted "delay" of the note three slots earlier on odd ones.</summary>
    private static void EchoGrid(TrackModel clean, int bar, int vel)
    {
        string[] main = { "s4:0", "s3:2", "s2:3", "s1:3", "s2:3", "s3:2", "s1:3", "s2:3" };
        var slot = new string[16];
        for (var i = 0; i < 8; i++) slot[2 * i] = main[i];
        for (var k = 1; k < 16; k += 2) slot[k] = k == 1 ? "s2:0" : slot[k - 3];               // slot 1 echoes the previous bar's last note
        Riff(clean, bar, string.Join(' ', slot.Select((s, k) => $"16:{s}{(k % 2 == 1 ? "+g" : "")}+lr")), vel);
        foreach (var n in BeatCells(clean, bar).SelectMany(c => c.Notes).Where(n => n.Ghost)) n.Velocity = Dyn.p;
    }

    private static void Strum(TrackModel clean, int bar, string shape, string name, int vel, double brush)
    {
        Riff(clean, bar, $"4:{shape}+bd 8:{shape}+bd 8:{shape}+bu 4:r 8:{shape}+bu 8:{shape}+bd", vel, swap: LetRing);
        foreach (var c in NoteCellsOf(clean, bar)) c.BrushStepSlots = brush;
        BeatCells(clean, bar)[0].ChordName = name;
    }

    // ------------------------------------------------------------------ Pad / Strings (plan 3.7)

    private static void BuildPad(TrackModel pad)
    {
        void Whole(int bar, string chord, int vel, string flags = "") => Riff(pad, bar, $"1:[{chord}]+tn{flags}", vel);

        string[] embers = { "A3 C4 E4 B4", "F3 A3 C4 E4", "C4 E4 G4 D5", "A3 D4 G4 C5" };
        for (var k = 0; k < 4; k++) Whole(2 + k, embers[k], Dyn.ppp, k == 0 ? "+fin" : "");
        for (var k = 0; k < 3; k++) Whole(6 + k, embers[k], Dyn.pp);
        Whole(9, "E3 G#3 B3 D4", Dyn.pp);
        Riff(pad, 10, "2.:[E3 G#3 B3 D4]+tn+fin", Dyn.mf);

        string[] intro = { "F3 A3 C4", "C4 E4 G4", "G3 B3 D4", "E3 G#3 B3" };
        for (var k = 0; k < 4; k++) Whole(15 + k, intro[k], Dyn.ff);

        string[] pre = { "A3 C4 E4", "F3 A3 C4", "G3 B3 D4", "E3 G#3 B3" };
        foreach (var first in new[] { 32, 63 })
            for (var k = 0; k < 8; k++) Whole(first + k, pre[k % 4], PreVel(first + k));

        string[] chorus = { "F4 A4 C5", "C4 E4 G4", "G4 B4 D5", "A4 C5 E5", "F4 A4 C5", "C4 E4 G4", "G4 B4 D5", "E4 G#4 B4" };
        foreach (var first in new[] { 40, 71 })
            for (var k = 0; k < 8; k++) Whole(first + k, chorus[k], Dyn.ff);

        string[] glass = { "D4 F4 A4 C5", "E4 G4 B4 D5", "F4 A4 C5 E5", "E4 G#4 B4 D5" };
        for (var k = 0; k < 4; k++) Whole(79 + k, glass[k], Dyn.pp);

        for (var k = 0; k < 8; k++) Whole(110 + k, chorus[k], Dyn.mp, k == 0 ? "+fin" : "");

        // Final chorus: the program change to Strings (mix at 119, skeleton).
        string[] strings = { "G3 B3 D4 G4", "D4 F#4 A4 D5", "A3 C#4 E4 A4", "B3 D4 F#4 B4", "G3 B3 D4 G4", "D4 F#4 A4 D5", "A3 C#4 E4 A4", "F#3 A#3 C#4 F#4" };
        for (var k = 0; k < 16; k++) Whole(119 + k, strings[k % 8], k < 8 ? Dyn.f : Dyn.ff);

        // Outro: the viola-register line (alto clef, skeleton).
        string[] viola = { "2:B3 2:A3", "2:G3 2:F#3", "2:B3 2:D4", "2:C#4 2:A3", "1:B3", "1:A#3" };
        for (var k = 0; k < 6; k++) Riff(pad, 135 + k, viola[k], k < 3 ? Dyn.ff : Dyn.f, swap: t => t + "+tn");
        Riff(pad, 141, "4:[B3 D4 F#4]>>+ferm 4:r 2:r", Dyn.fff);
        Riff(pad, 142, "1:r", Dyn.pp);
        BeatAt(pad, 142, 0).Fermata = true;                                                         // a rest with a fermata (the riff flags skip rests)
        Riff(pad, 144, "1:[B2 F#3 B3 D4]+fout", Dyn.ff);
    }

    // ------------------------------------------------------------------ Piano (plan 3.8): voice 1 = right hand, voice 2 = left hand

    private static void BuildPiano(TrackModel piano)
    {
        // Intro: Embers, the motif an octave up (8va) over whole-note roots.
        string[] rh = { "4.:E5 8:D5 4:C5 4:E5", "2:D5+tn 2:r", "4.:E5 8:D5 4:C5 4:G5", "2:B4 2:G#4" };
        string[] lh = { "1:A2", "1:F2", "1:C3", "1:E2" };
        for (var k = 0; k < 4; k++)
        {
            var bar = 6 + k;
            PrepareVoice2(piano, bar);
            Riff(piano, bar, rh[k], Dyn.mp);
            Riff(piano, bar, lh[k], Dyn.mp, voice: 1);
            Octave(piano, bar, 0, 12);
        }

        // Interlude: off-beat staccato stabs (swung) over whole-note roots.
        string[] stabs = { "[F4 A4 C5 E5]", "[G4 B4 D5 E5]", "[A4 C5 E5 F5]" };
        string[] roots = { "1:D3", "1:E3", "1:F3" };
        for (var k = 0; k < 3; k++)
        {
            var bar = 79 + k;
            PrepareVoice2(piano, bar);
            Riff(piano, bar, $"4:r 8:r 8:{stabs[k]}+st 4:r 8:r 8:{stabs[k]}+st", Dyn.mp);
            Riff(piano, bar, roots[k], Dyn.mp, voice: 1);
        }
        NoteCellsOf(piano, 81)[1].StemDirection = StemDirection.Invert;                            // B26
        PrepareVoice2(piano, 82);
        Riff(piano, 82, "1:[G#4 B4 D5 F5]+ad", Dyn.mp);   // rolled low to high (a down-stroke in Guitar Pro terms)
        Riff(piano, 82, "1:E2", Dyn.mp, voice: 1);

        // Breakdown: the A0 "event" hits, 15mb, bass clef (skeleton), reverb (mix).
        Riff(piano, 91, "1:A2>>", Dyn.fff);
        Riff(piano, 95, "1:A2>>", Dyn.ff);
        Octave(piano, 91, 0, -24);
        Octave(piano, 95, 0, -24);

        // Final chorus: whole-note triads over roots written an octave up (8vb), in halves.
        string[] triads = { "[G4 B4 D5]", "[F#4 A4 D5]", "[E4 A4 C#5]", "[F#4 B4 D5]", "[G4 B4 D5]", "[F#4 A4 D5]", "[E4 A4 C#5]", "[F#4 A#4 C#5]" };
        string[] bass = { "G3", "D3", "A3", "B3", "G3", "D3", "A3", "F#3" };
        for (var k = 0; k < 16; k++)
        {
            var bar = 119 + k;
            PrepareVoice2(piano, bar);
            Riff(piano, bar, $"1:{triads[k % 8]}+tn", Dyn.mf);
            Riff(piano, bar, $"2:{bass[k % 8]} 2:{bass[k % 8]}", Dyn.mf, voice: 1);
            Octave(piano, bar, 1, -12);
        }

        // Outro: the music box, two octaves up (15ma), mf with the piano fader up for these bars (Mix table, plan 5.1): at pp the two-octave-up piano was inaudible against the mix (audio audit A7-A04, -40 dB).
        for (var bar = 135; bar <= 138; bar++)
        {
            Riff(piano, bar, "8:B4 8:D5 8:F#5 8:D5 8:B4 8:D5 8:F#5 8:D5", Dyn.mf);
            Octave(piano, bar, 0, 24);
        }

        // Fake-out: the unison hit and the last chord.
        PrepareVoice2(piano, 141);
        // The piano has only 4 usable rows (octave strings 60/48/36/24) below a B4 chord, so both hands at once can hold at most 4 notes
        // without a voice-1 and a voice-2 number overprinting on one row. Octave doublings are dropped to fit: B3 (under the B4) and B2 (over the B1).
        Riff(piano, 141, "4:[D4 F#4 B4]>>+ferm 4:r 2:r", Dyn.fff);
        Riff(piano, 141, "4:B1+ferm 4:r 2:r", Dyn.fff, voice: 1);
        PrepareVoice2(piano, 144);
        Riff(piano, 144, "1:[B3 D4 F#4]+fout", Dyn.ff);
        Riff(piano, 144, "1:B1+fout", Dyn.ff, voice: 1);   // the B2 octave doubling is dropped: 3 + 2 notes will not fit the 4 rows without an overprint
    }
}
