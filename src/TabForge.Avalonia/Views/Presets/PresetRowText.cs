using System.Collections;
using Avalonia.Controls;

// Owns: the text and the shared ComboBox row of the preset drop-downs: the manage row is always last, carries a marker instead of a
//   preset name, and is never returned as a preset.
// Does not own: the rows themselves (PresetRows in step 3c), the manager window or the stores.
// Tests: PresetManagerOpenTests, NoteMapEditEntryTests, DrumMapEditorWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>The shared preset row words and the trailing manage row.</summary>
public static class PresetRowText
{
    /// <summary>The last row of every preset drop-down and menu: open the manager.</summary>
    public const string Manage = "Manage presets…";

    // One marker for every manage row: a row's Tag is never a preset name when this object is its Tag.
    private static readonly object ManageMarker = new();

    /// <summary>The manage row to append after the presets; its Tag is the marker, not a preset.</summary>
    public static ComboBoxItem ManageItem() => new() { Content = Manage, Tag = ManageMarker };

    /// <summary>Ends a list with the manage row.</summary>
    public static void AppendManage(IList items) => items.Add(ManageItem());

    /// <summary>True when the row is the manage row (never a preset or a map).</summary>
    public static bool IsManage(object? item) => item is ComboBoxItem { Tag: var tag } && ReferenceEquals(tag, ManageMarker);
}
