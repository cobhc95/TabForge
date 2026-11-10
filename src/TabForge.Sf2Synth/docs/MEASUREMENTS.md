# TabForge Synth: measurements log

Measurements, method notes and the reasoning behind each tuning step, newest topics last. Priorities, commands, the app seam and how to work with the owner are in `src/TabForge.Sf2Synth/HANDOVER.md`; the library map is its `README.md`.

## Owner verdict so far
The guitar layer (modelled string, stage amp, synthetic cab) sounded better than the plain SF2 guitars. Layer was ~10 dB louder: fixed, now matched (below).

## Measured (Nov 2026)
- Level vs GeneralUser GS guitar, same MIDI, velocity 100, programs 24-37 (mean of single note and chord): worst 0.5 dB.
  Velocity drift (layer minus bank, 40 to 127): clean programs up to 8 dB (layer has a flatter velocity curve, e.g. 28 muted +6 dB at
  vel 40, -1.5 at 127); 29/30 through the amp compress by design (30: +15.6 dB at vel 40, -0.1 at 127 single note). `GeneralUserBankTests`.
- NAM CPU, Release, synthetic weights, 48 kHz, 256-frame blocks, % of one core per active guitar channel: A1 standard 2.8, lite 1.6,
  feather 0.8, nano 0.35, A2 Full 2.2, A2 Lite 0.56, LSTM 1x16 2.1, 1x24 3.1. At 44.1 kHz (resampled) +0.3 to +0.4 points.
- A/B WAVs (`tfsynth2_*.wav`, scratch harness, not in the repo): DI alone (prog 27; palm mute on 28) peaks -10 to -21 dBFS, no NaN,
  max sample step <= 0.054; riff 29 amp vs SF2 rms -28.0 / -29.8 dBFS, riff 30 -31.7 / -32.3 dBFS; crest 10-11 dB (amp) vs 16-17 (SF2).

## Clean, acoustic and bass programs (Nov 2026, commits `0493834`, `7a42e6f`)
- [x] Acoustic 24/25: modal body (`BodyModes`: direct path + band-pass modes; nylon air 98 / top 195 / back 235 Hz + 4 higher, steel 102 / 210 / 300 Hz
  + 5 higher and brighter), nylon fingertip tap (1.5 kHz) vs steel pick snap (3.5 kHz), frequency-dependent decay per string. No squeak, no sympathetic strings.
- [x] Electric 26/27 (and 28/31): two-coil neck humbucker comb (155 + 173 mm, 2.8 kHz resonance) vs bright single coil (110 mm, 5 kHz Q 2.2); clean amp
  drive lowered, gentle compressor (-6 dBFS, 1.6:1) ahead of it, cabinet with a small room (`CabKind.Room`, reflections to 17 ms). User .nam/.wav rig
  applies to these as before (same slot, no new code).
- [x] Basses 32-37: five strings (B0 30.9 Hz to G1), P pickup (33/34) and J pair (35-37), DI -> feed-forward compressor (-20 dBFS, 3.5:1, 3 ms; slap 12 ms)
  -> tube stage -> bass cab. Finger noise tap, brighter picked click, fretless mwah (band-pass at 8 x f0, falling) and 0.12 s glide on every legato step,
  slap = struck bump near the neck + thumb thump + fret click, pop brighter, mid scoop in the tone stack; upright body (60 / 105 / 150 Hz ...) + knock.
- [x] Levels: every program 24-37 within 0.35 dB of the bank at velocity 100 (one test per program). Velocity exponent per program (1.47-2.1):
  layer minus bank between velocity 40 and 127 spans 0.0-3.0 dB on the clean programs, 4.7 dB on 37 (the bank's slap 2 jumps a sample layer at 127).
  29/30 unchanged (-0.02 / +0.02 dB).
- Tuning (full bass rig, autocorrelation): B0 0.06, D1 0.04, E1 0.07, A1 0.00-0.07 cents; DC under 0.3 % of rms; slap above 16 kHz -113 dB of the transient.
- CPU, Debug, % of one core per channel: acoustic 6 strings 0.6, clean electric 6 strings 3.4-3.5 (amp + room cab), bass 1 string 2.6-2.9, upright 0.3.
- Timbre vs the bank sample, same note, velocity 100, dry (bank / layer; `GeneralUserBankTests.TimbreIsMeasuredAgainstTheBankSamples`):

| prog | note | centroid 50 ms, 300 ms, 1 s (Hz) | T60 f0 (s) | T60 h5 (s) | attack (ms) | HNR (dB) |
|---|---|---|---|---|---|---|
| 24 | 52 | 1518 / 1089, 848 / 464, 560 / 295 | 10.0 / 4.1 | 6.9 / 3.5 | 7 / 7 | 18 / 45 |
| 25 | 52 | 2556 / 2315, 1489 / 924, 1073 / 463 | 10.7 / 5.1 | 9.7 / 4.4 | 15 / 7 | 20 / 46 |
| 26 | 52 | 591 / 642, 469 / 402, 420 / 260 | 8.8 / 4.3 | 9.2 / 3.4 | 12 / 11 | 43 / 49 |
| 27 | 52 | 1637 / 2169, 868 / 1660, 750 / 876 | 13.8 / 5.8 | 11.1 / 5.2 | 6 / 11 | 34 / 47 |
| 28 | 52 | 404 / 1072, 282 / 456, 282 / 36 | 1.6 / 0.7 | 0.4 / 0.7 | 5 / 5 | 31 / 42 |
| 31 | 52 | 1519 / 392, 1863 / 360, 1840 / 302 | 0.4 / 3.5 | 6.5 / 3.3 | 45 / 11 | 26 / 50 |
| 32 | 33 | 216 / 535, 150 / 191, 137 / 124 | 5.7 / 3.0 | 5.9 / 3.0 | 33 / 39 | 28 / 42 |
| 33 | 33 | 214 / 610, 174 / 395, 162 / 217 | 20.4 / 7.4 | 21.2 / 8.2 | 19 / 21 | 43 / 43 |
| 34 | 33 | 637 / 1089, 307 / 983, 179 / 680 | 18.4 / 7.4 | 31.8 / 7.4 | 32 / 20 | 39 / 44 |
| 35 | 33 | 247 / 813, 241 / 452, 227 / 260 | 8.9 / 5.2 | 9.8 / 5.3 | 16 / 22 | 30 / 44 |
| 36 | 33 | 649 / 1538, 435 / 1260, 182 / 913 | 31.5 / 4.5 | 25.5 / 4.5 | 7 / 23 | 43 / 46 |
| 37 | 33 | 775 / 1354, 375 / 1139, 304 / 813 | 56.6 / 4.5 | 39.1 / 4.5 | 2 / 4 | 20 / 45 |

  Reading it: the bank's T60s of 9-57 s are sample loops (a real string decays in 3-8 s), so the layer is shorter on purpose. Its HNR is higher because
  the samples carry recording noise. The basses are 2-3x brighter at the attack than the bank's dull samples (fresh-string finger, pick and slap definition);
  the acoustics are darker after 300 ms (highs decay first, as on a real string). 36 attack 23 ms: the 1 ms envelope peaks on a later slap pulse.
- A/B phrases (single notes, strum or double stop, a line; fretless line overlaps so it glides): `tfsynth3_<24|25|26|27|32|33|34|35|36>_<sf2|layer>.wav`
  in the agent scratch folder, not in the repo. Owner to listen.
- Open: 31 harmonics stay a sine-like tone (bank much brighter); 28 palm mute decays faster than the bank; squeak and sympathetic strings not built;
  nothing checked in the running app; no adapter or settings change was needed.

## Humanizer v3 (Nov 2026): a pass over the written notes
- v1 (`d0dbb5d`) and v2 (`b55d043`) were streaming MIDI pre-stages inside `Synthesizer` with a 20-50 ms look-ahead. Owner on v2: the
  velocity went down at random instead of by what is played, and fast single notes sounded robotic (chords were fine). v3 replaces them.
- [x] Library (`Humanize/`, `README.md` section Humanizer): `Humanizer.Apply` over one track's written notes. Window per bar: 2 bars back,
  4 ahead. Level = structural parts (meter from the real time signature, syncopation, length, melodic peaks and rises, 4-bar arch from
  the section, energy, pick hand and string crossing, run groups, motif memory keyed by each beat's rhythm and intervals, legato
  unevenness) + one slow residue bounded to +-1 dB. Timing = bias + wander (1-8 bars) + smooth pushes and pulls + small jitter + motif
  timing; fast-run model below 250 ms (groups of 2/3/4, upstrokes late, crossing +1..4 ms, rush up / lean down, legato late, 1.5 %
  smoothly odd notes). Everything is a hash or value noise of position (no running state). Strum model of v2 kept for chords.
  The streaming humanizer, its look-ahead, `Synthesizer.Latency` and `GmSynth` latency reporting are gone; `VoiceVariation` keeps the
  per-note `Nuance` in the synth (allocation-free).
- [x] App hook: `PlaybackOptions.Humanizer` (`ITimelineHumanizer`, Core) is applied at the end of `ScoreToMidiCompiler.Build`, then the
  events are re-sorted. The app's `Transport/SynthHumanizePass` acts only with Sound engine TabForge Synth and "Humanize flat dynamics"
  on, on tracks that are not audio and have no playing VST instrument, never the drum channel. Set by `AvaloniaPlaybackSessions`
  (every start) and `MainWindowFileExports` -> `RenderWindow` (render); exports pass none. Its key is part of the schedule-reuse stamp.
  Bar keys = position in the full performed order; sections from the markers; strokes and marks from the timeline's notes and the score.
  Controllers and pitch wheel move with the note sounding when they are sent. Settings: `vst.synthfeel` "Humanize feel" (Tight /
  Natural / Loose, `PluginSettings.SynthFeel`).
- Measured (`HumanizerTests`, Debug, natural unless stated):
  - Level std 2.2 dB on a riff + run song (tight / natural / loose 1.5 / 2.2 / 3.3 dB; onset std 4.7 / 7.9 / 11.8 ms). Explained variance
    of the level by the structural parts 96.5 %; largest step between neighbours not explained by them 0.64 dB; residue within +-0.9 dB.
  - Identical riff bars: contour difference 1.27 dB after removing each bar's mean (3.0 dB raw, the phrase arch).
  - 16th runs at 100 and 180 bpm: onset std 8.4 ms at both (constant in ms), lag-1 correlation of the offsets 0.57 (smooth, not white),
    group accent depth 3.5 dB (first of group vs the rest, meter and picking included; the group part alone 1 dB), upstrokes 4.7 ms later,
    closest onsets 0.77 of the interval (no flams).
  - Strums: spread median down 22.7 ms, up 16.0 ms, largest gap 7.4 ms.
  - Start mid-song: identical notes from 2 bars after the start (library and through the compiler); an edit in bar 20 changed only bars
    within 16-22; per-key order kept under a 3000-note fuzz; human input (velocity std 10 and 19, jitter 8 and 12 ms) untouched.
  - Pass cost 2.3 us per note (5.3 min of music, 1760 notes, in 4 ms) on the compile worker; nothing on the audio thread.
- Demo `tfsynth_FULL_DEMO_v3.wav` (66 s, scratch folder, not in the repo; flat source `demo_v3_flat.mid`): riff 30 (6 bars, repeats and a
  variation), 16th runs on 30 and 27 at 140 bpm, clean strums 27 with a fast fill, acoustic strum 25, bass 33 (8ths then 16ths); new DI,
  captured amp, Natural, 1 s gaps, one gain (+18 dB), peak 0.90. Per part: level std 2.0-2.3 dB with 96-97 % explained on the single-note
  parts; strums 5.5-6.1 dB, 28-29 % explained per note (the kept strum model's per-string levels); onset std 5.6-10.9 ms. Render
  6.6 % of one core per channel (Debug). Owner to listen: fast runs and the repeated riff first.
- Open / uncertain: not checked in the running app. After a seek the first 2 bars lack the context before the start (energy, evidence,
  the previous note's string and pitch), so they can differ slightly from a play from the top. The pass number of a repeat started with
  "start on pass 2" is keyed as the first pass. Mid-bar program changes use the track's setup program. Tracks with a playing VST
  instrument are never humanized, even when their GM sound is layered. The level spread is now 4.5 dB natural (Attack and peaks). Drums untouched.

## Attack and peaks (Nov 2026)
Owner: every note and chord needs much more attack; the takes sounded like one velocity and sustain. Measured per onset (own detector,
python, statistics only): attack-to-sustain = peak of the first 15 ms over the rms of 100-300 ms (cut at the next onset); initial slope =
fit of the 5 ms rms in dB over 200 ms from its peak; loudness range = p95 - p10 of 50 ms rms. Same onsets for the real take and ours.
- Real take (8.5 s, 57 onsets; ours = the recovered notes with velocities fitted to the attack peaks, program 29), real / before / after:

| measure | DI real | DI before | DI after | amp real | amp before | amp after |
|---|---|---|---|---|---|---|
| attack-to-sustain (dB) | 17.7 | 13.8 | 18.3 (27: 17.0) | 7.7 | 8.6 | 8.5 |
| initial slope (dB/s) | -58 | -48 | -65 (27: -58) | -0.4 | -3.6 | -3.8 |
| crest per onset (dB) | 15.1 | 13.6 | 16.2 | 8.9 | 8.6 | 8.5 |
| loudness range (dB) | 15.5 | 12.7 | 14.5 | 2.9 | 6.9 | 7.2 |
| attack peak std (dB) | 6.9 | 3.1 | 4.5 | 1.2 | 1.0 | 0.9 |

- Where the peaks were lost: (1) the string: one-stage decay, 4 dB too little attack over sustain, and at the capture's input level 40 of 57
  real attacks were louder than our velocity 127; (2) the humanizer: level std 2.2 dB and no room above a flat velocity near 100 (a +4 dB
  accent clipped at 127, so only the dips were heard); (3) the captured amp itself: velocity 50 -> 127 moves its attack peak by -1.5 dB
  (32 dB of input -> 0.7 dB of output). The real amp does the same (attack peak std 1.2 dB, loudness range 2.9 dB), so it stays.
  Not the cause: the clean compressor (0.3 dB), the clean tube stages (0.1 dB), the bass compressor (1.5 dB), the SF2 voice path.
- Changed: double decay in `PluckedString` (`StringSpec.Drop` 4 dB over `DropMs` 12 ms, × velocity; the pluck starts that much louder,
  sustain unchanged), every program but palm-muted 28 (already 25 dB); 27/29/30 string velocity exponent 1.85; levels re-trimmed (all
  24-37 within 0.14 dB of the bank at velocity 100; 29/30 DI trim unchanged: riff -29.7 dBFS rms, peak 0.376 of the 0.4 limit).
  Humanizer: structural amounts about ×2 (meter 6/3.5/2.5/-3.5/-6 dB, sync 3, peak 2.4, group 2.4, motif 1.8, arch 3), ceiling knee 8 dB,
  headroom `HeadDb` 9 (a flat loud input is lowered so the accents fit under 127), strum first string +2 / last -2 dB.
- Single note, velocities 50/80/110/127, bus output, attack-to-sustain before -> after (dB): 24 10.9 -> 13.0, 25 11.4 -> 13.6,
  26 9.8 -> 11.7, 27 12.7 -> 15.6, 28 25.4 (same), 29/30 8.4 -> 8.5, 31 9.4 -> 11.5, 32 2.9 -> 6.1, 33 10.1 -> 12.8, 34 9.7 -> 12.2,
  35 8.6 -> 11.4, 36 14.9 -> 18.1, 37 15.8 -> 19.2; 6-string strum 24 9.4 -> 11.4, 25 7.8 -> 10.3, 26 1.8 -> 4.3, 27 8.2 -> 11.0.
  Attack peak 50 -> 127 on 27: 14.3 -> 16.6 dB. The bank's samples (same notes): 26 strum 6.7, 27 single 16.5, 33 5.6.
- Humanizer (`HumanizerTests`): level std tight / natural / loose 3.0 / 4.5 / 6.4 dB (was 1.5 / 2.2 / 3.3), explained 99 %, group
  accent 7.3 dB, identical bars 2.1 dB contour. Test ranges changed: level std 3.8-5.2 (was 2-5), group accent 4-9 (was 1-5), identical
  bars <= 2.5 dB (was 2), tight 2.5-3.5 and loose 5.2-7 added.
- Demo parts v3 -> v4 (attack-to-sustain / loudness range, dB): riff 30 6.4 / 1.6 -> 6.2 / 2.8, run 30 6.6 / 2.5 -> 6.8 / 2.3,
  run 27 12.0 / 7.5 -> 11.7 / 11.9, strum 27 4.3 / 11.1 -> 13.0 / 16.1, strum 25 3.5 / 8.1 -> 7.5 / 10.7, bass 33 5.1 / 4.1 -> 8.3 / 8.5.
- SF2 path (GeneralUser GS, ours / MeltySynth, single notes): attack-to-sustain piano 8.6 / 8.0, saw lead 7.4 / 5.2, drums 35.8 / 30.4,
  strings both negative (slow attack); reverb on or off within 0.1 dB. Attack peak 50 -> 127: piano 11.6 / 14.0, strings 27 / 33 dB:
  we apply the bank's own velocity modulators (spec), MeltySynth does not. No change.
- Guards (`GuitarLayerTests.Attack`): per program 24-37 attack-to-sustain ±2 dB, initial slope ±8 dB/s, attack peak 50 -> 127; strums
  24-27, 29, 30; the 29 DI within 2 dB of the real DI's 17.7.
- Demo `tfsynth_FULL_DEMO_v4.wav` (88 s, scratch folder): the v3 parts (same flat `demo_v3_flat.mid`, Natural), then flat piano chords
  (program 0) and a flat drum groove (channel 10) on GeneralUser GS; one gain (+13.7 dB), peak 0.90 (bass part).
- Still different: the 29 DI initial slope is 7 dB/s steeper than the real one; attack peak std 4.5 vs 6.9 dB (29 still caps 24 of 57
  real attacks at the capture's level); our amp output has the larger loudness range (no hiss or hum between notes); acoustic strums
  swell for ~40 ms after the strike (body modes; the bank's strum falls at once); the distorted programs keep the capture's squeeze.
  Options if 29/30 need more bite: a gentle expander ahead of the captured amp, or a little of the DI transient mixed after it.

## Captured amp output EQ (Nov 2026)
Owner: compared with the real amp recording the EQ could be better, maybe too high in the upper mids. Measured on the 8.5 s take
(python, statistics only): our recovered notes (velocities fitted to the attack peaks, program 29, voice variation Natural) through the
current DI and captured amp vs the same take through the real amp; long-term spectrum in 1/6 octaves, level-matched on 60 Hz-14 kHz;
time slices = STFT frames 0-30 / 30-300 / 300+ ms after each onset; distance = RMS of the band differences 60 Hz-14 kHz.
- Where the gaps come from:
  - The capture itself (the owner's DI through it vs the real amp): within 0.4 dB up to 5 kHz, +1.6..+5.9 dB at 6.3-16 kHz (fizz); distance 1.6.
  - Our DI vs the real DI: distance 2.85, 1-4 kHz +2.2 dB (rms 4.2). On the muted chugs (39 of 57 onsets) 1-4 kHz +7.6 (2.5-4.5 kHz
    +13-15 dB: hard pick, no palm mute); on the single notes 1-4 kHz -0.6.
  - Low mids: under the real single notes 110-400 Hz is 13-19 dB stronger (a low string keeps ringing, hum); our list cuts it. Not tone;
    on the chugs alone ours was 3.9 dB low there.
  - The built-in cabinet: far too dark (distance 9.2, 4-16 kHz -18 dB). DI tone changes ahead of the amp (3.4 kHz cut, low shelf): 3.4 at best.
- Changed: four biquads after the captured amp (`Voicings.CapturedEq` in `CaptureSpec.Eq`): Peak 210 Hz +4 Q 0.9, Peak 830 Hz -6 Q 1.1,
  Peak 2.55 kHz -3.5 Q 1, HighShelf 5.7 kHz -10 Q 0.7; fitted with a penalty on an attack brighter than the real one and a cap of +4 dB
  on the low boost (an unconstrained fit wanted +8 dB at 200 Hz for the missing ringing string). `SynthOptions.CapturedAmpEq` (on), not
  with a cabinet. Output `Level` 0.84 / 0.65 -> 0.94 / 0.73: riff 29/30 rms unchanged (-0.01 / +0.02 dB), captured - stage -1.1 / +1.2 dB.
- Ours minus the real amp, dB (before -> after):

| measure | before | after |
|---|---|---|
| distance 1/6 oct 60 Hz-14 kHz | 4.04 | 1.74 |
| 1-4 kHz rms / mean | 1.75 / +1.5 | 1.01 / -0.5 |
| 110-400 Hz / 600 Hz-1 kHz / 4-16 kHz mean | -5.0 / +3.4 / +4.8 | -0.5 / +0.3 / -0.7 |
| attack 0-30 ms: 1-4 kHz / 4-16 kHz | +2.4 / +6.3 | +0.4 / +0.9 |
| body 30-300 ms: 1-4 kHz / 4-16 kHz | +1.4 / +4.2 | -0.6 / -1.2 |
| tail 300 ms+: 1-4 kHz / 4-16 kHz | -3.0 / 0.0 | -5.0 / -5.4 |
| chugs only: distance, 1-4 kHz, 4-16 kHz | 4.83, +3.2, +6.7 | 2.34, -0.1, 0.0 |
| single notes only: 1-4 kHz, 4-16 kHz | -0.6, +2.1 | -1.3, -2.1 |
| octave bands 63/125/250/500/1k/2k/4k/8k (dB re sum; real -12.3/-7.2/-6.8/-6.8/-7.9/-9.7/-12.9/-20.4) | -14.7/-11.2/-10.2/-6.0/-5.3/-7.9/-11.1/-18.5 | -11.3/-7.4/-7.1/-6.5/-7.8/-9.9/-13.1/-20.6 |

- Second material: the power-chord riff (1/3 oct) +2..+4 dB at 125-315 Hz, -4..-6 at 630 Hz-4 kHz, -6..-10 above 6 kHz relative to
  before, same rms; demo parts v4 -> v5 rms: riff 30 -23.6 -> -23.9, run 30 -23.4 -> -24.5 dBFS, every other part identical.
- CPU, Release, riff 30, % of one core per channel: 2.22-2.27 without the EQ, 2.29 with it. No allocation (fixed `Biquad[4]`).
- Guard: `GuitarLayerTests.TheCapturedAmpMatchesTheRealAmp` (29/30): octave bands of the DI phrase through the captured amp within 2.5 dB of
  the real amp's (now within 0.8; the take before the EQ was off by up to 4).
- Clean programs and basses (no captured amp, unchanged): layer vs the bank sample, same notes, single note at 4 velocities and strums,
  level-matched, power in <250 Hz / 250 Hz-2 kHz / 2-8 kHz / 8-14 kHz (dB): 24 +3.4/-6.6/-6.8/+8.6, 25 +3.8/-4.3/-5.1/-1.3,
  26 +0.7/-2.8/0.0/+3.4, 27 +15.8/-1.4/-12.2/-45.7, 28 +0.1/-1.0/+16.4/-3.9, 31 +19.4/-10.9/-23.7/-14.9, 32 +0.6/-6.3/+12.7/+13.8,
  33 +1.6/+1.3/+25.3/-6.5, 34 -0.8/-2.3/+11.5/+0.3, 35 +0.7/+0.2/+29.0/-17.3, 36 -2.4/+11.1/+14.7/-12.4, 37 -1.8/+4.1/+11.8/-1.6.
  Two thirds of the 1/3-octave bands differ by more than 4 dB on every program (a modelled string vs one sample). Flagged, not changed:
  27 is dark on purpose (fitted to the real DI), the basses are brighter on purpose (fresh strings, pick and slap), 31 is the open
  harmonics item, 28's 2-8 kHz +16 dB and 35's +29 dB deserve a listen against a real muted guitar and fretless.
- Still different: tails 1-4 kHz 5 dB under the real amp (its hiss and the ringing low string between notes; our notes end at the
  note-off); the muted chugs' attack is still brighter in our DI (no palm mute) and the EQ only averages it out; the single notes are
  now about 1-2 dB darker than the real ones above 1 kHz. Demo `tfsynth_FULL_DEMO_v5.wav` (same flat MIDI and path as v4, Natural, gain
  +13.7 dB, peak 0.90). Listen first to the riff and the 16th runs on 30 against v4: less fizz and honk, fuller low mids.

## Captured amp, default for 29/30 (Nov 2026)
- [x] The owner's approved capture (WaveNet lite, 48 kHz) is embedded as `Guitar/Amps/captured.nam` (metadata reduced to name "Captured amp",
  gear type, loudness, gain). `SynthOptions.DistAmp` = Captured (default) | Stage | CleanOnly, `CapturedAmpCabinet` (off); a user `.nam` wins.
  Live switching fades over two 64-frame blocks (test: no step beyond the playing's own, notes still end). Not checked in the running app.
- Gain staging, riff `GuitarLayerTests.Riff` (bus output, playing part): DI rms -20.7 / -20.5 dBFS, peak 0.75 / 0.79 (29 / 30); trim 0.385 / 0.376
  -> -29.0 dBFS rms, peak 0.29 / 0.30 (training DI -29, peak 0.31). Drive = `RigSpec.Drive` (1).
- Stability, input -20..+12 dB around nominal (riff 30): finite, peak 0.36-0.41 (NamAmp out), output rms -18.8..-18.1 dBFS (the capture is
  heavily compressed: 32 dB of input range moves the output 0.7 dB). Silence in = exactly 0 out after removing the model's rest output
  (it was 0.014, a click when the bus stopped). The model makes DC while playing (-7..-10 % of rms): 15 Hz high-pass after it, bus DC < 1e-5 of rms.
  Onset from silence: first 2 ms step 0.037-0.039 vs 0.068 in the next 48 ms; the end decays to 7e-10 before the bus stops.
- Level: output trim 0.87 / 0.68 (29 / 30; 0.94 / 0.73 with the output EQ, above). Riff vs stage amp -1.8 / +0.4 dB; vs the bank at velocity 100 within 1 dB (`GeneralUserBankTests`).
  With the cabinet added it is about 3.5 dB louder (the cabinet's gain was folded into the stage amp's level).
- CPU, Release, 48 kHz, 256-frame blocks, program 30 six strings, % of one core per channel: captured 2.2, captured + cabinet 2.2 (the FFT cabinet
  is under 0.1), stage 0.85, clean only 0.1. Debug about 15. Layer creation 51 ms (16 prewarmed captured amps, off the audio thread).
  Settling a captured amp that played (switching back to it, or `Reset`) costs one prewarm, a few ms, on that thread.
- A/B WAVs (humanizer off, riff of `tfsynth2`): `tfsynth5_riff<29|30>_<captured|stage>.wav`, `tfsynth5_riff30_cleanonly.wav`, agent scratch folder. Owner to listen.
- Open: the adapter has no Settings row for DistAmp yet (the engine plays the default, Captured); status text still says "built-in amp".

## DI tone fitted to a measured real guitar DI (Nov 2026)
- Data: one 8.5 s riff DI (48 kHz) and the same take through the real amp the captured model learned; private, only statistics kept.
  57 onsets: muted low D2/Bb2 chugs (read 20-50 cents sharp) between single notes E3-G3; attack peak -17.1 ± 5.6 dBFS (median -14.9).
- Method: Python (1/6-octave LTAS, per-onset centroid 0-30 / 30-300 ms, band T60, 2-8 kHz share of the first 15 ms, pitch by harmonic sum);
  `GuitarLayerTests.ANoteListRendersAsDiAtItsAttackLevels` plays the recovered notes at the measured onsets, note-offs (20 dB under the
  attack) and attack peaks (velocity interpolated on our own peak-vs-velocity curve per note; 40 of 57 clamp at 127 at the training trim).
- Measured, DI (real / ours before / ours after, program 29; 27 after is within 0.2 of 29):

| measure | real DI | before | after |
|---|---|---|---|
| band distance, 1/6 oct, 50 Hz-12 kHz, level-matched (RMS dB) | 0 | 14.8 | 2.8 |
| octave bands 63/125/250/500/1k/2k/4k/8k (dB re sum) | -16.9/-10.0/-5.8/-4.0/-7.6/-14.6/-21.4/-28.3 | -13.9/-8.9/-7.4/-7.6/-10.1/-8.0/-7.2/-15.5 | -16.4/-9.2/-5.8/-4.4/-7.2/-14.5/-21.3/-28.2 |
| level 3.2 kHz band re 500 Hz (dB) | -36.5 | +3.1 | -31.0 |
| below 80 Hz re 100-400 Hz (dB) | -21.2 | -11.8 | -22.5 |
| centroid 0-30 / 30-300 ms, single notes (Hz) | 526 / 488 | 1508 / 1388 | 458 / 468 |
| centroid 0-30 / 30-300 ms, muted chugs (Hz) | 373 / 249 | 1343 / 1000 | 361 / 326 |
| T60 f0 / 2 kHz, held notes (s) | 4.1 / 0.76 | 4.1 / 3.1 | 4.1 / 0.84 |
| 2-8 kHz share of the first 15 ms (dB re broadband), notes / chugs | -26 / -33 | -5 / -5 | -26 / -24 |
| crest (dB) | 19.1 | 16.8 | 17.4 |
| noise floor / hum | -42 dBFS rms quietest frames, 100/120 Hz hum -64/-60 dB | none | none |

- Pickup resonance: no peak in 2-6 kHz; the fit puts it near 900 Hz under a 2 kHz roll-off (a heavily loaded bridge humbucker). Above 5 kHz
  the real DI is mostly pick and hiss.
- Changed (`Voicings`: `MeasuredDi`, `MeasuredString`, 27/29/30): low cut 135 Hz; resonance Peak 900 Hz +13.5 dB Q 1 + LowPass 2.07 kHz Q 0.4
  (was LowPass 3.3 kHz Q 2.2 on 29/30, 5 kHz Q 2.2 on 27); loop Darkness 0.55 (was 0.10-0.12); pluck 3.5 kHz, noise 0.25 (was 7-8 kHz, 0.3-0.4);
  second coil at 63 mm. 29/30 now share one string. Trim 0.30 (riff DI -29.1 dBFS rms, peak 0.27, as before); 27 Level 2.08 (bank 0.00 dB).
  29/30 vs the bank at velocity 100: +0.20 / +0.22 dB; captured - stage riff -0.8 / +1.6 dB.
- End to end, ours DI -> captured amp vs the real amp recording (same notes; real / before / after): 1/6-octave distance 8.4 -> 3.75 dB;
  centroid 0-30 / 30-300 ms single notes 1007 / 1112, 1836 / 1789, 1101 / 1226 Hz; chugs 855 / 539, 1877 / 1446, 1078 / 746 Hz;
  crest 10.9 / 11.2 / 11.4 dB. The amp's tone barely depends on input level here (distance 3.64-3.80 over -7..+3 dB of trim).
- Still different: our amp output has 2-8 dB less at 110-400 Hz and 4-9 dB more fizz at 7-14 kHz (now the output EQ, above); muted chugs ring brighter after the attack
  (no palm mute, only release damping); the real picking is 5 dB hotter per note than ours at velocity 100 and twice as varied; no hum or hiss.
- Guard: `GuitarLayerTests.TheDiMatchesTheMeasuredGuitarDi` (27/29/30): octave bands within 3 dB of the curve above (a phrase in the same
  register, not the take), 2 kHz T60 of a held F3 within 0.76 s ×/÷ 1.6 (now 1.15 s).
- A/B WAVs, all at -22 dBFS rms (gain: real DI +7.4, real amp -8.3, ours DI +9.8 / +10.4, ours amp -1.4 / -1.4 dB):
  `tfsynth7_di_<owner|ours_before|ours_after>.wav`, `tfsynth7_amp_<owner_fx|ours_before|ours_after>.wav`, agent scratch folder. Owner to listen.

## State after DI fit + humanizer v2 + captured amp (Nov 2026)
- Combined tree builds with 0 warnings; Sf2SynthTests, GuitarLayerTests, HumanizerTests, NamTests, SoundEngineTests, GeneralUserBankTests: 122/122.
- No interaction fix needed: the DI fit already re-trimmed the captured amp input (29/30 riff DI -29.1 dBFS rms, peak 0.27; limit 0.4).
  The humanizer only moves velocities and times, which the amp's stability range (-20..+12 dB) covers.
- Listening set `tfsynth8_<riff30|riff29|strum27|strum25|arp27|bass33>_<flat|tight|natural|loose>.wav` and the flat `tfsynth8_<name>.mid`
  (`HumanizerTests.WritesListeningFilesWhenAsked`; riff30, strum25, arp27, bass33 byte-equal to `tfsynth4_*.mid`): new DI, captured amp on
  29/30, streaming humanizer (output shifted back by its look-ahead), one gain per song (flat at -20 dBFS rms). After gain: rms -19.5..-21.8 dBFS,
  peak -8.6..-0.4 dBFS (presets up to 3.9 dB higher peak than flat).
- Humanizer per song, tight / natural / loose: level std riff 2.7 / 4.3 / 5.9 dB, arp 2.3 / 3.5 / 5.4, strum 3.9 / 6.1 / 9.6, bass 2.4 / 3.4 / 5.0;
  onset std riff 6.4 / 10.8 / 18.2 ms, arp 7.6 / 12.8 / 20.8, strum 8.1 / 14.3 / 27.3, bass 5.9 / 10.0 / 17.7. Look-ahead 0 / 20 / 35 / 50 ms (off, feels).
- CPU, Release, one channel, all layers on (DI, amp, humanizer, voice variation), % of one core: riff 29/30 1.7-2.0, arp 27 0.7, strum 27 0.8,
  strum 25 0.3, bass 33 0.6; humanizer on or off makes no measurable difference.

## Modelled bass restarted (Nov 2026)
Owner: the basses sounded synthy and thin (32-bit like). Restart from the modelling layer, no third-party samples/models; guitars (24-31) untouched.
Genuinely new, per program 32-37 (values in `Voicings`, DSP in `GuitarBus`, `BodyModes`, `Compressor`, `StageAmp`, `CabinetImpulse`, `PluckedString.Attack`):
- Electric basses (33-37) get a parallel low-frequency body (`BassBody`: 48 / 92 / 155 / 240 / 380 Hz, broad Q 7-15, direct 1.0) like the upright but lower and broader; the upright keeps its own `Upright` body.
- P vs J pickup inductance/cable resonance: P = peak 1.3 kHz +2.5 dB under a 2.3 kHz low-pass (higher inductance, lower resonance); J = peak 2.7 kHz +4 dB under a 4.4 kHz low-pass (parallel coils, brighter).
- No stacked high-passes above ~20 Hz: the only cut left in the bass chain is the cabinet's 14 Hz; the pre-EQ 18 Hz and stage 15 Hz cuts are removed (stage coupling now 5 Hz, effectively a DC guard).
- Gentle drive preamp: two-stage `BassStages` (1.2 + 1.15, bias 0.06 / 0.01, 5 Hz coupling, 6 / 7.5 kHz plate) for finger/pick warmth; the earlier hotter 2.2 + 1.6 version cost 4-5 dB of velocity range, so it was kept soft.
- Two-band compression (`Compressor` with `SplitHz = 180`): the low band (<=180 Hz) gets -26 dB / 2:1 / 250 ms release (sustain weight) and the rest -16 dB / 2.5:1 / 90 ms (attack definition); slap 220 ms. Guitars keep the single-band path (SplitHz 0).
- Longer, low-end–rich cabinet: 120 ms (was 40 ms) with sub body 55 Hz +3, cone body 130 Hz +2.5, low cone break-up 340 Hz +1.5, boxiness 750 Hz -2.
- Per-technique transients tied to velocity: finger tap 650 Hz, pick tap 2.8 kHz + click 2.5, fretless mwah 8x f0 falling + 0.12 s glide, thumb thump 110 Hz broad, pop 2.4 kHz + click 6; click/tap strength follows velocity^ClickVel / velocity^TapVel (1.1-1.8 on bass, 1 on guitars).
- Measured (note 33, vel 100, layer only, 48 kHz; `GuitarLayerTests.Attack` centers updated for bass only): centroid 50 / 300 ms / 1 s (Hz) before -> after: 33 610 / 395 / 217 -> 366 / 266 / 160, 34 1089 / 983 / 680 -> 631 / 580 / 371, 35 813 / 452 / 260 -> 594 / 360 / 218, 36 1538 / 1260 / 913 -> 1178 / 990 / 748, 37 1354 / 1139 / 813 -> 1065 / 925 / 689; 32 unchanged. Layer is darker at the attack (pickup roll-off + cabinet), ordering kept (finger darkest, slap brightest).
- Attack-to-sustain (dB) before -> after: 32 6.9 -> 6.9 (unchanged), 33 10.4 -> 11.4, 34 10.9 -> 9.5, 35 8.4 -> 10.8, 36 15.7 -> 14.4, 37 17.8 -> 16.2. Initial slopes: 33 -14.8 -> -12.0, 34 -10.9 -> -19.0, 35 -15.5 -> -16.1, 36 -25.4 -> -31.5, 37 -30.8 -> -27.6 dB/s. Attack-peak range 50 -> 127 (dB): 33 17.3 -> 16.1, 34 17.8 -> 17.8, 35 15.9 -> 16.4, 36 13.1 -> 12.9, 37 15.2 -> 15.5.
- Levels re-trimmed so rms100 (note 33, 1 s) matches the old within 0.1 dB: 33 -17.98 -> -18.04, 34 -23.08 -> -23.00, 35 -18.50 -> -18.51, 36 -23.10 -> -23.10, 37 -29.95 -> -29.89 dBFS (the bank-match tests need TABFORGE_GS_SF2, which was absent; drift and relative loudness re-checked against the pre-change chain instead).
- Tuning (autocorrelation, full bass rig): B0 30.9 Hz -0.16 cents, D1 -0.02, E1 -0.04, A1 +0.07, G1 +0.45 cents (all within 1 cent).
- `GuitarLayerTests.Attack` bass centers set to the new numbers (±2 dB / ±8 dB/s / range lower bounds kept); guitar/distorted guards unchanged. No bass octave-band guard existed (only the guitar DI and captured-amp guards), so nothing there to move.
- Demo `tfsynth_BASS_HUMANIZED.wav` (Downloads, not in the repo): six programs 32-37, each the 6-bar 120 bpm `Bass` part flat-written then Humanizer Natural, via Synthesizer + GuitarLayer at 48000, MasterVolume 0.5, 1 s gaps, peak 0.90. Human listening still pending (no one has played it through the app).

## Upright bass (program 32) rebuilt as an acoustic body (Nov 2026)
Owner: the upright still sounded like a synthy electric bass after the electric restart (33-37 untouched, bit-identical). Fix is Voicings-only (program 32 only; no DSP change, no pickup, no amp, no cabinet — the body is the sound, as before):
- String: T60 3.5 / 0.8 -> 4.0 / 0.9 s, Darkness 0.50 -> 0.35, NoiseMix 0.10 -> 0.16, PluckHz 1200 -> 1800 Hz. Longer sustain, less muffled, more flesh; peak/dynamics = Drop 4 / 12 -> 7 dB / 22 ms.
- Finger attack: Tap 2.5 @ 90 Hz / 25 ms (an electric-style sub thump) -> 1.5 @ 300 Hz / 20 ms, TapVel 1.2: a low-mid finger thump with a short wooden knock decay, not a click; Click 1 -> 0.3 (less pick tink).
- Body: Direct 0.62 -> 0.65; modes 60 / 105 / 150 / 230 / 400 / 700 Hz (Q 10-20, narrow) -> 60 / 110 / 155 / 215 / 360 / 600 Hz (Q 7-14, broader): strong air/f-hole at 60/110 Hz, top resonance at 215 Hz.
- Pre-EQ: HighPass 18 kept; LowShelf 85 Hz +4 -> 100 Hz +3; HighShelf 2000 Hz -6 -> 2500 Hz -5. Level 2.30 -> 2.00.
- Measured (note 33, vel 100, layer only, 48 kHz): rms100 1 s -18.92 -> -18.94 dBFS (-0.02 dB; Level retrimmed). Tuning B0 / E1 / A1 / D1 / G1 before 0.32 / 0.60 / 0.59 / 0.01 / 0.39 -> after 0.24 / 0.28 / 0.47 / 0.15 / 0.35 cents (all within 1 cent).
- Attack-to-sustain (vel 110, note 33, guard center 6.9 kept, no guard edit) 6.87 -> 7.22 dB; initial slope -32.64 -> -34.87 dB/s (target -32.6); attack-peak range 50 -> 127 17.4 -> 18.1 dB. Full suite 125 passing (Debug), electric basses unchanged.
- Demo `tfsynth_BASS_UPRIGHT.wav` (Downloads, not in the repo): only program 32, the 6-bar 120 bpm `Bass` line flat-written then Humanizer Natural, via Synthesizer + GuitarLayer at 48000, MasterVolume 0.5, peak 0.90, 16-bit stereo. Human listening still pending (no one has played it through the app).

## Architecture decision
Clean pickup-style guitar DI, then a user-chosen Neural Amp Modeler `.nam` model as the amp, then an optional cabinet `.wav` IR. Built-in defaults: the captured amp (no cabinet) on 29/30, the stage amp and synthetic cab on the other electric programs. NAM runtime is written from scratch from the public format spec (no copied code).
