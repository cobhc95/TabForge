using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The trill, grace-note and harmonic editors, the twins of the WPF OrnamentEditors: the same fields,
/// order and texts, each returned in an <see cref="EffectValuesWindow"/>. The caller passes plain starting values (the WPF side
/// reads them from the note).
/// Owns: the fields and the value-to-field mapping. Does not own: the frame and presets (EffectValuesWindow) or the model writes.
/// Tests: TabForge.Avalonia.Tests.MixTableColourChooserEffectValuesTests.
/// </summary>
public static class OrnamentEditorWindows
{
    private static readonly IReadOnlyList<(string, int)> Speeds = new (string, int)[] { ("16th", 16), ("32nd", 32), ("64th", 64) };
    private static int Int(IReadOnlyDictionary<string, double> v, string key, int fallback) => EffectValuesPresenter.IntValue(v, key, fallback);

    /// <summary>Trill: the fret it alternates with and its speed.</summary>
    public static EffectValuesWindow Trill(int fret, int step, int speed, IEnumerable<EffectValuesPreset> presets, bool canClean,
        Func<IReadOnlyDictionary<string, double>, EffectValuesPreset?> save)
    {
        var target = new NumberField("Trill fret", 0, 36, fret + step);
        var speedField = new ChoiceField("Speed", Speeds, speed);
        return new EffectValuesWindow("Trill editor", Column(target, speedField),
            () => new() { ["Step"] = target.Value - fret, ["Speed"] = speedField.Value },
            v => { target.Value = fret + Int(v, "Step", 2); speedField.Value = Int(v, "Speed", 16); }, presets, canClean, save);
    }

    /// <summary>Grace note: fret, dead note, position, duration, dynamic and transition.</summary>
    public static EffectValuesWindow Grace(int principalFret, int step, bool dead, bool beforeBeat, int speed, int dynamic, int transition,
        IEnumerable<EffectValuesPreset> presets, bool canClean, Func<IReadOnlyDictionary<string, double>, EffectValuesPreset?> save)
    {
        var fret = new NumberField("Fret", 0, 36, principalFret + step);
        var deadBox = new CheckBox { Content = "Dead note", Margin = new Thickness(0, 4, 0, 4), IsChecked = dead };
        var position = new ChoiceField("Position", new[] { ("Before the beat", 1), ("On the beat", 0) }, beforeBeat ? 1 : 0);
        var duration = new ChoiceField("Duration", Speeds, speed);
        var dynamicField = new ChoiceField("Dynamic", Dynamics.Names.Select((n, i) => (n, i)).ToArray(), dynamic);
        var transitionField = new ChoiceField("Transition", new[] { ("None", 0), ("Bend", 1), ("Slide", 2), ("Hammer", 3) }, transition);
        return new EffectValuesWindow("Grace note editor", Column(fret, deadBox, position, duration, dynamicField, transitionField),
            () => new()
            {
                ["Step"] = fret.Value - principalFret, ["Dead"] = deadBox.IsChecked == true ? 1 : 0, ["Before"] = position.Value, ["Speed"] = duration.Value,
                ["Dynamic"] = dynamicField.Value, ["Transition"] = transitionField.Value
            },
            v =>
            {
                fret.Value = principalFret + Int(v, "Step", 0);
                deadBox.IsChecked = Int(v, "Dead", 0) == 1;
                position.Value = Int(v, "Before", 1);
                duration.Value = Int(v, "Speed", 32);
                if (Int(v, "Dynamic", -1) is >= 0 and var d) dynamicField.Value = d;
                transitionField.Value = Int(v, "Transition", 0);
            }, presets, canClean, save);
    }

    /// <summary>Harmonic: the kind, plus the harmonic fret for artificial and tapped harmonics.</summary>
    public static EffectValuesWindow Harmonic(int type, int harmonicFret, IEnumerable<EffectValuesPreset> presets, bool canClean,
        Func<IReadOnlyDictionary<string, double>, EffectValuesPreset?> save)
    {
        var kind = new ChoiceField("Type", new[] { ("Natural", 0), ("Artificial", 1), ("Tapped", 2), ("Pinch", 3), ("Semi", 4) }, Math.Max(0, type));
        var fret = new NumberField("Harmonic fret", 1, 36, harmonicFret);
        void Sync() => fret.IsEnabled = EffectValuesPresenter.UsesHarmonicFret(kind.Value);
        kind.Changed += Sync;
        Sync();
        return new EffectValuesWindow("Harmonic editor", Column(kind, fret),
            () => new() { ["Type"] = kind.Value, ["Fret"] = fret.Value },
            v => { kind.Value = Int(v, "Type", 0); fret.Value = Int(v, "Fret", 12); Sync(); }, presets, canClean, save);
    }

    private static StackPanel Column(params Control[] fields)
    {
        var panel = new StackPanel { MinWidth = 330 };
        panel.Children.AddRange(fields);
        return panel;
    }

    private static TextBlock Label(string text) => new() { Text = text, Width = 96, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A whole number between bounds: label, -, the box, +; Up and Down step it. The twin of the WPF NumberField.</summary>
    public sealed class NumberField : StackPanel
    {
        private readonly int _min, _max;
        private readonly TextBox _box = new() { Width = 46, Height = 26, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };

        public NumberField(string label, int min, int max, int value)
        {
            _min = min; _max = max;
            Orientation = Orientation.Horizontal;
            Margin = new Thickness(0, 4, 0, 4);
            AutomationProperties.SetName(_box, label);
            _box.KeyDown += (_, e) => { if (e.Key is Key.Up or Key.Down) { Value += e.Key == Key.Up ? 1 : -1; e.Handled = true; } };
            _box.LostFocus += (_, _) => Value = Value;
            Children.AddRange(new Control[] { Label(label), Step("-", -1), _box, Step("+", 1) });
            Value = value;
        }

        public int Value
        {
            get => int.TryParse(_box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, _min, _max) : _min;
            set => _box.Text = Math.Clamp(value, _min, _max).ToString(CultureInfo.InvariantCulture);
        }

        private Button Step(string text, int delta)
        {
            var b = new Button
            {
                Content = text, Width = 26, Height = 26, Margin = new Thickness(4, 0, 4, 0), IsTabStop = false,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            b.Click += (_, _) => Value += delta;
            return b;
        }
    }

    /// <summary>One choice from a few: label, then a radio button per option. The twin of the WPF ChoiceField (without its icons).</summary>
    public sealed class ChoiceField : StackPanel
    {
        private readonly List<(RadioButton Button, int Value)> _items = new();
        private int _value;

        public ChoiceField(string label, IReadOnlyList<(string Text, int Value)> options, int value)
        {
            Orientation = Orientation.Horizontal;
            Margin = new Thickness(0, 4, 0, 4);
            Children.Add(Label(label));
            var group = label + Guid.NewGuid().ToString("N");
            foreach (var (text, v) in options)
            {
                var radio = new RadioButton { Content = text, GroupName = group, Margin = new Thickness(0, 0, 12, 0) };
                radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) { _value = v; Changed?.Invoke(); } };   // the group unchecks the old one after this
                _items.Add((radio, v));
                Children.Add(radio);
            }
            Value = value;
        }

        public event Action? Changed;

        public int Value
        {
            get => _value;
            set { _value = value; foreach (var (button, v) in _items) button.IsChecked = v == value; }
        }
    }
}
