using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Builtin.Eq;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the EQ window: the frequency curve with four draggable band points above, then one column per band (on, shelf or bell, frequency, gain, Q), the low cut
//   and high cut rows (frequency and slope), the output gain and the bypass. A point dragged on the curve and a slider of the same band set the same parameter.
// Does not own: the curve and its mouse handling (EqCurveView), the poll and the engine talk (BuiltinEffectPanel), the filters (EqDesign).
// Tests: BuiltinEditorWindowTests.
public sealed class EqEditor : BuiltinEffectPanel
{
    private static readonly IReadOnlyList<string> BandHeadColours = ["Accent", "Success", "Warning", "Danger"];
    private static readonly IReadOnlyList<string> SlopeNames = ["Off", "12 dB/oct", "24 dB/oct"];
    private static readonly IReadOnlyList<string> TypeNames = ["Shelf", "Bell"];
    private readonly EqCurveView _curve = new();
    private readonly double[] _values = new double[EqEffect.Descriptors.Count];

    public EqEditor(IFxChainHost host, TrackModel track, PluginSlot slot)
        : base(host, track, slot, "EQ", EqEffect.Descriptors, new Size2(DialogMetrics.EqWidth, DialogMetrics.EqHeight, DialogMetrics.EqMinWidth, DialogMetrics.EqMinHeight))
    {
        _curve.Changed += Send;
        var bands = new ResponsiveColumns { MinColumnWidth = DialogMetrics.FxBandColumnMinWidth };
        for (var b = 0; b < EqEffect.Bands; b++) bands.Children.Add(BandColumn(b));
        var cuts = new ResponsiveColumns { MinColumnWidth = DialogMetrics.FxCutColumnMinWidth };
        cuts.Children.Add(CutColumn(false));
        cuts.Children.Add(CutColumn(true));
        var footer = new StackPanel
        {
            Children =
            {
                bands,
                cuts.Spaced(DialogMetrics.FxSectionGap),
                Slider(EqEffect.OutputId, "Output gain", "Level after the filters, in dB.").Spaced(DialogMetrics.FxRowGap),
                Switch(EqEffect.BypassId, "Bypass", "Hear the sound without the EQ (a short fade, no click)."),
            },
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        Frame(new DockPanel { Children = { footer, _curve.Spaced(DialogMetrics.FxRowGap) } });
        for (var i = 0; i < _values.Length; i++) _values[i] = EqEffect.Descriptors[i].Default;
    }

    private Control BandColumn(int band)
    {
        var n = band + 1;
        var head = new TextBlock { Text = $"Band {n}", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        head[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(BandHeadColours[band]);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Children = { head, Switch(EqEffect.BandId(band, EqEffect.OnField), "On", $"Band {n} on or off.").WithMargin(DialogMetrics.FxButtonGap) } };
        var column = new StackPanel { Children = { top.Spaced(DialogMetrics.FxBandHeadGap) } };
        if (EqEffect.HasType(band))
            column.Children.Add(Choice(band == 0 ? EqEffect.Band1TypeId : EqEffect.Band4TypeId, "Type", band == 0 ? "A low shelf lifts or cuts everything below the frequency; a bell works around it." : "A high shelf lifts or cuts everything above the frequency; a bell works around it.", TypeNames, compact: true).Spaced(DialogMetrics.FxRowGap));
        column.Children.Add(Slider(EqEffect.BandId(band, EqEffect.FreqField), "Freq", $"Band {n} frequency.", ParamScale.Log, compact: true, text: ParamSlider.Hertz).Spaced(DialogMetrics.FxRowGap));
        column.Children.Add(Slider(EqEffect.BandId(band, EqEffect.GainField), "Gain", $"Band {n} boost or cut in dB.", compact: true).Spaced(DialogMetrics.FxRowGap));
        column.Children.Add(Slider(EqEffect.BandId(band, EqEffect.QField), "Q", $"Band {n} width: a higher Q is narrower. The mouse wheel on the curve changes it too.", ParamScale.Log, compact: true, text: q => q.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Spaced(DialogMetrics.FxRowGap));
        return column.Spaced(band < EqEffect.Bands - 1 ? DialogMetrics.FxColumnGap : default);
    }

    private Control CutColumn(bool high)
    {
        var column = new StackPanel
        {
            Children =
            {
                Slider(high ? EqEffect.HighCutFreqId : EqEffect.LowCutFreqId, high ? "High cut" : "Low cut", high ? "Frequency above which the sound is cut." : "Frequency below which the sound is cut.", ParamScale.Log, text: ParamSlider.Hertz).Spaced(DialogMetrics.FxRowGap),
                Choice(high ? EqEffect.HighCutSlopeId : EqEffect.LowCutSlopeId, "Slope", "Off, or how steeply the cut falls: 12 or 24 dB per octave.", SlopeNames).Spaced(DialogMetrics.FxRowGap),
            },
        };
        return column.Spaced(high ? default : DialogMetrics.FxColumnGap);
    }

    protected override void ShowValues(IReadOnlyDictionary<uint, PluginParameterInfo> byId)
    {
        for (var i = 0; i < _values.Length; i++) _values[i] = byId.TryGetValue(EqEffect.Descriptors[i].Id, out var p) ? p.Value : EqEffect.Descriptors[i].Default;
        _curve.SetValues(_values);
    }

    /// <summary>The curve (for tests).</summary>
    public EqCurveView Curve => _curve;
}
