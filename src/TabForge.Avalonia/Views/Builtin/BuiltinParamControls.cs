using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using TabForge.AudioEngine.Builtin;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the parameter controls of the built-in effect windows: a slider row (linear or logarithmic, double-click resets to the default), a choice box for a
//   stepped parameter, and a check box for a switch. Each shows the value the engine reported without sending it back, and sends only what the person changed.
// Does not own: the engine talk and the poll (BuiltinEffectPanel), the windows that place them.
// Tests: BuiltinEditorWindowTests.
public enum ParamScale { Linear, Log }

/// <summary>A control that shows one effect parameter: <see cref="Id"/> is the parameter id, <see cref="Show"/> takes the value the engine reported.</summary>
public interface IParamControl
{
    uint Id { get; }
    void Show(double value);
}

public sealed class ParamSlider : Grid, IParamControl
{
    private readonly BuiltinParameter _parameter;
    private readonly ParamScale _scale;
    private readonly Func<double, string> _text;
    private readonly Slider _slider;
    private readonly TextBlock _value = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _setting;

    public ParamSlider(BuiltinParameter parameter, string label, string tip, ParamScale scale, bool compact, Func<double, string>? text, Action<uint, double> send)
    {
        (_parameter, _scale) = (parameter, scale);
        _text = text ?? parameter.Format;
        Name = "Fx" + parameter.Key + "Row";
        ColumnDefinitions = new ColumnDefinitions($"{(compact ? DialogMetrics.FxBandLabelWidth : DialogMetrics.FxLabelWidth)},*,{(compact ? DialogMetrics.FxBandValueWidth : DialogMetrics.FxValueWidth)}");
        _slider = parameter.Stepped
            ? new Slider { Minimum = parameter.Min, Maximum = parameter.Max, TickFrequency = 1, IsSnapToTickEnabled = true, Name = "Fx" + parameter.Key }
            : new Slider { Minimum = 0, Maximum = 1, Name = "Fx" + parameter.Key };
        ToolTip.SetTip(_slider, tip + " Double-click resets it.");
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(_slider, 1);
        SetColumn(_value.WithMargin(DialogMetrics.FxButtonGap), 2);
        Children.Add(name);
        Children.Add(_slider);
        Children.Add(_value);
        _slider.ValueChanged += (_, e) =>
        {
            var value = FromSlider(e.NewValue);
            _value.Text = _text(value);
            if (!_setting) send(parameter.Id, value);
        };
        _slider.DoubleTapped += (_, _) => { Show(parameter.Default, force: true); send(parameter.Id, parameter.Default); };
        Show(parameter.Default, force: true);
    }

    public uint Id => _parameter.Id;

    /// <summary>The value the slider shows now.</summary>
    public double Value => FromSlider(_slider.Value);

    public void Show(double value) => Show(value, force: false);

    private void Show(double value, bool force)
    {
        if (!force && _slider.IsPointerOver) return;   // the person is holding it: the engine echo would fight the drag
        _setting = true;
        try { _slider.Value = ToSlider(value); }
        finally { _setting = false; }
        _value.Text = _text(value);
    }

    private double ToSlider(double value)
    {
        if (_parameter.Stepped) return value;
        var v = Math.Clamp(value, _parameter.Min, _parameter.Max);
        return _scale == ParamScale.Log ? Math.Log(v / _parameter.Min) / Math.Log(_parameter.Max / _parameter.Min) : (v - _parameter.Min) / (_parameter.Max - _parameter.Min);
    }

    private double FromSlider(double x)
    {
        if (_parameter.Stepped) return Math.Round(x);
        return _scale == ParamScale.Log ? _parameter.Min * Math.Pow(_parameter.Max / _parameter.Min, x) : _parameter.Min + x * (_parameter.Max - _parameter.Min);
    }

    /// <summary>"1.25 kHz" or "440 Hz".</summary>
    public static string Hertz(double hz) => hz >= 1000 ? (hz / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " kHz" : hz.ToString("0", CultureInfo.InvariantCulture) + " Hz";
}

public sealed class ParamChoice : Grid, IParamControl
{
    private readonly BuiltinParameter _parameter;
    private readonly ComboBox _box = new();
    private bool _setting;

    public ParamChoice(BuiltinParameter parameter, string label, string tip, IReadOnlyList<string> names, bool compact, Action<uint, double> send)
    {
        _parameter = parameter;
        Name = "Fx" + parameter.Key + "Row";
        ColumnDefinitions = new ColumnDefinitions($"{(compact ? DialogMetrics.FxBandLabelWidth : DialogMetrics.FxLabelWidth)},*");
        _box.Name = "Fx" + parameter.Key;
        _box.ItemsSource = names;
        _box.SelectedIndex = (int)parameter.Default;
        _box.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(_box, tip);
        SetColumn(_box, 1);
        Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(_box);
        _box.SelectionChanged += (_, _) => { if (!_setting && _box.SelectedIndex >= 0) send(parameter.Id, _box.SelectedIndex); };
    }

    public uint Id => _parameter.Id;

    public int Selected => _box.SelectedIndex;

    public void Show(double value)
    {
        _setting = true;
        try { _box.SelectedIndex = (int)Math.Round(value); }
        finally { _setting = false; }
    }
}

public sealed class ParamSwitch : BuiltinToggle, IParamControl
{
    private readonly BuiltinParameter _parameter;
    private bool _setting;

    public ParamSwitch(BuiltinParameter parameter, string text, string tip, Action<uint, double> send)
    {
        _parameter = parameter;
        Content = text;
        Name = "Fx" + parameter.Key;
        IsChecked = parameter.Default >= 0.5;
        ToolTip.SetTip(this, tip);
        IsCheckedChanged += (_, _) => { if (!_setting) send(parameter.Id, IsChecked == true ? 1 : 0); };
    }

    public uint Id => _parameter.Id;

    public void Show(double value)
    {
        _setting = true;
        try { IsChecked = value >= 0.5; }
        finally { _setting = false; }
    }
}
