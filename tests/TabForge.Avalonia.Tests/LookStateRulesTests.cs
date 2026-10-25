using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// One check over the whole look table. Every rule whose conditions are all plain states (pointer over, highlighted, pressed, dragging, keyboard
/// focus, selected, checked, enabled) is applied to a real control of its look's kind in that state, in the dark and the light theme. Each colour
/// setter is compared with the theme value of the palette brush it names, and each opacity setter with its literal. A rule that a later rule of
/// the same look also matches is skipped, because the later rule paints. A rule that AvaloniaLooks leaves out is skipped.
/// Not checked, and why: looks of kind Border (TabBarTab), DataGridCell (no DataGrid package) and GridViewColumnHeader (ListHeader), which have no
/// Avalonia control here; conditions that are not plain states (IsDropDownOpen, IsEditable, IsReadOnly on PrefsTextBox, Orientation on the
/// scroll bars, the ancestor-bound ScrollBarThumbLength, CaptionButtonState on ChromeCaptionButton), which the test cannot put a control in; setters
/// that are not colours or opacity (visibility, thickness, text, icon content), which are not compared here.
/// Owns: the state-to-control mapping of this check. Does not own: the look values (LookTable), the styles (AvaloniaLooks) or the palette (AvaloniaPalette).
/// Tests: this class.
/// </summary>
public class LookStateRulesTests
{
    // Each plain condition property to the pseudo class that means it. IsEnabled False is the disabled state, which the test sets as IsEnabled.
    private static readonly IReadOnlyDictionary<string, string> PseudoOfCondition = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["IsMouseOver"] = ":pointerover", ["IsHighlighted"] = ":pointerover",
        ["IsPressed"] = ":pressed", ["IsDragging"] = ":pressed",
        ["IsKeyboardFocused"] = ":focus", ["IsKeyboardFocusVisible"] = ":focus-visible", ["IsSelected"] = ":selected", ["IsChecked"] = ":checked", ["IsEnabled"] = DisabledState,
    };

    private const string DisabledState = ":disabled";

    [AvaloniaFact]
    public void MappedLookStateRulesPaintTheirThemeValueInBothThemes()
    {
        var app = Application.Current!;
        var leftOut = AvaloniaLooks.Build().Unsupported;
        var problems = new List<string>();
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            foreach (var look in LookTable.Looks.Values)
                problems.AddRange(LookProblems(app, look, variant, leftOut));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // The mismatches of one look's checked rules in one theme: each rule's control is built, put in the rule's state, then compared.
    private static List<string> LookProblems(Application app, Look look, ThemeVariant variant, IReadOnlyList<string> leftOut)
    {
        var problems = new List<string>();
        for (var index = 0; index < look.Rules.Count; index++)
        {
            var rule = look.Rules[index];
            if (!IsPlainStateRule(rule) || leftOut.Any(line => line.StartsWith($"{look.Name}.{rule.Name}:", StringComparison.Ordinal))) continue;
            var active = ActiveStates(rule);
            if (look.Rules.Skip(index + 1).Any(later => IsPlainStateRule(later) && Holds(later, active))) continue;
            if (NewControl(look.Kind) is not { } control) continue;
            problems.AddRange(RuleProblems(app, look, rule, active, control, variant));
        }
        return problems;
    }

    // Shows the control in a window of the theme, puts it in the rule's states, and compares each of the rule's setters.
    private static List<string> RuleProblems(Application app, Look look, LookRule rule, IReadOnlySet<string> active, Control control, ThemeVariant variant)
    {
        var problems = new List<string>();
        control.Classes.Add(look.Name);
        var window = new Window { Width = 320, Height = 200, Content = control, RequestedThemeVariant = variant };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        foreach (var state in active)
        {
            if (state == DisabledState) control.IsEnabled = false;
            else ((IStateControl)control).SetState(state, true);
        }
        Dispatcher.UIThread.RunJobs();
        foreach (var setter in rule.Set)
            if (SetterProblem(app, control, look.Name, rule, setter, variant) is { } problem) problems.Add(problem);
        window.Close();
        return problems;
    }

    // The mismatch of one setter of a rule, or null when it matches the theme value or is not a colour or opacity.
    private static string? SetterProblem(Application app, Control control, string lookName, LookRule rule, LookSetter setter, ThemeVariant variant)
    {
        var property = AvaloniaLooks.PropertyOf(setter);
        if (property is null) return null;
        var where = $"{lookName}.{rule.Name} {setter.Part ?? "control"} {setter.Property} ({variant})";
        var target = setter.Part is null ? control : TemplatePart(control, AvaloniaLooks.PartNameOf(lookName, setter.Part));
        if (target is null) return $"{where}: no template part {AvaloniaLooks.PartNameOf(lookName, setter.Part!)}";
        var actual = target.GetValue(property);
        if (setter.Brush is not null)
        {
            if (!app.TryGetResource(setter.Brush, variant, out var brush) || brush is not ISolidColorBrush expected) return $"{where}: {setter.Brush} has no colour in {variant}";
            var actualColour = (actual as ISolidColorBrush)?.Color;
            return actualColour == expected.Color ? null : $"{where}: expected {expected.Color}, got {actualColour}";
        }
        if (setter.Literal is not null && Equals(property, Visual.OpacityProperty) && double.TryParse(setter.Literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
            return Equals(actual, opacity) ? null : $"{where}: expected opacity {opacity}, got {actual}";
        return null;
    }

    private static Control? TemplatePart(Control control, string name) =>
        control.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == name);

    // A rule the test checks: it has conditions, and each one is a plain state the test can put a control in.
    private static bool IsPlainStateRule(LookRule rule) =>
        rule.When.Count > 0 && rule.When.All(c => c.Binding is null && c.Source is null && c.Property is not null && PseudoOfCondition.ContainsKey(c.Property) && bool.TryParse(c.Value, out _));

    // The states a rule's conditions name: a True condition is its pseudo class, a False IsEnabled condition is the disabled state.
    private static IReadOnlySet<string> ActiveStates(LookRule rule) => rule.When
        .Where(c => (c.Property == "IsEnabled") != bool.Parse(c.Value))
        .Select(c => PseudoOfCondition[c.Property!])
        .ToHashSet(StringComparer.Ordinal);

    // True when the rule's conditions hold in the given states; IsEnabled is true unless the disabled state is active.
    private static bool Holds(LookRule rule, IReadOnlySet<string> active) => rule.When.All(c =>
    {
        var pseudo = PseudoOfCondition[c.Property!];
        var actual = pseudo == DisabledState ? !active.Contains(DisabledState) : active.Contains(pseudo);
        return actual == bool.Parse(c.Value);
    });

    /// <summary>A control whose pseudo classes the test sets the way a pointer, a press, a selection or a check would.</summary>
    private interface IStateControl
    {
        void SetState(string pseudoClass, bool on);
    }

    // The control of each look kind that has an Avalonia control here. Each keeps the Fluent template of its own type (StyleKeyOverride),
    // so the look's own template and rules are the only change. Null for a kind with no control (Border, DataGridCell, GridViewColumnHeader).
    private static Control? NewControl(string kind) => kind switch
    {
        "Button" => new StateButton(),
        "ToggleButton" => new StateToggleButton(),
        "ComboBox" => new StateComboBox(),
        "ComboBoxItem" => new StateComboBoxItem(),
        "CheckBox" => new StateCheckBox(),
        "TextBox" => new StateTextBox(),
        "TextBlock" => new StateTextBlock(),
        "ListBoxItem" or "ListViewItem" => new StateListBoxItem(),
        "MenuItem" => new StateMenuItem(),
        "TabItem" => new StateTabItem(),
        "ScrollBar" => new StateScrollBar(),
        "Thumb" => new StateThumb(),
        "Slider" => new StateSlider(),
        _ => null,
    };

    private sealed class StateButton : Button, IStateControl { protected override Type StyleKeyOverride => typeof(Button); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateToggleButton : ToggleButton, IStateControl { protected override Type StyleKeyOverride => typeof(ToggleButton); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateComboBox : ComboBox, IStateControl { protected override Type StyleKeyOverride => typeof(ComboBox); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateComboBoxItem : ComboBoxItem, IStateControl { protected override Type StyleKeyOverride => typeof(ComboBoxItem); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateCheckBox : CheckBox, IStateControl { protected override Type StyleKeyOverride => typeof(CheckBox); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateTextBox : TextBox, IStateControl { protected override Type StyleKeyOverride => typeof(TextBox); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateTextBlock : TextBlock, IStateControl { protected override Type StyleKeyOverride => typeof(TextBlock); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateListBoxItem : ListBoxItem, IStateControl { protected override Type StyleKeyOverride => typeof(ListBoxItem); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateMenuItem : MenuItem, IStateControl { protected override Type StyleKeyOverride => typeof(MenuItem); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateTabItem : TabItem, IStateControl { protected override Type StyleKeyOverride => typeof(TabItem); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateScrollBar : ScrollBar, IStateControl { protected override Type StyleKeyOverride => typeof(ScrollBar); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateThumb : Thumb, IStateControl { protected override Type StyleKeyOverride => typeof(Thumb); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
    private sealed class StateSlider : Slider, IStateControl { protected override Type StyleKeyOverride => typeof(Slider); public void SetState(string pseudoClass, bool on) => PseudoClasses.Set(pseudoClass, on); }
}
