namespace TabForge.Views;

/// <summary>One entry of a right-click menu as plain data (a separator has <see cref="IsSeparator"/>). Built by
/// <see cref="TimelineMenus"/>, turned into WPF items by the window, and enumerated by the self-test.</summary>
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

    public static MenuSpec Separator() => new() { IsSeparator = true };
}

/// <summary>Every command the arrangement-timeline menus can run; the window maps each to the existing handler.</summary>
internal enum TimelineCommand
{
    None,
    // bar menu
    CopyBar, PasteBar, DeleteBar, InsertBarBefore, InsertBarAfter, CopyBarAllTracks, PasteBarAllTracks, DeleteBarAllTracks,
    CopySection, PasteSectionHere, ToggleSectionLockAtBar,
    // timeline display toggles
    ShowIndividualNotes, ShowContinuousLine, HideEmptyGrid, SubtleBarGlow,
    // selection menu
    CopySelection, CutSelection, PasteSelection, DeleteSelection, LoopSelection, MoveSelection, SkipSelection,
    PlaySkippedAgain, ClearSelection,
    // section menu
    AddSectionHere, CopySectionMenu, CutSection, PasteSectionAfter, DuplicateSection, DeleteSection, LoopSection,
    RenameSection, GoToSection, ToggleSectionLock, ShowSectionBrackets, SameColourSections,
    // clip menu
    ClipCopy, ClipCut, ClipPaste, ClipDuplicate, ClipDelete, ClipMute, ClipProperties, ClipWriteNotation, ClipAddAudioFile,
}

internal sealed record TimelineDisplayState(bool IndividualNotes, bool ContinuousLine, bool HideEmptyGrid, bool BarGlow);

internal sealed record BarMenuState(bool HasBar, bool HasTrack, int TrackCount, bool CanDeleteThisTrack, bool CanDeleteAllTracks,
    bool InSection, bool SectionLocked, bool CanPaste, bool CanPasteAllTracks, TimelineDisplayState Display);

internal sealed record SelectionMenuState(string Label, bool CanPaste, bool Looping, bool Skipped, bool AnySkipped, TimelineDisplayState Display);

internal sealed record SectionMenuState(int? AddAtBar, bool CanPaste, bool Looped, bool Locked, bool ShowBrackets, bool SameColours);

internal sealed record ClipMenuState(bool HasClip, bool IsMidi, bool CanPaste, bool Muted);

/// <summary>
/// The arrangement-timeline right-click menus (owner request 2026-09-30, docs/CONTEXT_MENU_AUDIT.md): Copy / Cut / Paste /
/// Delete stay on top, related items live in named submenus, items that do not apply are hidden, shortcuts come from the
/// user's bindings (<c>key</c> maps a hotkey id to its display text).
/// </summary>
internal static class TimelineMenus
{
    private static MenuSpec Item(TimelineCommand command, string header, string shortcut = "", bool enabled = true, string? toolTip = null) =>
        new() { Command = command, Header = header, Shortcut = shortcut, Enabled = enabled, ToolTip = toolTip };

    private static MenuSpec Check(TimelineCommand command, string header, bool isChecked, string shortcut = "", string? toolTip = null) =>
        new() { Command = command, Header = header, Shortcut = shortcut, Checkable = true, Checked = isChecked, ToolTip = toolTip };

    private static MenuSpec Sub(string header, params MenuSpec[] children) => new() { Header = header, Children = children.ToList() };

    /// <summary>The one place for the timeline's view settings (used by the bar and the selection menu).</summary>
    public static MenuSpec Display(TimelineDisplayState d) => Sub("Timeline display",
        Check(TimelineCommand.ShowIndividualNotes, "Show individual notes", d.IndividualNotes),
        Check(TimelineCommand.ShowContinuousLine, "Show continuous line", d.ContinuousLine),
        Check(TimelineCommand.HideEmptyGrid, "Hide grid in empty bars", d.HideEmptyGrid),
        Check(TimelineCommand.SubtleBarGlow, "Subtle bar glow", d.BarGlow));

    /// <summary>Right-click on a bar (no selection, or outside it).</summary>
    public static List<MenuSpec> Bar(BarMenuState s, Func<string, string> key)
    {
        var list = new List<MenuSpec>
        {
            Item(TimelineCommand.CopyBar, "Copy bar", key("Edit.Copy"), s.HasBar && s.HasTrack, "Copies this bar of the selected track"),
            Item(TimelineCommand.PasteBar, "Paste bar", key("Edit.Paste"), s.HasBar && s.HasTrack && s.CanPaste),
            Item(TimelineCommand.DeleteBar, "Delete bar", key("Bar.Delete"), s.HasBar && s.HasTrack && s.CanDeleteThisTrack, "Deletes this bar of the selected track"),
        };
        if (s.HasBar)
        {
            list.Add(MenuSpec.Separator());
            list.Add(Sub("Insert bar",
                Item(TimelineCommand.InsertBarBefore, "Before this bar", key("Bar.Insert")),
                Item(TimelineCommand.InsertBarAfter, "After this bar")));
            if (s.TrackCount > 1)
                list.Add(Sub("All tracks",
                    Item(TimelineCommand.CopyBarAllTracks, "Copy bar (all tracks)"),
                    Item(TimelineCommand.PasteBarAllTracks, "Paste bar into all tracks", enabled: s.CanPasteAllTracks),
                    Item(TimelineCommand.DeleteBarAllTracks, "Delete bar (all tracks)", enabled: s.CanDeleteAllTracks)));
            if (s.InSection)
                list.Add(Sub("Section",
                    Item(TimelineCommand.CopySection, "Copy section"),
                    Item(TimelineCommand.PasteSectionHere, "Paste section here", enabled: s.CanPaste),
                    Check(TimelineCommand.ToggleSectionLockAtBar, "Lock section position", s.SectionLocked)));
        }
        list.Add(MenuSpec.Separator());
        list.Add(Display(s.Display));
        return list;
    }

    /// <summary>Right-click inside the selected bars: the selection menu only (the single-bar items are for right-clicks outside it).</summary>
    public static List<MenuSpec> Selection(SelectionMenuState s, Func<string, string> key)
    {
        var arrange = new List<MenuSpec>
        {
            Item(TimelineCommand.MoveSelection, "Move selection… (click the new position)"),
            Check(TimelineCommand.SkipSelection, "Skip during playback", s.Skipped),
        };
        if (s.AnySkipped) arrange.Add(Item(TimelineCommand.PlaySkippedAgain, "Play all skipped areas again"));
        return new List<MenuSpec>
        {
            new() { Header = s.Label, IsLabel = true, Enabled = false },
            Item(TimelineCommand.CopySelection, "Copy", key("Edit.Copy"), toolTip: "Copies the selected bars of every track"),
            Item(TimelineCommand.CutSelection, "Cut", key("Edit.Cut"), toolTip: "Cut removes the bars and closes the gap"),
            Item(TimelineCommand.PasteSelection, "Paste", key("Edit.Paste"), s.CanPaste, "Pastes the copied bars in front of the selection"),
            Item(TimelineCommand.DeleteSelection, "Delete", toolTip: "Deletes the selected bars and closes the gap"),
            MenuSpec.Separator(),
            Check(TimelineCommand.LoopSelection, "Loop selection", s.Looping, key("Transport.Loop")),
            new MenuSpec { Header = "Arrange", Children = arrange },
            MenuSpec.Separator(),
            Item(TimelineCommand.ClearSelection, "Clear selection", "Esc"),
            Display(s.Display),
        };
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
        list.Add(Item(TimelineCommand.DeleteSection, "Delete section"));
        list.Add(MenuSpec.Separator());
        list.Add(Check(TimelineCommand.LoopSection, "Loop section", s.Looped, key("Transport.Loop")));
        list.Add(Item(TimelineCommand.RenameSection, "Rename / recolour section…"));
        list.Add(Sub("More",
            Item(TimelineCommand.GoToSection, "Go to section"),
            Check(TimelineCommand.ToggleSectionLock, s.Locked ? "Unlock section position" : "Lock section position", s.Locked),
            Check(TimelineCommand.ShowSectionBrackets, "Show section brackets [ ]", s.ShowBrackets),
            Check(TimelineCommand.SameColourSections, "Same colour for similar sections", s.SameColours,
                toolTip: "Verse 1, Verse 2… share the first one's colour. Display only; each section keeps its own colour.")));
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
            list.Add(Item(TimelineCommand.ClipDelete, "Delete", key("Clip.Delete")));
            list.Add(MenuSpec.Separator());
            list.Add(Check(TimelineCommand.ClipMute, "Mute", s.Muted, key("Clip.Mute")));
            list.Add(Item(TimelineCommand.ClipProperties, "Properties…", key("Clip.Properties")));
            // "Advanced notation conversion…" is hidden until it exists (docs/MIDI_TO_TAB_PLAN.md).
            if (s.IsMidi) list.Add(Sub("MIDI clip", Item(TimelineCommand.ClipWriteNotation, "Write into the track's notation")));
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
}
