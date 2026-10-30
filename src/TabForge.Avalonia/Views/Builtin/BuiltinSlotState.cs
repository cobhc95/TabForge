using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.Audio.Contracts.Builtin;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Builtin;

// Owns: the state of a built-in slot when the engine gave none for it: built from the effect's CURRENT parameter values (the same answer the editors poll) plus the
//   file paths and options the slot already holds (the IR Loader's files). Never defaults: when the values cannot be read the caller shows the error.
// Does not own: the engine's own state reply (AudioEngineClient), saving presets (PluginLibrary, FxChainWindow).
// Tests: BuiltinPresetRoundTripTests.
public static class BuiltinSlotState
{
    /// <summary>Asks the engine for the effect's current parameters and puts them in the slot's state. False when the slot is no built-in, the engine does not answer in time, or the answer does not fit.</summary>
    public static async Task<bool> CaptureAsync(IFxChainEngine engine, TrackModel track, PluginSlot slot, int timeoutMs)
    {
        if (!BuiltinEffects.IsBuiltin(slot.Path) || !engine.IsRunning) return false;
        var engineSlot = engine.SlotOf(track);
        var index = track.Rig.Plugins.IndexOf(slot);
        var done = new TaskCompletionSource<IReadOnlyList<PluginParameterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void On(int s, int i, IReadOnlyList<PluginParameterInfo> list) { if (s == engineSlot && i == index) done.TrySetResult(list); }
        engine.ParametersReceived += On;
        try
        {
            engine.RequestParameters(track, slot);
            if (await Task.WhenAny(done.Task, Task.Delay(timeoutMs)) != done.Task) return false;
            return FromParameters(slot, await done.Task);
        }
        finally { engine.ParametersReceived -= On; }
    }

    /// <summary>Sets the slot's state from the engine's parameter list: every editable parameter's current value, the text values (files) the slot's state already holds. False when the list is empty or names no known parameter.</summary>
    public static bool FromParameters(PluginSlot slot, IReadOnlyList<PluginParameterInfo> list)
    {
        var descriptors = BuiltinParameters.Of(slot.Path);
        if (descriptors.Count == 0 || list.Count == 0) return false;
        var state = new BuiltinState();
        try { if (slot.State is { Length: > 0 } old) state = BuiltinState.Parse(Convert.FromBase64String(old)); }
        catch (Exception ex) when (ex is FormatException or System.IO.InvalidDataException) { state = new BuiltinState(); } // Not logged: an unreadable old state contributes no files
        var byId = new Dictionary<uint, PluginParameterInfo>();
        foreach (var p in list) byId[p.Id] = p;
        var found = 0;
        foreach (var p in descriptors)
        {
            if (p.ReadOnly || !byId.TryGetValue(p.Id, out var live) || !double.IsFinite(live.Value)) continue;
            state.Values[p.Key] = live.Value;
            found++;
        }
        if (found == 0) return false;
        slot.State = Convert.ToBase64String(state.ToBytes());
        return true;
    }
}
