using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Timeline bar/area/section copies on the shared score clipboard (docs/COPY_PASTE_DESIGN.md chunk C7).</summary>
public static partial class SelfTest
{
    /// <summary>The arrangement-timeline right-click menus: short top level, grouped submenus, every command still reachable.</summary>
    private static void TestTimelineContextMenus()
    {
        Func<string, string> key = id => id == "Edit.Copy" ? "Ctrl+C" : "";
        var display = new Views.TimelineDisplayState(true, false, false, true);
        string[] Top(IEnumerable<Views.MenuSpec> m) => m.Where(x => !x.IsSeparator).Select(x => x.Header).ToArray();
        bool Has(IEnumerable<Views.MenuSpec> m, Views.TimelineCommand c) => Views.TimelineMenus.Leaves(m).Any(x => x.Command == c);

        // Bar menu: outside a section, several tracks.
        var bar = Views.TimelineMenus.Bar(new Views.BarMenuState(true, true, 3, true, true, false, false, true, true, display), key);
        Check("bar menu: Copy, Paste, Delete, then Insert / All tracks / Timeline display (6 top-level items)",
            string.Join("|", Top(bar)) == "Copy bar|Paste bar|Delete bar|Insert bar|All tracks|Timeline display", string.Join("|", Top(bar)));
        Check("bar menu shows the user's Copy shortcut", bar[0].Shortcut == "Ctrl+C");
        var section = Views.TimelineMenus.Bar(new Views.BarMenuState(true, true, 1, true, false, true, true, false, false, display), key);
        Check("bar menu inside a section adds 'Section >' and, with one track, hides 'All tracks >'",
            Top(section).Contains("Section") && !Top(section).Contains("All tracks") && Top(section).Length == 6, string.Join("|", Top(section)));
        var allBar = new[] { bar, section }.SelectMany(Views.TimelineMenus.Leaves).Select(x => x.Command).ToHashSet();
        Check("bar menus keep every old bar command reachable", new[]
        {
            Views.TimelineCommand.CopyBar, Views.TimelineCommand.CopyBarAllTracks, Views.TimelineCommand.CopySection, Views.TimelineCommand.ToggleSectionLockAtBar,
            Views.TimelineCommand.PasteBar, Views.TimelineCommand.PasteBarAllTracks, Views.TimelineCommand.PasteSectionHere, Views.TimelineCommand.InsertBarBefore,
            Views.TimelineCommand.InsertBarAfter, Views.TimelineCommand.DeleteBar, Views.TimelineCommand.DeleteBarAllTracks, Views.TimelineCommand.ShowIndividualNotes,
            Views.TimelineCommand.ShowContinuousLine, Views.TimelineCommand.HideEmptyGrid, Views.TimelineCommand.SubtleBarGlow
        }.All(allBar.Contains));
        var noBar = Views.TimelineMenus.Bar(new Views.BarMenuState(false, false, 0, false, false, false, false, false, false, display), key);
        Check("bar menu on an empty song hides the bar-only groups", Top(noBar).Length == 4 && !Top(noBar).Contains("Insert bar"), string.Join("|", Top(noBar)));

        // Selection menu.
        var sel = Views.TimelineMenus.Selection(new Views.SelectionMenuState("Bars 3-6 selected", true, false, false, true, display), key);
        Check("selection menu: grey label, Copy / Cut / Paste / Delete on top, 8 entries",
            sel[0].IsLabel && sel[0].Header == "Bars 3-6 selected" && Top(sel.Skip(1)).Length == 8 && string.Join("|", Top(sel.Skip(1)).Take(4)) == "Copy|Cut|Paste|Delete",
            string.Join("|", Top(sel)));
        Check("selection menu keeps loop, move, skip, play-skipped-again, clear and the display toggles", new[]
        {
            Views.TimelineCommand.LoopSelection, Views.TimelineCommand.MoveSelection, Views.TimelineCommand.SkipSelection, Views.TimelineCommand.PlaySkippedAgain,
            Views.TimelineCommand.ClearSelection, Views.TimelineCommand.ShowIndividualNotes, Views.TimelineCommand.SubtleBarGlow
        }.All(c => Has(sel, c)));
        var selNone = Views.TimelineMenus.Selection(new Views.SelectionMenuState("Bar 2 selected", false, false, false, false, display), key);
        Check("'Play all skipped areas again' is hidden when nothing is skipped", !Has(selNone, Views.TimelineCommand.PlaySkippedAgain));
        Check("selection menu does not repeat the single-bar items", !Has(sel, Views.TimelineCommand.CopyBar) && !Has(sel, Views.TimelineCommand.DeleteBar));

        // Section menu.
        var sec = Views.TimelineMenus.Section(new Views.SectionMenuState(null, true, false, false, true, true), key);
        Check("section menu: 8 top-level items ending with 'More >'", Top(sec).Length == 8 && Top(sec)[^1] == "More", string.Join("|", Top(sec)));
        Check("section menu keeps every old command", new[]
        {
            Views.TimelineCommand.CopySectionMenu, Views.TimelineCommand.CutSection, Views.TimelineCommand.PasteSectionAfter, Views.TimelineCommand.DuplicateSection,
            Views.TimelineCommand.DeleteSection, Views.TimelineCommand.LoopSection, Views.TimelineCommand.RenameSection, Views.TimelineCommand.GoToSection,
            Views.TimelineCommand.ToggleSectionLock, Views.TimelineCommand.ShowSectionBrackets, Views.TimelineCommand.SameColourSections
        }.All(c => Has(sec, c)));
        Check("section menu uses sentence case", Views.TimelineMenus.Leaves(sec).All(x => !x.Header.Contains("Section")));
        Check("section menu offers 'Add section' only away from the section's first bar",
            !Has(sec, Views.TimelineCommand.AddSectionHere) && Has(Views.TimelineMenus.Section(new Views.SectionMenuState(4, true, false, false, true, true), key), Views.TimelineCommand.AddSectionHere));

        // Clip menus.
        var audio = Views.TimelineMenus.Clip(new Views.ClipMenuState(true, false, true, false), key);
        Check("audio clip menu: Copy, Cut, Paste, Duplicate, Delete, Mute, Properties (7 entries, no submenu)",
            string.Join("|", Top(audio)) == "Copy|Cut|Paste|Duplicate|Delete|Mute|Properties…", string.Join("|", Top(audio)));
        var midi = Views.TimelineMenus.Clip(new Views.ClipMenuState(true, true, false, true), key);
        Check("MIDI clip menu adds 'MIDI clip >' with the notation writer and hides the not-built placeholder",
            Top(midi)[^1] == "MIDI clip" && Has(midi, Views.TimelineCommand.ClipWriteNotation) && Views.TimelineMenus.Leaves(midi).All(x => !x.Header.StartsWith("Advanced")));
        var lane = Views.TimelineMenus.Clip(new Views.ClipMenuState(false, false, true, false), key);
        Check("empty-lane menu is still Paste + Add audio file", string.Join("|", Top(lane)) == "Paste|Add audio file…", string.Join("|", Top(lane)));
    }

    private static void TestTimelineClipsShareClipboard()
    {
        var song = ClipSong();
        var system = new FakeScoreClipboard();
        var service = new ClipboardService(system);

        // Timeline copy -> a Bars clip the score editor reads (first track, both voices, bar settings).
        var copied = TimelineClips.CopyBar(song, 0, 0, allTracks: false);
        service.Copy(copied);
        var seen = service.TryGetClip(out _);
        Check("timeline copy is a Bars clip with the source track, both voices and no lyrics",
            seen is { Kind: ScoreClipKind.Bars, BarCount: 1 } && seen.Tracks[0].SourceTrackIndex == 0 && seen.Tracks[0].Bars[0].Voice2Cells.Count > 0 &&
            seen.Tracks[0].Bars[0].Cells[8].Lyrics == "");
        var restored = ProjectService.Restore(TimelineClips.ToEditorText(seen!));
        Check("timeline clip reaches the score editor's paste text (same bar content)",
            restored.Tracks[0].Measures.Count == 1 && restored.Tracks[0].Measures[0].Cells[4].Notes.Single().Fret == 5);

        // Text from another window (system clipboard changed) reads back the same way.
        system.Text = copied.ToJson();
        system.SequenceNumber++;
        Check("timeline clip survives the system clipboard text path", service.TryGetClip(out _) is { Kind: ScoreClipKind.Bars, BarCount: 1 });

        // Editor copy (whole bars 1-2) -> timeline paste target; a bars clip pastes, a beats clip is refused.
        var fromEditor = ClipboardService.CaptureSelection(song, 0, 0, 0, 0, 1, -1, TimelineClips.SongId(song));
        service.Copy(fromEditor);
        var target = ClipSong();
        Check("editor bars clip can be pasted on the timeline", TimelineClips.CanPasteOnTimeline(service.TryGetClip(out _)));
        var overwrite = TimelineClips.PasteBars(target, service.TryGetClip(out _)!, 2, 0, TimelinePasteKind.OverwriteThisTrack);
        Check("timeline overwrite paste replaces bars on the selected track only",
            overwrite.Changed && overwrite.BarsPasted == 2 && target.Tracks[0].Measures[2].Cells[4].Notes.Single().Fret == 5 &&
            !target.Tracks[0].Measures[3].RepeatStart /* repeats are form: never pasted (C4) */ && target.Tracks[1].Measures[2].Cells.All(c => c.Notes.Count == 0));
        Check("timeline paste is independent of the clip (deep copies)",
            !ReferenceEquals(target.Tracks[0].Measures[2], fromEditor.Tracks[0].Bars[0]));

        var beats = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 0, 3);
        var untouched = ClipSong();
        var before = ProjectService.Snapshot(untouched);
        var refused = TimelineClips.PasteBars(untouched, beats, 0, 0, TimelinePasteKind.OverwriteThisTrack);
        Check("timeline refuses a beats clip without changing anything",
            !refused.Changed && !TimelineClips.CanPasteOnTimeline(beats) && ProjectService.Snapshot(untouched) == before);

        // Overwrite past the end appends bars on every track; insert is structural on all tracks with a bar map.
        var grown = ClipSong();
        var tail = TimelineClips.PasteBars(grown, fromEditor, 3, 0, TimelinePasteKind.OverwriteThisTrack);
        Check("overwrite past the last bar appends bars on all tracks",
            tail.Changed && grown.Tracks.All(t => t.Measures.Count == 5) && !grown.Tracks[0].Measures[4].RepeatStart);
        var inserted = ClipSong();
        var insertResult = TimelineClips.PasteBars(inserted, fromEditor, 1, 0, TimelinePasteKind.InsertBars);
        Check("insert adds the bars on all tracks and returns the old-to-new bar map",
            insertResult.Changed && inserted.Tracks.All(t => t.Measures.Count == 6) && insertResult.OldToNewBar!.SequenceEqual(new[] { 0, 3, 4, 5 }) &&
            inserted.Tracks[1].Measures[1].Cells.All(c => c.Notes.Count == 0) && !inserted.Tracks[0].Measures[2].RepeatStart);

        // All-tracks clip: same song maps i -> i; instruments are mapped (guitar bars onto the 4-string bass re-fret by pitch).
        var both = TimelineClips.CopyArea(song, 0, 0);
        Check("area copy holds every track and maps track i to i within its song",
            both.Tracks.Count == 2 && both.BarCount == 1 && TimelineClips.MapTracks(both, song, 0).SequenceEqual(new[] { 0, 1 }));
        var toBass = ClipSong();
        var mapped = TimelineClips.PasteBars(toBass, TimelineClips.CopyBar(song, 0, 0, allTracks: false), 0, 1, TimelinePasteKind.OverwriteThisTrack);
        var bassNotes = toBass.Tracks[1].Measures[0].Cells.SelectMany(c => c.Notes).ToList();
        Check("pasting guitar bars onto a bass track keeps pitch and stays within its strings",
            mapped.Changed && bassNotes.Count > 0 && bassNotes.All(n => n.StringIndex < 4 && n.MidiValue == toBass.Tracks[1].PitchOf(n.StringIndex, n.Fret)));
    }

    private static void TestTimelineSectionCopiesAsBars()
    {
        var song = ClipSong();
        song.Markers.Add(new MarkerModel { MeasureIndex = 1, Title = "Verse" });
        var arrangement = new Controllers.ArrangementController();
        var system = new FakeScoreClipboard();
        var service = new ClipboardService(system);
        var clip = arrangement.CopySection(song, song.Markers[0], service, out var written);
        Check("section copy is a Bars clip of the section's bars on all tracks",
            written && clip is { Kind: ScoreClipKind.Bars, BarCount: 3 } && clip.Tracks.Count == 2 &&
            service.TryGetClip(out _) is { } current && ReferenceEquals(current, clip));
        Check("the copied section keeps its marker only while its clip is the clipboard",
            arrangement.SectionMarkerFor(clip)?.Title == "Verse" && arrangement.SectionMarkerFor(ClipboardService.CaptureBars(song, new[] { 0 }, 0, 0)) is null);

        // The same clip pastes in the editor direction and as bars on all tracks on the timeline.
        Check("section clip is readable by the score editor paste", ProjectService.Restore(TimelineClips.ToEditorText(clip!)).Tracks[0].Measures.Count == 3);
        var target = ClipSong();
        var lists = TimelineClips.BarsPerTrack(clip!, target, 0, out _);
        var map = BarRangeEditor.Insert(target, 4, lists);
        Check("section clip inserts its bars on every track",
            target.Tracks.All(t => t.Measures.Count == 7) && map.Length == 4 && target.Tracks[0].Measures[4].RepeatStart && target.Tracks[1].Measures[4].Cells.All(c => c.Notes.Count == 0));

        // Text from another window: the marker is not in the clip, the bars are.
        system.Text = clip!.ToJson();
        system.SequenceNumber++;
        var other = service.TryGetClip(out _);
        Check("section clip read back from the system clipboard is bars without a section marker",
            other is { Kind: ScoreClipKind.Bars, BarCount: 3 } && arrangement.SectionMarkerFor(other) is null);
    }
}
