using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: which Core menu each Avalonia control opens on a right-click, the state that menu needs from the control and its song (the bar and
//   track row under the pointer, the section bracket hit, the track's kind and colour, the master track row shown or not, the clipboard's CanPaste),
//   the clip menu for a clip under the pointer (which becomes the selected clip), the take-lane menu for an empty part of a clip lane (it captures the
//   clicked track, lane and snapped time in the host),
//   and the empty track list's menu when no track is under the pointer,
//   and which command id each row runs (CommandIdOf): the Avalonia window's handler ids, the same ids as the main menu's rows.
// Does not own: the menu rows (TimelineMenus, TrackRowMenus, ScoreMenus), the items (ContextMenuBuilder) or the commands they run (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.ContextMenuTests.
public static class ContextMenuRows
{
    /// <summary>The command ids of the bar menu's insert rows: the window inserts at the bar the right-click was on (ContextMenuHost.ClickedBar).</summary>
    public const string InsertBarBeforeId = "TimelineBar.InsertBefore", InsertBarAfterId = "TimelineBar.InsertAfter";

    /// <summary>The command ids of the bar menu's Copy bar rows: this track, and every track (the window copies ContextMenuHost.ClickedBar).</summary>
    public const string BarCopyId = "TimelineBar.Copy", BarCopyAllTracksId = "TimelineBar.CopyAllTracks";

    /// <summary>The command ids of the bar menu's Paste bar rows: this track, and every track (the window pastes at ContextMenuHost.ClickedBar).</summary>
    public const string BarPasteId = "TimelineBar.Paste", BarPasteAllTracksId = "TimelineBar.PasteAllTracks";

    /// <summary>The command ids of the bar menu's Delete bar rows: this track, and every track (the window asks what Delete does to ContextMenuHost.ClickedBar).</summary>
    public const string BarDeleteId = "TimelineBar.Delete", BarDeleteAllTracksId = "TimelineBar.DeleteAllTracks";

    /// <summary>The command id of the section lock rows (the section menu's and the bar menu's "Lock section position"): the window flips the lock of ContextMenuHost.ClickedSection.</summary>
    public const string SectionLockId = "Section.ToggleLock";
    public const string RetryReadId = "Media.RetryWaveform", RetryReadHeader = "Retry reading this audio";
    /// <summary>The missing-audio clip menu's top rows: Locate file… (this clip, and the others missing beside it) and Find all missing audio… (the search window).</summary>
    public const string LocateMediaId = "Media.LocateFile", LocateMediaHeader = "Locate file…", FindMissingMediaId = "Media.FindMissingAll", FindMissingMediaHeader = "Find all missing audio…";

    /// <summary>The command ids of the section menu's Copy, Cut, Paste, Duplicate and Delete rows (the window edits ContextMenuHost.ClickedSection).</summary>
    public const string SectionCopyId = "TimelineSection.Copy", SectionCutId = "TimelineSection.Cut",
        SectionDuplicateId = "TimelineSection.Duplicate", SectionPasteAfterId = "TimelineSection.PasteAfter", SectionDeleteId = "TimelineSection.Delete", SectionRemoveLabelId = "TimelineSection.RemoveLabel",
        SectionPasteAtBarId = "TimelineSection.PasteAtBar", SectionCloseGapId = "TimelineSection.CloseGap", SectionGoToId = "TimelineSection.GoTo", SectionAddHereId = "TimelineSection.AddHere", SectionRenameId = "TimelineSection.Rename";

    /// <summary>The command id of the section menu's Loop section row (the window loops ContextMenuHost.ClickedSection).</summary>
    public const string LoopSectionId = "Transport.LoopSection";
    /// <summary>The ruler menu's Move playhead here: the clicked bar (ContextMenuHost.ClickedBar).</summary>
    public const string MovePlayheadHereId = "Timeline.MovePlayheadHere";

    /// <summary>The command id of the highlight menu's "Select these bars" row.</summary>
    public const string SelectHighlightedBarsId = "Highlight.SelectBars";

    /// <summary>The command id of "Reset track list height" (the View menu's row of the same name).</summary>
    public const string ResetTrackRowHeightId = "View.ResetTrackRowHeight";

    /// <summary>The command id prefix of the page layout rows; the row's Arg completes it (page, continuous, vertical, horizontal).</summary>
    public const string ScoreLayoutIdPrefix = "score.layout.";

    /// <summary>The command ids of the timeline rows that have an Avalonia handler. Most section rows have none yet.
    /// The bar and section settings doors open the Timeline &amp; Tracks Preferences page, as the track list's settings row does.</summary>
    private static readonly IReadOnlyDictionary<TimelineCommand, string> TimelineCommandIds = new Dictionary<TimelineCommand, string>
    {
        [TimelineCommand.CopySelection] = "Edit.Copy",
        [TimelineCommand.CutSelection] = "Edit.Cut",
        [TimelineCommand.PasteSelection] = "Edit.Paste",
        [TimelineCommand.DeleteSelection] = "Range.Delete",
        [TimelineCommand.HighlightCopy] = "Highlight.Copy",
        [TimelineCommand.HighlightCut] = "Highlight.Cut",
        [TimelineCommand.HighlightClear] = "Highlight.Delete",
        [TimelineCommand.HighlightPaste] = "Highlight.Paste",
        [TimelineCommand.HighlightSelectBars] = SelectHighlightedBarsId,
        [TimelineCommand.LoopSelection] = "Transport.Loop",
        [TimelineCommand.ClearSelection] = "Transport.ClearLoopArea",
        [TimelineCommand.RulerLoop] = "Transport.Loop",
        [TimelineCommand.RulerClearLoop] = "Transport.ClearLoopArea",
        [TimelineCommand.RulerLoopSection] = LoopSectionId,
        [TimelineCommand.RulerLoopSelectedBars] = "Transport.LoopSelectedBars",
        [TimelineCommand.RulerMovePlayhead] = MovePlayheadHereId,
        [TimelineCommand.InsertBarBefore] = InsertBarBeforeId,
        [TimelineCommand.InsertBarAfter] = InsertBarAfterId,
        [TimelineCommand.CopyBar] = BarCopyId,
        [TimelineCommand.CopyBarAllTracks] = BarCopyAllTracksId,
        [TimelineCommand.PasteBar] = BarPasteId,
        [TimelineCommand.PasteBarAllTracks] = BarPasteAllTracksId,
        [TimelineCommand.DeleteBar] = BarDeleteId,
        [TimelineCommand.DeleteBarAllTracks] = BarDeleteAllTracksId,
        [TimelineCommand.CopySectionMenu] = SectionCopyId,
        [TimelineCommand.CutSection] = SectionCutId,
        [TimelineCommand.PasteSectionAfter] = SectionPasteAfterId,
        [TimelineCommand.DuplicateSection] = SectionDuplicateId,
        [TimelineCommand.DeleteSection] = SectionDeleteId,
        [TimelineCommand.RemoveSectionLabel] = SectionRemoveLabelId,
        [TimelineCommand.GoToSection] = SectionGoToId,
        [TimelineCommand.LoopSection] = LoopSectionId,
        [TimelineCommand.AddSectionHere] = SectionAddHereId,
        [TimelineCommand.PasteSectionAtBar] = SectionPasteAtBarId,
        [TimelineCommand.CloseSectionGap] = SectionCloseGapId,
        [TimelineCommand.RenameSection] = SectionRenameId,
        [TimelineCommand.ToggleSectionLock] = SectionLockId,
        [TimelineCommand.ToggleSectionLockAtBar] = SectionLockId,
        [TimelineCommand.ResetTrackListHeight] = ResetTrackRowHeightId,
        [TimelineCommand.TimelineSettings] = TrackListMenus.SettingsId,
        [TimelineCommand.SectionSettings] = TrackListMenus.SettingsId,
        [TimelineCommand.ClipCopy] = "Clip.Copy",
        [TimelineCommand.ClipCut] = "Clip.Cut",
        [TimelineCommand.ClipPaste] = "Clip.Paste",
        [TimelineCommand.ClipDuplicate] = "Clip.Duplicate",
        [TimelineCommand.ClipSplit] = "Clip.Split",
        [TimelineCommand.ClipGlue] = "Clip.Glue",
        [TimelineCommand.ClipFadeReset] = "Clip.FadeReset",
        [TimelineCommand.ClipDelete] = "Clip.Delete",
        [TimelineCommand.ClipMute] = "Clip.Mute",
        [TimelineCommand.ClipProperties] = "Clip.Properties",
        [TimelineCommand.ClipReRecordPass] = MainWindowReRecordPass.CommandId,
        [TimelineCommand.LanePasteClip] = MainWindowTakeLane.PasteId,
        [TimelineCommand.LaneNewMidiClip] = MainWindowTakeLane.NewMidiClipId,
        [TimelineCommand.LaneImport] = MainWindowTakeLane.ImportId,
        [TimelineCommand.LaneRecordHere] = MainWindowTakeLane.RecordHereId,
        [TimelineCommand.LaneOpenPianoRoll] = MainWindowTakeLane.OpenPianoRollId,
        [TimelineCommand.LaneRename] = MainWindowTakeLane.RenameId,
        [TimelineCommand.LaneMoveUp] = MainWindowTakeLane.MoveUpId,
        [TimelineCommand.LaneMoveDown] = MainWindowTakeLane.MoveDownId,
        [TimelineCommand.LaneDelete] = MainWindowTakeLane.DeleteId,
    };

    /// <summary>The command id a row runs, or null when the row names none. Score rows use their own ids (paste, pastespecial) for the Edit commands,
    /// and one id per page layout choice (the choices share one row id and differ by Arg).</summary>
    public static string? CommandIdOf(MenuSpec row)
    {
        if (row.Command != TimelineCommand.None) return TimelineCommandIds.TryGetValue(row.Command, out var timelineId) ? timelineId : null;
        return row.Id switch
        {
            ScoreMenus.PasteId => "Edit.Paste",
            ScoreMenus.PasteSpecialId => "Edit.PasteSpecial",
            ScoreMenus.LayoutId => ScoreLayoutIdPrefix + row.Arg,
            var id => id,
        };
    }

    /// <summary>The score view's empty-area menu for the view's current state (no paste over a beat; paste is offered when the host can paste).</summary>
    public static IEnumerable<MenuSpec> ScoreAt(ContextMenuHost host, ScoreEmptyState state) =>
        ScoreMenus.Empty(state with { CanPaste = host.CanPaste }, Shortcuts());

    /// <summary>The note menu for the note under the pointer or the selection it acts on; paste is offered when the host can paste.</summary>
    internal static IEnumerable<MenuSpec> ScoreNoteAt(ContextMenuHost host, ScoreNoteMenuState state) =>
        ScoreNoteMenus.Rows(state with { CanPaste = host.CanPaste });

    // The marker index of the section whose bars hold bar, or -1.
    private static int SectionAt(SongProject song, int bar)
    {
        for (var i = 0; i < song.Markers.Count; i++)
            if (SectionMenuEdits.TryBounds(song, song.Markers[i], out var start, out var end) && bar >= start && bar < end) return i;
        return -1;
    }

    /// <summary>The timeline's menu at <paramref name="at"/>: a section's menu on its bracket band, else the bar menu.</summary>
    public static IEnumerable<MenuSpec> TimelineAt(TimelineView timeline, Point at)
    {
        var song = timeline.Song;
        var key = Shortcuts();
        var canPaste = timeline.ContextMenus.CanPaste;
        var inSectionBand = at.Y >= TimelineView.RulerHeight && at.Y < TimelineView.RulerHeight + TimelineView.SectionHeight;
        var slots = timeline.SectionSlots();
        var hit = Array.FindIndex(slots, slot => at.X >= slot.Left && at.X < slot.Left + slot.Width);   // -1 in a gap (FirstOrDefault of a struct would pick the first section)
        if (inSectionBand && hit >= 0)
        {
            var section = slots[hit];
            timeline.ContextMenus.ClickedSection = section.MarkerIndex;
            // Add section here runs at the clicked bar; it is offered only off the section's own first bar.
            var clickedBar = timeline.BarAtX(at.X);
            int? addAt = clickedBar >= 0 && clickedBar < timeline.BarCount && clickedBar != song.Markers[section.MarkerIndex].MeasureIndex ? clickedBar : null;
            timeline.ContextMenus.ClickedBar = addAt ?? -1;
            return TimelineMenus.Section(new SectionMenuState(AddAtBar: addAt, CanPaste: canPaste, Looped: false, Locked: song.Markers[section.MarkerIndex].LockPosition), key);
        }

        // An empty part of the section strip: no section is targeted (the section rows would act on a neighbour), only Add / Paste at the clicked bar.
        if (inSectionBand)
        {
            var gapBar = timeline.BarAtX(at.X);
            timeline.ContextMenus.ClickedSection = -1;
            timeline.ContextMenus.ClickedBar = gapBar >= 0 && gapBar < timeline.BarCount ? gapBar : -1;
            return TimelineMenus.SectionGap(Math.Max(0, gapBar), canPaste, gapBar >= 0 ? SectionGapEdits.Find(song, gapBar) : null, key);
        }

        // On the bar-number ruler: the loop region's menu (no work until it opens: the state is read here, at the right-click).
        if (at.Y < TimelineView.RulerHeight)
        {
            var rulerBar = Math.Clamp(timeline.BarAtX(at.X), 0, Math.Max(0, timeline.BarCount - 1));
            timeline.ContextMenus.ClickedBar = rulerBar;
            var holder = SectionAt(song, rulerBar);
            timeline.ContextMenus.ClickedSection = holder;
            var model = timeline.FocusModel;
            return TimelineMenus.Ruler(new RulerMenuState(model.LoopRepeat, model.HasLoop, holder >= 0, model.HasRange || model.ClipCount > 0), key);
        }

        // A right-click on a clip selects it and opens the clip menu, whose rows run the selected-clip commands.
        if (TimelineAudioClips.ClipAt(timeline, at) is { } clip)
        {
            timeline.SelectedClip = clip;
            var grouped = TakeGroups.IsGrouped(clip.Clip);
            var rows = TimelineMenus.Clip(new ClipMenuState(HasClip: true, IsMidi: clip.Clip.IsMidi, CanPaste: ClipClipboard.HasClip, Muted: clip.Clip.Muted,
                IsGrouped: grouped, Takes: TakesOf(timeline, clip)), key);
            var unreadable = !clip.Clip.IsMidi && timeline.Waveforms.StatusOf(clip.Clip).State == TabForge.Audio.WaveState.Failed;
            timeline.ContextMenus.ClickedUnreadableClip = unreadable ? clip.Clip : null;
            // A clip whose file could not be read offers to read it again (a file that was busy or copied late then shows its waveform).
            var withRetry = unreadable ? rows.Prepend(MenuSpec.Separator()).Prepend(new MenuSpec { Header = RetryReadHeader, Id = RetryReadId }) : rows;
            return !clip.Clip.IsMidi && timeline.Waveforms.StatusOf(clip.Clip).IsMissing ? MissingMediaRows(timeline).Concat(withRetry) : withRetry;
        }

        // On an empty part of a take lane (no clip under the pointer): the lane's own menu, with the loop region's items after a separator when inside a loop.
        if (TimelineAudioClips.LaneAt(timeline, at) is { } laneHit)
        {
            var laneRows = TakeLaneAt(timeline, laneHit.Row, laneHit.Lane, at, key);
            var laneLoop = LoopRowsAt(timeline, at, canPaste, key);
            return laneLoop is null ? laneRows : laneRows.Append(MenuSpec.Separator()).Concat(laneLoop).ToList();
        }

        // On the grey highlighted cells (a right-click puts the highlight there first): the highlight menu.
        if (timeline.TabCellAt(at.X, at.Y) is { } cell && timeline.Highlight is { } highlight && highlight.Contains(cell.Row, cell.Bar))
        {
            var bars = highlight.FirstBar == highlight.LastBar ? $"Bar {highlight.FirstBar + 1}" : $"Bars {highlight.FirstBar + 1}-{highlight.LastBar + 1}";
            var tracks = highlight.FirstTrack == highlight.LastTrack ? song.Tracks[highlight.FirstTrack].Name : $"{highlight.LastTrack - highlight.FirstTrack + 1} tracks";
            return TimelineMenus.Highlight(new HighlightMenuState($"{bars} · {tracks} highlighted", canPaste), key);
        }

        // Inside the loop region on the lanes: the loop menu (Copy/Cut/Paste, Delete…, Loop, Arrange, Clear loop).
        if (LoopRowsAt(timeline, at, canPaste, key) is { } loopRows) return loopRows;

        var bar = timeline.BarAtX(at.X);
        var row = timeline.RowAtY(at.Y);
        var hasBar = bar >= 0 && bar < timeline.BarCount;
        var hasTrack = row >= 0 && row < song.Tracks.Count;
        // The bar menu's insert rows run at this bar, so the window reads it from the host when the row runs.
        timeline.ContextMenus.ClickedBar = hasBar ? bar : -1;
        timeline.ContextMenus.ClickedTrack = hasTrack ? row : -1;
        var marker = hasBar ? song.Markers.Where(m => m.MeasureIndex <= bar).OrderBy(m => m.MeasureIndex).LastOrDefault() : null;
        var state = new BarMenuState(HasBar: hasBar, HasTrack: hasTrack, TrackCount: song.Tracks.Count, CanDeleteThisTrack: hasTrack, CanDeleteAllTracks: true,
            InSection: marker is not null, SectionLocked: marker?.LockPosition ?? false, CanPaste: canPaste, CanPasteAllTracks: canPaste);
        timeline.ContextMenus.ClickedSection = marker is null ? -1 : song.Markers.IndexOf(marker);
        return TimelineMenus.Bar(state, key);
    }

    // The loop region's menu rows when `at` is inside the loop (null otherwise); sets the clicked bar the rows run at.
    private static IEnumerable<MenuSpec>? LoopRowsAt(TimelineView timeline, Point at, bool canPaste, Func<string, string> key)
    {
        var selectedBar = timeline.BarAtX(at.X);
        if (timeline.FocusModel is not { HasLoop: true } looped || !looped.LoopContains(selectedBar)) return null;
        timeline.ContextMenus.ClickedBar = selectedBar;
        var label = looped.LoopStartBar == looped.LoopEndBar
            ? $"Bar {looped.LoopStartBar + 1} looped"
            : $"Bars {looped.LoopStartBar + 1}-{looped.LoopEndBar + 1} looped";
        return TimelineMenus.Selection(new SelectionMenuState(label, canPaste, timeline.LoopOn, Skipped: false, AnySkipped: false), key);
    }

    // The top of a missing clip's menu: locate this file by hand, or search a folder for all the missing audio (with how many are missing, as the row's right-hand text).
    private static IEnumerable<MenuSpec> MissingMediaRows(TimelineView timeline)
    {
        var missing = timeline.Song.Tracks.SelectMany(t => t.AudioClips).Where(c => !c.IsMidi && timeline.Waveforms.StatusOf(c).IsMissing).Select(c => c.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        yield return new MenuSpec { Header = LocateMediaHeader, Id = LocateMediaId };
        yield return new MenuSpec { Header = FindMissingMediaHeader, Id = FindMissingMediaId, Shortcut = $"{missing} missing" };
    }

    // The take-lane menu for lane `lane` of track row `row`; the commands read the clicked track, lane and snapped time from the host.
    private static IEnumerable<MenuSpec> TakeLaneAt(TimelineView timeline, int row, int lane, Point at, Func<string, string> key)
    {
        var track = timeline.Song.Tracks[row];
        var host = timeline.ContextMenus;
        var snappedX = TimelineClipSnap.SnapX(timeline, at.X, null, false, out _);
        var bar = timeline.BarAtX(at.X);
        host.ClickedTrack = row;
        host.ClickedLane = lane;
        host.ClickedSec = Math.Max(0, timeline.SecondsOfX(snappedX));
        host.ClickedBar = bar >= 0 && bar < timeline.BarCount ? bar : -1;
        host.ClickedUnreadableClip = null;
        var hasMidi = track.AudioClips.Any(c => c.Lane == lane && c.IsMidi);
        var laneEmpty = !track.AudioClips.Any(c => c.Lane == lane);
        var laneCount = Math.Max(1, timeline.LaidOutLaneCountOf(track));
        var state = new TakeLaneMenuState(CanPaste: ClipClipboard.HasClip, CanNewMidiClip: !track.IsAudio || hasMidi || AudioInputs.TakesMidi(track),
            CanOpenInPianoRoll: hasMidi || (laneEmpty && !track.IsAudio && AudioInputs.IsMidiOnly(track)),
            CanMoveUp: lane > 0, CanMoveDown: lane < laneCount - 1, CanDelete: track.AudioClips.Count > 0 && lane < laneCount,
            EmptyLaneClick: track.IsAudio ? null : host.EmptyLaneClick());
        return TimelineMenus.TakeLane(state, key);
    }

    // The Takes submenu rows of a loop-recorded clip (null when it is not one of several takes).
    private static IReadOnlyList<(string Name, bool Active)>? TakesOf(TimelineView timeline, TimelineClipSelection clip)
    {
        var takes = LoopTakes.Of(clip.Track, clip.Clip);
        return takes.Count < 2 ? null : takes.Select((t, i) => ($"Take {i + 1}", LoopTakes.IsActive(clip.Track, t))).ToList();
    }

    /// <summary>The track row menu for the track under <paramref name="at"/> (in the timeline's coordinates); where no track is, the empty track list's menu.</summary>
    public static IEnumerable<MenuSpec> TrackRowAt(TimelineView timeline, Point at)
    {
        var song = timeline.Song;
        var row = timeline.RowAtY(at.Y);
        // The track row menu's commands run on the track the right-click was on (ContextMenuHost.ClickedTrack).
        var hasTrack = row >= 0 && row < song.Tracks.Count;
        timeline.ContextMenus.ClickedTrack = hasTrack ? row : -1;
        if (!hasTrack) return TrackListMenus.Build(new TrackListMenuState(GroupsShown: false, MasterLane: timeline.MasterLaneShown));
        var track = song.Tracks[row];
        var state = new TrackRowMenuState(IsAudio: track.IsAudio, CanPaste: timeline.ContextMenus.CanPaste, CanDelete: song.Tracks.Count > 1, ColourHex: track.ColorHex,
            MasterLane: timeline.MasterLaneShown);
        return TrackRowMenus.Build(state, Shortcuts());
    }

    /// <summary>Shortcut display text for a hotkey id, from the default bindings (the Avalonia views do not read the user's bindings yet).</summary>
    /// <summary>The hotkey id to shortcut text map the menus and the bar-range prompt show.</summary>
    public static Func<string, string> Shortcuts()
    {
        var hotkeys = new HotkeySettings();
        return id => HotkeyCatalog.DisplayAll(hotkeys, id);
    }
}
