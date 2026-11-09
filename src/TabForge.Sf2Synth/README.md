# TabForge.Sf2Synth

Standalone SoundFont 2.04 synthesizer (no dependencies) with an optional modelled guitar/bass layer.

## Files
- `Sf2/` file model: `SoundFont` (RIFF, samples), `Sf2Reader` (pdta hydra), `Zone` (zones, presets, instruments, sample headers), `Gen` (generator numbers, defaults), `Mod` (modulator curves, default modulators).
- `Synth/` playback: `Synthesizer` (public API, voice allocation), `Channel` (MIDI state, RPN), `Voice.Setup` (zones -> generators + modulators), `Voice` (render), `Envelope` (DAHDSR + LFO), `ILayer`.
- `Effects/` `Reverb` (Freeverb), `Chorus`.
- `Dsp/` shared: `Biquad`, `FirHistory` (SIMD FIR), `Oversampler2x`.
- `Guitar/` the layer: `GuitarLayer` (MIDI, strings, mix), `PluckedString` (waveguide), `GuitarBus` (rig), `StageAmp` (IAmpModel), `CabinetImpulse`, `Voicings` (all values).

## Signal flow
MIDI -> `Synthesizer.ProcessMidiMessage` -> note-on on a channel the `Layer` owns goes to the layer, everything else starts SF2 voices.
Voice: sample -> cubic Hermite -> resonant low-pass -> gain ramp (vol env, attenuation, modulators) -> pan -> mix + reverb/chorus sends.
`Render` adds the layer to the SF2 mix, then writes the sum × `MasterVolume` (layer and bank guitars level-matched, `GeneralUserBankTests`).
Guitar layer: strings (per channel) -> pre-EQ -> 2x oversampled amp stages -> tone stack -> cabinet FIR -> volume/pan.

## API
```csharp
var synth = new Synthesizer(new SoundFont("bank.sf2"), 48000, maxVoices: 64, effects: true) { Layer = new GuitarLayer(48000) };
synth.ProcessMidiMessage(channel, 0x90, key, velocity);
synth.Render(left, right);   // any length, no allocation
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
