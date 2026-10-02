using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// The built-in full demo song "Ashen Meridian" (docs/DEMO_SONG_PLAN.md 6.4): the skeleton's acceptance (validation, round trips,
// performed order, timeline length, slot sums) and the coverage walk. Tolerant of parts that are not written yet: it asserts the
// structure, tracks and mix table, and logs how many notes each track has.
public static partial class SelfTest
{
    private static void TestFullDemoSong()
    {
        SongProject song;
        try { song = FullDemoSongFactory.Create(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            Check("full demo song: builds and passes ProjectValidator", false, ex.Message);
            return;
        }
        Check("full demo song: builds and passes ProjectValidator", true);
        var notes = song.Tracks.Select(t => $"{t.Name} {t.Measures.Sum(m => m.Cells.Concat(m.Voice2Cells).Sum(c => c.Notes.Count))}");
        Log.Add($"  info  full demo notes per track: {string.Join(", ", notes)}");
        foreach (var moved in FullDemoSongFactory.MixPlacementNotes) Log.Add($"  info  full demo mix moved: {moved}");

        // ---- skeleton acceptance (plan 6.4)
        var names = new[]
        {
            FullDemoSongFactory.RhyL, FullDemoSongFactory.RhyR, FullDemoSongFactory.Lead, FullDemoSongFactory.LeadHarm, FullDemoSongFactory.Clean,
            FullDemoSongFactory.Bass, FullDemoSongFactory.Sub, FullDemoSongFactory.Drums, FullDemoSongFactory.Pad, FullDemoSongFactory.Piano,
        };
        Check("full demo song: the 10 tracks in plan order", song.Tracks.Select(t => t.Name).SequenceEqual(names), string.Join(" | ", song.Tracks.Select(t => t.Name)));
        Check("full demo song: 144 bars on every track", song.Tracks.All(t => t.Measures.Count == 144), string.Join("/", song.Tracks.Select(t => t.Measures.Count)));
        var structureDiffers = FdStructureMismatches(song);
        Check("full demo song: bar structure is identical on every track", structureDiffers.Count == 0, string.Join("; ", structureDiffers.Take(5)));
        var problems = MusicTime.FindBarProblems(song);
        Check("full demo song: every bar fills its time signature (no partial or overfull bar)", problems.Count == 0,
            string.Join(", ", problems.Take(8).Select(p => $"bar {p.BarIndex + 1}")));

        var order = PlaybackOrder.Build(song, new PlaybackOptions()).Select(b => b + 1).ToList();
        var expectedOrder = FullDemoSongFactory.ExpectedPerformedBars;
        var firstDiff = Enumerable.Range(0, Math.Min(order.Count, expectedOrder.Count)).FirstOrDefault(i => order[i] != expectedOrder[i], -1);
        Check("full demo song: performed order is plan 2.2 (154 bars: repeats x2, \"1.2.\" x3, D.S. al Coda to 36, coda at 118, Fine at 144)",
            order.SequenceEqual(expectedOrder), $"{order.Count} bars; first difference at performed bar {firstDiff + 1}: {string.Join(" ", order.Skip(Math.Max(0, firstDiff - 2)).Take(8))}");
        Eq("full demo song: 154 performed bars", 154, order.Count);

        var tl = MidiTimelineBuilder.Build(song, new PlaybackOptions());
        var seconds = tl.TotalMs / 1000.0;
        // Plan 2.2: about 250 s; the fermatas on 141/142 now hold (+100 %), which adds about 2.3 s once those beats are written.
        Check("full demo song: timeline length 245..258 s (plan 2.2, plus the fermata holds)", seconds is >= 245 and <= 258, $"{seconds:0.0} s");
        Check("full demo song: the timeline plays the bars in the performed order", tl.Bars.Select(b => b.Bar + 1).SequenceEqual(order));
        var pianoOutro = song.Tracks.First(t => t.Name == FullDemoSongFactory.Piano).Measures[134];
        Check("full demo song: the Outro music box (Piano, bar 135) plays at mf with the piano fader lifted (A7-A04: at pp it was 40 dB under the mix)",
            pianoOutro.Cells.SelectMany(c => c.Notes).All(n => n.Velocity == Dynamics.VelocityFor("mf")) && pianoOutro.Cells.Any(c => c.Mix?.Volume == 16));
        Log.Add($"  info  full demo timeline:{seconds:0.0} s, {tl.Notes.Count} notes, {order.Count} performed bars");

        // ---- round trips: .tforge and embedded .gp with an empty allow-list; clean .gp logged only (plan 6.4)
        var folder = RtFolder();
        try
        {
            var expected = RtCopy(song);
            var tforge = RtViaTforge(song, folder, "fulldemo");
            RtVerify("full demo song", RtTforge, expected, tforge);
            RtVerify("full demo song", RtGpEmbedded, expected, RtViaGp(song, folder, "fulldemo", embed: true));
            var gpPath = Path.Combine(folder, "fulldemo-command.gp");
            GuitarProExporter.Save(song, gpPath, embedProject: true);
            var back = GuitarProExporter.TryReadEmbedded(gpPath);
            Check("full demo song: the embedded project reads back with the same content hash (--write-demo-song check)",
                back is not null && ProjectService.ContentHash(back).AsSpan().SequenceEqual(ProjectService.ContentHash(song)));
            Check("full demo song: performed order survives the .tforge round trip", PlaybackOrder.Build(tforge, new PlaybackOptions()).Select(b => b + 1).SequenceEqual(order));
            var clean = RtViaGp(song, folder, "fulldemo", embed: false);
            var cleanDiffs = RtCompare(RtScoreFacts(RtBakedTranspose(expected), out _, out _), RtScoreFacts(clean, out _, out _));
            // A7-E01: Sub Drop plays through track transpose -12; Guitar Pro has none, so the clean file holds the sounding pitches.
            static List<int> Sounding(SongProject p, string name) { var t = p.Tracks.First(x => x.Name == name); var semis = MixerGroups.Transpose(p, t);
                return t.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => n.MidiValue + semis).ToList(); }
            var subBefore = Sounding(song, FullDemoSongFactory.Sub); var subAfter = Sounding(clean, FullDemoSongFactory.Sub);
            // Review of A7-E01: gpif <Transpose> is display-only, so the transpose goes into the tuning; the frets stay as written.
            var subSong = song.Tracks.First(x => x.Name == FullDemoSongFactory.Sub); var subClean = clean.Tracks.First(x => x.Name == FullDemoSongFactory.Sub);
            static List<(int, int)> Frets(TrackModel t) => t.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => (n.StringIndex, n.Fret)).ToList();
            Check("full demo song: clean .gp writes the Sub Drop's transpose into its tuning (every string and fret as written)",
                subClean.StringTunings.SequenceEqual(subSong.StringTunings.Select(v => v + MixerGroups.Transpose(song, subSong))) && Frets(subSong).SequenceEqual(Frets(subClean)),
                $"tuning {string.Join(",", subSong.StringTunings)} -> {string.Join(",", subClean.StringTunings)}");
            Check("full demo song: clean .gp export leaves the song itself unchanged (Sub Drop transpose and tuning)",
                subSong.Transpose == -12 && subSong.StringTunings.SequenceEqual(new[] { 50, 45, 40, 33 }));
            Check("full demo song: clean .gp keeps the Sub Drop's sounding pitches (track transpose written into the tuning, dive-bomb bars included)",
                subBefore.Count > 0 && subBefore.SequenceEqual(subAfter) && subBefore.Min() < 40, $"{subBefore.Count} -> {subAfter.Count}; lowest {subBefore.DefaultIfEmpty().Min()} -> {subAfter.DefaultIfEmpty().Min()}");
            Log.Add($"  info  full demo clean .gp (logged, not asserted): {cleanDiffs.Count} differences: " +
                    string.Join(", ", cleanDiffs.GroupBy(d => d.Category).OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key} x{g.Count()}")));
        }
        finally { RtCleanup(folder); }

        FdCoverageSkeleton(song);
        FdCoverageParts(song);
        FdHelperSmoke();
        FdKeysPlacement(song);
    }

    /// <summary>Pad and Piano: chord notes sit on distinct strings (per beat onset, both voices), every note keeps its pitch (tuning + fret), the pitch sequence matches the pre-spread song.</summary>
    private static void FdKeysPlacement(SongProject song)
    {
        foreach (var t in song.Tracks.Where(x => x.Name is FullDemoSongFactory.Pad or FullDemoSongFactory.Piano))
        {
            var clashes = new List<string>(); var badPitch = 0; uint hash = 2166136261u; var chords = 0; var crossVoice = 0;
            for (var b = 0; b < t.Measures.Count; b++)
            {
                var m = t.Measures[b];
                var rows = new Dictionary<(int Voice, double Onset), List<TabNote>>();
                var both = new Dictionary<double, List<TabNote>>();
                var voice = 0;
                foreach (var cells in new[] { m.Cells, m.Voice2Cells })
                {
                    foreach (var (i, beatOnset) in FullDemoSongFactory.Beats(cells))
                    {
                        var onset = Math.Round(beatOnset, 6);
                        foreach (var n in cells[i].Notes.Where(x => !x.IsGraceNote))
                        {
                            if (t.StringTunings[n.StringIndex] + n.Fret != n.MidiValue) badPitch++;
                            hash = (hash ^ (uint)n.MidiValue) * 16777619u;
                            if (!rows.TryGetValue((voice, onset), out var list)) rows[(voice, onset)] = list = new List<TabNote>();
                            list.Add(n);
                            if (!both.TryGetValue(onset, out var all)) both[onset] = all = new List<TabNote>();
                            all.Add(n);
                        }
                    }
                    voice++;
                }
                foreach (var ((v, onset), list) in rows)
                {
                    if (list.Count > 1) chords++;
                    if (list.Select(n => n.StringIndex).Distinct().Count() != list.Count) clashes.Add($"bar {b + 1} voice {v + 1} slot {onset:0.##}");
                }
                crossVoice += both.Values.Count(l => l.Select(n => n.StringIndex).Distinct().Count() != l.Count);
            }
            Log.Add($"  info  full demo keys {t.Name}: {chords} chords, {crossVoice} beats where the two hands still share a string (the 7 octave strings have no room)");
            Check($"full demo keys: {t.Name}: no two notes of one beat share a string (StringIndex) and every note keeps tuning + fret = MidiValue",
                clashes.Count == 0 && badPitch == 0 && chords > 0, $"{chords} chords, {badPitch} wrong pitches; clashes: {string.Join("; ", clashes.Take(5))}");
            Log.Add($"  info  full demo keys pitch hash {t.Name}: {hash:X8}");
            Eq($"full demo keys: {t.Name} MIDI pitch sequence unchanged by the chord placement", KeysPitchHash[t.Name], hash);
        }
    }

    /// <summary>FNV-1a of every non-grace MidiValue in cell order (voice 1 then 2, bar by bar), taken when the chord placement was added (the MIDI export's pitch counts were checked identical before and after).</summary>
    private static readonly Dictionary<string, uint> KeysPitchHash = new() { [FullDemoSongFactory.Pad] = 0x1112D116u, [FullDemoSongFactory.Piano] = 0x1E8E5FE4u };   // Piano: re-taken when bars 141/144 dropped octave doublings (B3, B2 / B2) to clear the two-hand row collisions

    /// <summary>The shared helpers on a scratch song: riff grammar (durations, tuplets, chords, flags, slot-sum assertion), keys, drum grids, fills and rolls.</summary>
    private static void FdHelperSmoke()
    {
        var gtr = new TrackModel { Name = "g7", StringTunings = new() { 64, 59, 55, 50, 45, 40, 33 }, Measures = TemplateFactory.Measures(8) };
        var lead = new TrackModel { Name = "g6", Measures = TemplateFactory.Measures(8) };
        var keys = new TrackModel { Name = "k", Kind = TrackKind.Keys, StringTunings = new() { 96, 84, 72, 60, 48, 36, 24 }, Measures = TemplateFactory.Measures(8) };
        var drums = new TrackModel { Name = "d", Kind = TrackKind.Drums, MidiChannel = 9, StringTunings = new() { 49, 42, 48, 38, 43, 36 }, Measures = TemplateFactory.Measures(8) };
        var p = new SongProject { Tracks = { gtr, lead, keys, drums } };
        foreach (var t in p.Tracks) { t.Measures[7].TimeSigNum = 7; t.Measures[7].TimeSigDenom = 8; FullDemoSongFactory.EnsureSlots(t.Measures[7]); }
        bool Complete(int bar) => MusicTime.AnalyzeBar(p, bar - 1) is { Complete: true, Error: false };
        List<TabCell> BeatsOf(TrackModel t, int bar) => t.Measures[bar - 1].Cells.Where(c => c.Notes.Count > 0 || c.IsRest).ToList();

        FullDemoSongFactory.Riff(gtr, 1, "16:O> 16:O 8:s4:2> 16:O 16:O 8:s4:0 16:O 16:O 8:s5:3 8:O 8:s4:2", 95);   // RiffM1
        var b1 = BeatsOf(gtr, 1);
        Check("full demo helpers: RiffM1 is 11 beats, fills the bar, accent + palm mute + chug jitter within ±3",
            b1.Count == 11 && Complete(1) && b1[0].Accent == 1 && TechniqueNames.HasPalmMute(b1[0].Notes[0].Techniques) && b1[0].Notes[0].MidiValue == 33
            && b1.SelectMany(c => c.Notes).All(n => n.Velocity is >= 92 and <= 98) && b1[2].Notes[0].MidiValue == 52);
        FullDemoSongFactory.Riff(gtr, 2, "16:O> O P1 O O P3 X O> O P1 O O P3 X O> O", 112);                          // C7 stream, durations carried
        Check("full demo helpers: tokens without DUR reuse the previous duration; P1 is a 3-note chord; X is dead",
            BeatsOf(gtr, 2).Count == 16 && Complete(2) && BeatsOf(gtr, 2)[2].Notes.Count == 3 && BeatsOf(gtr, 2)[6].Notes[0].Dead);
        FullDemoSongFactory.Riff(lead, 1, "16(5:4):s3:7+h 16(5:4):s3:9+h 16(5:4):s3:10 16(5:4):s2:8+h 16(5:4):s2:10 4:s1:15+ph 2:[s1:19+wv s2:22]", 95);
        var l1 = BeatsOf(lead, 1);
        Check("full demo helpers: 5:4 tuplets get fractional positions, pinch harmonic via HarmonicMidi, chord with per-note flags",
            l1.Count == 7 && Complete(1) && Math.Abs((l1[1].RhythmicPosition ?? -1) - 0.8) < 1e-6 && l1[0].TupletNumerator == 5
            && l1[5].Notes[0].Techniques.Contains("PinchHarmonic") && l1[5].Notes[0].HarmonicFret == 27
            && l1[6].Notes.Count == 2 && l1[6].Notes[0].Techniques.Contains("WideVibrato") && !l1[6].Notes[1].Techniques.Contains("WideVibrato"));
        FullDemoSongFactory.Riff(lead, 2, "8t:s1:12 8t:s1:10 8t:s1:8 4:s2:10+tie+ls 2:s2:12+trem32", 80);
        Check("full demo helpers: triplets, tie, tremolo speed", Complete(2) && BeatsOf(lead, 2)[0].IsTriplet && BeatsOf(lead, 2)[3].IsTied && BeatsOf(lead, 2)[4].TremoloPickDenominator == 32);
        var threw = false;
        try { FullDemoSongFactory.Riff(lead, 3, "4:s1:0 4:s1:2 4:s1:3", 80); } catch (InvalidOperationException ex) { threw = ex.Message.Contains("bar 3") && ex.Message.Contains("12"); }
        Check("full demo helpers: a bar whose durations do not add up throws (naming track and bar) and writes nothing", threw && BeatsOf(lead, 3).Count == 0);
        FullDemoSongFactory.Riff(keys, 1, "1:[B2 F#3 B3 D4]+fout", 64);
        var k = BeatsOf(keys, 1)[0].Notes;
        Check("full demo helpers: pitch names on keys (C4 = 60) with the octave-string placement",
            k.Select(n => n.MidiValue).SequenceEqual(new[] { 47, 54, 59, 62 }) && k[0].StringIndex == 5 && k[0].Fret == 11 && k.All(n => n.Techniques.Contains("FadeOut")));
        FullDemoSongFactory.Riff(gtr, 8, "16:O> 16:O 8:P1 16:O> 16:O 8:P3 8(2:3):P6> 8(2:3):P5>", 95);             // B7d, 7/8 with a 2:3 duplet
        Check("full demo helpers: 7/8 bar with a 2:3 duplet fills 14 slots", Complete(8));

        var sec = FullDemoSongFactory.Section.Verse1;
        FullDemoSongFactory.Grid(drums, 3, sec, ("K", "x.....x.x.....x."), ("S", "....x..g....x..."), ("R", "X.x.X.x.X.x.X.x."));
        var d3 = BeatsOf(drums, 3).SelectMany(c => c.Notes).ToList();
        Check("full demo helpers: a drum grid merges lanes per slot, ghost clamped, bar complete",
            Complete(3) && BeatsOf(drums, 3).Count == 9 && d3.Count(n => n.MidiValue == 36) == 4 && d3.Single(n => n.Ghost).Velocity <= 60 && d3.All(n => n.Fret == n.MidiValue));
        FullDemoSongFactory.Fill(drums, 3, FullDemoSongFactory.FillKind.P4);
        Check("full demo helpers: P4 replaces kick/snare on beat 4 with a 4-note ramp", Complete(3)
            && BeatsOf(drums, 3).Where(c => c.RhythmicPosition is null).Count() >= 8 && BeatsOf(drums, 3).SelectMany(c => c.Notes).Count(n => n.MidiValue == 38) == 6,
            string.Join(" ", BeatsOf(drums, 3).Select(c => $"{c.RhythmicPosition}/{c.DurationDenominator}:{string.Join(",", c.Notes.Select(n => n.MidiValue))}")) + $" complete={Complete(3)}");
        FullDemoSongFactory.Grid(drums, 4, sec, ("K", "dddddddddddddddd"), ("S", "....x.......x..."));
        FullDemoSongFactory.Fill(drums, 4, FullDemoSongFactory.FillKind.F16, flam: true);
        Check("full demo helpers: F16 with a flam rewrites the whole bar", Complete(4) && BeatsOf(drums, 4).Count == 16
            && BeatsOf(drums, 4)[0].Notes.Any(n => n.IsGraceNote && n.Velocity is >= 55 and <= 70) && BeatsOf(drums, 4).SelectMany(c => c.Notes).Count(n => n.MidiValue == 36) == 4);
        FullDemoSongFactory.Fill(drums, 5, FullDemoSongFactory.FillKind.Roll39);
        FullDemoSongFactory.Fill(drums, 6, FullDemoSongFactory.FillKind.FTrip8);
        Check("full demo helpers: Roll39 (16ths, 16th triplets, 32nds) and FTrip8 fill their bars", Complete(5) && BeatsOf(drums, 5).Count == 22 && Complete(6) && BeatsOf(drums, 6).Count == 12);
        FullDemoSongFactory.Grid(drums, 7, sec, ("K", "x.........x.x..."), ("CL", "........x......."), ("H", "X.x.X.x........."));
        FullDemoSongFactory.Roll(drums, 7, 8, 6, 6, 4, 16, 42, 60, 90);
        FullDemoSongFactory.Roll(drums, 7, 12, 3, 0, 0, 16, 42, 60, 80);
        FullDemoSongFactory.Roll(drums, 7, 15, 4, 0, 0, 64, 42, 80, 95);
        var d7 = BeatsOf(drums, 7);
        Check("full demo helpers: rolls (6:4, 16ths, 64ths at 15.25/15.5/15.75) merge with the grid and fill the bar",
            Complete(7) && d7.Count(c => c.TupletNumerator == 6) == 6 && d7.Count(c => c.DurationDenominator == 64) == 4
            && d7.Any(c => Math.Abs((c.RhythmicPosition ?? -1) - 15.75) < 1e-6) && d7.Any(c => c.TupletNumerator == 6 && c.Notes.Any(n => n.MidiValue == 36)), string.Join(" ", d7.Select(c => $"{c.RhythmicPosition}:{c.DurationDenominator}")));
        var h1 = FullDemoSongFactory.Hv("x", 5, 3, 36, 100, 4, sec, FullDemoSongFactory.Lane.Kick);
        Check("full demo helpers: Hv is deterministic and stays within the jitter", h1 == FullDemoSongFactory.Hv("x", 5, 3, 36, 100, 4, sec, FullDemoSongFactory.Lane.Kick) && h1 is >= 96 and <= 104);
    }

    /// <summary>Bars whose structure fields differ between the first track and any other track.</summary>
    private static List<string> FdStructureMismatches(SongProject song)
    {
        static string Key(MeasureModel m) => string.Join("|", m.TimeSigNum, m.TimeSigDenom, m.KeySignature, m.KeySignatureMinor, m.RepeatStart, m.RepeatEnd,
            m.RepeatCount, m.AlternateEnding, m.AlternateEndingMask, m.IsDoubleBar, m.SectionName, m.TempoChange,
            string.Join(";", m.MidBarTempos ?? new()), m.TripletFeelKind, m.FreeTime, m.ForceLineBreak, m.PreventLineBreak, m.Anacrusis, m.Directions);
        var list = new List<string>();
        var first = song.Tracks[0];
        foreach (var t in song.Tracks.Skip(1))
            for (var i = 0; i < Math.Min(first.Measures.Count, t.Measures.Count); i++)
                if (Key(first.Measures[i]) != Key(t.Measures[i])) list.Add($"{t.Name} bar {i + 1}");
        return list;
    }

    /// <summary>Coverage walk of the S- and T-items (and B30) the skeleton owns (plan 4.3, 4.4, 5.1).</summary>
    private static void FdCoverageSkeleton(SongProject song)
    {
        var bars = song.Tracks[0].Measures;
        MeasureModel B(int bar) => bars[bar - 1];
        TrackModel T(string name) => song.Tracks.First(t => t.Name == name);
        void Cover(string id, bool ok, string? detail = null) => Check($"full demo coverage {id}", ok, detail);

        Cover("S01 song 4/4", song.TimeSignatureNumerator == 4 && song.TimeSignatureDenominator == 4);
        Cover("S02/S48 bar signatures 1/4 (1), 3/4 (10), 7/8 (83-86), 2/4 (118), 4/4 again after each",
            (B(1).TimeSigNum, B(1).TimeSigDenom) == (1, 4) && (B(10).TimeSigNum, B(10).TimeSigDenom) == (3, 4) &&
            Enumerable.Range(83, 4).All(b => (B(b).TimeSigNum, B(b).TimeSigDenom) == (7, 8)) && (B(118).TimeSigNum, B(118).TimeSigDenom) == (2, 4) &&
            new[] { 2, 11, 87, 119 }.All(b => B(b).TimeSigNum == 4 && B(b).TimeSigDenom == 4));
        Cover("S02 bars hold their slots on every track", song.Tracks.All(t => t.Measures.All(m => m.Cells.Count >= FullDemoSongFactory.SlotsOf(m))));
        Cover("S03/S05 song key A minor", song.KeySignature == 0 && song.KeySignatureMinor);
        Cover("S04/S05 B minor from 118 to the end", Enumerable.Range(118, 27).All(b => B(b).KeySignature == 2 && B(b).KeySignatureMinor == true) && B(117).KeySignature is null);
        var clefs = song.Tracks.SelectMany(t => t.Measures.Select(m => m.Clef)).ToHashSet();
        Cover("S06 clefs guitar, treble, bass, alto", new[] { Clefs.Guitar, Clefs.Treble, Clefs.Bass, Clefs.Alto }.All(clefs.Contains)
            && T(FullDemoSongFactory.Piano).Measures[90].Clef == Clefs.Bass && T(FullDemoSongFactory.Pad).Measures[134].Clef == Clefs.Alto
            && T(FullDemoSongFactory.Sub).Measures.All(m => m.Clef == Clefs.Bass));
        Cover("S07/S08 repeat 19..22 x2, 48..49 x3", B(19).RepeatStart && B(22).RepeatEnd && B(22).RepeatCount == 2 && B(48).RepeatStart && B(49).RepeatEnd && B(49).RepeatCount == 3);
        Cover("S09 endings 1 (22), 2 (23), 3 (50)", B(22).AlternateEnding == 1 && B(23).AlternateEnding == 2 && B(50).AlternateEnding == 3);
        Cover("S10 ending \"1.2.\" on 49", B(49).AlternateEndingMask == 0b011 && B(49).EndingLabel == "1.2.");
        Cover("S11 double bars", FullDemoSongFactory.DoubleBars.All(b => B(b).IsDoubleBar) && bars.Count(m => m.IsDoubleBar) == FullDemoSongFactory.DoubleBars.Length);
        Cover("S12 tempo 150", song.Tempo == 150);
        Cover("S13 tempo changes 91 (112), 102 (150)", B(91).TempoChange == 112 && B(102).TempoChange == 150);
        Cover("S14 mid-bar tempo 90 slot 12 -> 112", B(90).MidBarTempos is [{ Slot: 12, Tempo: 112, RampSlots: 0 }]);
        Cover("S15 ramps 101 (->150), 139 (->141), 140 (->132)",
            B(101).MidBarTempos is [{ Slot: 0, Tempo: 150, RampSlots: 16 }] && B(139).MidBarTempos is [{ Tempo: 141, RampSlots: 16 }] && B(140).MidBarTempos is [{ Tempo: 132, RampSlots: 16 }]
            && MusicTime.TempoAt(song, 143) == 132);
        Cover("S16 triplet feel 8th 79-82", Enumerable.Range(79, 4).All(b => B(b).TripletFeelKind == TripletFeels.Eighth));
        Cover("S17 triplet feel 16th 99-100", Enumerable.Range(99, 2).All(b => B(b).TripletFeelKind == TripletFeels.Sixteenth));
        Cover("S18 free time 142", B(142).FreeTime && bars.Count(m => m.FreeTime) == 1);
        Cover("S19 anacrusis 1", B(1).Anacrusis && bars.Count(m => m.Anacrusis) == 1);
        var simile1 = new[] { FullDemoSongFactory.RhyL, FullDemoSongFactory.RhyR, FullDemoSongFactory.Bass, FullDemoSongFactory.Drums };
        Cover("S20 simile 1 bar on 25 and 29 (RhyL, RhyR, Bass, Drums only)",
            song.Tracks.All(t => (t.Measures[24].SimileOneBar && t.Measures[28].SimileOneBar) == simile1.Contains(t.Name) && t.Measures.Count(m => m.SimileOneBar) == (simile1.Contains(t.Name) ? 2 : 0)));
        var simile2 = new[] { FullDemoSongFactory.RhyL, FullDemoSongFactory.RhyR, FullDemoSongFactory.Bass };
        Cover("S21 simile 2 bars on 57-58 (RhyL, RhyR, Bass only)",
            song.Tracks.All(t => (t.Measures[56].SimileTwoBar && t.Measures[57].SimileTwoBar) == simile2.Contains(t.Name) && t.Measures.Count(m => m.SimileTwoBar) == (simile2.Contains(t.Name) ? 2 : 0)));
        var directions = bars.Select((m, i) => (Bar: i + 1, m.Directions)).Where(x => x.Directions.Length > 0).ToList();
        Cover("S22/S24/S25/S26/S27/S28/S33 directions exactly as planned",
            directions.SequenceEqual(FullDemoSongFactory.BarDirections.Select(d => (d.Bar, d.Direction))), string.Join(", ", directions));
        Cover("S37 20 markers with colours, one locked, the swell 1 bar long, SectionName on every track",
            song.Markers.Count == 20 && song.Markers.Count(m => m.LockPosition) == 1 && song.Markers.Single(m => m.MeasureIndex == 9).LengthBars == 1
            && song.Markers.All(mk => song.Tracks.All(t => t.Measures[mk.MeasureIndex].SectionName == mk.Title))
            && song.Markers.Select(m => m.ColorHex).Distinct().Count() >= 8);
        Cover("S38 forced line breaks", FullDemoSongFactory.LineBreaks.All(b => B(b).ForceLineBreak) && bars.Count(m => m.ForceLineBreak) == FullDemoSongFactory.LineBreaks.Length);
        Cover("S39 prevented line breaks 10 and 119", B(10).PreventLineBreak && B(119).PreventLineBreak && bars.Count(m => m.PreventLineBreak) == 2);
        Cover("S41 title block", new[] { song.Title, song.Subtitle, song.Artist, song.Album, song.MusicAuthor, song.LyricsAuthor, song.Copyright, song.TabAuthor, song.Instructions, song.Notice }
            .All(s => !string.IsNullOrWhiteSpace(s)) && song.Title == "Ashen Meridian" && song.Copyright.Contains("CC0"));
        Cover("S42 song lyrics (both choruses, HEY!, RISE!)", song.Lyrics.Contains("Hold the last light") && song.Lyrics.Contains("Sing through the smoke") && song.Lyrics.Contains("HEY!") && song.Lyrics.Contains("RISE!"));
        Cover("S43 gray inactive voice", song.GrayInactiveVoice);

        var rhyL = T(FullDemoSongFactory.RhyL); var rhyR = T(FullDemoSongFactory.RhyR); var lead = T(FullDemoSongFactory.Lead);
        var clean = T(FullDemoSongFactory.Clean); var bass = T(FullDemoSongFactory.Bass); var sub = T(FullDemoSongFactory.Sub);
        var drums = T(FullDemoSongFactory.Drums); var pad = T(FullDemoSongFactory.Pad); var piano = T(FullDemoSongFactory.Piano);
        Cover("T01 6-string guitars (Lead, Harmony, Clean in E standard)", new[] { lead, T(FullDemoSongFactory.LeadHarm), clean }.All(t => t.StringTunings.SequenceEqual(new[] { 64, 59, 55, 50, 45, 40 })));
        Cover("T02 7-string drop A", rhyL.StringTunings.SequenceEqual(new[] { 64, 59, 55, 50, 45, 40, 33 }));
        Cover("T03 8-string", rhyR.StringTunings.SequenceEqual(new[] { 64, 59, 55, 50, 45, 40, 33, 28 }));
        Cover("T04/T11 4-string custom tuning (Sub)", sub.Kind == TrackKind.Bass && sub.StringTunings.SequenceEqual(new[] { 50, 45, 40, 33 }));
        Cover("T05 5-string bass drop A", bass.Kind == TrackKind.Bass && bass.StringTunings.SequenceEqual(new[] { 43, 38, 33, 28, 21 }));
        Cover("T07 drums (channel 9, six lines)", drums.Kind == TrackKind.Drums && drums.MidiChannel == 9 && drums.StringTunings.Count == 6);
        Cover("T08 keys (Pad, Piano on octave strings)", pad.Kind == TrackKind.Keys && piano.Kind == TrackKind.Keys && pad.StringTunings.Count == 7);
        Cover("T09 other GM programs (pad 89, synth bass 38, strings 48 by mix)", pad.MidiProgram == 89 && sub.MidiProgram == 38);
        Cover("T10 drum kits 0 and 25 (mix program change)", drums.MidiProgram == 0 && drums.Measures[0].Cells.Any(c => c.Mix?.Program == 25) && drums.Measures[10].Cells.Any(c => c.Mix?.Program == 0));
        Cover("T12 capo 5 on Clean", clean.Capo == 5);
        Cover("T13 frets (Clean 22, guitars 24)", clean.NumberOfFrets == 22 && rhyL.NumberOfFrets == 24);
        Cover("T14/T15 custom drum map: ride bell as a diamond", drums.DrumMapPreset == DrumMaps.Custom
            && drums.CustomDrumMap is [{ Midi: 53, TabLine: 0, Label: "RB", Head: "diamond" }] && DrumMaps.For(drums, 53).Head == "diamond");
        var mix = new (string Name, int Program, int Channel, int Volume, int Pan, int Reverb, int Chorus)[]
        {
            (FullDemoSongFactory.RhyL, 30, 0, 100, 0, 16, 0), (FullDemoSongFactory.RhyR, 30, 1, 100, 127, 16, 0), (FullDemoSongFactory.Lead, 29, 2, 96, 64, 40, 8),
            (FullDemoSongFactory.LeadHarm, 29, 3, 86, 50, 40, 8), (FullDemoSongFactory.Clean, 27, 4, 84, 84, 72, 48), (FullDemoSongFactory.Bass, 34, 5, 104, 64, 8, 0),
            (FullDemoSongFactory.Sub, 38, 6, 92, 64, 0, 0), (FullDemoSongFactory.Drums, 0, 9, 108, 64, 28, 0), (FullDemoSongFactory.Pad, 89, 7, 70, 64, 80, 40),
            (FullDemoSongFactory.Piano, 0, 8, 80, 44, 64, 0),
        };
        Cover("T16-T20 program, channel, volume, pan, sends as in plan 1.5",
            mix.All(x => T(x.Name) is var t && (t.MidiProgram, t.MidiChannel, t.Volume, t.Pan, t.Reverb, t.Chorus) == (x.Program, x.Channel, x.Volume, x.Pan, x.Reverb, x.Chorus)));
        Cover("T21 transpose -12 on Sub only", sub.Transpose == -12 && song.Tracks.Count(t => t.Transpose != 0) == 1);
        Cover("T22 no mute or solo", song.Tracks.All(t => !t.Mute && !t.Solo));
        Cover("T23 names, colours, performer, notes", song.Tracks.All(t => t.Performer == "TabForge Demo" && t.ColorHex.StartsWith('#'))
            && song.Tracks.Select(t => t.ColorHex).Distinct().Count() == 10 && rhyR.TrackNotes.Length > 0 && sub.TrackNotes.Length > 0 && clean.TrackNotes.Length > 0);
        Cover("T24 mixer groups Guitars / Rhythm section / Keys", song.Tracks.All(t => t.MixerGroup is "Guitars" or "Rhythm section" or "Keys")
            && song.Tracks.Count(t => t.MixerGroup == "Guitars") == 5 && song.Tracks.Count(t => t.MixerGroup == "Keys") == 2);
        Cover("T25 group levels (Keys 85 %)", song.Mixer.Grouping == MixerGrouping.ByInstrument && song.Mixer.Levels("Keys").Volume == 85 && song.Mixer.Levels("Guitars").Volume == 100);
        Cover("T26 master and buses left at defaults", song.Mixer.Master.IsDefault && song.Mixer.Buses.Count == 0 && song.Mixer.MasterPan == 0);
        Cover("T27 sound source MIDI and rigs", song.Tracks.All(t => t.SoundSource == SoundSources.Midi && t.Rig.Plugins.Count == 0)
            && rhyL.Rig.Name == "High Gain" && drums.Rig.ArticulationMap == "GM Drums" && bass.Rig.ArticulationMap == "Generic Bass" && piano.Rig.ArticulationMap == "Piano");

        var mixes = song.Tracks.SelectMany(t => t.Measures.SelectMany((m, i) => m.Cells.Where(c => c.Mix is not null).Select(c => (t.Name, Bar: i + 1, c)))).ToList();
        Cover("B30 mix table: every change of plan 5.1 sits on a beat (volume, pan, chorus, reverb, phaser, tremolo, program, transitions, all tracks)",
            FullDemoSongFactory.MixTable().All(x => mixes.Any(m => m.Name == x.Track && m.Bar == x.Bar))
            && mixes.All(m => m.c.Notes.Count > 0 || m.c.IsRest)
            && mixes.Any(m => m.c.Mix!.Phaser is not null) && mixes.Any(m => m.c.Mix!.Tremolo is not null) && mixes.Any(m => m.c.Mix!.Chorus is not null)
            && mixes.Any(m => m.c.Mix!.TransitionBeats > 0) && mixes.Any(m => m.c.Mix!.AllTracks) && mixes.Any(m => m.Name == FullDemoSongFactory.Pad && m.Bar == 119 && m.c.Mix!.Program == 48));
    }

    /// <summary>
    /// Coverage of the musical parts (plan 4.1 / 4.2 / 6.4). OWNERS FILL THESE IN when their part lands; until then each pending
    /// item is only logged. Replace an entry's "pending" log with a Check (use FdAllTechniques / FdCells below).
    /// </summary>
    private static void FdCoverageParts(SongProject song)
    {
        DrumCoverageChecks(song);
        // ---- TODO owner (b) Drums: B35 drum notes in every drum bar except 2 and the simile bars; B05 tuplets 5:4 6:4 7:4 9:8 10:8 11:8
        //      in drums (bars 8-10, 70); B01 64ths at 9.4 (RhythmicPosition 15.25/15.5/15.75); N31 ghost snares ~10 %; N33 flams
        //      (18, 31, 47, 90, 91, 109, 118, 134); N24 crash FadeIn at 3; N25 FadeOut at 144; B08 fermata at 141/142; T10 808 at bar 1.
        // ---- TODO owner (c1) Rhythm/Bass/Sub: N01 N06 N08 N09 N12 (TremBar, Dive, Hold, Predive, PrediveDive) N15 N17 N20 N21 N23 N30
        //      N32 N34; B02 B03 B04 B05 (2:3 duplet at 86) B09 B10 B11 B15 B16 B27 (Force 83-86, Break 87-89, BreakSecondaryBeamBefore)
        //      B29 (60 % stabs, 80 % slap bass) B34; S47 empty bars; every Pn octave on RhyR choruses.
        RhythmCoverageChecks(song);
        // ---- TODO owner (c2) Lead/Keys: N02 N03 N04 (all bend types) N05 N07 N10 N11 N13 N14 N16 N18 N19 N22 N26 N27 N28 N29 N33
        //      (GraceBefore/OnBeat/Bend) N35 N36; B12 B13 B17 B18 B20 (two-line lyrics 127-134) B23 B25 (+12 -12 +24 -24) B26 B28 B31 B32.
        LeadKeysCoverageChecks(song);
        // ---- shared (whoever lands last): every GpEffects.All string present (Slide/Legato counted as LegatoSlide; flag-backed:
        //      Ghost, Dead, Accent, HeavyAccent, Staccato, Tenuto, Fermata, Tie, GraceBefore/GraceOnBeat/GraceBend) — see FdAllTechniques.
        var present = FdAllTechniques(song);
        var missing = GpEffects.All.Where(x => !present.Contains(x)).ToList();
        Log.Add($"  info  full demo coverage (pending, owners b/c1/c2): {GpEffects.All.Length - missing.Count}/{GpEffects.All.Length} technique names present; missing: {string.Join(", ", missing)}");
    }

    /// <summary>Coverage of owner (c1): Rhythm Gtr L/R, Bass and Sub Drop (plan 3.2-3.4, the N/B items of 4.1/4.2 placed on them).</summary>
    private static void RhythmCoverageChecks(SongProject song)
    {
        TrackModel T(string name) => song.Tracks.First(t => t.Name == name);
        var rhyL = T(FullDemoSongFactory.RhyL); var rhyR = T(FullDemoSongFactory.RhyR);
        var bass = T(FullDemoSongFactory.Bass); var sub = T(FullDemoSongFactory.Sub);
        List<TabCell> Beats(TrackModel t, int bar) => t.Measures[bar - 1].Cells.Where(c => c.Notes.Count > 0 || c.IsRest).ToList();
        IEnumerable<TabNote> Notes(TrackModel t, int bar) => Beats(t, bar).SelectMany(c => c.Notes);
        IEnumerable<TabNote> NotesIn(TrackModel t, int from, int to) => Enumerable.Range(from, to - from + 1).SelectMany(b => Notes(t, b));
        bool Has(TrackModel t, int bar, string tech) => Notes(t, bar).Any(n => n.Techniques.Contains(tech));
        void Cover(string id, bool ok, string? detail = null) => Check($"full demo coverage (rhythm) {id}", ok, detail);
        var parts = new[] { rhyL, rhyR, bass, sub };

        Cover("S47 empty bars: rhythm 1-9, bass 1-5, sub except 11/91/119/141-143", Enumerable.Range(1, 9).All(b => Beats(rhyL, b).Count == 0 && Beats(rhyR, b).Count == 0)
            && Enumerable.Range(1, 5).All(b => Beats(bass, b).Count == 0)
            && Enumerable.Range(1, 144).All(b => (Beats(sub, b).Count > 0) == (b is 11 or 91 or 119 or 141 or 142 or 143)));
        Cover("simile bars 25/29/57/58 left empty under the glyph (RhyL, RhyR, Bass)", new[] { 25, 29, 57, 58 }.All(b => new[] { rhyL, rhyR, bass }.All(t => Beats(t, b).Count == 0)));
        Cover("N01 palm-muted chugs on RhyL/R and Bass (19.1), jitter within ±3 of f", new[] { rhyL, rhyR, bass }.All(t => Notes(t, 19).Count(n => TechniqueNames.HasPalmMute(n.Techniques)) >= 8)
            && Notes(rhyL, 19).Where(n => TechniqueNames.HasPalmMute(n.Techniques)).All(n => n.Velocity is >= 92 and <= 98)
            && Notes(rhyL, 19).Where(n => TechniqueNames.HasPalmMute(n.Techniques)).Select(n => n.Velocity).Distinct().Count() > 1);
        Cover("double-tracking: R is L -2 with its own seed (never identical note for note)",
            Notes(rhyR, 11).Where(n => !TechniqueNames.HasPalmMute(n.Techniques)).All(n => n.Velocity == 110)
            && !Notes(rhyL, 19).Select(n => n.Velocity).SequenceEqual(Notes(rhyR, 19).Select(n => n.Velocity)));
        Cover("N02 let ring RhyL/R 32-39", new[] { rhyL, rhyR }.All(t => Enumerable.Range(32, 7).All(b => Notes(t, b).All(n => n.Techniques.Contains("LetRing")))));
        Cover("N06 shift slide RhyL/R and Bass 54.4", new[] { rhyL, rhyR, bass }.All(t => Has(t, 54, "ShiftSlide")));
        Cover("N07 slide in below RhyL/R 40.1", new[] { rhyL, rhyR }.All(t => Beats(t, 40)[0].Notes.All(n => n.Techniques.Contains("SlideInBelow"))));
        Cover("N08 slide out down RhyL/R and Bass 47/78/109/117/126/134", new[] { 47, 78, 109, 117, 126, 134 }.All(b => new[] { rhyL, rhyR, bass }.All(t => Has(t, b, "SlideOutDown"))));
        Cover("N09 pick slide down RhyL 90, up RhyR 70", Has(rhyL, 90, "PickSlideDown") && Has(rhyR, 70, "PickSlideUp") && !Has(rhyR, 70, "PickSlideDown"));
        Cover("N12 whammy: TremBar L92, Dive L94 + Sub 11/91/119/143, Hold Sub 91, Predive R96, PrediveDive R92",
            Has(rhyL, 92, "TremBar") && Has(rhyL, 94, "TremBarDive") && new[] { 11, 91, 119, 143 }.All(b => Has(sub, b, "TremBarDive")) && Has(sub, 91, "TremBarHold")
            && Has(rhyR, 96, "TremBarPredive") && Has(rhyR, 92, "TremBarPrediveDive") && Beats(rhyL, 94).Any(c => c.WhammyPoints is { Count: 4 } w && w[^1].Value == -24));
        Cover("N12 R 94 dives the 8-string low E1 (only in the breakdown)", Notes(rhyR, 94).Any(n => n.StringIndex == 7 && n.MidiValue == 28 && n.Techniques.Contains("TremBarDive"))
            && Enumerable.Range(1, 144).Where(b => Notes(rhyR, b).Any(n => n.StringIndex == 7)).SequenceEqual(new[] { 94, 98 }));
        Cover("N15 pinch harmonics RhyL 14/27/92, RhyR 92/96", new[] { 14, 27, 92 }.All(b => Has(rhyL, b, "PinchHarmonic")) && Has(rhyR, 92, "PinchHarmonic") && Has(rhyR, 96, "PinchHarmonic")
            && Notes(rhyL, 14).Single(n => n.Techniques.Contains("PinchHarmonic")).HarmonicFret == 13);
        Cover("N17 semi harmonic RhyR 53.4 only", Has(rhyR, 53, "SemiHarmonic") && !Has(rhyL, 53, "SemiHarmonic"));
        Cover("N20/N21 slap, pop and dead slapped on Bass 79-82", Enumerable.Range(79, 4).All(b => Has(bass, b, "Slap") && Has(bass, b, "DeadSlapped"))
            && Enumerable.Range(79, 3).All(b => Has(bass, b, "Pop")) && NotesIn(bass, 79, 82).Where(n => n.Techniques.Contains("DeadSlapped")).All(n => n.Dead) && Has(bass, 82, "Harmonic"));
        Cover("N23/B16 tremolo picking RhyL/R 59-62 (16ths)", new[] { rhyL, rhyR }.All(t => Enumerable.Range(59, 4).All(b => Beats(t, b)[0].TremoloPickDenominator == 16 && Has(t, b, "TremoloPick"))));
        Cover("N24/N25 bass fade in 6, fade out RhyL/R and Bass 144", Has(bass, 6, "FadeIn") && new[] { rhyL, rhyR, bass }.All(t => Has(t, 144, "FadeOut")));
        Cover("N30 pick strokes: RhyL 19 all down, 24 alternate; none on RhyR", Notes(rhyL, 19).All(n => n.Techniques.Contains("PickDown"))
            && Has(rhyL, 24, "PickUp") && Has(rhyL, 24, "PickDown") && !NotesIn(rhyR, 1, 144).Any(n => n.Techniques.Contains("PickDown") || n.Techniques.Contains("PickUp")));
        Cover("N31/B34 ghost + palm mute on RhyR 21 and 49", Notes(rhyR, 21).Count(n => n.Ghost && TechniqueNames.HasPalmMute(n.Techniques)) == 1
            && Notes(rhyR, 49).Count(n => n.Ghost) == 2 && !NotesIn(rhyL, 1, 144).Any(n => n.Ghost));
        Cover("N32/B34 dead notes (RiffM2, RiffD, 31 dead + accent, 50)", Notes(rhyL, 12).Any(n => n.Dead) && Notes(rhyL, 24).Any(n => n.Dead)
            && Beats(rhyL, 31).Any(c => c.Accent == 1 && c.Notes.All(n => n.Dead)) && Notes(rhyL, 50).Any(n => n.Dead));
        Cover("N34 tie: Bass 9 -> 10 and Sub 91.3", Beats(bass, 10)[0].IsTied && Beats(sub, 91)[1].IsTied);
        Cover("B02/B03/B04 dotted (ChordM), double-dotted (BD1 91.3), triplets (RiffV 19.4)", Beats(rhyL, 15)[0].Dots == 1
            && Beats(rhyL, 91).Any(c => c.Dots == 2) && Beats(rhyL, 19).Count(c => c.IsTriplet) == 3);
        Cover("B05 2:3 duplet at 86 on RhyL/R and Bass", new[] { rhyL, rhyR, bass }.All(t => Beats(t, 86).Count(c => c.TupletNumerator == 2 && c.TupletDenominator == 3) == 2));
        Cover("B08 fermata 141 and 142 (RhyL/R, Bass, Sub)", parts.All(t => Beats(t, 141)[0].Fermata && Beats(t, 142)[0].Fermata));
        Cover("B09/B10 accents and heavy accents (98, 141, BD1)", Beats(rhyL, 98).All(c => c.Accent == 2) && Beats(rhyL, 141)[0].Accent == 2 && Beats(rhyL, 91)[0].Accent == 2
            && Beats(rhyL, 91)[0].Notes.All(n => n.Velocity == 127));
        Cover("B11/B29 staccato stabs 48-50 at 60 %, slap bass 79-82 at 80 %", Enumerable.Range(48, 3).All(b => Beats(rhyL, b).Where(c => c.Staccato).All(c => c.SoundDurationPercent == 60)
            && Beats(rhyL, b).Any(c => c.Staccato)) && Enumerable.Range(79, 4).All(b => Beats(bass, b).All(c => c.SoundDurationPercent == 80)));
        Cover("B15 whammy curves on every whammy note", parts.SelectMany(t => t.Measures.SelectMany(m => m.Cells))
            .Where(c => c.Notes.Any(n => n.Techniques.Any(x => x.StartsWith("TremBar", StringComparison.Ordinal)))).All(c => c.WhammyPoints.Count >= 2));
        Cover("B17 beat text (19, 24, 50 HEY!, 83, 87, 90, 91 RISE!, 94)", new[] { 19, 24, 50, 83, 87, 90, 91, 94 }.All(b => Beats(rhyL, b).Any(c => !string.IsNullOrWhiteSpace(c.Text)))
            && Beats(rhyL, 50)[1].Text == "gang: HEY!");
        Cover("B27 beams: RiffD secondary breaks, 7/8 Force, 7-over-4 Break (RhyL/R, Bass)", new[] { rhyL, rhyR, bass }.All(t =>
            Beats(t, 24).Count(c => c.BreakSecondaryBeamBefore) == 3 && Enumerable.Range(83, 4).All(b => Beats(t, b).Any(c => c.BeamMode == BeamMode.Force))
            && Enumerable.Range(87, 3).All(b => Beats(t, b).Count(c => c.BeamMode == BeamMode.Break) == 2)));
        Cover("R delta 1: every power chord in R's choruses carries the octave s4:n+2", new[] { 40, 46, 71, 102, 110, 117, 119, 127, 134 }.All(b =>
            Beats(rhyR, b).Where(c => c.Notes.Count >= 3).All(c => c.Notes.Count == 4 && c.Notes.Any(n => n.StringIndex == 3 && n.Fret == c.Notes[0].Fret + 2)))
            && Beats(rhyL, 40).All(c => c.Notes.Count <= 3));
        Cover("R delta 2: pre-chorus 2 octaves move up on R", Beats(rhyR, 63)[0].Notes.Select(n => n.Fret).SequenceEqual(new[] { 12, 14 }) && Beats(rhyL, 63)[0].Notes.Count == 3);
        Cover("bass locks: follows RhyL an octave down (19), roots in the choruses, B minor at 119", Beats(bass, 19).Count == Beats(rhyL, 19).Count
            && Beats(bass, 19).Zip(Beats(rhyL, 19)).All(p => p.First.Notes[0].MidiValue == p.Second.Notes.OrderByDescending(n => n.StringIndex).First().MidiValue - 12)
            && Notes(bass, 40).All(n => n.MidiValue == 29) && Notes(bass, 119).All(n => n.MidiValue == 31));
        Cover("outro riff transposed +2 (135-138) and Sub sounds via Transpose -12", Beats(rhyL, 135)[0].Notes[0].Fret == 2 && Notes(sub, 119).All(n => n.MidiValue == 47) && sub.Transpose == -12);
    }

    /// <summary>Owner (b): the drum items of the coverage matrix (plan 3.9, 4.1, 4.2, 5.2).</summary>
    private static void DrumCoverageChecks(SongProject song)
    {
        var d = song.Tracks.First(t => t.Name == FullDemoSongFactory.Drums);
        void Cover(string id, bool ok, string? detail = null) => Check($"full demo coverage (drums) {id}", ok, detail);
        List<TabCell> Beats(int bar) => d.Measures[bar - 1].Cells.Concat(d.Measures[bar - 1].Voice2Cells).Where(c => c.Notes.Count > 0 || c.IsRest).ToList();
        IEnumerable<TabNote> Notes(int bar) => Beats(bar).SelectMany(c => c.Notes);
        var all = Enumerable.Range(1, 144).SelectMany(b => Notes(b).Select(n => (Bar: b, Note: n))).ToList();

        var silent = new[] { 1, 2, 142 };
        var missing = Enumerable.Range(1, 144).Where(b => !silent.Contains(b) && !Notes(b).Any()).ToList();
        Cover("B35 drum notes in every bar except 1, 2 and 142, each on its GP line (Fret = MidiValue, line = DrumLine)",
            missing.Count == 0 && silent.All(b => !Notes(b).Any()) && all.All(x => x.Note.Fret == x.Note.MidiValue && x.Note.StringIndex == GuitarProImporter.DrumLine(x.Note.MidiValue)),
            $"no notes in: {string.Join(",", missing)}");
        var badBars = Enumerable.Range(1, 144).Where(b => b is not (2 or 142) && MusicTime.AnalyzeBar(song, b - 1) is not { Complete: true, Error: false }).ToList();
        Cover("slot sums: every bar complete with the drums in it", badBars.Count == 0, string.Join(",", badBars.Take(10)));

        bool Tup(int bar, int n, int dd, int count) => Beats(bar).Count(c => c.TupletNumerator == n && c.TupletDenominator == dd) == count;
        Cover("B05 tuplets 5:4 (8.4), 6:4 (8.3, 10.2, F6 70), 7:4 (9.1), 9:8 (9.2), 10:8 (9.3), 11:8 (10.3)",
            Tup(8, 5, 4, 5) && Tup(8, 6, 4, 6) && Tup(10, 6, 4, 6) && Tup(70, 6, 4, 12) && Tup(9, 7, 4, 7) && Tup(9, 9, 8, 9) && Tup(9, 10, 8, 10) && Tup(10, 11, 8, 11),
            string.Join(" ", new[] { 8, 9, 10, 70 }.Select(b => $"{b}:" + string.Join(",", Beats(b).Where(c => c.TupletNumerator > 0).GroupBy(c => $"{c.TupletNumerator}/{c.TupletDenominator}").Select(g => $"{g.Key}x{g.Count()}")))));
        var sixtyFourths = Beats(9).Where(c => c.DurationDenominator == 64).Select(c => c.RhythmicPosition ?? -1).ToList();
        Cover("B01 64ths at 9.4 (15, 15.25, 15.5, 15.75), the last an open hat",
            sixtyFourths.Count == 4 && new[] { 15.25, 15.5, 15.75 }.All(p => sixtyFourths.Any(x => Math.Abs(x - p) < 1e-6))
            && Beats(9).Single(c => Math.Abs((c.RhythmicPosition ?? -1) - 15.75) < 1e-6).Notes.Any(n => n.MidiValue == 46),
            string.Join(" ", Beats(9).Select(c => $"{c.RhythmicPosition}/{c.DurationDenominator}")));

        // performed-bar weighting: 19-21 and 48-49 play more than once, but the ratio is about the written part
        var snares = all.Where(x => x.Note.MidiValue == 38 && !x.Note.IsGraceNote && x.Bar > 10).ToList();
        var ghosts = snares.Count(x => x.Note.Ghost);
        Cover("N31 ghost snares about 10 % of snare hits, all <= 60", ghosts > 0 && ghosts * 100.0 / snares.Count is >= 5 and <= 16 && snares.Where(x => x.Note.Ghost).All(x => x.Note.Velocity <= 60),
            $"{ghosts} ghosts of {snares.Count} snare hits");
        var flamBars = all.Where(x => x.Note.IsGraceNote && x.Note.MidiValue == 38 && x.Note.GraceBeforeBeat && x.Note.Techniques.Contains("GraceBefore")).Select(x => x.Bar).Distinct().ToList();
        Cover("N33 flams at 18, 31, 47, 90, 91, 109, 118, 134 (grace 38 before the beat, 55..70)",
            flamBars.SequenceEqual(new[] { 18, 31, 47, 90, 91, 109, 118, 134 }) && all.Where(x => x.Note.IsGraceNote).All(x => x.Note.Velocity is >= 55 and <= 70), string.Join(",", flamBars));
        Cover("N24 reverse-cymbal swell alone in bar 3 (half note on beat 3, FadeIn) and no fade-in over bar 10's kick and snare roll (A7-A07)",
            d.Measures[2].Cells.Any(c => c.DurationDenominator == 2 && c.Notes.Count == 1 && c.Notes.Any(n => n.MidiValue == 49 && n.Techniques.Contains("FadeIn")))
            && d.Measures[2].Cells.Count(c => c.Notes.Count > 0) == 1
            && !d.Measures[9].Cells.Concat(d.Measures[9].Voice2Cells).Any(c => c.Notes.Any(n => n.Techniques.Contains("FadeIn"))));
        Cover("N25 crash FadeOut at 144", Notes(144).Any(n => n.MidiValue == 49 && n.Techniques.Contains("FadeOut")));
        Cover("B08 fermata at 141 (hit) and 142 (whole rest)", Beats(141).Any(c => c.Fermata && c.Notes.Count >= 4) && Beats(142) is [{ IsRest: true, Fermata: true, DurationDenominator: 1 }]);
        Cover("B17 drum beat texts \"808 kit\" (1) and \"hold for the feedback\" (142)", Beats(1).Any(c => c.Text == "808 kit") && Beats(142).Any(c => c.Text == "hold for the feedback"));
        Cover("T10 808 program on the bar-1 pickup, standard kit back at 11", d.Measures[0].Cells.Any(c => c.Mix?.Program == 25 && c.IsRest) && d.Measures[10].Cells.Any(c => c.Mix?.Program == 0 && c.Notes.Count > 0));
        Cover("B09/B10 heavy-accented stabs (48) and unison hit (141)", Beats(48).Count(c => c.Accent == 2) == 3 && Beats(141).Any(c => c.Accent == 2));
        bool Has(int bar, int midi) => Notes(bar).Any(n => n.MidiValue == midi);
        Cover("kit colours: china in the breakdown, ride bell, splash, crash 57, clap and cross-stick, hat foot and open hat",
            Enumerable.Range(91, 8).All(b => Has(b, 52)) && Has(52, 53) && Has(87, 53) && Has(45, 55) && Has(40, 57) && Has(6, 39) && Has(79, 37) && Has(79, 44) && Has(34, 46),
            string.Join(" ", new[] { (52, 53), (87, 53), (45, 55), (40, 57), (6, 39), (79, 37), (79, 44), (34, 46) }.Where(x => !Has(x.Item1, x.Item2))));
        Cover("blast 59-62 (alternating kick and snare 8ths) and double-bass 16ths 19-21",
            Enumerable.Range(59, 3).All(b => Beats(b).Count == 16 && Notes(b).Count(n => n.MidiValue == 36) == 8 && Notes(b).Count(n => n.MidiValue == 38) == 8)
            && Enumerable.Range(19, 3).All(b => Notes(b).Count(n => n.MidiValue == 36) == 16));
        Cover("B27 drum beam overrides (Force in 83-86, Break at the 7-cells of 87-89)",
            Enumerable.Range(83, 4).All(b => Beats(b).Any(c => c.BeamMode == BeamMode.Force)) && Enumerable.Range(87, 3).All(b => Beats(b).Any(c => c.BeamMode == BeamMode.Break)),
            string.Join(" ", Enumerable.Range(83, 7).Select(b => $"{b}:{Beats(b).Count(c => c.BeamMode != BeamMode.Auto)}")));
        Cover("S20 simile bars 25 and 29 are exact copies of 24 and 28",
            new[] { 25, 29 }.All(b => Beats(b).Count == Beats(b - 1).Count && Notes(b).Select(n => (n.MidiValue, n.Velocity)).SequenceEqual(Notes(b - 1).Select(n => (n.MidiValue, n.Velocity)))));
        var afterFills = FullDemoSongFactory.DrumFillBars.Select(b => b + 1).Where(b => d.Measures[b - 1].Cells.Any(c => c.Notes.Any(n => n.MidiValue is 49 or 57)))
            .Select(b => (b, Beats(b).FirstOrDefault(c => c.RhythmicPosition is null or 0)?.Notes.Where(n => n.MidiValue is 49 or 57).Select(n => n.Velocity).DefaultIfEmpty(127).Min() ?? 127)).ToList();
        Cover("dynamics: the crash after every fill is fff (>= 118)", afterFills.All(x => x.Item2 >= 118), string.Join(" ", afterFills.Where(x => x.Item2 < 118)));
        double Avg(IEnumerable<int> bars) => bars.SelectMany(b => Notes(b).Where(n => n.MidiValue is 36 or 38 && !n.Ghost && !n.IsGraceNote)).Select(n => (double)n.Velocity).DefaultIfEmpty(0).Average();
        var intro = Avg(Enumerable.Range(4, 6)); var verse = Avg(Enumerable.Range(19, 13)); var interlude = Avg(Enumerable.Range(79, 4)); var finale = Avg(Enumerable.Range(119, 16));
        Cover("N36 dynamics arc: 808 intro and interlude soft, verse strong, final chorus strongest", intro < verse - 15 && interlude < verse - 15 && finale > verse,
            $"kick/snare mean: intro {intro:0}, verse {verse:0}, interlude {interlude:0}, final {finale:0}");
        var again = FullDemoSongFactory.Create().Tracks.First(t => t.Name == FullDemoSongFactory.Drums);
        Cover("humanisation is deterministic (a second build has the same drum velocities)",
            d.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes).Select(n => n.Velocity)
             .SequenceEqual(again.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes).Select(n => n.Velocity)));
    }

    /// <summary>Coverage of owner (c2): Lead, Lead Harmony, Clean, Pad and Piano (plan 3.5-3.8, 4.1, 4.2).</summary>
    private static void LeadKeysCoverageChecks(SongProject song)
    {
        TrackModel T(string name) => song.Tracks.First(t => t.Name == name);
        var lead = T(FullDemoSongFactory.Lead); var harm = T(FullDemoSongFactory.LeadHarm); var clean = T(FullDemoSongFactory.Clean);
        var pad = T(FullDemoSongFactory.Pad); var piano = T(FullDemoSongFactory.Piano);
        void Cover(string id, bool ok, string? detail = null) => Check($"full demo coverage {id}", ok, detail);
        static List<TabCell> Beats(TrackModel t, int bar, int voice = 0) =>
            (voice == 0 ? t.Measures[bar - 1].Cells : t.Measures[bar - 1].Voice2Cells).Where(c => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation).ToList();
        static List<TabCell> Hits(TrackModel t, int bar, int voice = 0) => Beats(t, bar, voice).Where(c => c.Notes.Any(n => !n.IsGraceNote)).ToList();
        static IEnumerable<TabNote> Notes(TrackModel t, int bar, int voice = 0) => Hits(t, bar, voice).SelectMany(c => c.Notes);
        static IEnumerable<int> R(int a, int b) => Enumerable.Range(a, b - a + 1);
        bool Has(TrackModel t, string tech, params int[] bars) => bars.All(b => Notes(t, b).Concat(Notes(t, b, 1)).Any(n => n.Techniques.Contains(tech)));
        TabNote BendAt(int bar, string type) => Notes(lead, bar).First(n => n.BendTypeName == type);
        static double Onset(TabCell c, List<TabCell> cells) => c.RhythmicPosition ?? cells.IndexOf(c);

        // Harmony intervals, onset by onset: chorus 1 in 3rds (3/4 semitones), chorus 2 and the final chorus in 6ths (8/9).
        List<int> Intervals(int bar)
        {
            var list = new List<int>();
            var lc = lead.Measures[bar - 1].Cells; var hc = harm.Measures[bar - 1].Cells;
            foreach (var h in Hits(harm, bar))
            {
                var l = Hits(lead, bar).FirstOrDefault(c => Math.Abs(Onset(c, lc) - Onset(h, hc)) < 1e-6);
                list.Add(l is null ? -1 : l.Notes.First(n => !n.IsGraceNote).MidiValue - h.Notes.First(n => !n.IsGraceNote).MidiValue);
            }
            return list;
        }
        var thirds = R(40, 47).SelectMany(Intervals).ToList();
        var sixths = R(71, 78).Concat(R(119, 134)).SelectMany(Intervals).ToList();
        Cover("lead harmony: chorus 1 in 3rds, chorus 2 and final chorus in 6ths (every onset)",
            thirds.Count == 27 && thirds.All(i => i is 3 or 4) && sixths.Count == 81 && sixths.All(i => i is 8 or 9),
            $"3rds {string.Join(",", thirds.Distinct())} ({thirds.Count}); 6ths {string.Join(",", sixths.Distinct())} ({sixths.Count})");
        Cover("lead: the hook is E5 D5 C5 A4 over F (40), a whole step up in the final chorus (119)",
            Notes(lead, 40).Select(n => n.MidiValue).SequenceEqual(new[] { 76, 74, 72, 69 }) && Notes(lead, 119).Select(n => n.MidiValue).SequenceEqual(new[] { 78, 76, 74, 71 }));
        Cover("lead: the solo call is motif M (E5 D5 C5 at 102)", Notes(lead, 102).Select(n => n.MidiValue).SequenceEqual(new[] { 76, 74, 72 }));

        Cover("N02 let ring (Clean 2-9)", R(2, 9).All(b => Notes(clean, b).All(n => n.Techniques.Contains("LetRing"))));
        Cover("N03 HOPO origin/destination pairs (Lead 108, 109, 110, 111, 114)", new[] { 108, 109, 110, 111, 114 }.All(b =>
            Notes(lead, b).Count(n => n.Techniques.Contains("HOPOOrigin")) is var o && o > 0 && o == Notes(lead, b).Count(n => n.Techniques.Contains("HOPODestination"))));
        Cover("N04 all bend types (Bend, Release, BendRelease, Prebend, PrebendRelease x2, PrebendBend, Custom, Hold)",
            BendAt(108, "Bend") is not null && BendAt(108, "Release") is not null && BendAt(103, "BendRelease") is not null && BendAt(105, "Prebend") is not null
            && BendAt(104, "PrebendRelease") is not null && BendAt(109, "PrebendRelease") is not null && BendAt(117, "PrebendBend") is not null
            && BendAt(116, "Custom") is not null && BendAt(116, "Hold") is not null && BendAt(103, "BendRelease").BendPoints.Count == 5);
        var slide52 = Notes(lead, 52).Last(); var slide107 = Notes(lead, 107).First(); var grace44 = Beats(lead, 44).SelectMany(c => c.Notes).First(n => n.IsGraceNote);
        Cover("N05 legato slide (Lead 52 → C5 at 53, 107 → G4, grace at 44 → A5)",
            slide52.Techniques.Contains("LegatoSlide") && slide52.SlideTargetMidi == 72 && slide107.SlideTargetMidi == 67
            && grace44.Techniques.Contains("LegatoSlide") && grace44.SlideTargetMidi == 81);
        Cover("N06/N07/N08 shift slide 106, slide in above 71, slide out up 115", Has(lead, "ShiftSlide", 106) && Has(lead, "SlideInAbove", 71) && Has(lead, "SlideOutUp", 115));
        Cover("N10/N11 vibrato 102, wide vibrato 18, 103, 105, 116", Has(lead, "Vibrato", 102) && Has(lead, "WideVibrato", 18, 103, 105, 116));
        Cover("N12 whammy on the Lead: Dip 78, Wide 140, Custom 117 (curve on the beat)",
            Has(lead, "TremBarDip", 78) && Has(lead, "TremBarWide", 140) && Has(lead, "TremBarCustom", 117)
            && Hits(lead, 117).Last().WhammyPoints.Count == 6 && Hits(lead, 140)[0].WhammyPoints.Count == 7);
        Cover("N13 natural harmonics: Clean 5 sounds C5 E5 A5 D5 G5 (capo 5), 9 sounds E5 E6",
            Notes(clean, 5).Select(n => n.MidiValue).SequenceEqual(new[] { 72, 76, 81, 74, 79 }) && Notes(clean, 9).Where(n => n.Techniques.Contains("Harmonic")).Select(n => n.MidiValue).SequenceEqual(new[] { 76, 88 }));
        Cover("N14 artificial harmonics an octave up: Clean 79 sounds A5 G5 F5 A5",
            Notes(clean, 79).All(n => n.Techniques.Contains("ArtificialHarmonic")) && Notes(clean, 79).Select(n => n.MidiValue).SequenceEqual(new[] { 81, 79, 77, 81 }) && Has(clean, "ArtificialHarmonic", 80, 81));
        Cover("N15/N16/N18 pinch (107 G6, 117 E5), tap harmonic (111 G5), feedback (142 B4)",
            Notes(lead, 107).Any(n => n.Techniques.Contains("PinchHarmonic") && n.MidiValue == 91) && Notes(lead, 117).Any(n => n.Techniques.Contains("PinchHarmonic") && n.MidiValue == 76)
            && Notes(lead, 111).Any(n => n.Techniques.Contains("TapHarmonic") && n.MidiValue == 79) && Notes(lead, 142).Single() is { MidiValue: 71 } fb && fb.Techniques.Contains("FeedbackHarmonic"));
        Cover("N19 tapping (110, 111) and left tap (110)", Has(lead, "Tapping", 110, 111) && Has(lead, "LeftTap", 110) && Beats(lead, 110).Count == 24);
        Cover("N22 trill on Lead and Harmony 113 (target, 16ths)", new[] { lead, harm }.All(t => Notes(t, 113).Any(n => n.Techniques.Contains("Trill") && n.TrillTargetMidi > n.MidiValue && n.TrillDurationDenominator == 16)));
        Cover("N23/B16 tremolo picking Lead 67-70 at 8/16/32/64, 112-113 at 32",
            Has(lead, "TremoloPick", 67, 68, 69, 70, 112, 113) && new[] { (67, 8), (68, 16), (69, 32), (70, 64), (112, 32) }.All(x => Hits(lead, x.Item1).All(c => c.TremoloPickDenominator == x.Item2)));
        Cover("N24 fade in: Pad 2, 10, 110; Lead 51, 142", Has(pad, "FadeIn", 2, 10, 110) && Has(lead, "FadeIn", 51, 142));
        Cover("N25 fade out: Pad and Piano 144", Has(pad, "FadeOut", 144) && Has(piano, "FadeOut", 144));
        Cover("N26 wah open / closed (Lead 63-66)", Has(lead, "WahClose", 63, 65) && Has(lead, "WahOpen", 64, 66));
        Cover("N27/B13 brush strums: Clean 40-47 and 71-78 at 0.33, 119-134 at 0.5",
            R(40, 47).Concat(R(71, 78)).All(b => Has(clean, "BrushDown", b) && Has(clean, "BrushUp", b) && Hits(clean, b).All(c => Math.Abs(c.BrushStepSlots - 0.33) < 1e-9))
            && R(119, 134).All(b => Has(clean, "BrushUp", b) && Hits(clean, b).All(c => Math.Abs(c.BrushStepSlots - 0.5) < 1e-9)));
        Cover("N28/N29 arpeggio up (Clean 9, Piano 82), down (Clean 82), rasgueado (Clean 82)",
            Has(clean, "ArpeggioUp", 9) && Has(piano, "ArpeggioDown", 82) && Has(clean, "ArpeggioDown", 82) && Has(clean, "Rasgueado", 82));
        var g123 = Beats(lead, 123).SelectMany(c => c.Notes).FirstOrDefault(n => n.IsGraceNote);
        var g75 = Beats(lead, 75).SelectMany(c => c.Notes).FirstOrDefault(n => n.IsGraceNote);
        Cover("N33 grace before (44), on the beat (123), grace bend (75)",
            grace44.GraceBeforeBeat && grace44.Techniques.Contains("GraceBefore") && g123 is { GraceBeforeBeat: false } && g123.Techniques.Contains("GraceOnBeat")
            && g75 is not null && g75.Techniques.Contains("GraceBend") && g75.BendPoints.Count > 0);
        Cover("N34 ties: Lead 108.4 and 116.3, Clean voice 2 across 9-10",
            Hits(lead, 108).Last().IsTied && Hits(lead, 116).Last().IsTied && Hits(clean, 10, 1).Single().IsTied && Notes(clean, 9, 1).Single().Techniques.Contains("Tie"));
        Cover("N35 fingering Clean 2-3 (left and right hand)",
            Notes(clean, 2).Concat(Notes(clean, 2, 1)).All(n => n.RightHandFinger is not null) && Notes(clean, 2, 1).Any(n => n.LeftHandFinger == 1) && Notes(clean, 3, 1).Any(n => n.LeftHandFinger == 3));
        Cover("N36/B28 dynamics: Pad 2 ppp, Pad 6 pp, Clean 1 p, Clean 4 mp (echoes p), Piano 6 mp, Clean 6 mf, Lead 40 f, Lead 71 ff, Piano 91 fff",
            Notes(pad, 2).All(n => n.Velocity == 16) && Notes(pad, 6).All(n => n.Velocity == 33) && Notes(clean, 1).All(n => n.Velocity == 49)
            && Notes(clean, 4).All(n => n.Velocity == (n.Ghost ? 49 : 64)) && Notes(piano, 6).All(n => n.Velocity == 64) && Notes(clean, 6).All(n => n.Velocity == 80)
            && Notes(lead, 40).All(n => n.Velocity == 95) && Notes(lead, 71).All(n => n.Velocity == 112) && Notes(piano, 91).All(n => n.Velocity == 127));
        Cover("N31 clean echo grid: 8 ghosted echoes in bars 4 and 8", Notes(clean, 4).Count(n => n.Ghost) == 8 && Notes(clean, 8).Count(n => n.Ghost) == 8);

        Cover("B03 double-dotted halves in the hook (43, 74, 122)", new[] { 43, 74, 122 }.All(b => Hits(lead, b)[0].Dots == 2));
        Cover("B04 triplets (Lead 106 x9, 110 x24, 111 x12)", Hits(lead, 106).Count(c => c.IsTriplet) == 9 && Hits(lead, 110).Count(c => c.IsTriplet) == 24 && Hits(lead, 111).Count(c => c.IsTriplet) == 12);
        Cover("B05 lead tuplets 5:4 (108), 7:4 (109), 6:4 (114, 115), 9:8 (114), 13:8 (115)",
            Hits(lead, 108).Count(c => c.Tuplet == (5, 4)) == 10 && Hits(lead, 109).Count(c => c.Tuplet == (7, 4)) == 7 && Hits(lead, 114).Count(c => c.Tuplet == (6, 4)) == 12
            && Hits(lead, 114).Count(c => c.Tuplet == (9, 8)) == 9 && Hits(lead, 115).Count(c => c.Tuplet == (13, 8)) == 13 && Hits(lead, 115).Count(c => c.Tuplet == (6, 4)) == 12);
        Cover("B31 off-grid onsets in the solo tuplets", new[] { 108, 109, 110, 114, 115 }.All(b => Hits(lead, b).Any(c => c.RhythmicPosition is { } p && Math.Abs(p - Math.Round(p)) > 1e-6)));
        Cover("B08 fermatas on 141 (Lead, Harmony, Pad, Piano both hands) and the Pad rest on 142",
            new[] { lead, harm, pad, piano }.All(t => Hits(t, 141)[0].Fermata) && Hits(piano, 141, 1)[0].Fermata && Beats(pad, 142).Single() is { IsRest: true, Fermata: true });
        Cover("B11 piano staccato stabs 79-81", R(79, 81).All(b => Hits(piano, b).Count == 2 && Hits(piano, b).All(c => c.Staccato)));
        Cover("B12 tenuto: Pad whole notes, Lead 41.1, 51, 139", Hits(pad, 2)[0].Tenuto && Hits(pad, 40)[0].Tenuto && Hits(lead, 41)[0].Tenuto && Hits(lead, 51)[0].Tenuto && Hits(lead, 139)[0].Tenuto);
        string? Text(TrackModel t, int bar) => Beats(t, bar).FirstOrDefault(c => !string.IsNullOrEmpty(c.Text))?.Text;
        Cover("B17 beat text: Clean 2, Lead 91, 110, 112, 117, 127, 142",
            Text(clean, 2) == "let ring, fingerstyle" && Text(lead, 91) == "gang vocal" && Text(lead, 110) == "tap" && Text(lead, 112) == "harmony"
            && Text(lead, 117) == "dive!" && Text(lead, 127) == "octave pedal +1 oct" && Text(lead, 142) == "(feedback)");
        var chords = R(2, 10).Concat(R(40, 47)).Concat(R(71, 78)).Concat(R(79, 82)).Concat(R(119, 134)).Select(b => Beats(clean, b)[0].ChordName).ToList();
        Cover("B18 chord names on Clean 2-10, 40-47, 71-78, 79-82, 119-134", chords.All(c => !string.IsNullOrEmpty(c)) && chords[0] == "Am(add9)" && chords[^1] == "F#",
            string.Join(" ", chords.Take(12)));
        Cover("B20 lyrics: one syllable per Lead note in 40-47, 71-78, 119-126; two lines in 127-134",
            R(40, 47).Concat(R(71, 78)).Concat(R(119, 126)).All(b => Hits(lead, b).All(c => c.Lyrics.Length > 0 && !c.Lyrics.Contains('\n')))
            && R(127, 134).All(b => Hits(lead, b).All(c => c.Lyrics.Split('\n').Length == 2)) && Hits(lead, 40)[0].Lyrics == "Hold" && Hits(lead, 47)[0].Lyrics == "mer-"
            && Hits(lead, 127)[0].Lyrics == "Hold\nSing");
        Cover("B23 voice 2: Clean 2-10, Piano 6-9, 79-82, 119-134, 141, 144",
            R(2, 10).All(b => Hits(clean, b, 1).Count > 0) && R(6, 9).Concat(R(79, 82)).Concat(R(119, 134)).Concat(new[] { 141, 144 }).All(b => Hits(piano, b, 1).Count > 0));
        Cover("B25 octave shifts: Lead 127-134 +12, Piano 6-9 +12, Piano LH 119-134 -12, Piano 135-138 +24, Piano 91/95 -24 (not folded into the pitch)",
            R(127, 134).All(b => Beats(lead, b).All(c => c.OctaveShiftSemitones == 12)) && R(6, 9).All(b => Beats(piano, b).All(c => c.OctaveShiftSemitones == 12))
            && R(119, 134).All(b => Beats(piano, b, 1).All(c => c.OctaveShiftSemitones == -12)) && R(135, 138).All(b => Beats(piano, b).All(c => c.OctaveShiftSemitones == 24))
            && new[] { 91, 95 }.All(b => Hits(piano, b).Single().OctaveShiftSemitones == -24)
            && Notes(lead, 127).Select(n => n.MidiValue).SequenceEqual(Notes(lead, 119).Select(n => n.MidiValue)));
        Cover("B26 stems: Clean voice 1 up and voice 2 down (2-9), Piano 81 inverted",
            R(2, 9).All(b => Beats(clean, b).All(c => c.StemDirection == StemDirection.Up) && Beats(clean, b, 1).All(c => c.StemDirection == StemDirection.Down))
            && Hits(piano, 81)[1].StemDirection == StemDirection.Invert);
        Cover("B32 annotation-only beat: Lead 91 \"RISE!\"", Beats(lead, 91).Single() is { IsRest: false, Lyrics: "RISE!" } c91 && c91.Notes.Count == 0);
        Cover("S06 Pad alto-register line 135-140 (single notes)", R(135, 140).All(b => Hits(pad, b).All(c => c.Notes.Count == 1)));
    }

    /// <summary>Every technique name used in the song, including the flag-backed ones and the LegatoSlide aliases (plan 4.5).</summary>
    private static HashSet<string> FdAllTechniques(SongProject song)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cell, _) in FdCells(song))
        {
            if (cell.Accent == 1) set.Add("Accent");
            if (cell.Accent == 2) set.Add("HeavyAccent");
            if (cell.Staccato) set.Add("Staccato");
            if (cell.Tenuto) set.Add("Tenuto");
            if (cell.Fermata) set.Add("Fermata");
            foreach (var n in cell.Notes)
            {
                set.UnionWith(n.Techniques);
                if (n.Ghost) set.Add("Ghost");
                if (n.Dead) set.Add("Dead");
                if (n.Techniques.Contains("LegatoSlide")) { set.Add("Slide"); set.Add("Legato"); }
            }
        }
        return set;
    }

    /// <summary>Every cell of the song (both voices) with its track.</summary>
    private static IEnumerable<(TabCell Cell, TrackModel Track)> FdCells(SongProject song) =>
        song.Tracks.SelectMany(t => t.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).Select(c => (c, t)));
}
