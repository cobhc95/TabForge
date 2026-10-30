using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Audio.Contracts;
using LimiterEffect = TabForge.Audio.Contracts.Builtin.LimiterParameters;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the limiter window: input gain, ceiling (dBTP), release and auto release, bypass, the gain-reduction and output-level meters, and the look-ahead / latency line.
// Does not own: the poll and the engine talk (BuiltinEffectPanel), the sound (LimiterEffect).
// Tests: BuiltinEditorWindowTests.
public sealed class LimiterEditor : BuiltinEffectPanel
{
    protected override bool HasReadouts => true;

    private const double ReductionRangeDb = 12, LevelFloorDb = -36, WarmDb = -3;
    private readonly MeterBar _reduction = new("LimiterReductionMeter", "Warning", fromRight: true), _level = new("LimiterLevelMeter", "Success");
    private readonly TextBlock _reductionText = new() { Name = "LimiterReductionText" }, _levelText = new() { Name = "LimiterLevelText" };
    private readonly TextBlock _latency = new() { Name = "LimiterLatency", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };

    public LimiterEditor(IFxChainHost host, TrackModel track, PluginSlot slot)
        : base(host, track, slot, "Limiter", LimiterEffect.Descriptors, new Size2(DialogMetrics.LimiterWidth, DialogMetrics.LimiterHeight, DialogMetrics.LimiterMinWidth, DialogMetrics.LimiterMinHeight))
    {
        Frame(new StackPanel
        {
            Children =
            {
                Slider(LimiterEffect.InputGainId, "Input gain", "Gain before the limiter: push the sound into it.").Spaced(DialogMetrics.FxRowGap),
                Slider(LimiterEffect.CeilingId, "Ceiling", "The highest true peak the output reaches (dBTP, found at 4 times the sample rate).").Spaced(DialogMetrics.FxRowGap),
                Slider(LimiterEffect.ReleaseId, "Release", "How fast the gain comes back after a peak (ignored while Auto release is on).", ParamScale.Log).Spaced(DialogMetrics.FxRowGap),
                Row(Switch(LimiterEffect.AutoReleaseId, "Auto release", "Fast for single peaks, slower for sustained loud sound."), Switch(LimiterEffect.BypassId, "Bypass", "Hear the sound without the limiter (a short fade, no click).")),
                MeterRow("Gain reduction", _reduction, _reductionText).Spaced(DialogMetrics.FxSectionGap),
                MeterRow("Output level", _level, _levelText),
                _latency.Spaced(DialogMetrics.FxSectionGap),
            },
        });
    }

    protected override void ShowValues(IReadOnlyDictionary<uint, PluginParameterInfo> byId)
    {
        double V(uint id, double fallback) => byId.TryGetValue(id, out var p) ? p.Value : fallback;
        var reduction = V(LimiterEffect.GainReductionId, 0);
        var level = V(LimiterEffect.OutputLevelId, LimiterEffect.MeterFloorDb);
        _reduction.Set(reduction / ReductionRangeDb);
        _reductionText.Text = reduction.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        _level.Set(Fraction(level, LevelFloorDb), level >= V(LimiterEffect.CeilingId, 0) + 0.2 ? "Danger" : level >= WarmDb ? "Warning" : "Success");
        _levelText.Text = level <= LimiterEffect.MeterFloorDb ? "-inf dB" : level.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        _latency.Text = byId.TryGetValue(LimiterEffect.LatencyId, out var latency) ? $"Look-ahead {LimiterEffect.LookAheadMs:0.0} ms · latency {latency.Text} (reported to the engine, so other tracks stay in time)" : "";
    }
}
