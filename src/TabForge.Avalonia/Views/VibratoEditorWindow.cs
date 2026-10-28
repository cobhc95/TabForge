using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the vibrato settings fields (Type Normal/Wide, Depth and Speed sliders with their value labels, the hint line), in an EffectValuesWindow.
// Does not own: the frame (EffectValuesWindow), the model writes (Core VibratoEdits) or the flow (VibratoEditorFlow).
// Tests: TabForge.Avalonia.Tests.VibratoSettingsTests.
public static class VibratoEditorWindow
{
    public const double DepthMin = 0.05, DepthMax = 3, SpeedMin = 1, SpeedMax = 12;

    /// <summary>The window; its values are Type (0 normal, 1 wide), Depth (semitones) and Speed (Hz).</summary>
    public static EffectValuesWindow Create(bool wide, double depth, double speed, double normalDefault, double wideDefault, double speedDefault, bool canClean)
    {
        var kind = new OrnamentEditorWindows.ChoiceField("Type", new[] { ("Normal", 0), ("Wide", 1) }, wide ? 1 : 0);
        var depthRow = Row("Depth", DepthMin, DepthMax, depth, v => v.ToString("0.00", CultureInfo.InvariantCulture) + " st", out var depthSlider);
        var speedRow = Row("Speed", SpeedMin, SpeedMax, speed, v => v.ToString("0.0", CultureInfo.InvariantCulture) + " Hz", out var speedSlider);
        var hint = new TextBlock { Text = "Per note (right-click) or global default (Settings)" };
        DialogMetrics.SpaceVibratoHint(hint);
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var fields = new StackPanel { MinWidth = DialogMetrics.VibratoFieldsWidth, Children = { kind, depthRow, speedRow, hint } };
        return new EffectValuesWindow("Vibrato settings", fields,
            () => new() { ["Type"] = kind.Value, ["Depth"] = depthSlider.Value, ["Speed"] = speedSlider.Value },
            v =>
            {
                kind.Value = EffectValuesPresenter.IntValue(v, "Type", 0);
                depthSlider.Value = v.TryGetValue("Depth", out var d) ? d : kind.Value == 1 ? wideDefault : normalDefault;
                speedSlider.Value = v.TryGetValue("Speed", out var s) ? s : speedDefault;
            },
            Array.Empty<EffectValuesPreset>(), canClean, _ => null, showPresets: false, cleanLabel: "Remove");
    }

    private static StackPanel Row(string label, double min, double max, double value, Func<double, string> format, out Slider slider)
    {
        var text = new TextBlock { Width = DialogMetrics.VibratoValueWidth, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var made = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = DialogMetrics.VibratoSliderWidth, Name = label + "Slider" };
        AutomationProperties.SetName(made, label);
        text.Text = format(made.Value);
        made.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) text.Text = format(made.Value); };
        slider = made;
        var name = new TextBlock { Text = label, Width = DialogMetrics.VibratoNameWidth, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, made, text } };
        DialogMetrics.SpaceVibratoRow(row);
        return row;
    }
}
