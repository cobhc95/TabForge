namespace TabForge.Views;

/// <summary>
/// The score note menu's top-level order. The score empty-area menu and the fretboard menu are
/// data too: <see cref="ScoreMenus"/> and <see cref="InstrumentMenus"/>. The menus in MainWindow are assembled from these, and
/// the self-test checks them, so a layout cannot drift from the decisions. <see cref="Sep"/> is a separator.
/// </summary>
public static class ContextMenuLayouts
{
    public const string Sep = "-";
    public const string Copy = "Copy", Cut = "Cut", Paste = "Paste", PasteSpecial = "Paste special…", Delete = "Delete";
    public const string Duration = "Duration", Dynamics = "Dynamics", Effects = "Effects", Beat = "Beat", PitchAndString = "Pitch and string";

    /// <summary>
    /// The note menu's shortcut texts for the editor's FIXED keys (they are not commands in the hotkey catalog, so they cannot be rebound and
    /// there is no binding to read). One place for the texts; every bindable command reads the user's binding instead.
    /// </summary>
    public static class FixedKeys
    {
        public const string DeleteNote = "Backspace";
    }

    /// <summary>Right-click on a note or inside the selection. The paste items exist only while the clipboard holds a clip.</summary>
    public static IReadOnlyList<string> NoteMenu(bool canPaste)
    {
        var items = new List<string> { Copy, Cut };
        if (canPaste) { items.Add(Paste); items.Add(PasteSpecial); }
        items.AddRange(new[] { Delete, Sep, Duration, Dynamics, Effects, Beat, PitchAndString });
        return items;
    }

    public static int TopLevelCount(IEnumerable<string> items) => items.Count(i => i != Sep);
}
