using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Audit 3 section 6b: playback behaviours that were measured against the reference's own MIDI export. Each test compiles
/// a tiny project (120 BPM: a sixteenth slot is 125 ms, a beat 500 ms) and asserts the MIDI events.
/// </summary>
public static partial class SelfTest
{
    private static int PitchWheelValue(ScoreEvent e) => e.Data1 | (e.Data2 << 7);

    private static void TestPlaybackDifferences()
    {
        // P-08: the written tremolo speed is the repeat rate (importer: Guitar Pro 3-5 marks, alphaTab's speed name is one step slow).
        var tremolo = SingleTrack();
        var tremoloCell = Beat(tremolo, 0, 0, 0, 4, 60);
        tremoloCell.Notes[0].Techniques.Add("TremoloPick");
        tremoloCell.TremoloPickDenominator = 16;
        var tremoloOns = MidiTimelineBuilder.Build(tremolo, new PlaybackOptions()).Events.Where(e => e.IsNoteOn).OrderBy(e => e.TimeMs).ToList();
        Check("P-08: tremolo 1/16 on a quarter repeats sixteenths", tremoloOns.Count == 4 && Math.Abs(tremoloOns[1].TimeMs - tremoloOns[0].TimeMs - 125) < 1,
            $"attacks={tremoloOns.Count}");
        Check("P-08: Guitar Pro 3-5 marks 1/2/3 read as 1/8, 1/16, 1/32",
            GuitarProImporter.TremoloDenominator(new { TremoloPicking = new { Marks = 1.0 } }, true) == 8 &&
            GuitarProImporter.TremoloDenominator(new { TremoloPicking = new { Marks = 2.0 }, TremoloSpeed = "Eighth" }, true) == 16 &&
            GuitarProImporter.TremoloDenominator(new { TremoloPicking = new { Marks = 3.0 } }, true) == 32 &&
            GuitarProImporter.TremoloDenominator(new { TremoloSpeed = "Sixteenth" }, false) == 16);

        // P-11: a down-stroke plays the low string first, the spread follows the written stroke speed.
        foreach (var upStroke in new[] { false, true })
        {
            var strum = SingleTrack();
            var chord = Beat(strum, 0, 0, 0, 4, 64, 0);
            chord.Notes.Add(new TabNote { StringIndex = 1, MidiValue = 59 });
            chord.Notes.Add(new TabNote { StringIndex = 2, MidiValue = 55 });
            foreach (var n in chord.Notes) n.Techniques.Add(upStroke ? "BrushUp" : "BrushDown");
            chord.BrushStepSlots = 1.0 / 6;   // a 1/16 stroke: 40 ticks per string
            var ons = MidiTimelineBuilder.Build(strum, new PlaybackOptions()).Events.Where(e => e.IsNoteOn).OrderBy(e => e.TimeMs).ToList();
            var expected = upStroke ? new[] { 64, 59, 55 } : new[] { 55, 59, 64 };
            Check($"P-11: {(upStroke ? "up" : "down")}-stroke order", ons.Select(e => e.Data1).SequenceEqual(expected), string.Join(",", ons.Select(e => e.Data1)));
            Check($"P-11: {(upStroke ? "up" : "down")}-stroke spread follows the stroke speed",
                ons.Count == 3 && Math.Abs(ons[1].TimeMs - ons[0].TimeMs - 125.0 / 6) < 0.5 && Math.Abs(ons[2].TimeMs - ons[0].TimeMs - 125.0 / 3) < 0.5,
                string.Join(",", ons.Select(e => e.TimeMs.ToString("0.#"))));
        }

        // P-10: a grace note before the beat comes out of the previous beat; the principal note stays on the beat.
        foreach (var beforeBeat in new[] { true, false })
        {
            var grace = SingleTrack();
            Beat(grace, 0, 0, 0, 4, 60);
            var target = Beat(grace, 0, 0, 4, 4, 64);
            target.Notes.Add(new TabNote { StringIndex = 1, MidiValue = 62, IsGraceNote = true, GraceBeforeBeat = beforeBeat, GraceDurationSlots = 1 });
            var tl = MidiTimelineBuilder.Build(grace, new PlaybackOptions());
            var principal = tl.Notes.First(n => n.Midi == 64);
            var ornament = tl.Notes.First(n => n.Midi == 62);
            var previous = tl.Notes.First(n => n.Midi == 60);
            if (beforeBeat)
                Check("P-10: grace before the beat", Math.Abs(principal.OnsetMs - 500) < 0.5 && Math.Abs(ornament.OnsetMs - 437.5) < 0.5 &&
                    previous.EndMs <= ornament.OnsetMs + 0.5, $"principal {principal.OnsetMs:0.#}, grace {ornament.OnsetMs:0.#}, previous ends {previous.EndMs:0.#}");
            else
                Check("P-10: grace on the beat takes an eighth of a beat from the principal note",
                    Math.Abs(ornament.OnsetMs - 500) < 0.5 && Math.Abs(principal.OnsetMs - 562.5) < 0.5, $"grace {ornament.OnsetMs:0.#}, principal {principal.OnsetMs:0.#}");
        }

        // P-02: a plain bend reaches its target by the middle of the note and holds it.
        var bend = SingleTrack();
        var bendNote = Beat(bend, 0, 0, 0, 4, 69).Notes[0];
        bendNote.Techniques.Add("Bend");
        bendNote.BendTypeName = "Bend";
        bendNote.BendPoints.Add(new BendPointModel { Offset = 0, Value = 0 });
        bendNote.BendPoints.Add(new BendPointModel { Offset = 60, Value = 4 });   // a full step = 2 semitones
        var bendWheel = MidiTimelineBuilder.Build(bend, new PlaybackOptions()).Events.Where(e => (e.Status & 0xF0) == 0xE0 && !e.IsSetup).ToList();
        var fullAt250 = bendWheel.Where(e => Math.Abs(e.TimeMs - 250) < 1).Select(PitchWheelValue).DefaultIfEmpty(-1).Last();
        Check("P-02: full bend is at its target at the bend point (50% of the note)", Math.Abs(fullAt250 - (8192 + 2 * 8192 / 12.0)) < 8, $"wheel {fullAt250}");
        Check("P-02: the bend holds the target after the bend point",
            bendWheel.Where(e => e.TimeMs > 260 && e.TimeMs < 490).All(e => PitchWheelValue(e) >= fullAt250 - 2));

        // P-05: a legato slide is one pick; the target note is not attacked again.
        var legato = SingleTrack();
        var slideNote = Beat(legato, 0, 0, 0, 4, 60).Notes[0];
        slideNote.Techniques.Add("LegatoSlide");
        slideNote.SlideTargetMidi = 64;
        Beat(legato, 0, 0, 4, 4, 64);
        var legatoTl = MidiTimelineBuilder.Build(legato, new PlaybackOptions());
        Check("P-05: legato slide does not re-attack the target", legatoTl.Events.Count(e => e.IsNoteOn) == 1 &&
            legatoTl.Events.Any(e => (e.Status & 0xF0) == 0xE0 && !e.IsSetup && PitchWheelValue(e) > 8192 + 4 * 8192 / 12 - 8),
            $"note-ons {legatoTl.Events.Count(e => e.IsNoteOn)}");
        var shift = SingleTrack();
        var shiftNote = Beat(shift, 0, 0, 0, 4, 60).Notes[0];
        shiftNote.Techniques.Add("ShiftSlide");
        shiftNote.SlideTargetMidi = 64;
        Beat(shift, 0, 0, 4, 4, 64);
        Check("P-05: shift slide still picks the target", MidiTimelineBuilder.Build(shift, new PlaybackOptions()).Events.Count(e => e.IsNoteOn) == 2);

        // P-07: notes of a let-ring run ring together until the run ends.
        var ring = SingleTrack();
        for (var i = 0; i < 4; i++) Beat(ring, 0, 0, i * 2, 8, 60 + i, i).Notes[0].Techniques.Add("LetRing");
        var ringTl = MidiTimelineBuilder.Build(ring, new PlaybackOptions());
        Check("P-07: let-ring run rings together until the group ends", ringTl.Notes.Count == 4 && ringTl.Notes.All(n => Math.Abs(n.EndMs - 1000) < 15),
            string.Join(",", ringTl.Notes.Select(n => n.EndMs.ToString("0"))));

        // P-12: harmonic pitches.
        Check("P-12: artificial harmonic plays the written interval, tapped harmonic tapped fret + octave, natural from the table",
            GuitarProImporter.HarmonicMidi("Artificial", 55, 5, 16) == 76 && GuitarProImporter.HarmonicMidi("Artificial", 55, 5, 12) == 72 &&
            GuitarProImporter.HarmonicMidi("Tap", 55, 5, 17) == 84 && GuitarProImporter.HarmonicMidi("Natural", 55, 12, 12) == 67);
        var semi = SingleTrack();
        var semiNote = Beat(semi, 0, 0, 0, 4, 72, 0, 5).Notes[0];
        semiNote.Techniques.Add("SemiHarmonic");
        semi.Tracks[0].StringTunings = new List<int> { 55, 50, 45, 40 };
        var semiOn = MidiTimelineBuilder.Build(semi, new PlaybackOptions()).Events.Where(e => e.IsNoteOn).Select(e => e.Data1).OrderBy(x => x).ToList();
        Check("P-12: semi harmonic sounds the fundamental and the harmonic", semiOn.SequenceEqual(new[] { 60, 72 }), string.Join(",", semiOn));

        // P-14: the note duration percentage shortens the sounding length.
        var half = SingleTrack();
        Beat(half, 0, 0, 0, 4, 60).SoundDurationPercent = 50;
        Near("P-14: 50% note duration plays half length", 250, MidiTimelineBuilder.Build(half, new PlaybackOptions()).Notes[0].DurationMs, 2);

        // P-03: reference vibrato depths (compared with Guitar Pro 5 files) (note +/-0.38 semitone, wide +/-0.94).
        foreach (var (name, technique, depth) in new[] { ("vibrato", "Vibrato", 0.38), ("wide vibrato", "WideVibrato", 0.94) })
        {
            var vib = SingleTrack();
            Beat(vib, 0, 0, 0, 2, 60).Notes[0].Techniques.Add(technique);
            var peak = MidiTimelineBuilder.Build(vib, new PlaybackOptions()).Events.Where(e => (e.Status & 0xF0) == 0xE0 && !e.IsSetup)
                .Select(e => Math.Abs(PitchWheelValue(e) - 8192) / (8192.0 / 12)).DefaultIfEmpty(0).Max();
            Check($"P-03: {name} depth", peak > depth * 0.85 && peak <= depth + 0.02, $"peak {peak:0.###} semitones");
        }

        // P-09: a trill alternates at the written speed.
        var trill = SingleTrack();
        var trillCell = Beat(trill, 0, 0, 0, 2, 60);
        trillCell.Notes[0].Techniques.Add("Trill");
        trillCell.Notes[0].TrillTargetMidi = 62;
        trillCell.Notes[0].TrillDurationDenominator = 16;
        var trillOns = MidiTimelineBuilder.Build(trill, new PlaybackOptions()).Events.Where(e => e.IsNoteOn).OrderBy(e => e.TimeMs).ToList();
        Check("P-09: trill 1/16 alternates every sixteenth", trillOns.Count == 8 && trillOns.Zip(trillOns.Skip(1), (a, b) => b.TimeMs - a.TimeMs).All(gap => Math.Abs(gap - 125) < 1),
            string.Join(",", trillOns.Select(e => e.TimeMs.ToString("0"))));

        // P-04 / P-15: hammer-on and ghost at 80%, dead notes full velocity but very short.
        var feel = SingleTrack();
        var hammer = Beat(feel, 0, 0, 0, 4, 60); hammer.Notes[0].Techniques.Add("HOPODestination");
        Beat(feel, 0, 0, 4, 4, 62).Notes[0].Ghost = true;
        var dead = Beat(feel, 0, 0, 8, 4, 64); dead.Notes[0].Dead = true;
        var feelTl = MidiTimelineBuilder.Build(feel, new PlaybackOptions());
        var feelOns = feelTl.Events.Where(e => e.IsNoteOn).OrderBy(e => e.TimeMs).ToList();
        Check("P-04: hammered-on note is softer", feelOns[0].Data2 == 80, $"{feelOns[0].Data2}");
        Check("P-15: ghost note at 80%", feelOns[1].Data2 == 80, $"{feelOns[1].Data2}");
        Check("P-15: dead note keeps full velocity and is very short", feelOns[2].Data2 == Dynamics.Clamp(100) && feelTl.Notes.First(n => n.Dead).DurationMs <= 25,
            $"vel {feelOns[2].Data2}, {feelTl.Notes.First(n => n.Dead).DurationMs:0.#} ms");

        // P-17: fade-in uses expression (CC11), and every channel starts (and restarts after a seek) with it back at full.
        var fade = SingleTrack();
        Beat(fade, 0, 0, 0, 4, 60).Notes[0].Techniques.Add("FadeIn");
        var fadeTl = MidiTimelineBuilder.Build(fade, new PlaybackOptions());
        Check("P-17: fade-in ramps CC11 and ends at full", fadeTl.Events.Any(e => !e.IsSetup && (e.Status & 0xF0) == 0xB0 && e.Data1 == 11 && e.Data2 < 40) &&
            fadeTl.Events.Last(e => !e.IsSetup && (e.Status & 0xF0) == 0xB0 && e.Data1 == 11).Data2 == 127);
        Check("P-17: channel setup resets CC11", fadeTl.ChannelSetup.Any(e => (e.Status & 0xF0) == 0xB0 && e.Data1 == 11 && e.Data2 == 127));

        // P-16: the exported tempo map and the note ticks follow the bar's mid-bar tempo.
        var ramp = SingleTrack();
        ramp.Tracks[0].Measures[0].MidBarTempos = new List<TempoPoint> { new(8, 240) };
        var rampTl = MidiTimelineBuilder.Build(ramp, new PlaybackOptions());
        var rampMap = new MidiExportService.TickMap(rampTl, ramp);
        Check("P-16: mid-bar tempo maps notes to the right ticks", rampMap.TickOf(1000) == 960 && rampMap.TickOf(1250) == 1440,
            $"{rampMap.TickOf(1000)}, {rampMap.TickOf(1250)}");

        // P-06: a slide in starts before the beat and reaches the written pitch on the beat.
        var slideIn = SingleTrack();
        Beat(slideIn, 0, 0, 0, 4, 60);
        Beat(slideIn, 0, 0, 4, 4, 64).Notes[0].Techniques.Add("SlideInBelow");
        var slideInTl = MidiTimelineBuilder.Build(slideIn, new PlaybackOptions());
        var slideInOn = slideInTl.Events.Where(e => e.IsNoteOn && e.Data1 == 64).First();
        var slideInWheel = slideInTl.Events.Where(e => (e.Status & 0xF0) == 0xE0 && !e.IsSetup).ToList();
        Check("P-06: slide in attacks before the beat, below the pitch, and arrives on the beat",
            slideInOn.TimeMs < 490 && slideInOn.TimeMs > 300 &&
            slideInWheel.Any(e => Math.Abs(e.TimeMs - slideInOn.TimeMs) < 1 && PitchWheelValue(e) < 8192 - 1000) &&
            slideInWheel.Any(e => Math.Abs(e.TimeMs - 500) < 2 && Math.Abs(PitchWheelValue(e) - 8192) < 40),
            $"attack {slideInOn.TimeMs:0.#}");

        // P-10 leftovers: grace transitions and dead graces.
        var graceSlide = SingleTrack();
        Beat(graceSlide, 0, 0, 0, 4, 60);
        var slideTarget = Beat(graceSlide, 0, 0, 4, 4, 64);
        var slideGrace = new TabNote { StringIndex = 0, MidiValue = 62, IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1 };
        slideGrace.Techniques.Add("Slide");
        slideTarget.Notes.Add(slideGrace);
        var graceSlideTl = MidiTimelineBuilder.Build(graceSlide, new PlaybackOptions());
        Check("P-10: a grace note with a slide moves to the principal pitch",
            graceSlideTl.Events.Any(e => (e.Status & 0xF0) == 0xE0 && !e.IsSetup && e.TimeMs > 437 && e.TimeMs < 500 && PitchWheelValue(e) > 8192 + 400));
        var deadGrace = SingleTrack();
        var deadTarget = Beat(deadGrace, 0, 0, 4, 4, 64);
        deadTarget.Notes.Add(new TabNote { StringIndex = 1, MidiValue = 62, IsGraceNote = true, GraceBeforeBeat = false, Dead = true, GraceDurationSlots = 1 });
        var deadGraceTl = MidiTimelineBuilder.Build(deadGrace, new PlaybackOptions());
        Check("P-10: a dead grace note does not delay the principal note", Math.Abs(deadGraceTl.Notes.First(n => n.Midi == 64).OnsetMs - 500) < 0.5);
        var hammerGrace = SingleTrack();
        var hammerTarget = Beat(hammerGrace, 0, 0, 4, 4, 64);
        var hammerNote = new TabNote { StringIndex = 1, MidiValue = 62, IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1 };
        hammerNote.Techniques.Add("HOPO");
        hammerTarget.Notes.Add(hammerNote);
        Check("P-10: a hammer-on grace plays the principal note softer",
            MidiTimelineBuilder.Build(hammerGrace, new PlaybackOptions()).Events.First(e => e.IsNoteOn && e.Data1 == 64).Data2 == 80);

        // Effect-channel hook (used by the engine later): bent and whammied notes are marked.
        var marked = SingleTrack();
        Beat(marked, 0, 0, 0, 4, 60).Notes[0].BendPoints.Add(new BendPointModel { Offset = 60, Value = 4 });
        Beat(marked, 0, 0, 4, 4, 62);
        Beat(marked, 0, 0, 8, 4, 64).WhammyPoints.Add(new BendPointModel { Offset = 30, Value = -4 });
        var markedTl = MidiTimelineBuilder.Build(marked, new PlaybackOptions()).Notes.OrderBy(n => n.OnsetMs).ToList();
        Check("UsesEffectChannel marks bent and whammied notes only", markedTl[0].UsesEffectChannel && !markedTl[1].UsesEffectChannel && markedTl[2].UsesEffectChannel);

        // V-26: a 9:8 tuplet (thirty-seconds on whole file ticks) fills a beat and does not mark the bar overfull.
        var nine = SingleTrack();
        var position = 0.0;
        for (var i = 0; i < 9; i++)
        {
            var cell = nine.Tracks[0].Measures[0].Cells[i];
            cell.DurationDenominator = 32; cell.TupletNumerator = 9; cell.TupletDenominator = 8;
            cell.Notes.Add(new TabNote { StringIndex = 0, MidiValue = 60 });
            cell.RhythmicPosition = Math.Round(position * 240) / 240.0;   // as read from the file: whole ticks
            position += MusicTime.CellSlots(cell);
        }
        for (var i = 0; i < 3; i++)
        {
            var cell = nine.Tracks[0].Measures[0].Cells[9 + i];
            cell.DurationDenominator = 4; cell.RhythmicPosition = 4 + i * 4;
            cell.Notes.Add(new TabNote { StringIndex = 0, MidiValue = 60 });
        }
        var nineState = MusicTime.AnalyzeBar(nine, 0);
        Near("V-26: nine 9:8 thirty-seconds span exactly one beat", 4.0, position, 0.0001);
        Check("V-26: a bar with a 9:8 tuplet is complete", nineState.Complete && !nineState.Error, $"used {nineState.Used:0.###}");

        // V-19: fingering values persist in .tforge and old files (without the fields) still load.
        var fingered = SingleTrack();
        var fingerNote = Beat(fingered, 0, 0, 0, 4, 60).Notes[0];
        fingerNote.LeftHandFinger = 2; fingerNote.RightHandFinger = 3;
        var clone = fingerNote.Clone();
        Check("V-19: fingering survives clone", clone.LeftHandFinger == 2 && clone.RightHandFinger == 3);
        var json = System.Text.Json.JsonSerializer.Serialize(fingerNote);
        var back = System.Text.Json.JsonSerializer.Deserialize<TabNote>(json)!;
        var plain = System.Text.Json.JsonSerializer.Serialize(new TabNote());
        var old = System.Text.Json.JsonSerializer.Deserialize<TabNote>("{\"StringIndex\":1,\"Fret\":3}")!;
        Check("V-19: fingering persists, absent fields default to none, unset fingering is not written",
            back.LeftHandFinger == 2 && back.RightHandFinger == 3 && old.LeftHandFinger is null && old.RightHandFinger is null &&
            !plain.Contains("HandFinger"));
        Check("V-19: importer maps alphaTab fingers", GuitarProImporter.FingerOf("Thumb") == 0 && GuitarProImporter.FingerOf("AnnularFinger") == 3 &&
            GuitarProImporter.FingerOf("Unknown") is null && GuitarProImporter.FingerOf("NoOrDead") is null && GuitarProImporter.FingerOf(null) is null);
    }
}
