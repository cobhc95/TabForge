namespace TabForge.Views;

/// <summary>What the track row menu needs to know about the track it opens on.</summary>
internal sealed record TrackRowMenuState(bool IsAudio, bool CanPaste, bool CanDelete, string ColourHex);

// Owns: the contents of the track row's right-click menu (plain data, one menu for every track kind).
// Does not own: opening the menu and running the commands (MainWindow.TrackMenu.cs, TrackClipboardFlow).
// Tests: TestTrackRowMenu.
/// <summary>
/// Right-click on a track row: Cut, Copy, Paste (after this track), Duplicate and Delete on top, then Rename, Colour and Properties,
/// and the items of one kind only (an audio track can become an instrument track). Shortcuts come from the user's bindings
/// (<c>key</c> maps a hotkey id to its display text).
/// </summary>
internal static class TrackRowMenus
{
    public const string Cut = "TrackRow.Cut", Copy = "TrackRow.Copy", Paste = "TrackRow.Paste", Duplicate = "TrackRow.Duplicate",
        Delete = "TrackRow.Delete", Rename = "TrackRow.Rename", Colour = "TrackRow.Colour", Properties = "TrackRow.Properties",
        Convert = "TrackRow.Convert";

    public static List<MenuSpec> Build(TrackRowMenuState s, Func<string, string> key)
    {
        var colours = TrackControlWidgets.TrackColourPalette.Select(c => new MenuSpec
        {
            Header = c.Name, Id = Colour, Arg = c.Hex, Checkable = true, Radio = true,
            Checked = string.Equals(c.Hex, s.ColourHex, StringComparison.OrdinalIgnoreCase), NoSetting = "state of one track (saved in the song)"
        }).ToList();
        var list = new List<MenuSpec>
        {
            Item(Cut, "Cut", key(Cut), s.CanDelete, "Copies the track and removes it from the song"),
            Item(Copy, "Copy", key(Copy)),
            Item(Paste, "Paste (after this track)", key(Paste), s.CanPaste, "Inserts the copied track after this one"),
            Item(Duplicate, "Duplicate", key(Duplicate)),
            Item(Delete, "Delete…", key(Delete), s.CanDelete, "Asks first; undo brings the track back"),
            MenuSpec.Separator(),
            Item(Rename, "Rename", ""),
            new MenuSpec { Header = "Colour", Children = colours },
            Item(Properties, "Properties…", key("Track.Properties")),
        };
        if (s.IsAudio)
        {
            list.Add(MenuSpec.Separator());
            list.Add(Item(Convert, "Convert to instrument track…", "", true, "Choose an instrument; the track keeps its audio and MIDI clips"));
        }
        return list;
    }

    private static MenuSpec Item(string id, string header, string shortcut, bool enabled = true, string? toolTip = null) =>
        new() { Id = id, Header = header, Shortcut = shortcut, Enabled = enabled, ToolTip = toolTip };
}
