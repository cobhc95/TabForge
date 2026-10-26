// Owns: the contents of the track row's right-click menu (plain data, one menu for every track kind), and the master track row's menu item (its
//   header, command id and setting key).
// Does not own: opening the menu and running the commands (the Avalonia track header).
// Tests: SelfTestTrackRowMenu, SelfTestMasterLane, TabForge.Avalonia.Tests.ContextMenuTests.
namespace TabForge.Menus;

/// <summary>What the track row menu needs to know about the track it opens on.</summary>
public sealed record TrackRowMenuState(bool IsAudio, bool CanPaste, bool CanDelete, string ColourHex, bool MasterLane = false);

/// <summary>
/// Right-click on a track row: Cut, Copy, Paste (after this track), Duplicate and Delete on top, then Rename, Colour and Properties,
/// and the conversion of its kind (an audio track becomes an instrument track, any other track becomes an audio track). Shortcuts come from the user's bindings
/// (<c>key</c> maps a hotkey id to its display text).
/// </summary>
public static class TrackRowMenus
{
    public const string Cut = "TrackRow.Cut", Copy = "TrackRow.Copy", Paste = "TrackRow.Paste", Duplicate = "TrackRow.Duplicate",
        Delete = "TrackRow.Delete", Rename = "TrackRow.Rename", Colour = "TrackRow.Colour", Properties = "TrackRow.Properties",
        Convert = "TrackRow.Convert", ConvertToAudio = "Track.ConvertToAudio", AddTrack = "Track.Add";

    /// <summary>The master track row's menu item: its caption, command id and Preferences row.</summary>
    public const string MasterLaneHeader = "Show master track", MasterLaneCommandId = "View.MasterTrack", MasterLaneSettingKey = "timeline.mastertrack";

    public static List<MenuSpec> Build(TrackRowMenuState s, Func<string, string> key)
    {
        var colours = TabForge.Presenters.TrackColoursPresenter.Palette.Select(c => new MenuSpec
        {
            Header = c.Name, Id = Colour, Arg = c.Hex, Checkable = true, Radio = true,
            Checked = string.Equals(c.Hex, s.ColourHex, StringComparison.OrdinalIgnoreCase), NoSetting = "state of one track (saved in the song)"
        }).ToList();
        var list = new List<MenuSpec>
        {
            Item(AddTrack, "Add track…", key(AddTrack), true, "Opens the Add track window"),
            MenuSpec.Separator(),
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
        list.Add(MenuSpec.Separator());
        list.Add(s.IsAudio
            ? Item(Convert, "Convert to instrument track…", "", true, "Choose an instrument; the track keeps its audio and MIDI clips")
            : Item(ConvertToAudio, "Convert to audio track…", key("Track.ConvertToAudio"), true, "The notation becomes a MIDI clip on the track; existing clips stay"));
        list.Add(MenuSpec.Separator());
        list.Add(new MenuSpec { Id = MasterLaneCommandId, Header = MasterLaneHeader, Checkable = true, Checked = s.MasterLane, SettingKey = MasterLaneSettingKey, ToolTip = "An overview lane above the first track: one thin line per track, filled where that track plays" });
        return list;
    }

    private static MenuSpec Item(string id, string header, string shortcut, bool enabled = true, string? toolTip = null) =>
        new() { Id = id, Header = header, Shortcut = shortcut, Enabled = enabled, ToolTip = toolTip };
}
