# TabForge Synth (experimental sound engine): status and next steps

Goal: an own, lightweight, very realistic GM engine. SF2-compatible library plus a modelled guitar layer. Selectable as the Sound engine "TabForge Synth (experimental)"; never the default.

## Where things are
- Library: `src/TabForge.Sf2Synth/` (standalone, no TabForge or MeltySynth dependency). Start at its `README.md`.
- Adapter (thin): `SoundEngines.cs`, `SoundFontStore.ActivePath` (sentinel), `GmBank` (`Choice.Sf2`, `Experimental`), `GmSynth` hook, settings text.
- Tests: `Sf2SynthTests`, `GuitarLayerTests`, `GeneralUserBankTests` (set `TABFORGE_GS_SF2` to a GeneralUser-GS.sf2), `NamTests`, `SoundEngineTests`.
- Listening WAVs: set `TABFORGE_SYNTH_WAV_DIR`. Agents cannot listen; the owner judges by ear.

## Commits (local, not pushed)
- `beb93c4` engine, library, guitar layer, adapter. Tested, basic self-test 218/0.
- `68f3fc5` initial-attenuation scale 0.4 (SF2 levels now within ~1 dB of MeltySynth at velocity 100).
- `1f272b0` NAM runtime (`Nam/`), `FftConvolver`, `Resampler`, `WavIr`, `StringPhysics`, `GuitarRig`, reworked string/bus/layer. Verified afterwards: builds, all five classes pass.
- `82f948b` layer inside `MasterVolume` (it was outside: +6 dB at master 0.5) and per-program `Level` in `Voicings`.
- `a7ddd53` NAM: slimmable WaveNet arrays at full width, A2 notes in README.
- `24dacf6` settings: amp `.nam` and cab `.wav` paths, rig-only swap, status line.
- `bbeeaef` NamConv: masked last vector (odd blocks / 44.1 kHz had doubled the NAM cost).

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

## Humanizer (Nov 2026)
- [x] `Humanize/` MIDI pre-stage in `Synthesizer` (off in the library; on for the Experimental engine via Settings `vst.synthhumanize`
  "Humanize flat dynamics", default on, carried as `|off` in the engine sentinel). All switches in `SynthOptions` (live, any thread).
- Evidence per channel (16 notes): flat velocities and grid timing are detected separately; human input passes unchanged (test: 0/65 events changed).
- Flat test MIDI (velocity 90, grid, 120 bpm), humanizer on, velocity std 0 -> riff 30: 5.2, arpeggio 27: 4.4, strum 25: 3.8, bass 33: 4.4;
  onset delay mean 2-5 ms, max 16 ms (bound 21 ms); strum spread 6.8-7.3 ms. Humanizer ~3 us per note-on (0.002 % of a core); render cost unchanged.
- Voice variation: seed, pick position ±8 %, brightness, release ±15 %, detune 0.5-3 cents falling in 0.15 s, vibrato 4.5-6 Hz ~5 cents after 0.4 s
  (strings; SF2 voices get detune, vibrato, filter brightness). Level drift is a slow velocity walk (±3). Drums untouched (no drum mode built).
- Fixed on the way: a pluck reused the previous note's click history and the cabinet convolver kept its slot over `Reset`, so a render
  after `Reset` was not bit-identical; now it is.
- WAVs `tfsynth4_<riff30|arp27|strum25|bass33>_<flat|human>.wav`, `tfsynth4_arp27_alreadyhuman.wav` and the flat `.mid` files in the agent scratch folder. Owner to listen; not checked in the running app.
- Without a readable SoundFont (MeltySynth + layer) there is no humanizer (it lives in `Synthesizer`).

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
- Level: output trim 0.87 / 0.68 (29 / 30). Riff vs stage amp -1.8 / +0.4 dB; vs the bank at velocity 100 within 1 dB (`GeneralUserBankTests`).
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
- Still different: our amp output has 2-8 dB less at 110-400 Hz and 4-9 dB more fizz at 7-14 kHz; muted chugs ring brighter after the attack
  (no palm mute, only release damping); the real picking is 5 dB hotter per note than ours at velocity 100 and twice as varied; no hum or hiss.
- Guard: `GuitarLayerTests.TheDiMatchesTheMeasuredGuitarDi` (27/29/30): octave bands within 3 dB of the curve above (a phrase in the same
  register, not the take), 2 kHz T60 of a held F3 within 0.76 s ×/÷ 1.6 (now 1.15 s).
- A/B WAVs, all at -22 dBFS rms (gain: real DI +7.4, real amp -8.3, ours DI +9.8 / +10.4, ours amp -1.4 / -1.4 dB):
  `tfsynth7_di_<owner|ours_before|ours_after>.wav`, `tfsynth7_amp_<owner_fx|ours_before|ours_after>.wav`, agent scratch folder. Owner to listen.

## Architecture decision
Clean pickup-style guitar DI, then a user-chosen Neural Amp Modeler `.nam` model as the amp, then an optional cabinet `.wav` IR. Built-in defaults: the captured amp (no cabinet) on 29/30, the stage amp and synthetic cab on the other electric programs. NAM runtime is written from scratch from the public format spec (no copied code).

## To do next, in order
1. [x] Build and the five test classes: nothing was broken; 62/62, now 63.
2. [x] Level calibration (see Measured). Steeper per-program velocity curve done (clean section).
3. [x] NAM A2 confirmed from NeuralAmpModelerCore (README section). Not supported (error -> built-in fallback): FiLM, groups, condition_dsp, post-stack head, `.namb`. Never run against a real A2 file.
4. [x] Settings `vst.synthamp` / `vst.synthcab` (Text rows, shown with TabForge Synth). Path rides in the engine sentinel `tabforge:synth|<sf2>|<nam>|<ir>`;
   same SoundFont = `RigOnly` choice, `GmSynth` calls `GuitarLayer.LoadRig` (off the audio thread, fade out/in over two 64-frame chunks).
   Status bar text comes from `GuitarLayer.DescribeRig` on a worker thread. Cabinet already ran on `FftConvolver`. Not checked in the running app.
5. [x] A/B WAVs rendered (numbers above). Owner to listen.
6. [x] CPU per architecture (above).
7. Later: palm-mute detection, drums, keys, then the other GM families (see the plan in the conversation: waveguide, modal, DDSP hybrids).

## Open questions for the owner
- Real `.nam` files and cabinet IRs to test with (none downloaded; third-party captures have their own licences, so bundling needs clearly permissive ones or our own).
- A longer capture (the standard re-amp signal or several minutes of varied playing): the current one learned from 8.5 s of one take.

## Rules the agents followed
Brief, small files with `Owns / Does not own / Tests` headers; no allocation in render; compile plus named test classes only; local commits, no push, no AI attribution.
