using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Services;

// Owns: the timeline section menu's edits on one section, without UI types: its bar bounds, copying it as a bar clip, duplicating it after itself, pasting bars after it
//   (a copied section comes back with its title and colour), deleting it with its bars from every track, adding a section at a bar, and renaming or
//   recolouring a section (an explicit colour spreads to its family when similar sections share a colour).
// Does not own: undo (callers wrap each call in DocumentEdits.Run), the clipboard, questions to the user, the bar moves themselves (SectionReorderService).
// Tests: TabForge.Avalonia.Tests.TimelineSectionMenuTests.
public static class SectionMenuEdits
{
    /// <summary>The start (inclusive) and end (exclusive) bars of <paramref name="marker"/>; false when it is not in the song or has no bars.</summary>
    public static bool TryBounds(SongProject project, MarkerModel marker, out int start, out int end)
    {
        start = end = 0;
        var sorted = SectionLayout.Sorted(project);
        var index = sorted.FindIndex(existing => ReferenceEquals(existing, marker));
        var barCount = project.Tracks.Select(track => track.Measures.Count).DefaultIfEmpty(0).Max();
        if (index < 0 || barCount == 0) return false;
        start = Math.Clamp(marker.MeasureIndex, 0, barCount);
        end = SectionLayout.End(sorted, index, barCount);
        return end > start;
    }

    /// <summary>The section's bars on every track as a clip, or null when the section has no bars.</summary>
    public static ScoreClip? Copy(SongProject project, MarkerModel marker) =>
        TryBounds(project, marker, out var start, out var end) ? TimelineClips.CopySection(project, start, end) : null;

    /// <summary>Inserts a copy of the section straight after it; returns the old-to-new bar map, or null when nothing changed.</summary>
    public static int[]? Duplicate(SongProject project, MarkerModel marker)
    {
        if (Copy(project, marker) is not { } clip || !TryBounds(project, marker, out _, out var end)) return null;
        var bars = TimelineClips.BarsPerTrack(clip, project, 0, out _);
        var copy = new MarkerModel { Title = marker.Title, ColorHex = marker.ColorHex, ColorIsExplicit = marker.ColorIsExplicit };
        return SectionReorderService.Insert(project, end, bars, copy);
    }

    /// <summary>Deletes the section and its bars from every track; false when it is the only bars left or not in the song.</summary>
    public static bool Delete(SongProject project, MarkerModel marker) => SectionReorderService.Delete(project, marker) is not null;

    /// <summary>
    /// Inserts a bar clip's bars on every track at bar <paramref name="at"/>; with <paramref name="section"/> (the copied section) the bars come back as
    /// a section with its title and colour, else as plain bars. Returns the old-to-new bar map, or null when nothing changed.
    /// </summary>
    public static int[]? PasteAt(SongProject project, int at, ScoreClip? clip, int anchorTrack, MarkerModel? section)
    {
        if (!TimelineClips.CanPasteOnTimeline(clip)) return null;
        var bars = TimelineClips.BarsPerTrack(clip!, project, anchorTrack, out _);
        at = Math.Clamp(at, 0, BarRangeEditor.MaxMeasures(project));
        if (section is null) return BarRangeEditor.Insert(project, at, bars);
        var copy = new MarkerModel { Title = section.Title, ColorHex = section.ColorHex, ColorIsExplicit = section.ColorIsExplicit };
        return SectionReorderService.Insert(project, at, bars, copy);
    }

    /// <summary>The section starting at <paramref name="bar"/>, or null (one section per bar).</summary>
    public static MarkerModel? StartingAt(SongProject project, int bar) => project.Markers.FirstOrDefault(marker => marker.MeasureIndex == bar);

    /// <summary>Adds a section at <paramref name="bar"/> (clamped into the song); false when a section already starts there.</summary>
    public static bool AddAt(SongProject project, int bar, string title, string colourHex)
    {
        bar = Math.Clamp(bar, 0, Math.Max(0, BarRangeEditor.MaxMeasures(project) - 1));
        if (StartingAt(project, bar) is not null) return false;
        // The colour the user picked is stored as explicit, so it stays with the section when sections move.
        project.Markers.Add(new MarkerModel { MeasureIndex = bar, Title = title, ColorHex = colourHex, ColorIsExplicit = true });
        return true;
    }

    /// <summary>
    /// Renames and recolours <paramref name="selected"/>: a rename alone keeps family colouring; a new colour is explicit and, with
    /// <paramref name="matchSimilar"/>, goes to every section of the same family. False when neither the title nor the colour changed.
    /// </summary>
    public static bool Rename(IReadOnlyList<MarkerModel> markers, MarkerModel selected, string title, string colourHex, bool matchSimilar)
    {
        var colourChanged = !string.Equals(colourHex, selected.ColorHex, StringComparison.OrdinalIgnoreCase);
        if (!colourChanged && string.Equals(title, selected.Title, StringComparison.Ordinal)) return false;
        selected.Title = title;
        if (!colourChanged) return true;
        var family = matchSimilar ? SectionColourPresenter.Family(title) : "";
        foreach (var marker in markers)
        {
            if (!ReferenceEquals(marker, selected) &&
                (family.Length == 0 || !string.Equals(SectionColourPresenter.Family(marker.Title), family, StringComparison.OrdinalIgnoreCase))) continue;
            marker.ColorHex = colourHex;
            marker.ColorIsExplicit = true;
        }
        return true;
    }
}
