using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using TabForge.Audio.Contracts;
using TabForge.Audio.Contracts.Builtin;
using CompressorEffect = TabForge.Audio.Contracts.Builtin.CompressorParameters;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the compressor window: the transfer curve with the live input dot on the left; threshold, ratio, knee, attack, release, makeup, auto makeup and bypass,
//   the factory presets (Bass, Vocal, Guitar, the same list the FX chain preset box shows) and the gain-reduction and input meters on the right.
// Does not own: the curve picture (TransferCurveView), the poll and the engine talk (BuiltinEffectPanel), the sound and the curve maths (CompressorEffect, CompressorCurve).
// Tests: BuiltinEditorWindowTests.
public sealed class CompressorEditor : BuiltinEffectPanel
{
    protected override bool HasReadouts => true;

    private const double MeterReductionDb = 24, MeterFloorDb = -60, WarmDb = -6, ClipDb = -0.5;
    private readonly TransferCurveView _curve = new();
    private readonly MeterBar _reduction = new("CompressorReductionMeter", "Warning", fromRight: true), _level = new("CompressorLevelMeter", "Success");
    private readonly TextBlock _reductionText = new() { Name = "CompressorReductionText" }, _levelText = new() { Name = "CompressorLevelText" };
    private readonly ComboBox _presets = new() { Name = "CompressorPresets", PlaceholderText = "Factory preset…" };
    private readonly Dictionary<string, string> _displayToName = new(StringComparer.OrdinalIgnoreCase);
    private const string PresetKind = "Builtin:compressor";
    private bool _applyingPreset;
    private ParamSlider _threshold = null!, _ratio = null!, _knee = null!;

    public CompressorEditor(IFxChainHost host, TrackModel track, PluginSlot slot)
        : base(host, track, slot, "Compressor", CompressorEffect.Descriptors, new Size2(DialogMetrics.CompressorWidth, DialogMetrics.CompressorHeight, DialogMetrics.CompressorMinWidth, DialogMetrics.CompressorMinHeight))
    {
        ToolTip.SetTip(_presets, "Sets the compressor for a bass, a voice or a guitar. The settings stay yours to change.");
        _presets.SelectionChanged += (_, _) => ApplyPreset();
        FillPresets();
        PresetCombo.Watch(_presets, () => Slot.AppliedPreset is { Kind: PresetKind } p ? p : null, () => Slot.State, FillPresets);
        _threshold = Slider(CompressorEffect.ThresholdId, "Threshold", "Level above which the compressor works.");
        _ratio = Slider(CompressorEffect.RatioId, "Ratio", "How much the level above the threshold is reduced: 4 : 1 lets 4 dB in give 1 dB out.");
        _knee = Slider(CompressorEffect.KneeId, "Knee", "0 is a sharp corner at the threshold; more eases into the compression.");
        var controls = new StackPanel
        {
            Children =
            {
                _presets.Spaced(DialogMetrics.FxRowGap),
                _threshold.Spaced(DialogMetrics.FxRowGap),
                _ratio.Spaced(DialogMetrics.FxRowGap),
                _knee.Spaced(DialogMetrics.FxRowGap),
                Slider(CompressorEffect.AttackId, "Attack", "How fast the compressor reacts to a louder sound.", ParamScale.Log).Spaced(DialogMetrics.FxRowGap),
                Slider(CompressorEffect.ReleaseId, "Release", "How fast the compressor lets go after the sound gets quieter.", ParamScale.Log).Spaced(DialogMetrics.FxRowGap),
                Slider(CompressorEffect.MakeupId, "Makeup gain", "Gain added after compressing (ignored while Auto makeup is on).").Spaced(DialogMetrics.FxRowGap),
                Row(Switch(CompressorEffect.AutoMakeupId, "Auto makeup", "Adds the gain that brings a full-scale signal back to full scale."), Switch(CompressorEffect.BypassId, "Bypass", "Hear the sound without the compressor (a short fade, no click).")),
                MeterRow("Gain reduction", _reduction, _reductionText).Spaced(DialogMetrics.FxSectionGap),
                MeterRow("Input level", _level, _levelText),
            },
        };
        var body = new ResponsiveColumns { MinColumnWidth = DialogMetrics.FxCompressorColumnMinWidth };
        body.Children.Add(_curve.Spaced(DialogMetrics.FxColumnGap));
        body.Children.Add(controls);
        Frame(body);
        _curve.SetCurve(_threshold.Value, _ratio.Value, _knee.Value);
    }

    private void ApplyPreset()
    {
        if (_applyingPreset || _presets.SelectedItem is not string shown || !_displayToName.TryGetValue(shown, out var name)) return;
        if (BuiltinPresets.State(BuiltinEffects.CompressorId, name) is { } state)
        {
            ApplyState(state);
            Slot.AppliedPreset = PresetCombo.Remember(name, PresetKind, Slot.State);
        }
        FillPresets();
    }

    /// <summary>Lists the factory presets with the remembered one selected (and marked when the settings differ).</summary>
    private void FillPresets()
    {
        _applyingPreset = true;
        try
        {
            var applied = Slot.AppliedPreset is { Kind: PresetKind } p ? p : null;
            var names = BuiltinPresets.Names(BuiltinEffects.CompressorId);
            var items = new List<string>();
            _displayToName.Clear();
            foreach (var name in names)
            {
                var text = PresetCombo.Decorate(name, name, applied, Slot.State, true);
                if (!_displayToName.ContainsKey(text)) _displayToName[text] = name;
                items.Add(text);
            }
            if (applied is { } missing && !names.Contains(missing.Name, StringComparer.OrdinalIgnoreCase))
            {
                var text = PresetCombo.Decorate(missing.Name, missing.Name, applied, Slot.State, false);
                _displayToName[text] = missing.Name;
                items.Add(text);
            }
            _presets.ItemsSource = items;
            _presets.SelectedItem = applied is { } pick
                ? items.FirstOrDefault(text => string.Equals(_displayToName[text], pick.Name, StringComparison.OrdinalIgnoreCase))
                : null;
        }
        finally { _applyingPreset = false; }
    }

    protected override void ShowValues(IReadOnlyDictionary<uint, PluginParameterInfo> byId)
    {
        double V(uint id, double fallback) => byId.TryGetValue(id, out var p) ? p.Value : fallback;
        _curve.SetCurve(V(CompressorEffect.ThresholdId, 0), V(CompressorEffect.RatioId, 1), V(CompressorEffect.KneeId, 0));
        var reduction = V(CompressorEffect.GainReductionId, 0);
        var level = V(CompressorEffect.InputLevelId, CompressorEffect.MeterFloorDb);
        _reduction.Set(reduction / MeterReductionDb);
        _reductionText.Text = reduction.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        _level.Set(Fraction(level, MeterFloorDb), level >= ClipDb ? "Danger" : level >= WarmDb ? "Warning" : "Success");
        _levelText.Text = level <= CompressorEffect.MeterFloorDb ? "-inf dB" : level.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        _curve.SetLevel(level);
    }

    /// <summary>The curve (for tests).</summary>
    public TransferCurveView Curve => _curve;
}
