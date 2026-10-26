// Owns: the right-click menu of the empty track list (the space below the tracks) as plain data: show tracks in groups, the Colours submenu,
// the master track row, reset track list height and the Track list settings door. Captions and tooltips are the menu's.
// Does not own: opening the menu (Avalonia ContextMenuRows), the colour dialogs or the Settings pages it opens,
// nor the menu's column items (auto fit, reset columns) and dock panel items, which the host adds to its own copy.
// Tests: TabForge.Avalonia.Tests.TrackListMenuTests (pins the captions and order).
namespace TabForge.Menus;

/// <summary>What the track list menu needs to know: whether tracks are shown in groups and whether the master track row is shown.</summary>
public sealed record TrackListMenuState(bool GroupsShown, bool MasterLane);

public static class TrackListMenus
{
    public const string GroupsId = "TrackList.Groups", ColourByGroupId = "TrackList.ColourByGroup", ColourTracksId = "TrackList.ColourTracks",
        SettingsId = "TrackList.Settings";
    public const string GroupsHeader = "Show tracks in groups", ColoursHeader = "Colours", ColourByGroupHeader = "Colour tracks by group",
        ColourTracksHeader = "Colour tracks…", ResetHeightHeader = "Reset track list height", SettingsHeader = "Track list settings…";

    public static List<MenuSpec> Build(TrackListMenuState s) =>
        new()
        {
            new MenuSpec
            {
                Id = GroupsId, Header = GroupsHeader, Checkable = true, Checked = s.GroupsShown, NoSetting = "state of this song's track list (saved in the song)",
                ToolTip = "Show a header per mixer group (guitars, basses...) in the track list, which can be collapsed and dragged as a whole"
            },
            new MenuSpec
            {
                Header = ColoursHeader,
                Children = new List<MenuSpec>
                {
                    new() { Id = ColourByGroupId, Header = ColourByGroupHeader, ToolTip = "Give every track the colour of its group (guitars, basses, drums...). The colour of each group is set in Settings > Appearance > Track colours." },
                    new() { Id = ColourTracksId, Header = ColourTracksHeader },
                }
            },
            MenuSpec.Separator(),
            new MenuSpec
            {
                Id = TrackRowMenus.MasterLaneCommandId, Header = TrackRowMenus.MasterLaneHeader, Checkable = true, Checked = s.MasterLane,
                SettingKey = TrackRowMenus.MasterLaneSettingKey, ToolTip = "An overview lane above the first track: one thin line per track, filled where that track plays"
            },
            MenuSpec.Separator(),
            new MenuSpec
            {
                Command = TimelineCommand.ResetTrackListHeight, Header = ResetHeightHeader,
                ToolTip = "Back to the default row height with every track in view (the same as double-clicking the border above the track list)"
            },
            new MenuSpec { Id = SettingsId, Header = SettingsHeader, ToolTip = "Open Settings > Timeline & Tracks (groups in new songs, automatic sizing, pan and volume controls)" },
        };
}
