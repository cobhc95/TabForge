using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using TabForge.Audio.Contracts;
using GateEffect = TabForge.Audio.Contracts.Builtin.GateParameters;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the noise gate window: threshold, attack, hold, release, range and bypass, the open / closed light, and the input level bar with the threshold mark.
// Does not own: the poll and the engine talk (BuiltinEffectPanel), the sound (GateEffect).
// Tests: BuiltinEditorWindowTests.
public sealed class GateEditor : BuiltinEffectPanel
{
    protected override bool HasReadouts => true;

    private const double LevelFloorDb = -80;
    private readonly StateLight _light = new("GateLight");
    private readonly TextBlock _state = new() { Name = "GateState", Text = "Closed", VerticalAlignment = VerticalAlignment.Center };
    private readonly MeterBar _level = new("GateLevelMeter", "Success");
    private readonly TextBlock _levelText = new() { Name = "GateLevelText" };

    public GateEditor(IFxChainHost host, TrackModel track, PluginSlot slot)
        : base(host, track, slot, "Noise Gate", GateEffect.Descriptors, new Size2(DialogMetrics.GateWidth, DialogMetrics.GateHeight, DialogMetrics.GateMinWidth, DialogMetrics.GateMinHeight))
    {
        ToolTip.SetTip(_light, "Lit while the gate is open.");
        Frame(new StackPanel
        {
            Children =
            {
                Slider(GateEffect.ThresholdId, "Threshold", "Level above which the gate opens (it closes 3 dB lower).").Spaced(DialogMetrics.FxRowGap),
                Slider(GateEffect.AttackId, "Attack", "How fast the gate opens.", ParamScale.Log).Spaced(DialogMetrics.FxRowGap),
                Slider(GateEffect.HoldId, "Hold", "How long the gate stays open after the sound falls below the threshold.").Spaced(DialogMetrics.FxRowGap),
                Slider(GateEffect.ReleaseId, "Release", "How fast the gate closes.", ParamScale.Log).Spaced(DialogMetrics.FxRowGap),
                Slider(GateEffect.RangeId, "Range", "How far the sound is turned down while the gate is closed: 0 dB does nothing, -80 dB is silence.").Spaced(DialogMetrics.FxRowGap),
                Row(Switch(GateEffect.BypassId, "Bypass", "Hear the sound without the gate (a short fade, no click).")),
                MeterRow("Input level", _level, _levelText).Spaced(DialogMetrics.FxSectionGap),
                Row(_light, _state),
            },
        });
    }

    protected override void ShowValues(IReadOnlyDictionary<uint, PluginParameterInfo> byId)
    {
        double V(uint id, double fallback) => byId.TryGetValue(id, out var p) ? p.Value : fallback;
        var open = V(GateEffect.OpenId, 0) >= 0.5;
        var level = V(GateEffect.LevelId, GateEffect.MeterFloorDb);
        _light.Set(open);
        _state.Text = open ? "Open" : "Closed";
        _level.Set(Fraction(level, LevelFloorDb), open ? "Success" : "Warning");
        _level.SetMarker(Fraction(V(GateEffect.ThresholdId, GateEffect.MinThreshold), LevelFloorDb));
        _levelText.Text = level <= GateEffect.MeterFloorDb ? "-inf dB" : level.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
    }

    /// <summary>The open light (for tests).</summary>
    public StateLight Light => _light;
}
