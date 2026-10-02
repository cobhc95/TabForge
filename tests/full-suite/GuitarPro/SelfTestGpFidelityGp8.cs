namespace TabForge;

// The "Guitar Pro 8 result" column of the capability record (--gp-capability), kept next to the cases so a regenerated record keeps it.
// Evidence, all from the night-2026-10-02 desktop session (Guitar Pro 8.1.5 trial):
//   * the 15 clean fixture songs (gp-fixtures) were opened in Guitar Pro 8, checked visually by the orchestrator, and saved from Guitar Pro 8;
//   * `TabForge.exe --gp-compare <original> <gp8 re-save>` reads both through TabForge's importer and compares every round-trip fact
//     (pitch, rhythm, techniques, bends, whammy, fingering, mixer, tempo, repeats ... ~900 facts per song, no tolerance): 0 differences in 14 songs,
//     2 in song 05 (the ghost note, see A54);
//   * round 2 (second desktop session): the one-field files A08, A11, A31, A32, A39 and A40, the probe ghost-combos and the exact-volume songs 01, 05 and 15 were re-saved
//     in Guitar Pro 8 and compared the same way: 0 differences everywhere except ghost-combos (the finding of A54);
//   * the gpif of the re-saves and of the 28 real Guitar Pro 6-8 files in the Tabs folder were searched for the elements a row is about.
// The one-field files (gp-capability\A01.gp ...) were not opened in Guitar Pro 8 unless a row says so (round 2: A08, A11, A31, A32, A39, A40); a row whose field is in none of the files says so.
// "visual" = what only a person looking at Guitar Pro 8 can confirm; those stay with the orchestrator.
public static partial class SelfTest
{
    internal enum Gp8Verdict { Pass, Changed, NotCovered, NotApplicable, EvidenceOnly }

    internal static readonly Dictionary<string, (Gp8Verdict Verdict, string Short, string Detail)> Gp8Results = new()
    {
        ["A01"] = (Gp8Verdict.Pass, "pass (8 steps); per-note velocity unknown", "447 notes of the 15 songs: every beat dynamic and note velocity identical after the re-save (0 changed); all eight dynamics (ppp..fff) appear in the re-saved files. The snapping to eight steps happens in TabForge's export, Guitar Pro 8 neither adds nor removes it. New: a real Guitar Pro 7 file has a note-level <Velocity> (see the cause); not tried in Guitar Pro 8."),
        ["A02"] = (Gp8Verdict.Pass, "pass (same as export); per-note velocity unknown", "04-drums has a chord of three hits at 112/64/127: after the re-save it reads exactly as before (one dynamic for the beat), no <Dynamic> appears on any re-saved Note. Whether Guitar Pro 8 can keep a per-note velocity is open (cause)."),
        ["A03"] = (Gp8Verdict.Pass, "pass for step values; exact floats not yet tried", "The 15 songs were written BEFORE the exact-volume patch (the quantised volume 0.75 = 96 for volume 100, pan 1.0 for pan 127), and Guitar Pro 8 kept all 27 tracks' volume and pan (0 of 27 changed; mute and solo of song 15 too; visual: mixer shown correctly). The exact floats the patch writes (0.78125 = 100) are in desk\\songs-v2: re-save them in Guitar Pro 8 to confirm a non-step float survives."),
        ["A04"] = (Gp8Verdict.Pass, "pass for step values; exact floats not yet tried", "As A03 (song 15: the centre 0.5 and the hard right 1.0 kept; the exact 0.9921875 for pan 127 is in songs-v2)."),
        ["A05"] = (Gp8Verdict.Pass, "pass up to 4 points; 5 points not tried", "14 bends of songs 10 and 12 (none above four points): 0 changed. The 8 bends Guitar Pro 8 wrote use only the four-point property set, as do all 438 of the real files (cause). Drawing a 5-point curve in Guitar Pro 8's editor: not tried."),
        ["A06"] = (Gp8Verdict.Pass, "pass up to 4 points; 7 points not tried", "17 whammy beats of songs 10 and 12: 0 changed; Guitar Pro 8's own <Whammy> has the seven attributes only (cause). A 7-point curve in its editor: not tried."),
        ["A07"] = (Gp8Verdict.Pass, "pass (visual: finger 2 shown)", "Song 14: left finger 2 kept (1 fact, 0 changed); Guitar Pro 8 re-wrote it as <LeftFingering>. Visual by the orchestrator: the \"2\" is shown."),
        ["A08"] = (Gp8Verdict.Pass, "pass (round 2: 0 differences)", "Round 2: the one-field file gp-capability\\A08.gp (a right-hand finger) was re-saved in Guitar Pro 8: 95 facts compared, 0 changed. The visual check of the drawn finger stays with the orchestrator."),
        ["A09"] = (Gp8Verdict.Pass, "pass (visual: tenuto line shown)", "3 tenuto beats in songs 10 and 14: 0 changed. Visual by the orchestrator: the tenuto line is shown."),
        ["A10"] = (Gp8Verdict.Pass, "pass (as A09)", "The tag name is not a fact of its own; see A09."),
        ["A11"] = (Gp8Verdict.Pass, "pass (round 2: 0 differences; nothing written to lose)", "Round 2: gp-capability\\A11.gp (a beat mix change) re-saved in Guitar Pro 8: 131 facts compared, 0 changed. The export does not write a beat mix change at all (alphaTab's writer has no automation for it, and the preflight lists it), so there was nothing for Guitar Pro 8 to lose; whether Guitar Pro 8 itself stores a beat volume / pan / instrument change is still open."),
        ["A12"] = (Gp8Verdict.Pass, "pass (visual: P.M. shown)", "Songs 10 and 14: 3 palm-muted notes, one of them a single tone of a chord: 0 changed. Visual by the orchestrator: P.M. is shown."),
        ["A13"] = (Gp8Verdict.Pass, "pass for one track; two tracks not tried", "3 fermatas of songs 07 and 10 (single track): 0 changed; the fermata is a <MasterBar> element in the original and the re-save. A fermata on one of two tracks: not in the songs."),
        ["A14"] = (Gp8Verdict.Pass, "pass", "Channel facts of 27 tracks identical (the channel is assigned by the importer in track order)."),
        ["A15"] = (Gp8Verdict.Pass, "pass", "Song 10: 6 slide targets, 0 changed."),
        ["A16"] = (Gp8Verdict.Pass, "pass", "Song 10: 2 trill targets, 0 changed."),
        ["A17"] = (Gp8Verdict.Pass, "pass; the speed is written now (patch 0003)", "Song 10's trills carry no speed in the source and none was added (0 changed). The exported speed uses the note XProperty 688062467 exactly as a real Guitar Pro 7 file stores it (ticks of a 960-tick quarter); not yet re-saved in Guitar Pro 8."),
        ["A18"] = (Gp8Verdict.Pass, "pass", "Song 10: 2 tremolo-pick beats, 0 changed."),
        ["A19"] = (Gp8Verdict.Pass, "pass for a single note; chord case not tried", "Song 10: 2 fade-ins, 0 changed (single notes). One faded note of a chord is not in the songs; the format keeps the fade on the beat (<Fadding>) in the original and the re-save."),
        ["A20"] = (Gp8Verdict.Pass, "pass for a single note; chord case not tried", "As A19 (2 fade-outs)."),
        ["A21"] = (Gp8Verdict.Pass, "pass", "Song 09: 4 navigation marks, 0 changed."),
        ["A22"] = (Gp8Verdict.Pass, "pass", "Songs 08 and 13: 183 bar tempos and 3 tempo changes, 0 changed; mid-bar tempos of song 08 too."),
        ["A23"] = (Gp8Verdict.Pass, "pass", "Songs 10 and 12: 17 whammy beats with their sub-type tags, 0 changed."),
        ["A24"] = (Gp8Verdict.Pass, "pass (playback: orchestrator)", "Songs 10 and 12: 15 harmonic notes (all five kinds, harmonic fret): 0 changed. The sounding pitch of harmonics (fret 12 = 71, fret 7 = 78) is for the orchestrator's playback check."),
        ["A25"] = (Gp8Verdict.Pass, "pass", "Song 10: 4 dead notes, 0 changed."),
        ["A26"] = (Gp8Verdict.Changed, "changed ONLY next to an accent or staccato (see A54)", "Song 10: 3 ghost notes on their own: 0 changed; round 2 (ghost-combos): a ghost note alone is kept as well. A ghost note that also carries any <Accent> value (staccato, accent, heavy accent, tenuto) loses its ghost mark (A54)."),
        ["A27"] = (Gp8Verdict.Pass, "pass", "Song 10: 2 hammer-on/pull-off origins and 2 destinations, 0 changed."),
        ["A28"] = (Gp8Verdict.Pass, "pass", "As A27."),
        ["A29"] = (Gp8Verdict.EvidenceOnly, "fixed; Guitar Pro 8 not yet shown the file", "Not in any song. Written now as <Legato origin destination/> on the beat pair, the element the real Guitar Pro 6-8 files use (24 in them); a Guitar Pro 8 re-save of gp-capability\\A29.gp is still to do."),
        ["A30"] = (Gp8Verdict.EvidenceOnly, "fixed; Guitar Pro 8 not yet shown the file", "Not in any song. Written now as the beat Property Rasgueado (ii_1 ... peami_1, the pattern names alphaTab writes); no real file of the 45 uses one, so a Guitar Pro 8 re-save of gp-capability\\A30.gp is still to do."),
        ["A31"] = (Gp8Verdict.Pass, "pass (round 2: 0 differences; drawn P.S. with an up line)", "Round 2: gp-capability\\A31.gp (pick slide up) re-saved in Guitar Pro 8: 96 facts compared, 0 changed; Guitar Pro 8 draws it as \"P.S.\" with an up line. The flags 128 / 64 come from alphaTab; no real file of the 45 has a pick slide."),
        ["A32"] = (Gp8Verdict.Pass, "pass (round 2: 0 differences; drawn P.S. with a down line)", "Round 2: gp-capability\\A32.gp (pick slide down) re-saved in Guitar Pro 8: 96 facts compared, 0 changed; Guitar Pro 8 draws it as \"P.S.\" with a down line."),
        ["A33"] = (Gp8Verdict.Pass, "pass", "Song 10: 2 left-hand taps, 0 changed (<LeftHandTapped> in both files)."),
        ["A34"] = (Gp8Verdict.Pass, "pass (visual: grace length drawn by Guitar Pro 8)", "Songs 06 and 10: 6 grace notes, 0 changed (both files read 2 slots). What length Guitar Pro 8 draws for the eighth-note grace rhythm: visual, orchestrator."),
        ["A35"] = (Gp8Verdict.Pass, "pass", "Songs 06 and 10: 3 before-beat graces, 0 changed."),
        ["A36"] = (Gp8Verdict.Pass, "pass", "Songs 06 and 10: 3 on-beat graces, 0 changed."),
        ["A37"] = (Gp8Verdict.Pass, "pass", "Song 10: 4 brush-down and 2 arpeggio-down strokes, 0 changed."),
        ["A38"] = (Gp8Verdict.Pass, "pass", "Song 10: 4 brush-up and 2 arpeggio-up strokes, 0 changed."),
        ["A39"] = (Gp8Verdict.Pass, "pass (round 2: 0 differences)", "Round 2: gp-capability\\A39.gp (a hammer-on across a bar line) re-saved in Guitar Pro 8: 147 facts compared, 0 changed."),
        ["A40"] = (Gp8Verdict.Pass, "pass (round 2: 0 differences; the tag is already dropped by the export)", "Round 2: gp-capability\\A40.gp (a bend grace note) re-saved in Guitar Pro 8: 115 facts compared, 0 changed. The grace-bend tag is already dropped by TabForge's export (the grace is written as a before-beat grace, its bend stays on the note), so Guitar Pro 8 had no further fact to lose."),
        ["A41"] = (Gp8Verdict.Pass, "pass", "All 15 titles kept."),
        ["A42"] = (Gp8Verdict.NotApplicable, "n/a (never in the .gp)", "A TabForge-side field; Guitar Pro 8 never sees it."),
        ["A43"] = (Gp8Verdict.NotApplicable, "n/a (TabForge-only)", "As A42. With the .tfaudio left beside a Guitar Pro 8 save the sidecar is applied by track position and name (mode test)."),
        ["A44"] = (Gp8Verdict.NotApplicable, "n/a (TabForge-only)", "As A43."),
        ["A45"] = (Gp8Verdict.NotApplicable, "n/a (TabForge-only)", "As A43."),
        ["A46"] = (Gp8Verdict.EvidenceOnly, "resolved by file evidence: format", "Guitar Pro 6-8 files have no per-track send; see the cause. Guitar Pro 8's mixer window not looked at."),
        ["A47"] = (Gp8Verdict.EvidenceOnly, "resolved by file evidence: format", "As A46."),
        ["A48"] = (Gp8Verdict.Pass, "pass", "Song 03 (transposed tracks): the baked tuning and every pitch identical after the re-save."),
        ["A49"] = (Gp8Verdict.NotApplicable, "n/a (TabForge-only)", "As A43."),
        ["A50"] = (Gp8Verdict.NotApplicable, "n/a (TabForge-only)", "As A43."),
        ["A51"] = (Gp8Verdict.Pass, "pass", "Instrument names of 27 tracks identical."),
        ["A52"] = (Gp8Verdict.NotApplicable, "n/a (TabForge-only)", "As A43."),
        ["A53"] = (Gp8Verdict.Pass, "pass for the songs' colours", "Track colours of 27 tracks identical. The colour #12AB34 of the A53 file itself was not opened in Guitar Pro 8."),
        ["A54"] = (Gp8Verdict.Changed, "changed: any <Accent> value replaces the ghost mark; ghost alone kept", "Song 05, bar 3 beat 2 (a chord: string 1 fret 3 ghost + string 2 fret 2, beat accented): the file held <AntiAccent>normal</AntiAccent><Accent>8</Accent> on the ghost note; the Guitar Pro 8 re-save has <Accent>8</Accent> only on both notes. Round 2, probe ghost-combos.gp (written by --write-gp-probes: seven beats, a ghost note beside a plain note, the ghost note also carrying each mark): all five marks were tried (staccato 1, heavy accent 4, accent 8, tenuto 16, and the pairs staccato+accent 9 and staccato+tenuto 17) and every one of them kept its mark and lost the ghost mark (note.ghost 1 -> 0 on all six beats); the control beat with the ghost mark alone (no <Accent> element) kept its brackets. So Guitar Pro 8 keeps a ghost mark only on a note with no <Accent> element at all. In 28 real Guitar Pro 6-8 files none of the 294 ghost notes has any <Accent>. The exporter keeps the ghost mark and writes accent, heavy accent, tenuto and staccato on the other notes of the chord only."),
    };
}
