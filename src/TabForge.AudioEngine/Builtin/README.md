# Built-in effects

TabForge's own effects: C# classes that run inside the engine process (Windows and Linux), with no file, scan or trust step.
A slot's `Path` is the built-in id (`builtin:...`) and its `Format` is "Built-in"; the state is `BuiltinState` JSON.

## Add an effect (a few lines outside its own class)
One-page recipe: `../../../docs/guides/ADD_EFFECT.md`.
1. `TabForge.Audio.Contracts/Builtin/BuiltinEffects.cs`: an id constant and an entry in `All` (name, description). It then shows first in the Add plug-in window.
2. `TabForge.Audio.Contracts/Builtin/BuiltinParameterCatalog.cs`: the parameter ids, keys and descriptors (shared with the editor; return them from `BuiltinParameters.Of`).
3. `BuiltinEffectFactory.cs`: one case `BuiltinEffects.MyId => new MyEffect(sampleRate, maxBlock)`.
4. `MyEffect : BuiltinEffect` in its own folder here: pass the descriptor list and text keys (file paths),
   implement `ProcessStereo` (audio thread: no allocation, no locks; read values with `Value(i)`), react in `OnChanged` / `OnTextChanged` (main thread).
   Generic controls, automation, presets, bypass, undo of the panel and saving come from the base class.
5. Optional own window: a case in `BuiltinEditors` (app, Views/Builtin), reading ids and descriptors from `Contracts.Builtin`. Without one the FX chain window shows the generic controls.
6. Tests next to `BuiltinEffectTests`.

## Pieces
- `BuiltinEffect` (+ `.State.cs`): IPluginInstance + IPluginParameters base, partial state merge, Edited / Problem events (the session forwards them).
- `PartitionedConvolver`: zero-latency convolution (direct head + uniformly-partitioned FFT stages); `Fft`.
- `IrLoader/`: `IrLoaderEffect` (worker-built `IrKernel`, swapped in one reference), `IrWaveFile`, `IrPrep`, `IrPictures`.
- `Dsp/`: what the effects share: `DspMath` (dB, one-pole time constants, denormal flush, `Smoothed`), `Biquad` (RBJ coefficients, section state, response from the coefficients).
- `Eq/`: `EqEffect` (+ `.Process`), `EqBand`, `EqCut`, `EqDesign` (coefficients and the curve the editor draws).
- `Compressor/`: `CompressorEffect` (+ `.Process`), `CompressorCurve` (static gain computer, also drawn by the editor).
- `Limiter/`: `LimiterEffect` (+ `.Process`), `TruePeakDetector` (4x), `SlidingMinimum`; reports its look-ahead as latency.
- `Delay/`: `DelayEffect` (+ `.Process`); the note lengths (`DelayDivision`) and the other parameter tables are in `Audio.Contracts/Builtin`.
- `Gate/`: `GateEffect` (+ `.Process`).
- Windows for these are in `Views/Builtin/<Effect>/` in the app; they share `BuiltinEffectPanel`, `BuiltinParamControls`, `BuiltinMeters`. Factory presets: `BuiltinPresets` (Contracts).
- Rules for every effect: no allocation or locks in `ProcessStereo`, glides (`Smoothed`) for every knob, denormals flushed, times from milliseconds and the sample rate, a bypass that fades, ids and keys never change.
