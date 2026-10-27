// Owns: the arrangement-timeline right-click menus as data: the bar, loop region, ruler, section and clip menus, and the command each row runs (TimelineCommand).
// Does not own: opening the menus or running their commands (the Avalonia timeline menu).
// Tests: SelfTestMenuGolden, SelfTestSelection, SelfTestBarDeleteGuards, SelfTestTimelineClips.

namespace TabForge.Menus;

/// <summary>Every command the arrangement-timeline menus can run; the window maps each to the existing handler.</summary>
public enum TimelineCommand
{
    None,
    // bar menu
    CopyBar, PasteBar, DeleteBar, InsertBarBefore, InsertBarAfter, CopyBarAllTracks, PasteBarAllTracks, DeleteBarAllTracks, ToggleTrackLines, ResetTrackListHeight,
    CopySection, PasteSectionHere, ToggleSectionLockAtBar,
    // selection menu
    CopySelection, CutSelection, PasteSelection, DeleteSelection, DeleteEmptyBars, LoopSelection, MoveSelection, SkipSelection,
    PlaySkippedAgain, ClearSelection,
    // highlight menu (the grey cells)
    HighlightCopy, HighlightCut, HighlightClear, HighlightPaste, HighlightSelectBars,
    // section menu
    AddSectionHere, CloseSectionGap, PasteSectionAtBar, CopySectionMenu, CutSection, PasteSectionAfter, DuplicateSection, RemoveSectionLabel, DeleteSection, LoopSection,
    RenameSection, GoToSection, ToggleSectionLock,
    // ruler menu (the bar-number strip: the loop region)
    RulerLoop, RulerClearLoop, RulerLoopSection, RulerLoopSelectedBars, RulerMovePlayhead,
    // clip menu
    ClipCopy, ClipCut, ClipPaste, ClipDuplicate, ClipSplit, ClipGlue, ClipFadeReset, ClipDelete, ClipMute, ClipProperties, ClipWriteNotation, ClipAddAudioFile,
    ClipReRecordPass,
    // take-lane menu (an empty part of a clip lane)
    LanePasteClip, LaneNewMidiClip, LaneImport, LaneRecordHere, LaneOpenPianoRoll, LaneRename, LaneMoveUp, LaneMoveDown, LaneDelete,
    // the one "... settings..." door of the bar / selection menus and of the section menu (Preferences > Timeline & sections)
    TimelineSettings, SectionSettings,
}

public sealed record BarMenuState(bool HasBar, bool HasTrack, int TrackCount, bool CanDeleteThisTrack, bool CanDeleteAllTracks,
    bool InSection, bool SectionLocked, bool CanPaste, bool CanPasteAllTracks, bool TrackLines = false);

public sealed record SelectionMenuState(string Label, bool CanPaste, bool Looping, bool Skipped, bool AnySkipped, int EmptyBars = 0);

/// <summary>The ruler menu's state: Loop button on, a loop region exists, a section holds the clicked bar, and the white selection has bars.</summary>
public sealed record RulerMenuState(bool Looping, bool HasLoop, bool InSection, bool HasSelection);

/// <summary>The highlight menu's state: the label of the highlighted cells and whether a copied range can be pasted.</summary>
public sealed record HighlightMenuState(string Label, bool CanPaste);

/// <summary>The take-lane menu's state: a clip is copied, the lane takes MIDI (new clip row) or shows in the piano roll, and the lane can move up, down or be deleted.
/// <paramref name="EmptyLaneClick"/> is the stored "clicking an empty take lane shows" choice (null on an audio/MIDI track, whose lane always opens the piano roll: the row is hidden).</summary>
public sealed record TakeLaneMenuState(bool CanPaste, bool CanNewMidiClip, bool CanOpenInPianoRoll, bool CanMoveUp, bool CanMoveDown, bool CanDelete, string? EmptyLaneClick = null);

public sealed record SectionMenuState(int? AddAtBar, bool CanPaste, bool Looped, bool Locked);

/// <summary>The clip menu's state. <paramref name="IsGrouped"/> marks a repeat take (its parts move and delete as one).</summary>
public sealed record ClipMenuState(bool HasClip, bool IsMidi, bool CanPaste, bool Muted, bool IsGrouped = false, IReadOnlyList<(string Name, bool Active)>? Takes = null);

/// <summary>
/// The arrangement-timeline right-click menus: Copy / Cut / Paste /
/// Delete stay on top, this-track items come before "All tracks", items that do not apply are hidden, shortcuts come from the
/// user's bindings (<c>key</c> maps a hotkey id to its display text), and every appearance option lives in Preferences behind ONE
/// "Timeline settings..." entry.
/// </summary>
public static class TimelineMenus
{
    /// <summary>The Preferences row the timeline menus' settings entry scrolls to (the first timeline appearance row).</summary>
    public const string TimelineSettingsRow = "timeline.individualnotes";
    /// <summary>The row the section menu's settings entry scrolls to.</summary>
    public const string SectionSettingsRow = "timeline.brackets";

    private static MenuSpec Item(TimelineCommand command, string header, string shortcut = "", bool enabled = true, string? toolTip = null, string? arg = null) =>
        new() { Command = command, Header = header, Shortcut = shortcut, Enabled = enabled, ToolTip = toolTip, Arg = arg };

    /// <summary>A tick for state of the thing that was clicked (a section, a clip, the selection): never a preference.</summary>
    private static MenuSpec Check(TimelineCommand command, string header, bool isChecked, string noSetting, string shortcut = "", string? toolTip = null, string? arg = null) =>
        new() { Command = command, Header = header, Shortcut = shortcut, Checkable = true, Checked = isChecked, NoSetting = noSetting, ToolTip = toolTip, Arg = arg };

    private static MenuSpec Sub(string header, params MenuSpec[] children) => new() { Header = header, Children = children.ToList() };

    private static MenuSpec SettingsEntry(TimelineCommand command) =>
        Item(command, "Timeline settings…", toolTip: "Open Settings > Timeline & Tracks (appearance of the timeline, sections and track list)");

    /// <summary>Right-click on a bar (no selection, or outside it).</summary>
    public static List<MenuSpec> Bar(BarMenuState s, Func<string, string> key)
    {
        // With several tracks the first three act on the selected track only, so they say so; "All tracks" follows after a separator.
        var several = s.TrackCount > 1;
        var thisTrack = several ? " (this track)" : "";
        var list = new List<MenuSpec>
        {
            Item(TimelineCommand.CopyBar, "Copy bar" + thisTrack, key("Edit.Copy"), s.HasBar && s.HasTrack, "Copies this bar of the selected track"),
            Item(TimelineCommand.PasteBar, "Paste bar" + thisTrack, key("Edit.Paste"), s.HasBar && s.HasTrack && s.CanPaste),
            Item(TimelineCommand.DeleteBar, "Delete bar…", key("Range.Delete"), s.HasBar && s.HasTrack, "Asks what to do: leave the bar empty or remove it and close the gap; all tracks or this track"),
        };
        if (s.HasBar)
        {
            list.Add(MenuSpec.Separator());
            list.Add(Sub("Insert bar",
                Item(TimelineCommand.InsertBarBefore, "Before this bar", key("Bar.Insert")),
                Item(TimelineCommand.InsertBarAfter, "After this bar")));
            if (s.InSection)
                list.Add(Sub("Section",
                    Item(TimelineCommand.CopySection, "Copy section"),
                    Item(TimelineCommand.PasteSectionHere, "Paste section here", enabled: s.CanPaste),
                    Check(TimelineCommand.ToggleSectionLockAtBar, "Lock section position", s.SectionLocked, "state of one section (saved in the song)")));
            if (several)
            {
                list.Add(MenuSpec.Separator());
                list.Add(Sub("All tracks",
                    Item(TimelineCommand.CopyBarAllTracks, "Copy bar (all tracks)"),
                    Item(TimelineCommand.PasteBarAllTracks, "Paste bar into all tracks", enabled: s.CanPasteAllTracks)));
            }
        }
        list.Add(MenuSpec.Separator());
        list.Add(new MenuSpec { Command = TimelineCommand.ToggleTrackLines, Header = "Lines between tracks", Shortcut = key("View.ToggleTrackLines"), Checkable = true, Checked = s.TrackLines, SettingKey = "timeline.tracklines" });
        list.Add(new MenuSpec { Command = TimelineCommand.ResetTrackListHeight, Header = "Reset track list height", Shortcut = key("View.ResetTrackRowHeight"), ToolTip = "Back to the default row height with every track in view (the same as double-clicking the border above the track list)" });
        list.Add(SettingsEntry(TimelineCommand.TimelineSettings));
        return list;
    }

    /// <summary>Right-click on the bar-number ruler: the loop region's menu. Each loop change is one undo step.</summary>
    public static List<MenuSpec> Ruler(RulerMenuState s, Func<string, string> key) => new()
    {
        Check(TimelineCommand.RulerLoop, "Loop", s.Looping, "transport state (not saved as a preference)", key("Transport.Loop"), "Repeats the loop region (or the whole song) while on; the region stays when it is turned off"),
        Item(TimelineCommand.RulerClearLoop, "Clear loop", "Esc twice", s.HasLoop, "Removes the loop region; the Loop button stays as it is (Ctrl+Z brings it back)"),
        MenuSpec.Separator(),
        Item(TimelineCommand.RulerLoopSection, "Loop this section", enabled: s.InSection, toolTip: "Loops the bars of the section under the click"),
        Item(TimelineCommand.RulerLoopSelectedBars, "Loop selected bars", enabled: s.HasSelection, toolTip: "Loops the bars of the selected notes or clips"),
        MenuSpec.Separator(),
        Item(TimelineCommand.RulerMovePlayhead, "Move playhead here"),
    };

    /// <summary>Right-click inside the loop region on the lanes: the loop region menu only (the single-bar items are for right-clicks outside it).</summary>
    public static List<MenuSpec> Selection(SelectionMenuState s, Func<string, string> key)
    {
        var arrange = new List<MenuSpec>
        {
            Item(TimelineCommand.MoveSelection, "Move selection…", toolTip: "Then click the new position on the timeline"),
            Check(TimelineCommand.SkipSelection, "Skip during playback", s.Skipped, "state of the selected bars (this session)"),
        };
        if (s.AnySkipped) arrange.Add(Item(TimelineCommand.PlaySkippedAgain, "Play all skipped areas again"));
        var items = new List<MenuSpec>
        {
            new() { Header = s.Label, IsLabel = true, Enabled = false },
            Item(TimelineCommand.CopySelection, "Copy", key("Edit.Copy"), toolTip: "Copies the looped bars of every track"),
            Item(TimelineCommand.CutSelection, "Cut", key("Edit.Cut"), toolTip: "Cut copies the bars, then removes them and closes the gap (every track) or clears them (one track)"),
            Item(TimelineCommand.PasteSelection, "Paste", key("Edit.Paste"), s.CanPaste, "Bars of every track are inserted in front of the selection; one track's bars go onto the selected track"),
            Item(TimelineCommand.DeleteSelection, "Delete…", key("Range.Delete"), toolTip: "Asks what to do: clear the bars, remove them and close the gap, or insert a gap (all tracks or this track)"),
            MenuSpec.Separator(),
            Check(TimelineCommand.LoopSelection, "Loop", s.Looping, "transport state (not saved as a preference)", key("Transport.Loop")),
            new MenuSpec { Header = "Arrange", Children = arrange },
            MenuSpec.Separator(),
            Item(TimelineCommand.ClearSelection, "Clear loop", "Esc twice"),
            MenuSpec.Separator(),
            SettingsEntry(TimelineCommand.TimelineSettings),
        };
        if (s.EmptyBars > 0)
            items.Insert(items.FindIndex(m => m.Command == TimelineCommand.DeleteSelection) + 1,
                Item(TimelineCommand.DeleteEmptyBars, "Delete empty bars", toolTip: "Deletes only the selected bars that hold no notes on any track"));
        return items;
    }

    /// <summary>Right-click on the grey highlighted cells: Copy, Cut, Clear, Paste and Select these bars (the highlight's bars as a range selection).</summary>
    public static List<MenuSpec> Highlight(HighlightMenuState s, Func<string, string> key) => new()
    {
        new() { Header = s.Label, IsLabel = true, Enabled = false },
        Item(TimelineCommand.HighlightCopy, "Copy", key("Highlight.Copy"), toolTip: "Copies exactly the highlighted cells"),
        Item(TimelineCommand.HighlightCut, "Cut", key("Highlight.Cut"), toolTip: "Copies the highlighted cells, then clears their notes (the bars stay)"),
        Item(TimelineCommand.HighlightClear, "Clear", key("Highlight.Delete"), toolTip: "Clears the notes in the highlighted cells (the bars stay)"),
        Item(TimelineCommand.HighlightPaste, "Paste", key("Highlight.Paste"), s.CanPaste, "Pastes the copied cells from the top-left highlighted cell"),
        MenuSpec.Separator(),
        Item(TimelineCommand.HighlightSelectBars, "Loop these bars", toolTip: "Makes the highlighted bars the loop region"),
    };

    /// <summary>Right-click on an empty part of the section strip (no section under the pointer): Add section and Paste section at the clicked bar, Close gap when
    /// <paramref name="gap"/> (the empty run of bars before the next section) exists, and the settings.</summary>
    public static List<MenuSpec> SectionGap(int bar, bool canPaste, (int First, int Last)? gap, Func<string, string> key)
    {
        var list = new List<MenuSpec> { Item(TimelineCommand.AddSectionHere, $"Add section at bar {bar + 1}", key("Section.Add")) };
        if (gap is var (first, last))
        {
            var close = Item(TimelineCommand.CloseSectionGap, $"Close gap (bars {first + 1}–{last + 1})",
                toolTip: "Removes these bars and moves every later section, bar and clip back. Asks first when the bars are not empty.");
            close.HoverBars = (first, last);
            list.Add(close);
        }
        list.Add(Item(TimelineCommand.PasteSectionAtBar, "Paste section", key("Edit.Paste"), canPaste));
        list.Add(MenuSpec.Separator());
        list.Add(SettingsEntry(TimelineCommand.TimelineSettings));
        return list;
    }

    /// <summary>Right-click on a section bracket or body.</summary>
    public static List<MenuSpec> Section(SectionMenuState s, Func<string, string> key)
    {
        var list = new List<MenuSpec>();
        if (s.AddAtBar is int bar)
        {
            list.Add(Item(TimelineCommand.AddSectionHere, $"Add section at bar {bar + 1}", key("Section.Add")));
            list.Add(MenuSpec.Separator());
        }
        list.Add(Item(TimelineCommand.CopySectionMenu, "Copy section", key("Edit.Copy")));
        list.Add(Item(TimelineCommand.CutSection, "Cut section", key("Edit.Cut")));
        list.Add(Item(TimelineCommand.PasteSectionAfter, "Paste section", key("Edit.Paste"), s.CanPaste));
        list.Add(Item(TimelineCommand.DuplicateSection, "Duplicate section"));
        list.Add(Item(TimelineCommand.RemoveSectionLabel, "Remove label (keep bars)", toolTip: "Removes only the section name and marker; all bars, notes and clips stay and the previous section extends over them. Undo restores it."));
        list.Add(Item(TimelineCommand.DeleteSection, "Delete section and its bars…", toolTip: "Removes the section marker and its bars and notes from every track. Asks first (Preferences > Timeline and Tracks > Confirm section deletion); Undo restores it. To remove only the marker use Remove in the Sections panel."));
        list.Add(MenuSpec.Separator());
        list.Add(Check(TimelineCommand.LoopSection, "Loop section", s.Looped, "transport state (not saved as a preference)", key("Transport.Loop")));
        list.Add(Item(TimelineCommand.RenameSection, "Rename / recolour section…"));
        list.Add(Item(TimelineCommand.GoToSection, "Go to section"));
        list.Add(Check(TimelineCommand.ToggleSectionLock, "Lock section position", s.Locked, "state of one section (saved in the song)"));
        list.Add(MenuSpec.Separator());
        list.Add(SettingsEntry(TimelineCommand.SectionSettings));
        return list;
    }

    /// <summary>The command id of the Takes submenu rows; the row's Arg is the take number (1-based).</summary>
    public const string TakeId = "Clip.Take";

    /// <summary>The command id of the take-lane menu's "Clicking an empty lane shows" rows; the row's Arg is the stored value (EmptyLaneChoice), the Preferences row editing.emptylane.</summary>
    public const string EmptyLaneClickId = "TimelineLane.EmptyClick";

    /// <summary>Right-click on an audio / MIDI clip, or on an empty lane.</summary>
    public static List<MenuSpec> Clip(ClipMenuState s, Func<string, string> key)
    {
        var list = new List<MenuSpec>();
        if (s.HasClip)
        {
            list.Add(Item(TimelineCommand.ClipCopy, "Copy", key("Clip.Copy")));
            list.Add(Item(TimelineCommand.ClipCut, "Cut", key("Clip.Cut")));
        }
        list.Add(Item(TimelineCommand.ClipPaste, "Paste", key("Clip.Paste"), s.CanPaste));
        if (s.HasClip && s.IsGrouped)
        {
            list.Add(Item(TimelineCommand.ClipReRecordPass, "Re-record this pass",
                toolTip: "Count-in (when on), plays this pass's loop once, stops at its end and replaces only this pass"));
        }
        if (s.HasClip && s.Takes is { Count: > 1 } takes)
        {
            var sub = new List<MenuSpec>();
            for (var i = 0; i < takes.Count; i++)
                sub.Add(new MenuSpec { Header = takes[i].Name, Id = TakeId, Arg = (i + 1).ToString(), Checkable = true, Checked = takes[i].Active, Radio = true,
                    NoSetting = "state of one recording's takes (saved in the song)" });
            list.Add(new MenuSpec { Header = "Takes", Children = sub, ToolTip = "Chooses which loop-recording take plays" });
        }
        if (s.HasClip)
        {
            list.Add(Item(TimelineCommand.ClipDuplicate, "Duplicate", key("Clip.Duplicate")));
            list.Add(Item(TimelineCommand.ClipSplit, "Split at cursor", key("Clip.Split")));
            list.Add(Item(TimelineCommand.ClipGlue, "Glue", key("Clip.Glue")));
            if (!s.IsMidi) list.Add(Item(TimelineCommand.ClipFadeReset, "Reset fade in / out", key("Clip.FadeReset")));
            list.Add(Item(TimelineCommand.ClipDelete, "Delete", key("Clip.Delete")));
            list.Add(MenuSpec.Separator());
            list.Add(Check(TimelineCommand.ClipMute, "Mute", s.Muted, "state of one clip (saved in the song)", key("Clip.Mute")));
            list.Add(Item(TimelineCommand.ClipProperties, "Properties…", key("Clip.Properties")));
            // "Advanced notation conversion…" is hidden until it exists (docs/plans/MIDI_TO_TAB_PLAN.md).
            if (s.IsMidi) list.Add(Item(TimelineCommand.ClipWriteNotation, "Write into the track's notation"));
        }
        else
        {
            list.Add(MenuSpec.Separator());
            list.Add(Item(TimelineCommand.ClipAddAudioFile, "Add audio file…"));
        }
        return list;
    }

    /// <summary>Right-click on an empty part of a take lane (no clip under the pointer): the lane's own actions. Rows that do not apply to the lane are hidden.</summary>
    public static List<MenuSpec> TakeLane(TakeLaneMenuState s, Func<string, string> key)
    {
        var list = new List<MenuSpec>
        {
            Item(TimelineCommand.LanePasteClip, "Paste clip here", key("Clip.Paste"), s.CanPaste, "Pastes the copied clip at the clicked position (snapped like a clip drag)"),
        };
        if (s.CanNewMidiClip)
            list.Add(Item(TimelineCommand.LaneNewMidiClip, "New empty MIDI clip here", toolTip: "Adds an empty clip one bar long at the clicked position"));
        list.Add(Item(TimelineCommand.LaneImport, "Import audio or MIDI file here…", toolTip: "Picks a file and puts it on the timeline at the clicked position"));
        list.Add(Item(TimelineCommand.LaneRecordHere, "Record here", toolTip: "Arms this track and moves the playhead to the clicked bar; press Record to start"));
        if (s.CanOpenInPianoRoll)
            list.Add(Item(TimelineCommand.LaneOpenPianoRoll, "Open lane in piano roll", toolTip: "Edits this lane's MIDI notes in the piano roll"));
        if (s.EmptyLaneClick is { } shows)
            list.Add(Sub("Clicking an empty lane shows", TabForge.Services.EmptyLaneChoice.Values.Select((value, i) => new MenuSpec
            {
                Id = EmptyLaneClickId, Arg = value, Header = TabForge.Services.EmptyLaneChoice.Labels[i], Checkable = true, Radio = true, Checked = TabForge.Services.EmptyLaneChoice.Normalize(shows) == value,
                SettingKey = "editing.emptylane", ToolTip = "Chooses what a click on empty space in a take lane of a tab track shows",
            }).ToArray()));
        list.Add(MenuSpec.Separator());
        list.Add(Sub("Lane",
            Item(TimelineCommand.LaneRename, "Rename lane…"),
            Item(TimelineCommand.LaneMoveUp, "Move lane up", enabled: s.CanMoveUp),
            Item(TimelineCommand.LaneMoveDown, "Move lane down", enabled: s.CanMoveDown),
            Item(TimelineCommand.LaneDelete, "Delete lane…", enabled: s.CanDelete, toolTip: "Removes the lane and its clips (asks first when it has clips)")));
        list.Add(MenuSpec.Separator());
        list.Add(SettingsEntry(TimelineCommand.TimelineSettings));
        return list;
    }

    /// <summary>All clickable entries of a menu tree, depth first (for tests).</summary>
    public static IEnumerable<MenuSpec> Leaves(IEnumerable<MenuSpec> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator || item.IsLabel) continue;
            if (item.Children is null) yield return item;
            else foreach (var child in Leaves(item.Children)) yield return child;
        }
    }

    /// <summary>The deepest number of submenus above any entry (a top-level entry is 0, a child of a submenu 1).</summary>
    public static int MaxDepth(IEnumerable<MenuSpec> items, int depth = 0) =>
        items.Where(i => !i.IsSeparator).Select(i => i.Children is null ? depth : MaxDepth(i.Children, depth + 1)).DefaultIfEmpty(depth).Max();

    /// <summary>Submenus that sit inside a submenu of the same name (or of a name that contains it): "Appearance > Appearance".</summary>
    public static IEnumerable<string> SameNameNesting(IEnumerable<MenuSpec> items, string? parent = null)
    {
        foreach (var item in items)
        {
            if (item.Children is null) continue;
            if (parent is not null && (string.Equals(item.Header, parent, StringComparison.OrdinalIgnoreCase) ||
                                       item.Header.Contains(parent, StringComparison.OrdinalIgnoreCase)))
                yield return $"{parent} > {item.Header}";
            foreach (var inner in SameNameNesting(item.Children, item.Header)) yield return inner;
        }
    }

    /// <summary>Every checkable entry (the setting-like ones), depth first.</summary>
    public static IEnumerable<MenuSpec> Checkables(IEnumerable<MenuSpec> items) => Leaves(items).Where(i => i.Checkable);
}
