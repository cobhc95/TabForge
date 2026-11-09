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

## Architecture decision
Clean pickup-style guitar DI, then a user-chosen Neural Amp Modeler `.nam` model as the amp, then an optional cabinet `.wav` IR. Built-in stage amp and synthetic cab stay as the default. NAM runtime is written from scratch from the public format spec (no copied code).

## To do next, in order
1. [x] Build and the five test classes: nothing was broken; 62/62, now 63.
2. [x] Level calibration (see Measured). Open: a steeper layer velocity curve if the owner wants the bank's dynamics.
3. [x] NAM A2 confirmed from NeuralAmpModelerCore (README section). Not supported (error -> built-in fallback): FiLM, groups, condition_dsp, post-stack head, `.namb`. Never run against a real A2 file.
4. [x] Settings `vst.synthamp` / `vst.synthcab` (Text rows, shown with TabForge Synth). Path rides in the engine sentinel `tabforge:synth|<sf2>|<nam>|<ir>`;
   same SoundFont = `RigOnly` choice, `GmSynth` calls `GuitarLayer.LoadRig` (off the audio thread, fade out/in over two 64-frame chunks).
   Status bar text comes from `GuitarLayer.DescribeRig` on a worker thread. Cabinet already ran on `FftConvolver`. Not checked in the running app.
5. [x] A/B WAVs rendered (numbers above). Owner to listen.
6. [x] CPU per architecture (above).
7. Later: palm-mute detection and pick attack, bass and clean guitar, drums, keys, then the other GM families (see the plan in the conversation: waveguide, modal, DDSP hybrids).

## Open questions for the owner
- Real `.nam` files and cabinet IRs to test with (none downloaded; third-party captures have their own licences, so bundling needs clearly permissive ones or our own).
- Whether to ship a default NAM model of our own (train from the stage amp, or capture a real amp).

## Rules the agents followed
Brief, small files with `Owns / Does not own / Tests` headers; no allocation in render; compile plus named test classes only; local commits, no push, no AI attribution.
