# TabForge.Sf2Synth

Standalone SoundFont 2.04 synthesizer (no dependencies) with an optional modelled guitar/bass layer.

## Files
- `Sf2/` file model: `SoundFont` (RIFF, samples), `Sf2Reader` (pdta hydra), `Zone` (zones, presets, instruments, sample headers), `Gen` (generator numbers, defaults), `Mod` (modulator curves, default modulators).
- `Synth/` playback: `Synthesizer` (public API, voice allocation), `Channel` (MIDI state, RPN), `Voice.Setup` (zones -> generators + modulators), `Voice` (render), `Envelope` (DAHDSR + LFO), `ILayer`, `SynthOptions` (every switch, live).
- `Humanize/` the MIDI pre-stage: `Humanizer` (batch per instant, look-ahead queue, order per key, note lengths), `Evidence` (is the input flat?), `Phrase` (pulse, bar grid, level and timing parts of a stroke), `Strum` (strings of a stroke), `Noise` (seeded Rng, 1/f Pink), `Nuance` (per-note voice variation), `HumanTable` (all values: feel styles, families).
- `Effects/` `Reverb` (Freeverb), `Chorus`.
- `Dsp/` shared: `Biquad`, `FirHistory` (SIMD FIR), `Oversampler2x`.
- `Guitar/` the layer: `GuitarLayer` (MIDI, strings, mix), `PluckedString` (waveguide; `.Attack`: pluck, slap, tap noise, fretless mwah), `StringPhysics`, `GuitarBus` (rig), `BodyModes` (acoustic body), `Compressor`, `StageAmp` (IAmpModel), `CabinetImpulse` (guitar, bass, room), `Voicings` (all values), `VoicingSpecs` (their types), `GuitarRig` (user files, the embedded captured amp), `Amps/captured.nam` (embedded resource).

## Signal flow
MIDI -> `Synthesizer.ProcessMidiMessage` -> note-on on a channel the `Layer` owns goes to the layer, everything else starts SF2 voices.
Voice: sample -> cubic Hermite -> resonant low-pass -> gain ramp (vol env, attenuation, modulators) -> pan -> mix + reverb/chorus sends.
`Render` adds the layer to the SF2 mix, then writes the sum × `MasterVolume` (layer and bank guitars level-matched, `GeneralUserBankTests`).
Guitar layer: strings (per channel) -> pickup resonance -> body modes (acoustic) -> compressor -> pre-EQ -> 2x oversampled amp stages -> tone stack -> cabinet FIR -> level -> volume/pan.

## Distorted amp (`SynthOptions.DistAmp`)
Programs 29/30 only (`RigSpec.Captured`); clean, acoustic, muted and bass programs never use it. A user `.nam` always wins.
- `Captured` (default): the embedded WaveNet capture of a real amp (`Guitar/Amps/captured.nam`, one instance per channel, built at layer creation).
  DI × `Trim` × `Drive` feeds it at the capture's training level (about -29 dBFS rms, peak ≤ 0.4), its silence output and DC are removed,
  `Level` matches the old stage-amp loudness. No cabinet by default: the capture is a full amp output, cabinet path unknown;
  `CapturedAmpCabinet` adds the built-in guitar cabinet. If the resource fails to load, the stage amp plays.
- `Stage`: the modelled `StageAmp` and synthetic cabinet (the previous default).
- `CleanOnly`: the trimmed pickup DI, no amp or cabinet, for an amp plug-in after the synth.
Changes fade out one 64-frame block and in the next (`GuitarBus.Use`); notes keep sounding.

## DI tone
The pickup DI of 27/29/30 (`Voicings.MeasuredDi`, `MeasuredString`) is fitted to a measured real guitar DI, the kind the captured amp was
trained on: low cut 135 Hz, pickup resonance near 900 Hz under a 2 kHz roll-off, a dark loop (2 kHz decays in about 1 s), a soft pluck and a
two-coil bridge humbucker. `GuitarLayerTests.TheDiMatchesTheMeasuredGuitarDi` keeps its octave-band shape within 3 dB of the measured curve.

## Humanizer
Off in the library (`Options.Humanize`). The model and its numbers: `docs/specs/HUMAN_FEEL_SPEC.md`; every value in `HumanTable`.
- Evidence: per channel, over the last 16 notes, velocities on 4 levels or fewer, or with small spread and long identical runs, are flat; onsets whose intervals match each other in simple ratios within 0.1 ms are a grid. Only what is flat changes, scaled by how flat (0 = untouched); the first 6 notes never change. Guitars/basses (24-39) and bowed (40-51) only; never channel 10.
- Level in dB (no step cap): metric accent, a bar pattern that repeats bar to bar, phrase arch, 1/f noise, white, down/up picking, softer fast notes, soft ceiling, ghost strokes on 16th offbeats, steady palm-mute chugs. Timing: bar pattern, 1/f, jitter, tempo drift, laid-back or pushed bias, placed around a look-ahead so notes land early and late. Strums by direction, dynamics and tempo; note lengths vary.
- Feel presets (`SynthOptions.Feel`, also in `SynthOptions.Choices`): Tight (level std ~2.5 dB, onsets ~7 ms, strum 14 ms, look-ahead 20 ms), Natural (default: 4 dB, 12 ms, 25 ms, 35 ms), Loose (5 dB, 19 ms, 45 ms laid back, 50 ms).
- Latency: while on, every event of every channel waits for the look-ahead; `Synthesizer.Latency` reports it (0 when off), a host shifts the output back. Note-offs stay in order per key, never before their note-on, across switch-off, `NoteOffAll` and `Reset`. Seeded per channel; `Reset` repeats a render bit for bit.

`SynthOptions` holds every switch (guitar layer, humanize and its parts: velocity, timing, voice, drift, feel); swap the whole record from any thread; `SynthOptions.Fields` and `Choices` list name, help, get and set for a generated menu.

## API
```csharp
var synth = new Synthesizer(new SoundFont("bank.sf2"), 48000, maxVoices: 64, effects: true) { Layer = new GuitarLayer(48000) };
synth.ProcessMidiMessage(channel, 0x90, key, velocity);
synth.Render(left, right);   // any length, no allocation
synth.Options = synth.Options with { Humanize = true };   // any thread, mid-song too
```

## Add an instrument processor
Implement `ILayer` (own the channels whose program you model, render additively, allocate nothing in `Render`/`ProcessMidi`),
keep its values in one table like `Guitar/Voicings.cs`, and set it as `Synthesizer.Layer` (compose several in one layer if needed).
A learned amp replaces `StageAmp` by implementing `IAmpModel`.

## NAM A2 (checked against the official NeuralAmpModelerCore repo and its `example_models/A2.nam`, Nov 2026)
- A2 = "Architecture 2" of Neural Amp Modeler (2026, with TONE3000). Same JSON `.nam` file, `version` 0.7.0, 48 kHz.
- Top level `SlimmableContainer`: `submodels` with `max_value` 0.5 (A2-Lite, 3 channels) and 1.0 (A2-Full, 8 channels); each a `WaveNet`. We run Full.
- WaveNet A2: one layer array, 23 layers, dilations 1..239 in three stacks, `kernel_sizes` per layer (6, two of 15), LeakyReLU 0.01,
  `gating_mode` "none", `bottleneck`, `layer1x1`, per-array `head` conv (kernel 16, bias), `head_scale` = last weight; config-level `head` null.
- Also in the format, not used by A2 files: FiLM blocks, `groups_*` != 1, `condition_dsp`, config-level post-stack head: these load as an error
  (built-in amp fallback). A per-array `slimmable` (`slice_channels_uniform`) runs at full width, which equals the plain model.
- Levels: training data at -18 dB RMS, rescale folded into the head; `metadata.loudness` and `input_level_dbu` as in A1 (`NamAmp`).
- Receptive field 6332 samples (~132 ms). A2-Full ~30-40% cheaper than A1 standard (vendor figure). `.namb` (binary, embedded) not read.
- Core licence MIT, free; model files carry their creators' own terms.

## Test
`dotnet test Source/tests/TabForge.Avalonia.Tests -c Debug --no-build --filter "FullyQualifiedName~Sf2SynthTests|FullyQualifiedName~GuitarLayerTests"`.
Set `TABFORGE_SYNTH_WAV_DIR` to a folder to get listening WAVs from `GuitarLayerTests` and `Sf2SynthTests`.
