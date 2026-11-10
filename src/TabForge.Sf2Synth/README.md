# TabForge.Sf2Synth

Standalone SoundFont 2.04 synthesizer (no dependencies) with an optional modelled guitar/bass layer.

Handover for a new agent (priorities, commands, the app seam, how to work with the owner): `HANDOVER.md`. Measurements log: `docs/MEASUREMENTS.md` and feel spec: `docs/HUMAN_FEEL_SPEC.md`.

## Files
- `Sf2/` file model: `SoundFont` (RIFF, samples), `Sf2Reader` (pdta hydra), `Zone` (zones, presets, instruments, sample headers), `Gen` (generator numbers, defaults), `Mod` (modulator curves, default modulators).
- `Synth/` playback: `Synthesizer` (public API, voice allocation), `Channel` (MIDI state, RPN), `Voice.Setup` (zones -> generators + modulators), `Voice` (render), `Envelope` (DAHDSR + LFO), `ILayer`, `SynthOptions` (every switch, live).
- `Humanize/` the note pass a host runs on the written notes: `Humanizer` (strokes, window, motifs, neighbours, per-key order), `Notes` (input, output, level parts), `Dynamics` (level parts), `Timing` (onset parts, fast runs, lengths), `Strum` (strings of a stroke), `Evidence` (is the input flat?), `Noise` (position-keyed hashes and value noise), `HumanTable` (all values: feel styles, families); in the synth `VoiceVariation` draws each note's `Nuance` (voice variation).
- `Effects/` `Reverb` (Freeverb), `Chorus`.
- `Dsp/` shared: `Biquad`, `FirHistory` (SIMD FIR), `Oversampler2x`.
- `Guitar/` the layer: `GuitarLayer` (MIDI, strings, mix), `PluckedString` (waveguide; `.Attack`: pluck, slap, tap noise, fretless mwah), `StringPhysics`, `GuitarBus` (rig), `BodyModes` (acoustic body), `Compressor`, `TransientExpander` (post-amp attack boost, distorted programs only), `StageAmp` (IAmpModel), `CabinetImpulse` (guitar, bass, room), `Voicings` (all values), `VoicingSpecs` (their types), `GuitarRig` (user files, the embedded captured amp), `Amps/captured.nam` (embedded resource).

## Signal flow
MIDI -> `Synthesizer.ProcessMidiMessage` -> note-on on a channel the `Layer` owns goes to the layer, everything else starts SF2 voices.
Voice: sample -> cubic Hermite -> resonant low-pass -> gain ramp (vol env, attenuation, modulators) -> pan -> mix + reverb/chorus sends.
`Render` adds the layer to the SF2 mix, then writes the sum × `MasterVolume` (layer and bank guitars level-matched, `GeneralUserBankTests`).
Guitar layer: strings (per channel) -> pickup resonance -> body modes (acoustic) -> compressor -> pre-EQ -> 2x oversampled amp stages -> tone stack -> cabinet FIR -> level -> volume/pan.
Captured amp (29/30): pickup resonance -> trim -> captured amp -> DC blocker -> output EQ (or a cabinet) -> level -> volume/pan.

## Distorted amp (`SynthOptions.DistAmp`)
Programs 29/30 only (`RigSpec.Captured`); clean, acoustic, muted and bass programs never use it. A user `.nam` always wins.
- `Captured` (default): the embedded WaveNet capture of a real amp (`Guitar/Amps/captured.nam`, one instance per channel, built at layer creation).
  DI × `Trim` × `Drive` feeds it at the capture's training level (about -29 dBFS rms, peak ≤ 0.4), its silence output and DC are removed,
  `Level` matches the old stage-amp loudness. No cabinet by default: the capture is a full amp output, cabinet path unknown;
  `CapturedAmpCabinet` adds the built-in guitar cabinet. If the resource fails to load, the stage amp plays.
  Output EQ (`CaptureSpec.Eq`, `Voicings.CapturedEq`, `SynthOptions.CapturedAmpEq`, on; skipped when a cabinet follows): four biquads
  fitted to the real amp's recording of the measured take (210 Hz +4, 830 Hz -6, 2.55 kHz -3.5 dB, shelf 5.7 kHz -10 dB). It corrects
  the capture's own fizz above 6 kHz and what our DI does differently (bright pick on muted chugs, thin low mids); `Level` re-trimmed.
  `GuitarLayerTests.TheCapturedAmpMatchesTheRealAmp` keeps the octave bands within 2.5 dB of the real amp's.
- `Stage`: the modelled `StageAmp` and synthetic cabinet (the previous default).
- `CleanOnly`: the trimmed pickup DI, no amp or cabinet, for an amp plug-in after the synth.
Changes fade out one 64-frame block and in the next (`GuitarBus.Use`); notes keep sounding.

## DI tone
The pickup DI of 27/29/30 (`Voicings.MeasuredDi`, `MeasuredString`) is fitted to a measured real guitar DI, the kind the captured amp was
trained on: low cut 135 Hz, pickup resonance near 900 Hz under a 2 kHz roll-off, a dark loop (2 kHz decays in about 1 s), a soft pluck and a
two-coil bridge humbucker. `GuitarLayerTests.TheDiMatchesTheMeasuredGuitarDi` keeps its octave-band shape within 3 dB of the measured curve.

## Attack
Every string has a double decay (`StringSpec.Drop` dB over `DropMs`, × velocity^`DropVel`): the pluck starts that much louder and the
loop loses it fast, then the normal decay follows, so each note and chord peaks above its sustain and harder notes peak harder.
`GuitarLayerTests.Attack` guards attack-to-sustain, initial slope and the velocity range per program (real guitar DI: 17.7 dB, -58 dB/s).

## Humanizer
A pass over a track's WRITTEN notes, run by the host before playback or render (TabForge: `SynthHumanizePass` through
`PlaybackOptions.Humanizer`): `Humanizer.Apply(notes, output, program, options, seed)`. Early notes are simply scheduled earlier: no
look-ahead, no latency. Guitars/basses (24-39) and bowed (40-51) only; never drums. Every value is in `HumanTable`.
- Input (`HumanNote`): written on/off ms, key, velocity, string, the performed bar (a key: passes of a repeat differ), the section's first
  bar, beat position, meter (beats, unit), beat length, a stroke id (notes struck together) and written marks (palm mute, legato,
  slide, ghost, dead, accent, pick up/down).
- Window: each bar is judged from `Before` (2) bars back to `After` (4) ahead: energy (strokes per beat) and the evidence that the
  input is flat. Every random value is a pure hash of position (seed, bar, beat), and the slow drifts are octaves of value noise at
  that position, so a start mid-song gives the same notes as the whole song once the window is filled, and an edit changes only bars
  within the window of it (`HumanizerTests`).
- Level (dB, `Dynamics`): every part but one follows from the music: meter (downbeat, middle, beats, 8ths, 16ths from the real time
  signature; compound meters in threes), syncopation, note length, melodic peaks and rising lines, a 4-bar arch from the section start,
  energy, pick hand and string crossing, run grouping, motif memory (the hash of a beat's rhythm and intervals keys its offsets, so the
  same riff gets the same accents) and legato finger unevenness. The only free part, the residue, is slow, smooth and bounded to +-1 dB.
  Ghost and dead notes keep half; palm-mute chugs half of motif and residue. Then the feel's scale, a 3:1 ceiling above +8 dB, +-12 dB cap,
  and a headroom (`HeadDb`): a flat loud input is lowered so its accents fit under velocity 127.
- Timing (ms, `Timing`): bias, slow wander (1-8 bars), smooth pushes and pulls (1/8-1/2 bar), small jitter, the motif's own timing.
  Fast single-note runs (next note within 250 ms): first of each group of 2/3/4 early and louder, upstrokes late and softer (grid
  alternation unless marked), string crossing +1..4 ms, rush on rising and lean on falling lines, legato notes late, a rare smoothly early
  or late note (never a wrong or missing note). Offsets are about constant in ms and capped at 30 % of the neighbouring intervals.
  Lengths change by up to 12 % (15 ms), the same for the same motif position. Chords of 3+ notes keep the strum model (`Strum`).
- Order: per key every note keeps its note-off after its note-on and ends before the same key starts again.
- Leave human input alone (`Evidence`): velocity is human with many distinct values and a spread or short runs; timing with few
  onsets on the grid; human parts get 0.
- Feel presets (`SynthOptions.Feel`): Tight, Natural (default), Loose scale level and timing (0.65 / 1 / 1.5) and set strum, bias and odd-note rate.
- Add a rule: add its amount to `HumanTable`, compute it in `Dynamics.Parts` (a new `LevelParts` field, counted in `Structural` when it
  follows from the music) or in `Timing.Shift`, from the stroke's `Ctx` (add what it needs in `Humanizer.Contexts`, from notes within
  the window only); keep it a function of position and nearby content, and check it in `HumanizerTests`.

`SynthOptions` holds every switch (guitar layer, humanize and its parts: velocity, timing, voice, drift, feel); swap the whole record from any thread; `SynthOptions.Fields` and `Choices` list name, help, get and set for a generated menu.

## API
```csharp
var synth = new Synthesizer(new SoundFont("bank.sf2"), 48000, maxVoices: 64, effects: true) { Layer = new GuitarLayer(48000) };
synth.ProcessMidiMessage(channel, 0x90, key, velocity);
synth.Render(left, right);   // any length, no allocation
synth.Options = synth.Options with { Humanize = true };   // voice variation; any thread, mid-song too
Humanizer.Apply(notes, played, program: 30, synth.Options, seed: 1);   // the note pass, before playing
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
`dotnet test Source/tests/TabForge.Sf2Synth.Tests -c Debug --filter "FullyQualifiedName~Sf2SynthTests|FullyQualifiedName~GuitarLayerTests"`. Adapter checks in the app project: `Source/tests/TabForge.Avalonia.Tests` (`SynthAdapterTests`, `HumanizerAppTests`, `SoundEngineTests`).
Set `TABFORGE_SYNTH_WAV_DIR` to a folder to get listening WAVs from `GuitarLayerTests` and `Sf2SynthTests`.
