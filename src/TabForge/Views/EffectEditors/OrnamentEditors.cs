using System.Windows;
using System.Windows.Controls;
using TabForge.Models;

namespace TabForge.Views.EffectEditors;

// Owns: the controls and the value <-> control mapping of the trill, grace-note and harmonic editors (each returns a ValuesEffectDialog).
// Does not own: the preset list and frame (ValuesEffectDialog), the model writes (OrnamentEdits) or the edit (EffectEditorFlow.OpenOrnament).
// Tests: TestOrnamentEditors.
internal static class OrnamentEditors
{
    private static readonly (string, int, string?)[] Speeds =
    {
        ("16th", 16, "Duration/sixteenth_note"), ("32nd", 32, "Duration/thirty_second_note"), ("64th", 64, "Duration/sixty_fourth_note")
    };

    private static StackPanel Column(params UIElement[] fields)
    {
        var panel = new StackPanel { MinWidth = 330 };
        foreach (var field in fields) panel.Children.Add(field);
        return panel;
    }

    private static int Int(IReadOnlyDictionary<string, double> v, string key, int fallback) => v.TryGetValue(key, out var x) ? (int)Math.Round(x) : fallback;

    /// <summary>Trill: the fret it alternates with (model: TrillTargetMidi, converted with the string tuning) and its speed.</summary>
    public static ValuesEffectDialog Trill(TabNote first, Func<int, int, int> pitchOf, EffectPresetStore presets, bool canClean, Action? presetsChanged)
    {
        var (step, speed) = first.Techniques.Contains(TechniqueNames.Trill) ? OrnamentEdits.TrillOf(first, pitchOf) : (2, 16);
        var fret = new NumberField("Trill fret", 0, 36, first.Fret + step, 0);
        var speedField = new ChoiceField("Speed", Speeds, speed, 1);
        return new ValuesEffectDialog(EffectEditorKind.Trill, "Trill editor", Column(fret, speedField),
            () => new() { ["Step"] = fret.Value - first.Fret, ["Speed"] = speedField.Value },
            v => { fret.Value = first.Fret + Int(v, "Step", 2); speedField.Value = Int(v, "Speed", 16); }, presets, canClean, presetsChanged);
    }

    /// <summary>Grace note: fret, dead note, position, duration, dynamic and transition.</summary>
    public static ValuesEffectDialog Grace(TabCell cell, EffectPresetStore presets, bool canClean, Action? presetsChanged, int? onString = null)
    {
        var principalFret = OrnamentEdits.PrincipalOf(cell, onString)?.Fret ?? 0;
        var s = OrnamentEdits.GraceSettingsOf(cell, onString);
        var fret = new NumberField("Fret", 0, 36, principalFret + s.Step, 0);
        var dead = new CheckBox { Content = "Dead note", Margin = new Thickness(0, 4, 0, 4), IsChecked = s.Dead, TabIndex = 1 };
        var position = new ChoiceField("Position", new (string, int, string?)[] { ("Before the beat", 1, null), ("On the beat", 0, null) }, s.BeforeBeat ? 1 : 0, 2);
        var duration = new ChoiceField("Duration", Speeds, s.Speed, 3);
        var dynamic = new ChoiceField("Dynamic", Dynamics.Names.Select((n, i) => (n, i, (string?)null)).ToArray(), s.Dynamic, 4);
        var transition = new ChoiceField("Transition", new (string, int, string?)[] { ("None", 0, null), ("Bend", 1, null), ("Slide", 2, null), ("Hammer", 3, null) }, (int)s.Transition, 5);
        return new ValuesEffectDialog(EffectEditorKind.Grace, "Grace note editor", Column(fret, dead, position, duration, dynamic, transition),
            () => new()
            {
                ["Step"] = fret.Value - principalFret, ["Dead"] = dead.IsChecked == true ? 1 : 0, ["Before"] = position.Value, ["Speed"] = duration.Value,
                ["Dynamic"] = dynamic.Value, ["Transition"] = transition.Value
            },
            v =>
            {
                fret.Value = principalFret + Int(v, "Step", 0);
                dead.IsChecked = Int(v, "Dead", 0) == 1;
                position.Value = Int(v, "Before", 1);
                duration.Value = Int(v, "Speed", 32);
                if (Int(v, "Dynamic", -1) is >= 0 and var d) dynamic.Value = d;
                transition.Value = Int(v, "Transition", 0);
            }, presets, canClean, presetsChanged);
    }

    /// <summary>Harmonic: the kind, plus the harmonic fret for artificial and tapped harmonics.</summary>
    public static ValuesEffectDialog Harmonic(TabNote first, EffectPresetStore presets, bool canClean, Action? presetsChanged)
    {
        var type = Math.Max(0, OrnamentEdits.HarmonicTypeOf(first));
        var kinds = new (string, int, string?)[] { ("Natural", 0, null), ("Artificial", 1, null), ("Tapped", 2, null), ("Pinch", 3, null), ("Semi", 4, null) };
        var kind = new ChoiceField("Type", kinds, type, 0);
        var fret = new NumberField("Harmonic fret", 1, 36, (int)(first.HarmonicFret ?? 12), 1);
        void Sync() => fret.Enabled = OrnamentEdits.UsesHarmonicFret(kind.Value);
        kind.Changed += Sync;
        Sync();
        return new ValuesEffectDialog(EffectEditorKind.Harmonic, "Harmonic editor", Column(kind, fret),
            () => new() { ["Type"] = kind.Value, ["Fret"] = fret.Value },
            v => { kind.Value = Int(v, "Type", 0); fret.Value = Int(v, "Fret", 12); Sync(); }, presets, canClean, presetsChanged);
    }
}
