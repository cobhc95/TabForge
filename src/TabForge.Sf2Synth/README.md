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
`Render` overwrites the output with the SF2 mix × `MasterVolume`, then the layer adds its sound.
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

## Test
`dotnet test Source/tests/TabForge.Avalonia.Tests -c Debug --no-build --filter "FullyQualifiedName~Sf2SynthTests|FullyQualifiedName~GuitarLayerTests"`.
Set `TABFORGE_SYNTH_WAV_DIR` to a folder to get listening WAVs from `GuitarLayerTests` and `Sf2SynthTests`.
