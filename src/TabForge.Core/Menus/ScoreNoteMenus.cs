// Owns: the score's note right-click menu as data, in the note menu's order: Copy, Cut, Paste and Paste special (while the clipboard holds
//   a clip), Delete, then the Duration, Dynamics, Effects and Beat groups of the palette and the Pitch and string submenu. Each group row is
//   a palette tool: its command id is PaletteToolCatalog.CommandIdFor, its lit state and enabled rule come from the caller's editor.
// Does not own: opening the menu or running its rows (Avalonia ScorePreviewView and ContextMenuBuilder), the lit and enabled rules
//   (PaletteToolStates, reached through ScoreNoteMenuState), the palette tool rows (PaletteToolCatalog).
// Tests: TabForge.Avalonia.Tests.ScoreContextMenuTests.

using TabForge.Presenters;

namespace TabForge.Menus;

/// <summary>What the note menu reads from the editor: whether a clip can be pasted, a palette tool's lit state (null: no state) and enabled rule,
/// whether a duration tool can set its duration, and the shortcut text of a command id.</summary>
public sealed record ScoreNoteMenuState(bool CanPaste, Func<string, bool?> ToolState, Func<string, bool> ToolEnabled,
    Func<string, bool> CanSetDuration, Func<string, string> Key);

public static class ScoreNoteMenus
{
    /// <summary>The note menu's header texts and the palette groups it lists, in menu order.</summary>
    public const string CopyHeader = "Copy", CutHeader = "Cut", PasteHeader = "Paste", PasteSpecialHeader = "Paste special…", DeleteHeader = "Delete",
        DurationGroup = "Duration", DynamicsGroup = "Dynamics", EffectsGroup = "Effects", BeatGroup = "Beat", PitchAndStringHeader = "Pitch and string";

    /// <summary>The shortcut text of Delete: the editor's fixed Backspace key, which is not a bindable command.</summary>
    public const string DeleteShortcut = "Backspace";

    /// <summary>The prefix a duration tool's id carries; the rest of the id is the key <see cref="ScoreNoteMenuState.CanSetDuration"/> reads.</summary>
    private const string DurationToolPrefix = "duration:";

    /// <summary>The menu's rows for the note under the pointer or the selection the menu acts on.</summary>
    public static List<MenuSpec> Rows(ScoreNoteMenuState state)
    {
        var rows = new List<MenuSpec>
        {
            new() { Id = "Edit.Copy", Header = CopyHeader, Shortcut = state.Key("Edit.Copy") },
            new() { Id = "Edit.Cut", Header = CutHeader, Shortcut = state.Key("Edit.Cut") },
        };
        if (state.CanPaste)
        {
            rows.Add(new MenuSpec { Id = ScoreMenus.PasteId, Header = PasteHeader, Shortcut = state.Key("Edit.Paste") });
            rows.Add(new MenuSpec { Id = ScoreMenus.PasteSpecialId, Header = PasteSpecialHeader, Shortcut = state.Key("Edit.PasteSpecial") });
        }
        rows.Add(new MenuSpec { Id = PaletteToolCatalog.CommandIdFor("edit:erase_note"), Header = DeleteHeader, Shortcut = DeleteShortcut });
        rows.Add(MenuSpec.Separator());
        rows.Add(Group(DurationGroup, "Duration", state, radio: true));
        rows.Add(Group(DynamicsGroup, "Dynamic", state, radio: true));
        rows.Add(Group(EffectsGroup, "Effects", state, radio: false));
        rows.Add(Group(BeatGroup, "Beat", state, radio: false));
        rows.Add(PitchAndString(state));
        return rows;
    }

    /// <summary>A submenu of the supported palette tools in <paramref name="category"/>; choices of one value (durations, dynamics) are radio rows.</summary>
    private static MenuSpec Group(string header, string category, ScoreNoteMenuState state, bool radio) => new()
    {
        Header = header,
        Children = PaletteToolCatalog.PaletteTools.Where(tool => tool.Group == category && tool.Supported).Select(tool => ToolRow(tool, state, radio)).ToList(),
    };

    /// <summary>One palette tool as a row: checkable when the tool has a lit state (on or off), enabled when the tool can act at the cursor.</summary>
    private static MenuSpec ToolRow(PaletteTool tool, ScoreNoteMenuState state, bool radio)
    {
        var row = new MenuSpec { Id = PaletteToolCatalog.CommandIdFor(tool.Id), Header = tool.Label };
        if (state.ToolState(tool.Id) is { } lit)
        {
            row.Checkable = true;
            row.Checked = lit;
            row.Radio = radio;
        }
        var durationKey = tool.Id.StartsWith(DurationToolPrefix, StringComparison.Ordinal) ? tool.Id[DurationToolPrefix.Length..] : null;
        row.Enabled = (durationKey is null || state.CanSetDuration(durationKey)) && state.ToolEnabled(tool.Id);
        return row;
    }

    private static MenuSpec PitchAndString(ScoreNoteMenuState state) => new()
    {
        Header = PitchAndStringHeader,
        Children = new List<MenuSpec>
        {
            new() { Id = "Note.PitchUp", Header = "Pitch up a semitone", Shortcut = state.Key("Note.PitchUp") },
            new() { Id = "Note.PitchDown", Header = "Pitch down a semitone", Shortcut = state.Key("Note.PitchDown") },
            new() { Id = "Note.MoveStringUp", Header = "Move note to higher string", Shortcut = state.Key("Note.MoveStringUp") },
            new() { Id = "Note.MoveStringDown", Header = "Move note to lower string", Shortcut = state.Key("Note.MoveStringDown") },
        },
    };
}
