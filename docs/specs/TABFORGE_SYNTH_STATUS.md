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
- `1f272b0` WIP, NOT verified as a whole: NAM runtime (`Nam/`), `FftConvolver`, `Resampler`, `WavIr`, `StringPhysics`, `GuitarRig`, reworked `PluckedString`/`GuitarBus`/`GuitarLayer`/`Voicings`. Last agent note: 27 tests passed, tuning within 0.3 cents, before it was stopped mid-way.

## Owner verdict so far
The guitar layer (modelled string, stage amp, synthetic cab) sounded better than the plain SF2 guitars. Layer was ~10 dB louder than the SF2 guitars (calibration was in progress).

## Architecture decision
Clean pickup-style guitar DI, then a user-chosen Neural Amp Modeler `.nam` model as the amp, then an optional cabinet `.wav` IR. Built-in stage amp and synthetic cab stay as the default. NAM runtime is written from scratch from the public format spec (no copied code).

## To do next, in order
1. `dotnet build`, then run the five test classes above. Fix whatever the WIP commit broke.
2. Finish level calibration: same velocity = comparable loudness to the SF2 guitar (add a test against the real bank).
3. Confirm what "NAM A2" is from the official NAM sources; the research results were not written to the README before the stop. Check `Nam/` supports it.
4. Settings: amp `.nam` path and cab `.wav` path for the Experimental engine only (next to the Sound engine entry). Bad or missing file falls back to the built-in amp with a status message; loading off the audio thread, glitch-free swap.
5. Render A/B WAVs (clean DI alone; riff 29/30 through built-in amp). Report objective numbers only.
6. Measure CPU per NAM architecture (standard, lite, feather, nano, LSTM) with synthetic weights.
7. Later: palm-mute detection and pick attack, bass and clean guitar, drums, keys, then the other GM families (see the plan in the conversation: waveguide, modal, DDSP hybrids).

## Open questions for the owner
- Real `.nam` files and cabinet IRs to test with (none downloaded; third-party captures have their own licences, so bundling needs clearly permissive ones or our own).
- Whether to ship a default NAM model of our own (train from the stage amp, or capture a real amp).

## Rules the agents followed
Brief, small files with `Owns / Does not own / Tests` headers; no allocation in render; compile plus named test classes only; local commits, no push, no AI attribution.
