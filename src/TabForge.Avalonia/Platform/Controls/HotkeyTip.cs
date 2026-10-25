using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Platform.Controls;

/// <summary>
/// The one hotkey-tooltip mechanism: a control carries <see cref="CommandIdProperty"/> and <see cref="TextProperty"/>, a window sets
/// <see cref="SourceProperty"/> (inherited down the tree) to the live hotkey settings, and the tooltip text is composed when it opens,
/// so a shortcut changed in Settings shows at once: "Hide fretboard / keyboard (F9)".
/// Owns: the attached properties and composing the text through HotkeyCatalog.TooltipWithKey.
/// Does not own: the key display rules (Core HotkeyCatalog), where the settings live (the window that sets the source).
/// Tests: TabForge.Avalonia.Tests.HotkeyTipTests.
/// </summary>
public static class HotkeyTip
{
    public static readonly AttachedProperty<string?> CommandIdProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("CommandId", typeof(HotkeyTip));

    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Text", typeof(HotkeyTip));

    public static readonly AttachedProperty<Func<HotkeySettings>?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Control, Func<HotkeySettings>?>("Source", typeof(HotkeyTip), inherits: true);

    static HotkeyTip()
    {
        // Class handler registered once for every control; it rewrites the tip only for controls that carry a command id.
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((control, _) => Refresh(control));
    }

    /// <summary>Marks a control with its command and base text and sets the first tooltip.</summary>
    public static void Set(Control control, string commandId, string text)
    {
        control.SetValue(CommandIdProperty, commandId);
        control.SetValue(TextProperty, text);
        Refresh(control);
    }

    public static string? GetCommandId(Control control) => control.GetValue(CommandIdProperty);

    public static Func<HotkeySettings>? GetSource(Control control) => control.GetValue(SourceProperty);

    public static void SetSource(Control control, Func<HotkeySettings> source) => control.SetValue(SourceProperty, source);

    /// <summary>The tooltip from the current bindings: the base text, with the key in brackets when the command has one.</summary>
    public static string Compose(Control control)
    {
        var text = control.GetValue(TextProperty) ?? "";
        var id = control.GetValue(CommandIdProperty);
        var keys = control.GetValue(SourceProperty)?.Invoke() ?? new HotkeySettings();
        return id is null ? text : HotkeyCatalog.TooltipWithKey(keys, text, id);
    }

    public static void Refresh(Control control)
    {
        if (control.GetValue(CommandIdProperty) is null) return;
        ToolTip.SetTip(control, Compose(control));
    }
}
