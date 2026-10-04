namespace TabForge.Views;

/// <summary>One entry of a right-click menu as plain data (a separator has <see cref="IsSeparator"/>). Built by
/// <see cref="TimelineMenus"/>, <see cref="InstrumentMenus"/> and <see cref="ScoreMenus"/>, turned into WPF items by the window,
/// and enumerated by the self-test.</summary>
internal sealed class MenuSpec
{
    public string Header = "";
    public string Shortcut = "";
    public string? ToolTip;
    public bool Enabled = true;
    public bool Checkable;
    public bool Checked;
    public bool IsSeparator;
    /// <summary>A grey information line (never clickable).</summary>
    public bool IsLabel;
    public TimelineCommand Command;
    public List<MenuSpec>? Children;
    /// <summary>A one-of-several choice: drawn as a dot, not a tick (<see cref="MenuMarks"/>).</summary>
    public bool Radio;
    /// <summary>The <see cref="TabForge.Services.SettingsCatalog"/> row this setting-like item mirrors (every checkable item has this or <see cref="NoSetting"/>).</summary>
    public string? SettingKey;
    /// <summary>Why a checkable item has no Preferences row: it is state of one track, bar, section, clip or the selection, not a preference.</summary>
    public string? NoSetting;
    /// <summary>Command id and argument of the menus that are not the timeline's (fretboard, score).</summary>
    public string? Id;
    public string? Arg;

    public static MenuSpec Separator() => new() { IsSeparator = true };
}

/// <summary>Every command the arrangement-timeline menus can run; the window maps each to the existing handler.</summary>
internal enum TimelineCommand
{
    None,
    // bar menu
    CopyBar, PasteBar, DeleteBar, InsertBarBefore, InsertBarAfter, CopyBarAllTracks, PasteBarAllTracks, DeleteBarAllTracks,
    CopySection, PasteSectionHere, ToggleSectionLockAtBar,
    // selection menu
    CopySelection, CutSelection, PasteSelection, DeleteSelection, DeleteEmptyBars, LoopSelection, MoveSelection, SkipSelection,
    PlaySkippedAgain, ClearSelection,
    // section menu
    AddSectionHere, CopySectionMenu, CutSection, PasteSectionAfter, DuplicateSection, DeleteSection, LoopSection,
    RenameSection, GoToSection, ToggleSectionLock,
    // clip menu
    ClipCopy, ClipCut, ClipPaste, ClipDuplicate, ClipSplit, ClipGlue, ClipFadeReset, ClipDelete, ClipMute, ClipProperties, ClipWriteNotation, ClipAddAudioFile,
    // the one "... settings..." door of the bar / selection menus and of the section menu (Preferences > Timeline & sections)
    TimelineSettings, SectionSettings,
}

internal sealed record BarMenuState(bool HasBar, bool HasTrack, int TrackCount, bool CanDeleteThisTrack, bool CanDeleteAllTracks,
    bool InSection, bool SectionLocked, bool CanPaste, bool CanPasteAllTracks);

internal sealed record SelectionMenuState(string Label, bool CanPaste, bool Looping, bool Skipped, bool AnySkipped, int EmptyBars = 0);

internal sealed record SectionMenuState(int? AddAtBar, bool CanPaste, bool Looped, bool Locked);

internal sealed record ClipMenuState(bool HasClip, bool IsMidi, bool CanPaste, bool Muted);

/// <summary>
/// The arrangement-timeline right-click menus (owner request 2026-09-30, docs/CONTEXT_MENU_AUDIT.md and _2): Copy / Cut / Paste /
/// Delete stay on top, this-track items come before "All tracks", items that do not apply are hidden, shortcuts come from the
/// user's bindings (<c>key</c> maps a hotkey id to its display text), and every appearance option lives in Preferences behind ONE
/// "Timeline settings..." entry.
/// </summary>
internal static class TimelineMenus
{
    /// <summary>The Preferences row the timeline menus' settings entry scrolls to (the first timeline appearance row).</summary>
    public const string TimelineSettingsRow = "timeline.individualnotes";
    /// <summary>The row the section menu's settings entry scrolls to.</summary>
    public const string SectionSettingsRow = "timeline.brackets";

    private static MenuSpec Item(TimelineCommand command, string header, string shortcut = "", bool enabled = true, string? toolTip = null) =>
        new() { Command = command, Header = header, Shortcut = shortcut, Enabled = enabled, ToolTip = toolTip };

    /// <summary>A tick for state of the thing that was clicked (a section, a clip, the selection): never a preference.</summary>
    private static MenuSpec Check(TimelineCommand command, string header, bool isChecked, string noSetting, string shortcut = "", string? toolTip = null) =>
        new() { Command = command, Header = header, Shortcut = shortcut, Checkable = true, Checked = isChecked, NoSetting = noSetting, ToolTip = toolTip };

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
            Item(TimelineCommand.DeleteBar, "Delete bar…", key("Range.Delete"), s.HasBar && s.HasTrack, "Asks what to do: delete the bar and leave or close the gap, or insert a gap; all tracks or this track"),
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
        list.Add(SettingsEntry(TimelineCommand.TimelineSettings));
        return list;
    }

    /// <summary>Right-click inside the selected bars: the selection menu only (the single-bar items are for right-clicks outside it).</summary>
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
            Item(TimelineCommand.CopySelection, "Copy", key("Edit.Copy"), toolTip: "Copies the selected bars of every track"),
            Item(TimelineCommand.CutSelection, "Cut", key("Edit.Cut"), toolTip: "Cut removes the bars and closes the gap"),
            Item(TimelineCommand.PasteSelection, "Paste", key("Edit.Paste"), s.CanPaste, "Pastes the copied bars in front of the selection"),
            Item(TimelineCommand.DeleteSelection, "Delete…", key("Range.Delete"), toolTip: "Asks what to do: clear the bars, remove them and close the gap, or insert a gap (all tracks or this track)"),
            MenuSpec.Separator(),
            Check(TimelineCommand.LoopSelection, "Loop selection", s.Looping, "transport state (not saved as a preference)", key("Transport.Loop")),
            new MenuSpec { Header = "Arrange", Children = arrange },
            MenuSpec.Separator(),
            Item(TimelineCommand.ClearSelection, "Clear selection", key("Clip.Deselect")),
            MenuSpec.Separator(),
            SettingsEntry(TimelineCommand.TimelineSettings),
        };
        if (s.EmptyBars > 0)
            items.Insert(items.FindIndex(m => m.Command == TimelineCommand.DeleteSelection) + 1,
                Item(TimelineCommand.DeleteEmptyBars, "Delete empty bars", toolTip: "Deletes only the selected bars that hold no notes on any track"));
        return items;
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
        if (s.HasClip)
        {
            list.Add(Item(TimelineCommand.ClipDuplicate, "Duplicate", key("Clip.Duplicate")));
            list.Add(Item(TimelineCommand.ClipSplit, "Split at cursor", key("Clip.Split")));
            list.Add(Item(TimelineCommand.ClipGlue, "Glue", key("Clip.Glue")));
            list.Add(Item(TimelineCommand.ClipFadeReset, "Reset fade in / out", key("Clip.FadeReset")));
            list.Add(Item(TimelineCommand.ClipDelete, "Delete", key("Clip.Delete")));
            list.Add(MenuSpec.Separator());
            list.Add(Check(TimelineCommand.ClipMute, "Mute", s.Muted, "state of one clip (saved in the song)", key("Clip.Mute")));
            list.Add(Item(TimelineCommand.ClipProperties, "Properties…", key("Clip.Properties")));
            // "Advanced notation conversion…" is hidden until it exists (docs/MIDI_TO_TAB_PLAN.md).
            if (s.IsMidi) list.Add(Item(TimelineCommand.ClipWriteNotation, "Write into the track's notation"));
        }
        else
        {
            list.Add(MenuSpec.Separator());
            list.Add(Item(TimelineCommand.ClipAddAudioFile, "Add audio file…"));
        }
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
