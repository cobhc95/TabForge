using System.Linq;
using Avalonia.Controls;
using TabForge.Audio.Contracts.Builtin;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: which built-in effect has an editor in TabForge, making it, and popping it out into a window (one per slot: a second open brings the first to the front). A built-in without
//   one shows the generic controls only. Adding an effect's editor: one case in CreateEditor and in HasWindow.
// Does not own: the editors themselves (IrLoaderEditor, BuiltinEffectPanel and its subclasses), the pop-out window (BuiltinEditorWindow), the embedding (BuiltinEditorHost), the generic controls (FxParameterPanel), the FX chain window's buttons that call this.
// Tests: BuiltinEffectTests.
public static class BuiltinEditors
{
    /// <summary>True for a slot that is one of TabForge's own effects (its controls show in the FX chain window instead of a plug-in editor).</summary>
    public static bool IsBuiltin(PluginSlot? slot) => slot is not null && BuiltinEffects.IsBuiltin(slot.Path);

    /// <summary>True when the built-in effect of <paramref name="slot"/> has its own window.</summary>
    public static bool HasWindow(PluginSlot? slot) => slot is not null && BuiltinEffects.Find(slot.Path)?.Id is
        BuiltinEffects.IrLoaderId or BuiltinEffects.EqId or BuiltinEffects.CompressorId or BuiltinEffects.LimiterId or BuiltinEffects.DelayId or BuiltinEffects.GateId;

    /// <summary>A new editor panel for the built-in effect of <paramref name="slot"/> (null when it has none); the caller places it: embedded (BuiltinEditorHost) or in a BuiltinEditorWindow.</summary>
    public static Control? CreateEditor(IFxChainHost host, TrackModel track, PluginSlot slot) => !HasWindow(slot) ? null : BuiltinEffects.Find(slot.Path)!.Id switch
    {
        BuiltinEffects.EqId => new EqEditor(host, track, slot),
        BuiltinEffects.CompressorId => new CompressorEditor(host, track, slot),
        BuiltinEffects.LimiterId => new LimiterEditor(host, track, slot),
        BuiltinEffects.DelayId => new DelayEditor(host, track, slot),
        BuiltinEffects.GateId => new GateEditor(host, track, slot),
        _ => new IrLoaderEditor(host, track, slot),
    };

    /// <summary>Pops the editor out into a window over <paramref name="owner"/> (one per slot: a second call brings the first forward); false when the slot has no editor.</summary>
    public static bool TryOpen(Window owner, IFxChainHost host, TrackModel track, PluginSlot slot)
    {
        if (!HasWindow(slot)) return false;
        if (Find(owner, slot) is { } open)
        {
            open.Activate();
            return true;
        }
        new BuiltinEditorWindow(slot, CreateEditor(host, track, slot)!).Show(owner);
        return true;
    }

    /// <summary>The pop-out window of this slot that is open over <paramref name="owner"/>, or null.</summary>
    public static Window? Find(Window owner, PluginSlot slot) =>
        owner.OwnedWindows.OfType<BuiltinEditorWindow>().FirstOrDefault(w => ReferenceEquals(w.Slot, slot));
}
