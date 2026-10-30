using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Midi;
using TabForge.Playback;

namespace TabForge.Avalonia.Views.Articulations;

// Owns: one row of the Articulations table (technique, action, value, second value, mode, Test) and reading it back as a rule.
// Does not own: the table, the presets or the edit (ArticulationsPane), or what a rule does (the compiler).
// Tests: no dedicated test class.
internal sealed class ArticulationRow
{
    private static (ArticulationAction Action, string Text, string Tip)[] AllActions() => new[]
    {
        (ArticulationAction.None, "None", "No rule: the note plays as written"),
        (ArticulationAction.Keyswitch, "Keyswitch note", "Sends a keyswitch note just before the note"),
        (ArticulationAction.Velocity, "Set velocity", "The note plays at this velocity (1 to 127)"),
        (ArticulationAction.Cc, "Send CC", "Sends a controller (number, then value) just before the note"),
        (ArticulationAction.Replace, "Replace note", "Plays this pitch instead of the written one"),
    };

    private readonly (ArticulationAction Action, string Text, string Tip)[] _actions;
    private bool _filling;

    public ArticulationTechnique Technique { get; }
    public TextBlock Label { get; }
    public ComboBox ActionBox { get; }
    public TextBox ValueBox { get; } = new TextBox { MinWidth = DialogMetrics.ArticulationValueWidth, VerticalAlignment = VerticalAlignment.Center }.Spaced(DialogMetrics.ArticulationCellGap);
    public TextBox Value2Box { get; } = new TextBox { MinWidth = DialogMetrics.ArticulationValue2Width, VerticalAlignment = VerticalAlignment.Center }.Spaced(DialogMetrics.ArticulationCellGap);
    public ComboBox ModeBox { get; } = new ComboBox { ItemsSource = new[] { "Latched", "Per note" }, SelectedIndex = 0, MinWidth = DialogMetrics.ArticulationModeWidth, VerticalAlignment = VerticalAlignment.Center }.Spaced(DialogMetrics.ArticulationCellGap);
    public Button TestButton { get; } = new Button { Content = "Test", VerticalAlignment = VerticalAlignment.Center }.WithPadding(DialogMetrics.ArticulationButtonPadding).Spaced(DialogMetrics.ArticulationCellGap);

    /// <summary>Raised when the user changed the row (a pick, or Enter / leaving a box).</summary>
    public event Action? Edited;

    public ArticulationRow(ArticulationTechnique technique)
    {
        Technique = technique;
        var normal = technique.Id == ArticulationTechniques.Normal;
        _actions = AllActions();
        if (normal) _actions = _actions.Where(a => a.Action is ArticulationAction.None or ArticulationAction.Keyswitch or ArticulationAction.Cc).ToArray();
        Label = new TextBlock { Text = technique.Display, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ClipToBounds = true }.Spaced(DialogMetrics.ArticulationLabelGap);
        MidiSharedRows.Tip(Label, technique.Display);
        ActionBox = new ComboBox { ItemsSource = _actions.Select(a => a.Text).ToArray(), SelectedIndex = 0, MinWidth = DialogMetrics.ArticulationActionWidth, VerticalAlignment = VerticalAlignment.Center }.Spaced(DialogMetrics.ArticulationCellGap);
        var tag = technique.Id;
        (Label.Name, ActionBox.Name, ValueBox.Name, Value2Box.Name, ModeBox.Name, TestButton.Name) =
            ("Tech" + tag, "Action" + tag, "Value" + tag, "Value2" + tag, "Mode" + tag, "Test" + tag);
        MidiSharedRows.Tip(ModeBox, "Latched: sent only when the articulation changes, and the Normal rule is sent when the technique ends. Per note: sent before every note with the technique");
        MidiSharedRows.Tip(TestButton, "Sends this rule to the instrument, then a short test note");
        ActionBox.SelectionChanged += (_, _) => { if (!_filling) { SetDefaults(); Edited?.Invoke(); } };
        ModeBox.SelectionChanged += (_, _) => { if (!_filling) Edited?.Invoke(); };
        foreach (var box in new[] { ValueBox, Value2Box })
        {
            box.LostFocus += (_, _) => { if (!_filling) Edited?.Invoke(); };
            box.KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Enter && !_filling) Edited?.Invoke(); };
        }
        Show(null);
    }

    public ArticulationAction Action => _actions[Math.Clamp(ActionBox.SelectedIndex, 0, _actions.Length - 1)].Action;

    /// <summary>Puts a rule (null: none) into the controls without raising <see cref="Edited"/>.</summary>
    public void Show(ArticulationRule? rule)
    {
        _filling = true;
        try
        {
            var action = rule?.Action ?? ArticulationAction.None;
            ActionBox.SelectedIndex = Math.Max(0, Array.FindIndex(_actions, a => a.Action == action));
            if (rule is { } r)
            {
                ValueBox.Text = action is ArticulationAction.Keyswitch or ArticulationAction.Replace ? ArticulationTechniques.NoteName(r.A) : r.A.ToString();
                Value2Box.Text = r.B.ToString();
                ModeBox.SelectedIndex = r.Mode == ArticulationMode.Latched ? 0 : 1;
            }
            else { ValueBox.Text = ""; Value2Box.Text = ""; ModeBox.SelectedIndex = 0; }
            Refresh();
        }
        finally { _filling = false; }
    }

    private void SetDefaults()
    {
        (ValueBox.Text, Value2Box.Text) = Action switch
        {
            ArticulationAction.Keyswitch => (ArticulationTechniques.NoteName(24), "0"),
            ArticulationAction.Velocity => ("64", "0"),
            ArticulationAction.Cc => ("32", "127"),
            ArticulationAction.Replace => (ArticulationTechniques.NoteName(52), "0"),
            _ => ("", ""),
        };
        Refresh();
    }

    /// <summary>Enables the boxes the action uses.</summary>
    private void Refresh()
    {
        var action = Action;
        ValueBox.IsEnabled = action != ArticulationAction.None;
        Value2Box.IsEnabled = action == ArticulationAction.Cc;
        ModeBox.IsEnabled = action is ArticulationAction.Keyswitch or ArticulationAction.Cc;
        TestButton.IsEnabled = action != ArticulationAction.None;
        MidiSharedRows.Tip(ActionBox, _actions[Math.Clamp(ActionBox.SelectedIndex, 0, _actions.Length - 1)].Tip);
        MidiSharedRows.Tip(ValueBox, action switch
        {
            ArticulationAction.Keyswitch or ArticulationAction.Replace => "A note name (C0 = MIDI 24, so middle C is C3) or a MIDI number 0 to 127",
            ArticulationAction.Cc => "Controller number (0 to 127)",
            _ => "Velocity 1 to 127",
        });
        MidiSharedRows.Tip(Value2Box, "Controller value (0 to 127)");
    }

    /// <summary>The rule the controls hold: null for None; false (with the reason) when a box holds something unusable.</summary>
    public bool TryRule(out ArticulationRule? rule, out string? problem)
    {
        (rule, problem) = (null, null);
        var action = Action;
        if (action == ArticulationAction.None) return true;
        int a, b = 0;
        if (action is ArticulationAction.Keyswitch or ArticulationAction.Replace)
        {
            if (!ArticulationTechniques.TryParseNote(ValueBox.Text, out a)) { problem = $"{Technique.Display}: type a note name such as C0 or a number 0 to 127."; return false; }
        }
        else if (!int.TryParse(ValueBox.Text?.Trim(), out a) || a < (action == ArticulationAction.Velocity ? 1 : 0) || a > 127)
        {
            problem = $"{Technique.Display}: the value must be {(action == ArticulationAction.Velocity ? "1" : "0")} to 127.";
            return false;
        }
        if (action == ArticulationAction.Cc && (!int.TryParse(Value2Box.Text?.Trim(), out b) || b is < 0 or > 127)) { problem = $"{Technique.Display}: the controller value must be 0 to 127."; return false; }
        rule = new ArticulationRule(action, a, b, ModeBox.SelectedIndex == 1 && ModeBox.IsEnabled ? ArticulationMode.PerNote : ArticulationMode.Latched);
        return true;
    }

    /// <summary>Adds the row controls to a table grid at <paramref name="gridRow"/>.</summary>
    public void AddTo(Grid grid, int gridRow)
    {
        Control[] cells = { Label, ActionBox, ValueBox, Value2Box, ModeBox, TestButton };
        for (var column = 0; column < cells.Length; column++)
        {
            Grid.SetRow(cells[column], gridRow);
            Grid.SetColumn(cells[column], column);
            grid.Children.Add(cells[column]);
        }
    }
}
