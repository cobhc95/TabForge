using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the timeline section menu's Add section here, Copy, Cut, Paste, Duplicate, Delete, Rename / recolour and Go to section rows for the Avalonia
//   window, on the right-clicked section (ContextMenuHost.ClickedSection) or bar (ContextMenuHost.ClickedBar). Copy records no undo step and remembers
//   the copied section, so Paste brings it back with its title and colour; every edit is one undo step through DocumentEdits.Run. Delete asks first
//   (General.ConfirmDeleteSection) and again when audio clips lie under the bars.
// Does not own: the edits (Core SectionMenuEdits), the menu rows (TimelineMenus), the row-to-id mapping (ContextMenuRows), the clipboard
//   (ClipboardService), the windows (SectionMenuPrompts from the caller), Loop section (the Avalonia transport has no bar loop range).
// Tests: TabForge.Avalonia.Tests.TimelineSectionMenuTests.
public static class MainWindowTimelineSections
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { ContextMenuRows.SectionCopyId, ContextMenuRows.SectionCutId, ContextMenuRows.SectionPasteAfterId,
        ContextMenuRows.SectionPasteAtBarId, ContextMenuRows.SectionCloseGapId, ContextMenuRows.SectionDuplicateId, ContextMenuRows.SectionDeleteId, ContextMenuRows.SectionRemoveLabelId, ContextMenuRows.SectionGoToId, ContextMenuRows.SectionAddHereId,
        ContextMenuRows.SectionRenameId, ContextMenuRows.MovePlayheadHereId };

    /// <summary>The questions the section rows ask: a yes/no question (text, caption), the section editor (action, title, colour, bar) and the settings.</summary>
    public sealed record SectionMenuPrompts(Func<string, string, Task<bool>> Confirm, Func<string, string, string, int, Task<MarkerAnswer?>> EditSection,
        Func<AppSettings> Settings, Func<int, int, GapContents, Task<GapCloseMode?>>? AskGapClose = null);

    /// <summary>The colour a new section starts with (the SectionDefault palette token).</summary>
    public static readonly string NewSectionColour = ColourHex.FormatAuto(ToRgba(Palette.Get("SectionDefault")));

    private static Rgba ToRgba(UiColor colour) => new(colour.R, colour.G, colour.B, colour.A);

    /// <summary>The title a new section starts with.</summary>
    public const string NewSectionTitle = "Section";

    /// <summary>The caption of the delete questions.</summary>
    public const string DeleteCaption = "Delete Section";

    /// <summary>The notice when a remove edit would leave the song without bars.</summary>
    public const string KeepsOneBarNotice = "A song keeps at least one bar: this section cannot be removed.";

    /// <summary>The notice when Paste section finds nothing on the clipboard.</summary>
    public const string NothingToPasteNotice = "Nothing to paste: copy a section first.";

    /// <summary>The section last copied from this window and the clip it was copied as (Paste brings back its title and colour).</summary>
    public sealed class CopiedSection
    {
        public ScoreClip? Clip { get; set; }
        public MarkerModel? Marker { get; set; }
        /// <summary>The copied section when <paramref name="clip"/> is the clip it was copied as, else null.</summary>
        public MarkerModel? For(ScoreClip? clip) => clip is not null && ReferenceEquals(clip, Clip) ? Marker : null;
    }

    /// <summary>Adds the section rows' commands over the shown song and the section menu's clicked section.</summary>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, ContextMenuHost menus, Action<string> notice, Action<int>? moveToBar,
        SectionMenuPrompts prompts)
    {
        var copied = new CopiedSection();
        commands.Add(ContextMenuRows.SectionCopyId, () => Copy(songs.Active, menus, notice, copied));
        commands.Add(ContextMenuRows.SectionCutId, () => _ = CutAsync(songs.Active, menus, notice, prompts, copied));
        commands.Add(ContextMenuRows.SectionPasteAfterId, () => PasteAfter(songs.Active, menus, notice, copied));
        commands.Add(ContextMenuRows.SectionPasteAtBarId, () => PasteAtBar(songs.Active, menus, notice, copied));
        commands.Add(ContextMenuRows.SectionCloseGapId, () => { if (SectionGapEdits.Find(songs.Active.Project, menus.ClickedBar) is var (first, last)) _ = CloseGapAsync(songs.Active, first, last, prompts.AskGapClose, notice); });
        commands.Add(ContextMenuRows.SectionDuplicateId, () => Edit(songs.Active, menus, notice, (p, m) => SectionMenuEdits.Duplicate(p, m) is not null, "Duplicated section", label: "Duplicate section"));
        commands.Add(ContextMenuRows.SectionRemoveLabelId, () => Edit(songs.Active, menus, notice, SectionMenuEdits.RemoveLabel, "Removed label of section", label: "Remove section label"));
        commands.Add(ContextMenuRows.SectionDeleteId,() => _ = DeleteAsync(songs.Active, menus, notice, prompts));
        commands.Add(ContextMenuRows.MovePlayheadHereId, () => { if (menus.ClickedBar < 0) return; menus.MovePlayhead?.Invoke(menus.ClickedBar); moveToBar?.Invoke(menus.ClickedBar); });
        commands.Add(ContextMenuRows.SectionGoToId, () => { if (Clicked(songs.Active, menus) is { } marker) moveToBar?.Invoke(marker.MeasureIndex); });
        commands.Add(ContextMenuRows.SectionAddHereId, () => _ = AddHereAsync(songs.Active, menus.ClickedBar, notice, prompts));
        commands.Add(ContextMenuRows.SectionRenameId, () => { if (Clicked(songs.Active, menus) is { } marker) _ = RenameAsync(songs.Active, marker, prompts); });
    }

    /// <summary>Pastes the clipboard's bars after the clicked section as one undo step; a copied section comes back with its title and colour.</summary>
    public static void PasteAfter(IEditableDocument document, ContextMenuHost menus, Action<string> notice, CopiedSection copied)
    {
        if (Clicked(document, menus) is not { } marker) return;
        var clip = menus.Clipboard?.TryGetClip(out _);
        var section = copied.For(clip);
        var changed = DocumentEdits.Run(document, project => SectionMenuEdits.TryBounds(project, marker, out _, out var end)
            && SectionMenuEdits.PasteAt(project, end, clip, Math.Max(0, menus.ClickedTrack), section) is not null, label: "Paste section").Changed;
        notice(!changed ? NothingToPasteNotice : section is not null ? $"Pasted section '{section.Title}'" : $"Pasted bars after section '{marker.Title}'");
    }

    /// <summary>Closes the empty-or-not run of bars first..last as one undo step: no question when the bars hold nothing, else the three-way prompt (null answer = Cancel, nothing changes). True when the song changed.</summary>
    public static async Task<bool> CloseGapAsync(IEditableDocument document, int first, int last, Func<int, int, GapContents, Task<GapCloseMode?>>? ask, Action<string> notice)
    {
        var contents = SectionGapEdits.Contents(document.Project, first, last);
        var mode = GapCloseMode.RemoveAll;
        if (!contents.IsEmpty)
        {
            if (ask is null || await ask(first, last, contents) is not { } answer) return false;
            mode = answer;
        }
        string? refusal = null;
        var changed = DocumentEdits.Run(document, project =>
        {
            var result = SectionGapEdits.Close(project, first, last, mode);
            refusal = result.Refusal;
            return result.Changed;
        }, label: "Close gap").Changed;
        if (refusal is not null) notice(refusal);
        else if (changed) notice(mode == GapCloseMode.MakeSection ? $"Made bars {first + 1}-{last + 1} a section" : $"Closed the gap (bars {first + 1}-{last + 1})");
        return changed;
    }

    /// <summary>Pastes the clipboard's bars at the clicked bar of an empty part of the section strip as one undo step.</summary>
    public static void PasteAtBar(IEditableDocument document, ContextMenuHost menus, Action<string> notice, CopiedSection copied)
    {
        var bar = menus.ClickedBar;
        if (bar < 0) return;
        var clip = menus.Clipboard?.TryGetClip(out _);
        var section = copied.For(clip);
        var changed = DocumentEdits.Run(document, project => SectionMenuEdits.PasteAt(project, bar, clip, Math.Max(0, menus.ClickedTrack), section) is not null, label: "Paste section").Changed;
        notice(!changed ? NothingToPasteNotice : section is not null ? $"Pasted section '{section.Title}'" : $"Pasted bars at bar {bar + 1}");
    }

    /// <summary>Asks for the new section's title and colour, then adds it at <paramref name="bar"/>; a section already there is edited instead.</summary>
    public static async Task AddHereAsync(IEditableDocument document, int bar, Action<string> notice, SectionMenuPrompts prompts)
    {
        if (bar < 0) return;
        if (SectionMenuEdits.StartingAt(document.Project, bar) is { } existing) { await RenameAsync(document, existing, prompts); return; }
        if (await prompts.EditSection("Add", NewSectionTitle, NewSectionColour, bar) is not { } answer) return;
        if (DocumentEdits.Run(document, project => SectionMenuEdits.AddAt(project, bar, answer.Title, answer.ColourHex), label: "Add section").Changed)
            notice($"Added section \"{answer.Title}\" at bar {bar + 1}");
    }

    /// <summary>Asks for the section's new title and colour and applies them as one undo step (nothing when unchanged).</summary>
    public static async Task RenameAsync(IEditableDocument document, MarkerModel marker, SectionMenuPrompts prompts)
    {
        if (await prompts.EditSection("Save", marker.Title, marker.ColorHex, marker.MeasureIndex) is not { } answer) return;
        var matchSimilar = prompts.Settings().Timeline.MatchSimilarSectionColours;
        DocumentEdits.Run(document, project => SectionMenuEdits.Rename(project.Markers, marker, answer.Title, answer.ColourHex, matchSimilar), label: "Rename section");
    }

    /// <summary>Copies the clicked section, then removes it after the same confirm and clips-under-bars question as Delete section.</summary>
    public static async Task CutAsync(IEditableDocument document, ContextMenuHost menus, Action<string> notice, SectionMenuPrompts prompts, CopiedSection copied)
    {
        if (!await ConfirmRemoveAsync(document, menus, notice, prompts)) return;
        if (Copy(document, menus, notice, copied)) Edit(document, menus, notice, SectionMenuEdits.Delete, "Cut section", label: "Cut section");
    }

    /// <summary>Deletes the clicked section and its bars after the confirm (when the setting asks) and the clips-under-bars question.</summary>
    public static async Task DeleteAsync(IEditableDocument document, ContextMenuHost menus, Action<string> notice, SectionMenuPrompts prompts)
    {
        if (await ConfirmRemoveAsync(document, menus, notice, prompts)) Edit(document, menus, notice, SectionMenuEdits.Delete, "Deleted section", label: "Delete section");
    }

    // True when the clicked section may be removed: it leaves a bar, and the confirm and the clips question were not declined.
    private static async Task<bool> ConfirmRemoveAsync(IEditableDocument document, ContextMenuHost menus, Action<string> notice, SectionMenuPrompts prompts)
    {
        if (Clicked(document, menus) is not { } marker) return false;
        if (!SectionMenuEdits.TryBounds(document.Project, marker, out var from, out var to) || BarRangeEditor.MaxMeasures(document.Project) <= to - from)
        {
            notice(KeepsOneBarNotice);
            return false;
        }
        if (prompts.Settings().General.ConfirmDeleteSection && !await prompts.Confirm(
                $"Delete the '{marker.Title}' section and its bars and notes from every track? Undo can restore them.", DeleteCaption)) return false;
        return !(ClipDeleteImpact.Find(document.Project, new[] { (from, to - 1) }) is { } impact && !await prompts.Confirm(ClipDeleteImpact.Describe(impact), DeleteCaption));
    }

    private static MarkerModel? Clicked(IEditableDocument document, ContextMenuHost menus) =>
        menus.ClickedSection >= 0 && menus.ClickedSection < document.Project.Markers.Count ? document.Project.Markers[menus.ClickedSection] : null;

    private static bool Copy(IEditableDocument document, ContextMenuHost menus, Action<string> notice, CopiedSection copied)
    {
        if (Clicked(document, menus) is not { } marker || SectionMenuEdits.Copy(document.Project, marker) is not { } clip) return false;
        menus.Clipboard?.Copy(clip);
        copied.Clip = menus.Clipboard?.TryGetClip(out _) ?? clip;
        copied.Marker = new MarkerModel { Title = marker.Title, ColorHex = marker.ColorHex, ColorIsExplicit = marker.ColorIsExplicit };
        notice($"Copied section '{marker.Title}'");
        return true;
    }

    private static void Edit(IEditableDocument document, ContextMenuHost menus, Action<string> notice, Func<SongProject, MarkerModel, bool> edit, string verb,
        string failedNotice = KeepsOneBarNotice, string label = "Edit section")
    {
        if (Clicked(document, menus) is not { } marker) return;
        if (DocumentEdits.Run(document, project => edit(project, marker), label: label).Changed) notice($"{verb} '{marker.Title}'");
        else notice(failedNotice);
    }
}
