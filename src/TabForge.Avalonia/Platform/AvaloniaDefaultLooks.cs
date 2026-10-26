using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Gives every plain control of a kind the Core look the app's implicit style gives it (a Button the "Button" look, a list row the "ListBoxItem" look, ...), so hover, pressed, selected and disabled states come from the one look table in every window.
/// A control that already carries another look of its kind, or sets its own template, keeps it.
/// Owns: the type-to-default-look table and its attach hook. Does not own: the look rules (Core look table) or their styles (AvaloniaLooks).
/// Tests: TabForge.Avalonia.Tests.ThemeTests.
/// </summary>
public static class AvaloniaDefaultLooks
{
    public const string AccentLook = "AccentButton";

    /// <summary>The plain button's Core look; a button built after its window's content has been attached names it itself (the attach hook has run by then).</summary>
    public const string ButtonLook = "Button";

    // Exact control type to its default look and the Core look kinds that map to that type. Fixed at start; read on the UI thread only.
    private static readonly IReadOnlyDictionary<Type, (string Look, IReadOnlyList<string> Kinds)> DefaultLookOf = new Dictionary<Type, (string, IReadOnlyList<string>)>
    {
        [typeof(Button)] = (ButtonLook, new[] { ButtonLook }),
        [typeof(ComboBox)] = ("ComboBox", new[] { "ComboBox" }),
        [typeof(ComboBoxItem)] = ("ComboBoxItem", new[] { "ComboBoxItem" }),
        [typeof(MenuItem)] = ("MenuItem", new[] { "MenuItem" }),
        [typeof(ListBoxItem)] = ("ListBoxItem", new[] { "ListBoxItem", "ListViewItem" }),
        [typeof(TabItem)] = ("Tab", new[] { "TabItem" }),
    };

    /// <summary>The look a plain control takes, or null when it keeps what it has.</summary>
    public static string? LookFor(Control control)
    {
        if (!DefaultLookOf.TryGetValue(control.GetType(), out var entry)) return null;
        if (HasOwnTemplate((TemplatedControl)control)) return null;
        if (LookTable.Looks.Values.Any(look => entry.Kinds.Contains(look.Kind) && control.Classes.Contains(look.Name))) return null;
        return entry.Look;
    }

    // A template that is not the one the control's theme supplies. A popup item (a drop-down's rows) already has its theme template when it is parented, so IsSet alone cannot tell.
    private static bool HasOwnTemplate(TemplatedControl control)
    {
        if (!control.IsSet(TemplatedControl.TemplateProperty)) return false;
        var theme = control.Theme ?? (control.TryFindResource(control.StyleKey, control.ActualThemeVariant, out var found) ? found as ControlTheme : null);
        for (; theme is not null; theme = theme.BasedOn)
            if (theme.Setters.OfType<Setter>().FirstOrDefault(setter => setter.Property == TemplatedControl.TemplateProperty) is { } supplied)
                return !ReferenceEquals(supplied.Value, control.Template);
        return true;
    }

    /// <summary>Adds the default look class to each control of the application as it is put into a window.</summary>
    public static void Register() =>
        Visual.VisualParentProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (LookFor(control) is { } look) control.Classes.Add(look);
        });
}
