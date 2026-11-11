using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Audio.Contracts;
using TabForge.Audio.Contracts.Builtin;
using ReverbEffect = TabForge.Audio.Contracts.Builtin.ReverbParameters;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the reverb window: Size, Predelay, Mix, high cut and low cut on the tail, width and bypass.
// Does not own: the poll and the engine talk (BuiltinEffectPanel), the sound (ReverbEffect).
// Tests: BuiltinEditorWindowTests.
public sealed class ReverbEditor : BuiltinEffectPanel
{
    public ReverbEditor(IFxChainHost host, TrackModel track, PluginSlot slot)
        : base(host, track, slot, "Reverb", ReverbEffect.Descriptors, new Size2(DialogMetrics.ReverbWidth, DialogMetrics.ReverbHeight, DialogMetrics.ReverbMinWidth, DialogMetrics.ReverbMinHeight))
    {
        Frame(new StackPanel
        {
            Children =
            {
                Slider(ReverbEffect.SizeId, "Size", "The room size: longer comb delays for a bigger space.").WithMargin(DialogMetrics.FxRowGap),
                Slider(ReverbEffect.PredelayId, "Predelay", "How much time passes before the first reflections, for separation from the dry sound.").WithMargin(DialogMetrics.FxRowGap),
                Slider(ReverbEffect.MixId, "Mix", "Level of the tail; the dry sound is not changed.").WithMargin(DialogMetrics.FxRowGap),
                Slider(ReverbEffect.HighCutId, "High cut", "Takes the brightness out of the tail (left at the highest value it does nothing).", ParamScale.Log, text: ParamSlider.Hertz).WithMargin(DialogMetrics.FxRowGap),
                Slider(ReverbEffect.LowCutId, "Low cut", "Takes the bass out of the tail (left at the lowest value it does nothing).", ParamScale.Log, text: ParamSlider.Hertz).WithMargin(DialogMetrics.FxRowGap),
                Slider(ReverbEffect.WidthId, "Width", "How wide the tail spreads from mono (0) to full stereo (100).").WithMargin(DialogMetrics.FxRowGap),
                Row(Switch(ReverbEffect.BypassId, "Bypass", "Hear the sound without the reverb (a short fade, no click).")),
            },
        });
    }

    protected override void ShowValues(IReadOnlyDictionary<uint, PluginParameterInfo> byId) { }
}
