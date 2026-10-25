using System;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace TabForge.Avalonia;

// Owns: deciding whether keyboard focus sits in an editing control: a text box, the only control Space belongs to (WPF's text-input rule),
//   or, for Escape, a combo box or slider or one of their parts (such as its drop-down items).
// Does not own: the key table (MainWindowKeyboard), the text-box rule for the other keys (MainWindowKeyboard.Route).
// Tests: TabForge.Avalonia.Tests.MainWindowSpaceEscapeRoutingTests.
internal static class MainWindowEditingFocus
{
    /// <summary>True when <paramref name="focused"/> or one of its logical ancestors is a text box: Space types there, as WPF's text-input rule.</summary>
    public static bool IsTextInput(object? focused) => IsWithin(focused, static node => node is TextBox);

    /// <summary>True when <paramref name="focused"/> or one of its logical ancestors is an editing control (text box, combo box or slider).</summary>
    public static bool IsEditing(object? focused) => IsWithin(focused, static node => node is TextBox or ComboBox or Slider);

    private static bool IsWithin(object? focused, Func<ILogical, bool> match)
    {
        if (focused is not ILogical start) return false;
        for (ILogical? node = start; node is not null; node = node.LogicalParent)
            if (match(node)) return true;
        return false;
    }
}
