using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Features.Recording.Model;
using TabForge.Features.Recording.Rules;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the timeline clip key commands on the clip the timeline has selected: Deselect, Delete, Copy, Cut, Paste, Duplicate, the beat and fine
//   nudges left and right, and lane up and down. Each edit is one DocumentEdits step on the shown song. A key is used only while a clip is selected
//   (MainWindowCommands.AddWhen), so the arrows and Escape keep their editor meaning otherwise; Paste also needs a copied clip.
// Does not own: the key table (MainWindowKeyboard), the selection (the document's SelectionModel), the pure clip edits and lane rules (ClipEditService
//   over ClipLanes in TabForge.Core), the clip clipboard (ClipClipboard in TabForge.Core), and the click position Paste uses (LaneCursor):
//   Paste lands after the selected clip.
// Tests: TabForge.Avalonia.Tests.MainWindowClipKeysTests.
/// <summary>The timeline clip commands, bound to the selected clip of the shown song.</summary>
public static class MainWindowClipKeys
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "Clip.Deselect", "Clip.Delete", "Clip.Copy", "Clip.Cut", "Clip.Paste", "Clip.Duplicate",
        "Clip.NudgeLeft", "Clip.NudgeRight", "Clip.NudgeLeftFine", "Clip.NudgeRightFine", "Clip.LaneUp", "Clip.LaneDown", TimelineMenus.TakeId,
    };

    /// <summary>A fine nudge moves the clip this many seconds (10 ms).</summary>
    private const double FineNudgeSec = 0.01;

    /// <summary>The shown song, its arrangement timeline and the selected clip: what one clip command acts on.</summary>
    internal sealed record Target(IEditableDocument Document, TimelineView Timeline, TimelineClipSelection Selection, IReadOnlyList<TimelineClipSelection>? Selected = null)
    {
        /// <summary>Every selected clip, the primary one (<see cref="Selection"/>) first: what an edit key acts on.</summary>
        public IReadOnlyList<TimelineClipSelection> All => Selected ?? new[] { Selection };
    }

    /// <param name="commands">The window's command table; the clip commands are added with a guard.</param>
    /// <param name="songs">The tabs; the commands act on the shown song.</param>
    /// <param name="panes">The latest arrangement pane, whose timeline holds the clip selection.</param>
    /// <param name="trimEmptyBarsAtEnd">The Editing row: when on, a clip edit that shrinks the song's clips removes the empty bars at its end.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, Func<bool> trimEmptyBarsAtEnd)
    {
        Add(commands, songs, panes, "Clip.Deselect", target =>
        {
            target.Timeline.SelectedClip = null;
            target.Timeline.LaneCursor = null;   // the lane edit cursor belongs to the clip context
        });
        Add(commands, songs, panes, "Clip.Delete", target => Delete(target, trimEmptyBarsAtEnd));
        Add(commands, songs, panes, "Clip.Copy", Copy);
        Add(commands, songs, panes, "Clip.Cut", target => Cut(target, trimEmptyBarsAtEnd));
        Add(commands, songs, panes, "Clip.Paste", Paste, () => ClipClipboard.HasClip);
        Add(commands, songs, panes, "Clip.Duplicate", Duplicate);
        Add(commands, songs, panes, "Clip.NudgeLeft", target => Nudge(target, -ClipEditService.BeatSeconds(target.Document.Project.Tempo), trimEmptyBarsAtEnd));
        Add(commands, songs, panes, "Clip.NudgeRight", target => Nudge(target, ClipEditService.BeatSeconds(target.Document.Project.Tempo), () => false));
        Add(commands, songs, panes, "Clip.NudgeLeftFine", target => Nudge(target, -FineNudgeSec, trimEmptyBarsAtEnd));
        Add(commands, songs, panes, "Clip.NudgeRightFine", target => Nudge(target, FineNudgeSec, () => false));
        Add(commands, songs, panes, "Clip.LaneUp", target => Edit(target, () => Each(target, c => ClipEditService.LaneUp(c.Track, c.Clip))));
        Add(commands, songs, panes, "Clip.LaneDown", target => Edit(target, () => Each(target, c => ClipEditService.LaneDown(c.Track, c.Clip))));
        commands.AddWithArgument(TimelineMenus.TakeId, arg => { if (TargetOf(songs, panes) is { } target && int.TryParse(arg, out var number)) ActivateTake(target, number); });
    }

    /// <summary>
    /// Adds a clip command: it runs on the selected clip of the shown song, and its key is used only while such a clip exists and
    /// <paramref name="extraGuard"/> (when given) holds.
    /// </summary>
    internal static void Add(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, string commandId, Action<Target> run,
        Func<bool>? extraGuard = null) =>
        commands.AddWhen(commandId, () => { if (TargetOf(songs, panes) is { } target) run(target); },
            () => TargetOf(songs, panes) is not null && (extraGuard?.Invoke() ?? true));

    /// <summary>The selected clip of the shown song (its document's selection), or null when no clip is selected or the selection is not in the shown song.</summary>
    internal static Target? TargetOf(MainWindowTabs songs, MainWindowDocumentPanes panes)
    {
        var timeline = panes.Arrangement?.Timeline;
        var document = songs.Active;
        if (timeline is null || document.Selection.Clips.FirstOrDefault() is not { } selection) return null;
        var owned = document.Project.Tracks.Contains(selection.Track) && selection.Track.AudioClips.Contains(selection.Clip);
        if (!owned) return null;
        var all = document.Selection.Clips.Where(c => document.Project.Tracks.Contains(c.Track) && c.Track.AudioClips.Contains(c.Clip)).ToList();
        return new Target(document, timeline, selection, all);
    }

    /// <summary>Runs one clip edit as one undo step on the shown song, then redraws the timeline and lets the window do its post-edit refresh
    /// (empty-lane compaction and the normal engine sync). A change that reports nothing changed takes no step.</summary>
    internal static void Edit(Target target, Func<bool> change, string label = "Edit")
    {
        var fit = default(AutoFitResult);
        var changed = DocumentEdits.Run(target.Document, project =>
        {
            if (!change()) return false;
            fit = ClipAutoBars.Fit(project);   // grow or give back auto-added bars inside the same undo step
            return true;
        }, label: label).Changed;
        if (!changed) return;
        if (fit.EndsInJump) target.Timeline.ClipNotice?.Invoke(ClipAutoBars.JumpNotice);
        target.Timeline.ClipEdited?.Invoke(target.Document);
        target.Timeline.InvalidateVisual();
    }

    /// <summary>Makes take <paramref name="number"/> (1-based) of the selected loop-recorded clip the active one, as one undo step.</summary>
    internal static void ActivateTake(Target target, int number)
    {
        var track = target.Selection.Track;
        var takes = LoopTakes.Of(track, target.Selection.Clip);
        if (number < 1 || number > takes.Count) return;
        Edit(target, () => LoopTakes.Activate(track, takes, takes[number - 1]), "Choose take");
    }

    /// <summary>Moves every selected clip by <paramref name="deltaSec"/> as one undo step; a move that shrinks the song may trim its empty end bars.</summary>
    internal static void Nudge(Target target, double deltaSec, Func<bool> trimEmptyBarsAtEnd) =>
        Edit(target, () => TrimAfterShrink(target, Each(target, c => ClipEditService.Move(c.Track, c.Clip, deltaSec)), trimEmptyBarsAtEnd));

    /// <summary>Runs <paramref name="change"/> on every selected clip (inside one edit); true when any of them changed.</summary>
    internal static bool Each(Target target, Func<TimelineClipSelection, bool> change)
    {
        var changed = false;
        foreach (var selected in target.All) changed |= change(selected);
        return changed;
    }

    /// <summary>Removes every selected clip as one undo step and clears the selection.</summary>
    internal static void Delete(Target target, Func<bool> trimEmptyBarsAtEnd)
    {
        Edit(target, () => TrimAfterShrink(target, Each(target, c => TakeGroupEdits.Remove(c.Track, c.Clip)), trimEmptyBarsAtEnd));
        target.Timeline.SelectedClip = null;
    }

    /// <summary>Inside a clip edit's step: when the edit changed the song and the setting is on, removes the empty bars at the song's end (TrailingBars).</summary>
    private static bool TrimAfterShrink(Target target, bool changed, Func<bool> trimEmptyBarsAtEnd)
    {
        if (changed && trimEmptyBarsAtEnd()) TrailingBars.Trim(target.Document.Project);
        return changed;
    }

    /// <summary>Puts a copy of the selected clip (its whole take group), or of all the selected clips with their relative places, on the clipboard; no song change.</summary>
    internal static void Copy(Target target)
    {
        if (target.All.Count == 1) ClipClipboard.Copy(target.Selection.Track, target.Selection.Clip);
        else ClipClipboard.CopyMany(target.All.Select(c => (target.Document.Project.Tracks.IndexOf(c.Track), c.Clip)).ToList());
    }

    /// <summary>Copies the selected clip, or its whole take group, to the clipboard, then removes it.</summary>
    internal static void Cut(Target target, Func<bool> trimEmptyBarsAtEnd)
    {
        Copy(target);
        Delete(target, trimEmptyBarsAtEnd);
    }

    /// <summary>Adds a copy of each selected clip right after it, on its lane (the first free lane when that one is busy), as one undo step. A repeat take is
    /// duplicated as one object after the group end, on one free lane over its whole span, with a new group id. The copies become the selection.</summary>
    internal static void Duplicate(Target target)
    {
        var placed = new List<TimelineClipSelection>();
        Edit(target, () =>
        {
            foreach (var (track, clip) in target.All) placed.Add(DuplicateOne(track, clip));
            return true;
        });
        target.Timeline.SelectClips(placed);
    }

    private static TimelineClipSelection DuplicateOne(TrackModel track, AudioClip clip)
    {
        if (!TakeGroups.IsGrouped(clip)) return PlaceCopyIn(track, clip.Clone(), clip.EndSec, clip.Lane);
        var members = TakeGroups.Members(track, clip);
        var earliest = members.Min(member => member.StartSec);
        var copies = members.Select(member =>
        {
            var copy = member.Clone();
            copy.StartSec = member.StartSec - earliest;
            copy.Lane = 0;
            return copy;
        }).ToList();
        return PlaceGroupIn(track, copies, members.Max(member => member.EndSec), clip.Lane);
    }

    /// <summary>Adds the copied clip right after the selected clip, on its track and lane (the first free lane when that one is busy). A
    /// copied repeat take is pasted as one object after the selected clip or group end, on one free lane with a new group id. Several copied clips
    /// land after the last selected clip end, keeping their relative times, lanes and track rows (from the selected clip track), as one undo step.</summary>
    internal static void Paste(Target target)
    {
        if (ClipClipboard.IsMany) { PasteMany(target); return; }
        var copies = ClipClipboard.CloneAll();
        if (copies.Count == 0) return;
        var clip = target.Selection.Clip;
        TimelineClipSelection? placed = null;
        if (!ClipClipboard.IsGroup)
            Edit(target, () => { placed = PlaceCopyIn(target.Selection.Track, copies[0], clip.EndSec, clip.Lane); return true; });
        else
        {
            var start = TakeGroups.IsGrouped(clip)
                ? TakeGroups.Members(target.Selection.Track, clip).Max(member => member.EndSec) : clip.EndSec;
            Edit(target, () => { placed = PlaceGroupIn(target.Selection.Track, copies, start, clip.Lane); return true; });
        }
        target.Timeline.SelectedClip = placed;
    }

    private static void PasteMany(Target target)
    {
        var items = ClipClipboard.CloneAllWithRows();
        var tracks = target.Document.Project.Tracks;
        var home = target.Selection.Track;
        var firstRow = tracks.IndexOf(home);
        var anchor = target.All.Max(c => c.Clip.EndSec);
        var placed = new List<TimelineClipSelection>();
        Edit(target, () =>
        {
            foreach (var (offset, copy) in items)
            {
                var row = firstRow + offset;
                var track = row < tracks.Count && ClipRelocation.CanMove(home, tracks[row]) ? tracks[row] : home;
                placed.Add(PlaceCopyIn(track, copy, anchor + copy.StartSec, copy.Lane));
            }
            return true;
        });
        target.Timeline.SelectClips(placed);
    }

    /// <summary>Adds <paramref name="copy"/> to <paramref name="track"/> at <paramref name="startSec"/> on <paramref name="lane"/> (inside an edit).</summary>
    private static TimelineClipSelection PlaceCopyIn(TrackModel track, AudioClip copy, double startSec, int lane)
    {
        ClipEditService.PlaceCopy(track, copy, startSec, lane);
        return new TimelineClipSelection(track, copy);
    }

    /// <summary>Adds the copies of one repeat take, with starts relative to the earliest, under a fresh group id (inside an edit); returns the first.</summary>
    private static TimelineClipSelection PlaceGroupIn(TrackModel track, IReadOnlyList<AudioClip> copies, double startSec, int lane)
    {
        var groupId = Guid.NewGuid().ToString("N");
        foreach (var copy in copies) copy.TakeGroupId = groupId;
        ClipEditService.PlaceGroup(track, copies, Math.Max(0, startSec), lane);
        return new TimelineClipSelection(track, copies[0]);
    }
}
