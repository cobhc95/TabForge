using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the empty track list's right-click menu: its rows pinned to the captions and order (ArrangementPanel.Groups.cs BuildEmptyAreaMenu),
//   and which of its rows the Avalonia window runs (Colour tracks and Settings open their preview dialogs; the rest render disabled).
// Does not own: the Core rows (TrackListMenus), the pixels (ParityShotsTests, context-timeline-empty) or the Tools submenu (PreviewToolsMenu).
// Tests: this file.
public class TrackListMenuTests
{
    [Fact]
    public void ItemsAndOrderMatchTheWpfMenu()
    {
        var rows = TrackListMenus.Build(new TrackListMenuState(GroupsShown: false, MasterLane: false));

        var captions = rows.Select(r => r.IsSeparator ? "-" : r.Header).ToList();
        Assert.Equal(new[] { "Show tracks in groups", "Colours", "-", "Show master track", "-", "Reset track list height", "Track list settings…" }, captions);
        var colours = rows[1].Children!;
        Assert.Equal(new[] { "Colour tracks by group", "Colour tracks…" }, colours.Select(c => c.Header));
    }

    [Fact]
    public void GroupsAndMasterTrackTicksFollowTheirState()
    {
        var rows = TrackListMenus.Build(new TrackListMenuState(GroupsShown: true, MasterLane: true));

        Assert.True(rows[0].Checked);
        Assert.True(rows.Single(r => r.Id == TrackRowMenus.MasterLaneCommandId).Checked);
    }

    [AvaloniaFact]
    public void RowsWithNoAvaloniaHandlerRenderDisabled()
    {
        var rows = TrackListMenus.Build(new TrackListMenuState(GroupsShown: false, MasterLane: false));
        var menu = ContextMenuBuilder.Build(rows, ContextMenuHost.NoCommands);

        var groups = Assert.IsType<MenuItem>(menu.Items[0]);
        Assert.False(groups.IsEnabled);
        Assert.All(menu.Items.OfType<MenuItem>().Where(item => item.Items.Count == 0), item => Assert.False(item.IsEnabled));
    }

    [AvaloniaFact]
    public void ColourTracksIsBoundByTheTrackListRowsOnly()
    {
        var owner = new Window();
        var commands = new MainWindowCommands(owner, () => { });

        // The table binds no track list row: MainWindowTrackListRows binds Colour tracks, groups and settings.
        Assert.False(commands.Handles(TrackListMenus.ColourTracksId));
        Assert.False(commands.Handles(TrackListMenus.SettingsId));
        Assert.False(commands.Handles(TrackListMenus.GroupsId));
        Assert.False(commands.Handles(TrackListMenus.ColourByGroupId));
        // The Tools submenu does not list the Colour tracks window: it opens from the track list row.
        Assert.DoesNotContain(commands.Unclaimed, p => p.Header == "Colour tracks…");
    }
}
