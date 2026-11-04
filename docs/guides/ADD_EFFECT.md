# Add a built-in effect (one page)

Built-in effects are TabForge's own C# effects: no plug-in file, scan or trust step. The pieces are:

- Source/src/TabForge.Audio.Contracts/Builtin/BuiltinEffects.cs: the stable id, name and description.
- Source/src/TabForge.Audio.Contracts/Builtin/BuiltinParameterCatalog.cs: the parameter ids, keys and descriptor list the editors and the engine share.
- Source/src/TabForge.AudioEngine/Builtin/: the effect class and its DSP.
- Source/src/TabForge.Avalonia/Views/Builtin/: the optional editor window.

## Steps

1. **Id and name.** Add an id constant (Prefix + "my-effect") and an entry to BuiltinEffects.All in BuiltinEffects.cs. Never change a shipped id.
2. **Parameters.** Add a MyEffectParameters class to BuiltinParameterCatalog.cs with the ids, keys, ranges and defaults, and return it from BuiltinParameters.Of. Keys are saved in songs and presets: never change a shipped key.
3. **Effect.** Add MyEffect : BuiltinEffect under Source/src/TabForge.AudioEngine/Builtin/MyEffect/:
   - pass BuiltinEffects.Find(BuiltinEffects.MyEffectId)! and the descriptor list to the base constructor;
   - implement ProcessStereo (audio thread: no allocation, no locks, no exceptions);
   - read values with Value(i), smooth every knob (DspMath.Smoothed), flush denormals, fade on bypass;
   - react to main-thread changes in OnChanged / OnTextChanged, and override TextOf when the value needs special wording.
4. **Factory.** Add one case to BuiltinEffectFactory.Create: BuiltinEffects.MyEffectId => new MyEffect(sampleRate, maxBlock).
5. **Editor (optional).** Add a case to BuiltinEditors.CreateEditor and HasWindow in Views/Builtin/BuiltinEditors.cs, then a MyEffectEditor : BuiltinEffectPanel in Views/Builtin/MyEffect/. Read ids and descriptors from the Contracts.Builtin alias (as the other editors do), never from the engine namespace.
6. **Tests.** Extend BuiltinEffectSetTests (every descriptor key exists and the engine list matches the catalog) and add a DSP test beside BuiltinEffectTests.

Keep the file split at the same seams as the existing effects (MyEffect.cs for parameters/state, MyEffect.Process.cs for the audio thread) so each file stays under the size limit.
