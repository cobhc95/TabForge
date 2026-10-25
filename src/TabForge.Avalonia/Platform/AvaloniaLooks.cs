using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>The Avalonia styles built from the look table, and the rule parts they leave out.</summary>
public sealed record AvaloniaLookStyles(Styles Styles, IReadOnlyList<string> Unsupported);

/// <summary>
/// Builds one Avalonia style per Core look rule: the look name is the selector class, the state conditions become pseudo-class selectors, template-part setters target the named borders of AvaloniaButtonTemplates,
/// and the setters write brushes as dynamic resources so a theme switch repaints them.
/// Owns: the look-to-style mapping and its list of left-out parts. Does not own: colours (AvaloniaPalette) or the look table (Core LookTable).
/// Tests: TabForge.Avalonia.Tests.ThemeTests.
/// </summary>
public static class AvaloniaLooks
{
    // The control kinds this proof maps. Other kinds are listed as unsupported instead of guessed.
    private static readonly IReadOnlyDictionary<string, Type> KindTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["Button"] = typeof(Button),
        ["ToggleButton"] = typeof(ToggleButton),
        ["ComboBox"] = typeof(ComboBox),
        ["ComboBoxItem"] = typeof(ComboBoxItem),
        ["CheckBox"] = typeof(CheckBox),
        ["TextBox"] = typeof(TextBox),
        ["TextBlock"] = typeof(TextBlock),
        ["ListBoxItem"] = typeof(ListBoxItem),
        // Avalonia has no ListViewItem: a ListView row is a ListBoxItem there.
        ["ListViewItem"] = typeof(ListBoxItem),
        ["MenuItem"] = typeof(MenuItem),
        ["TabItem"] = typeof(TabItem),
        ["ScrollBar"] = typeof(ScrollBar),
        ["Thumb"] = typeof(Thumb),
        ["Slider"] = typeof(Slider),
    };

    // The control kinds whose looks get the border template of AvaloniaButtonTemplates instead of their Fluent template. The Fluent
    // ListBoxItem, ComboBoxItem and Thumb templates have no border to paint; the border template gives them one.
    private static readonly IReadOnlySet<string> BorderTemplateKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Button", "ToggleButton", "ListBoxItem", "ListViewItem", "ComboBoxItem", "Thumb",
    };

    // Property condition to pseudo class, and the condition value that applies it (IsEnabled False is :disabled). IsHighlighted is a
    // menu item's hover or keyboard highlight, and IsDragging is a thumb held down, so both show the pressed or pointer-over look.
    private static readonly IReadOnlyDictionary<string, (string Pseudo, bool WhenTrue)> StateConditions = new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
    {
        ["IsMouseOver"] = (":pointerover", true),
        ["IsHighlighted"] = (":pointerover", true),
        ["IsPressed"] = (":pressed", true),
        ["IsDragging"] = (":pressed", true),
        ["IsKeyboardFocused"] = (":focus", true),
        // The focus ring: a focus reached by the keyboard, never by a click.
        ["IsKeyboardFocusVisible"] = (":focus-visible", true),
        ["IsEnabled"] = (":disabled", false),
        ["IsSelected"] = (":selected", true),
        ["IsChecked"] = (":checked", true),
        ["IsDropDownOpen"] = (":dropdownopen", true),
        ["IsReadOnly"] = (":readonly", true),
        ["IsEditable"] = (":editable", true),
        // The caption button state of the window shell (hot, down) is the pointer state Avalonia sets on the same button.
        ["CaptionButtonState.IsHot"] = (":pointerover", true),
        ["CaptionButtonState.IsDown"] = (":pressed", true),
    };

    // Core template part name, per look, to the Avalonia template part with the same meaning, where the names differ. A part not listed
    // here is looked up by its Core name. The Fluent menu item and tab item name their root border PART_LayoutRoot.
    private static readonly IReadOnlyDictionary<(string Look, string CorePart), string> AvaloniaPartNames = new Dictionary<(string, string), string>
    {
        [("PrefsCheckBox", "CheckBorder")] = "box",
        [("PrefsCheckBox", "CheckMark")] = "check",
        [("PrefsTextBox", "TextBorder")] = "PART_BorderElement",
        [("PrefsComboBox", "ComboBorder")] = "Background",
        [("MenuItem", "bd")] = "PART_LayoutRoot",
        [("PrefsMenuItem", "MenuItemBorder")] = "PART_LayoutRoot",
        [("Tab", "bd")] = "PART_LayoutRoot",
    };

    // Core parts whose Avalonia control has no template part to paint (the Fluent slider has no single track border), with the reason
    // each rule for them is listed as unsupported instead of being applied to nothing.
    private static readonly IReadOnlyDictionary<(string Look, string CorePart), string> PartsWithoutAvaloniaCounterpart = new Dictionary<(string, string), string>
    {
        [("ArrangementSlider", "TrackBar")] = "the Fluent slider has no single track border",
    };

    // Setter property name to the Avalonia property. Brushes take a theme brush name, the others a literal number.
    private static readonly IReadOnlyDictionary<string, AvaloniaProperty> SetterProperties = new Dictionary<string, AvaloniaProperty>(StringComparer.Ordinal)
    {
        ["Background"] = TemplatedControl.BackgroundProperty,
        ["BorderBrush"] = TemplatedControl.BorderBrushProperty,
        ["Foreground"] = TemplatedControl.ForegroundProperty,
        ["Opacity"] = Visual.OpacityProperty,
        ["Width"] = Layoutable.WidthProperty,
        ["Height"] = Layoutable.HeightProperty,
        ["MinWidth"] = Layoutable.MinWidthProperty,
        ["MinHeight"] = Layoutable.MinHeightProperty,
    };

    // Property conditions that name a value, not true or false: (property, value) to the pseudo class Avalonia sets for that state.
    private static readonly IReadOnlyDictionary<(string Property, string Value), string> ValueStateConditions = new Dictionary<(string, string), string>
    {
        [("Orientation", "Horizontal")] = ":horizontal",
        [("Orientation", "Vertical")] = ":vertical",
    };

    // Template part property (as the look JSON names it) to the element type of the part and the property written; other part properties are listed as unsupported.
    // A Track part is the Fluent PART_Track of the scroll bar and slider; the border parts come from AvaloniaButtonTemplates or the Fluent templates.
    private static readonly IReadOnlyDictionary<string, (Type PartType, AvaloniaProperty Property)> PartProperties = new Dictionary<string, (Type, AvaloniaProperty)>(StringComparer.Ordinal)
    {
        ["Border.Background"] = (typeof(Border), Border.BackgroundProperty),
        ["Border.BorderBrush"] = (typeof(Border), Border.BorderBrushProperty),
        ["Path.Visibility"] = (typeof(global::Avalonia.Controls.Shapes.Path), Visual.IsVisibleProperty),
        ["Border.Opacity"] = (typeof(Border), Visual.OpacityProperty),
        ["UIElement.Opacity"] = (typeof(Border), Visual.OpacityProperty),
        ["Track.IsDirectionReversed"] = (typeof(Track), Track.IsDirectionReversedProperty),
        ["Track.Focusable"] = (typeof(Track), InputElement.FocusableProperty),
        ["TextBox.Visibility"] = (typeof(TextBox), Visual.IsVisibleProperty),
    };

    /// <summary>The styles for every mappable look rule, and one line per part that was left out.</summary>
    public static AvaloniaLookStyles Build()
    {
        var styles = new Styles();
        var unsupported = new List<string>();
        foreach (var look in LookTable.Looks.Values)
        {
            if (!KindTypes.TryGetValue(look.Kind, out var kind))
            {
                unsupported.Add($"{look.Name}: control kind {look.Kind} is not mapped (all rules left out)");
                continue;
            }
            // Button-family looks get the border template of AvaloniaButtonTemplates. Form kinds keep their Fluent template (it has
            // PART_Popup and the text presenters a border-only template would lose) and map their part names in AvaloniaPartNames.
            if (BorderTemplateKinds.Contains(look.Kind) && AvaloniaButtonTemplates.TemplateStyle(kind, look) is { } templateStyle) styles.Add(templateStyle);
            foreach (var rule in look.Rules)
            {
                var where = $"{look.Name}.{rule.Name}";
                var selector = RuleSelector(kind, look.Name, rule, where, unsupported);
                if (selector is null) continue;
                // One style for the control's own setters and one per template part, selected as "Kind.Look:state /template/ Border#part".
                foreach (var group in rule.Set.GroupBy(setter => (setter.Part, PartType: PartTypeOf(setter))))
                {
                    if (group.Key.Part is not null && PartsWithoutAvaloniaCounterpart.TryGetValue((look.Name, group.Key.Part), out var reason))
                    {
                        foreach (var setter in group) unsupported.Add($"{where}: part {group.Key.Part} {setter.Property} ({reason}, left out)");
                        continue;
                    }
                    var target = group.Key.Part is null ? selector : Selectors.Name(Selectors.OfType(Selectors.Template(selector), group.Key.PartType), AvaloniaPartName(look.Name, group.Key.Part));
                    var style = new Style(_ => target);
                    foreach (var setter in group)
                        if (SetterFor(setter, where, unsupported) is { } built) style.Setters.Add(built);
                    if (style.Setters.Count > 0) styles.Add(style);
                }
            }
        }
        return new AvaloniaLookStyles(styles, unsupported);
    }

    // The element type a part setter writes to (Border when the property is not mapped, so the setter is listed as unsupported).
    private static Type PartTypeOf(LookSetter setter) =>
        setter.Part is not null && PartProperties.TryGetValue(setter.Property, out var part) ? part.PartType : typeof(Border);

    // The Avalonia part name for a Core part of a look: the mapped name where the two differ, otherwise the Core name.
    private static string AvaloniaPartName(string lookName, string corePart) =>
        AvaloniaPartNames.TryGetValue((lookName, corePart), out var mapped) ? mapped : corePart;

    /// <summary>The name the Avalonia template gives the part a Core look names: the mapped name where the two differ, otherwise the Core name.</summary>
    public static string PartNameOf(string lookName, string corePart) => AvaloniaPartName(lookName, corePart);

    /// <summary>The Avalonia property a Core setter writes, or null when the setter is left out (Build lists it as unsupported).</summary>
    public static AvaloniaProperty? PropertyOf(LookSetter setter)
    {
        if (setter.Part is null) return SetterProperties.TryGetValue(setter.Property, out var property) ? property : null;
        return TryPartProperty(setter.Property, out var partProperty) ? partProperty : null;
    }

    private static Selector? RuleSelector(Type kind, string lookName, LookRule rule, string where, List<string> unsupported)
    {
        var selector = Selectors.Class(Selectors.Is(null, kind), lookName);
        foreach (var condition in rule.When)
        {
            if (condition.Binding is null && condition.Source is null && condition.Property is not null && condition.Value is not null
                && ValueStateConditions.TryGetValue((condition.Property, condition.Value), out var valuePseudo))
            {
                selector = Selectors.Class(selector, valuePseudo);
                continue;
            }
            if (condition.Binding is not null || condition.Source is not null || condition.Property is null || !StateConditions.TryGetValue(condition.Property, out var state) || !bool.TryParse(condition.Value, out var value))
            {
                unsupported.Add($"{where}: condition {condition.Property ?? condition.Binding} is not a plain state (rule left out)");
                return null;
            }
            var pseudo = state.Pseudo;
            selector = value == state.WhenTrue
                ? Selectors.Class(selector, pseudo)
                : Selectors.Not(selector, x => Selectors.Class(x, pseudo));
            // A disabled control shows no hover or pressed look.
            if (value && pseudo is ":pointerover" or ":pressed") selector = Selectors.Not(selector, x => Selectors.Class(x, ":disabled"));
        }
        return selector;
    }

    private static bool TryPartProperty(string name, [NotNullWhen(true)] out AvaloniaProperty? property)
    {
        var found = PartProperties.TryGetValue(name, out var part);
        property = found ? part.Property : null;
        return found;
    }

    // A boolean literal: True or False, or a Visibility name (Visible is true, Collapsed and Hidden are false).
    private static bool TryFlag(string literal, out bool flag)
    {
        flag = literal == "Visible";
        return flag || literal is "Collapsed" or "Hidden" || bool.TryParse(literal, out flag);
    }

    private static Setter? SetterFor(LookSetter setter, string where, List<string> unsupported)
    {
        var partText = setter.Part is null ? "" : "part " + setter.Part + " ";
        var target = where + ": " + partText + setter.Property;
        AvaloniaProperty? property;
        var mapped = setter.Part is null ? SetterProperties.TryGetValue(setter.Property, out property) : TryPartProperty(setter.Property, out property);
        if (!mapped || property is null)
        {
            unsupported.Add($"{target} (property not mapped, left out)");
            return null;
        }
        if (setter.Brush is not null)
        {
            if (!AvaloniaPalette.HasBrush(setter.Brush))
            {
                unsupported.Add($"{target} (brush {setter.Brush} has no palette key, left out)");
                return null;
            }
            return new Setter(property, new DynamicResourceExtension(setter.Brush));
        }
        if (setter.Literal is not null && property.PropertyType == typeof(IBrush) && Color.TryParse(setter.Literal, out var colour))
            return new Setter(property, new SolidColorBrush(colour));
        if (setter.Literal is not null && property.PropertyType == typeof(bool) && TryFlag(setter.Literal, out var flag))
            return new Setter(property, flag);
        if (setter.Literal is not null && double.TryParse(setter.Literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return new Setter(property, number);
        unsupported.Add($"{target} (value not supported, left out)");
        return null;
    }
}
