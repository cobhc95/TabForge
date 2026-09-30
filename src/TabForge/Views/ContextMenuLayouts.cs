namespace TabForge.Views;

/// <summary>
/// The top-level order of the score note menu, the score empty-area menu and the fretboard menu
/// (docs/CONTEXT_MENU_AUDIT.md sections 5-7). The menus in MainWindow are assembled from these lists, and the self-test
/// checks them, so the layout cannot drift from the proposal. <see cref="Sep"/> is a separator.
/// </summary>
public static class ContextMenuLayouts
{
    public const string Sep = "-";
    public const string Copy = "Copy", Cut = "Cut", Paste = "Paste", PasteSpecial = "Paste special…", Delete = "Delete";
    public const string Duration = "Duration", Dynamics = "Dynamics", Effects = "Effects", Beat = "Beat", PitchAndString = "Pitch and string";
    public const string ShowNotation = "Show standard notation", Zoom = "Zoom", ScoreDisplay = "Score display", PlaybackDisplay = "Playback display";
    public const string ShowThisTrackAs = "Show this track as", Scale = "Scale", NoteNames = "Note names", PreviewNext = "Preview next notes";
    public const string AppearanceAndLayout = "Appearance and layout", LockSize = "Lock fretboard size";

    /// <summary>Right-click on a note or inside the selection. The paste items exist only while the clipboard holds a clip.</summary>
    public static IReadOnlyList<string> NoteMenu(bool canPaste)
    {
        var items = new List<string> { Copy, Cut };
        if (canPaste) { items.Add(Paste); items.Add(PasteSpecial); }
        items.AddRange(new[] { Delete, Sep, Duration, Dynamics, Effects, Beat, PitchAndString });
        return items;
    }

    /// <summary>Right-click on empty page. Paste belongs to the beat under the pointer, and only with something to paste.</summary>
    public static IReadOnlyList<string> ScoreEmptyMenu(bool overBeat, bool canPaste)
    {
        var items = new List<string>();
        if (overBeat && canPaste) items.AddRange(new[] { Paste, PasteSpecial, Sep });
        items.AddRange(new[] { ShowNotation, Zoom, ScoreDisplay, PlaybackDisplay });
        return items;
    }

    /// <summary>Fretboard / keyboard (6 entries) and drum pads (4).</summary>
    public static IReadOnlyList<string> Fretboard(bool keyboard, bool drums)
    {
        var items = new List<string> { ShowThisTrackAs, Scale, Sep };
        if (!drums) { items.Add(NoteNames); items.Add(PreviewNext); }
        items.AddRange(new[] { AppearanceAndLayout, Sep, LockSize });
        return items;
    }

    public static int TopLevelCount(IEnumerable<string> items) => items.Count(i => i != Sep);
}
