using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the take-lane menu's commands (right-click on an empty part of a clip lane): Paste clip here, New empty MIDI clip here, Import audio or MIDI file here,
//   Record here, Open lane in piano roll, and the Lane rows (Rename, Move up, Move down, Delete). They act on the track, lane and snapped time the menu captured
//   (ContextMenuHost.ClickedTrack, ClickedLane, ClickedSec); each edit is one undo step through MainWindowClipKeys.Edit.
// Does not own: the menu rows (TimelineMenus.TakeLane), which menu opens (ContextMenuRows), the model edits (ClipLaneEdits, ClipEditService), the audio import
//   itself (MainWindowAudioDrop), the dialogs (passed in as Prompts) or the piano roll (EditorPaneSwitch).
// Tests: TabForge.Avalonia.Tests.TakeLaneMenuTests, TabForge.Avalonia.Tests.MainWindowMenuTests.
public static class MainWindowTakeLane
{
    public const string PasteId = "TimelineLane.PasteClip", NewMidiClipId = "TimelineLane.NewMidiClip", ImportId = "TimelineLane.Import",
        RecordHereId = "TimelineLane.RecordHere", OpenPianoRollId = "TimelineLane.OpenPianoRoll", RenameId = "TimelineLane.Rename",
        MoveUpId = "TimelineLane.MoveUp", MoveDownId = "TimelineLane.MoveDown", DeleteId = "TimelineLane.Delete";

    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { PasteId, NewMidiClipId, ImportId, RecordHereId, OpenPianoRollId, RenameId, MoveUpId, MoveDownId, DeleteId };

    /// <summary>The largest MIDI file the import reads (a score file is a few hundred KB; this stops a mistaken pick).</summary>
    private const long MaxMidiBytes = 16L * 1024 * 1024;

    /// <summary>The window's dialogs: confirm (text, caption), ask for text (title, label, initial; null on Cancel) and pick one file to import (null on Cancel).</summary>
    public sealed record Prompts(Func<string, string, Task<bool>> Confirm, Func<string, string, string, Task<string?>> AskText, Func<string?> PickFile);

    /// <param name="importAudio">Lands audio files on a track from a song time through the drop's own import.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, ContextMenuHost menus, Action<string> status,
        Prompts prompts, EditorPaneSwitch editor, Action<IEnumerable<string>, TrackModel, double> importAudio)
    {
        void Add(string id, Action<SongDocument, TimelineView> run) => commands.Add(id, () =>
        {
            if (panes.Arrangement?.Timeline is { } timeline && songs.Songs.Count > 0 && ReferenceEquals(timeline.Song, songs.Active.Project)) run(songs.Active, timeline);
        });
        Add(PasteId, (document, timeline) => PasteHere(document, timeline, menus));
        Add(NewMidiClipId, (document, timeline) => NewMidiClipHere(document, timeline, menus));
        Add(ImportId, (document, timeline) => UiTasks.Forget(ImportHere(songs, document, timeline, menus, status, prompts, importAudio), "Import", status));
        Add(RecordHereId, (document, timeline) => RecordHere(document, menus, () => commands.Run("Track.Arm")));
        Add(OpenPianoRollId, (document, timeline) => OpenInPianoRoll(document, menus, editor));
        Add(RenameId, (document, timeline) => UiTasks.Forget(RenameAsync(document, timeline, menus, prompts.AskText), "Rename lane", status));
        Add(MoveUpId, (document, timeline) => MoveLane(document, timeline, menus, -1));
        Add(MoveDownId, (document, timeline) => MoveLane(document, timeline, menus, 1));
        Add(DeleteId, (document, timeline) => UiTasks.Forget(DeleteLaneAsync(document, timeline, menus, prompts.Confirm), "Delete lane", status));
    }

    // The track, lane and snapped time the last take-lane menu captured, or null when the track is gone (a stale menu).
    private static (TrackModel Track, int Lane, double Sec)? Clicked(IEditableDocument document, ContextMenuHost menus)
    {
        var tracks = document.Project.Tracks;
        return menus.ClickedTrack >= 0 && menus.ClickedTrack < tracks.Count && menus.ClickedLane >= 0 ? (tracks[menus.ClickedTrack], menus.ClickedLane, menus.ClickedSec) : null;
    }

    /// <summary>Pastes the copied clip (a take group, or several clips with their relative places) at the clicked time on the clicked lane, as one undo step; the copies become the selection.</summary>
    internal static void PasteHere(IEditableDocument document, TimelineView timeline, ContextMenuHost menus)
    {
        if (Clicked(document, menus) is not { } hit || !ClipClipboard.HasClip) return;
        var (track, lane, sec) = hit;
        var placed = new List<TimelineClipSelection>();
        MainWindowClipKeys.Edit(document, timeline, () =>
        {
            if (ClipClipboard.IsMany)
            {
                var tracks = document.Project.Tracks;
                var firstRow = tracks.IndexOf(track);
                foreach (var (offset, copy) in ClipClipboard.CloneAllWithRows())
                {
                    var row = firstRow + offset;
                    var home = row < tracks.Count && ClipRelocation.CanMove(track, tracks[row]) ? tracks[row] : track;
                    ClipEditService.PlaceCopy(home, copy, sec + copy.StartSec, copy.Lane, timeline.ClipOverlap is null);
                    placed.Add(new TimelineClipSelection(home, copy));
                }
            }
            else if (ClipClipboard.IsGroup)
            {
                var copies = ClipClipboard.CloneAll();
                var groupId = Guid.NewGuid().ToString("N");
                foreach (var copy in copies) copy.TakeGroupId = groupId;
                ClipEditService.PlaceGroup(track, copies, sec, lane);
                placed.Add(new TimelineClipSelection(track, copies[0]));
            }
            else
            {
                var copy = ClipClipboard.CloneAll()[0];
                ClipEditService.PlaceCopy(track, copy, sec, lane, timeline.ClipOverlap is null);
                placed.Add(new TimelineClipSelection(track, copy));
            }
            return true;
        }, "Paste clip", () => placed.Select(p => p.Clip).ToList());
        if (placed.Count > 0) timeline.SelectClips(placed);
    }

    /// <summary>Adds an empty MIDI clip one bar long at the clicked time on the clicked lane, as one undo step; the clip becomes the selection.</summary>
    internal static void NewMidiClipHere(IEditableDocument document, TimelineView timeline, ContextMenuHost menus)
    {
        if (Clicked(document, menus) is not { } hit) return;
        var (track, lane, sec) = hit;
        AudioClip? made = null;
        MainWindowClipKeys.Edit(document, timeline, () => { made = ClipLaneEdits.NewMidiClip(document.Project, track, lane, sec, timeline.ClipOverlap is null); return true; }, "New MIDI clip",
            () => made is null ? Array.Empty<AudioClip>() : new[] { made });
        if (made is not null) timeline.SelectedClip = new TimelineClipSelection(track, made);
    }

    /// <summary>Picks a file; audio lands through the drop import, a MIDI file becomes one MIDI clip at the clicked time on the clicked lane (one undo step). Nothing starts recording.</summary>
    internal static async Task ImportHere(MainWindowTabs songs, SongDocument document, TimelineView timeline, ContextMenuHost menus, Action<string> status,
        Prompts prompts, Action<IEnumerable<string>, TrackModel, double> importAudio)
    {
        if (Clicked(document, menus) is not { } hit) return;
        var (track, lane, sec) = hit;
        if (prompts.PickFile() is not { } path) return;
        if (!FileTypes.IsMidi(Path.GetExtension(path))) { importAudio(new[] { path }, track, sec); return; }
        var name = Path.GetFileName(path);
        AudioClip? clip;
        try { clip = await Task.Run(() => ReadMidi(path)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            status("Cannot add " + name + ": not a readable MIDI file");
            return;
        }
        if (clip is null) { status("Cannot add " + name + ": it holds no notes"); return; }
        if (!ReferenceEquals(songs.Active, document) || !document.Project.Tracks.Contains(track)) return;
        MainWindowClipKeys.Edit(document, timeline, () => { ClipEditService.PlaceCopy(track, clip, sec, lane, timeline.ClipOverlap is null); return true; }, "Import MIDI file", () => new[] { clip });
        timeline.SelectedClip = new TimelineClipSelection(track, clip);
        status("Added " + name + " to " + track.Name + " at " + sec.ToString("0.00") + " s");
    }

    private static AudioClip? ReadMidi(string path)
    {
        if (new FileInfo(path).Length > MaxMidiBytes) throw new InvalidDataException("The MIDI file is too large.");
        return MidiClipImport.Read(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Selects the clicked track, arms it when it is not armed (arm is the Track.Arm command, with its undo step and no-input notice) and moves the playhead to the clicked bar. Recording does not start.</summary>
    internal static void RecordHere(SongDocument document, ContextMenuHost menus, Action arm)
    {
        if (Clicked(document, menus) is not { } hit) return;
        var (track, _, _) = hit;
        document.TrackIndex = document.Project.Tracks.IndexOf(track);
        if (!track.RecordArm) arm();
        if (menus.ClickedBar >= 0) menus.MovePlayhead?.Invoke(menus.ClickedBar);
    }

    /// <summary>Shows the clicked lane in the piano roll (its first MIDI clip, whose lane the roll edits); an empty MIDI lane opens its track in the piano roll.</summary>
    internal static void OpenInPianoRoll(SongDocument document, ContextMenuHost menus, EditorPaneSwitch editor)
    {
        if (Clicked(document, menus) is not { } hit) return;
        var (track, lane, _) = hit;
        var row = document.Project.Tracks.IndexOf(track);
        if (track.AudioClips.FirstOrDefault(clip => clip.Lane == lane && clip.IsMidi) is { } midi) editor.ShowTakeInPianoRollByChoice(row, midi);
        else
        {
            document.TrackIndex = row;
            editor.SetView(TrackEditorView.PianoRoll);
        }
    }

    /// <summary>Asks for the lane name (blank clears it) and sets it as one undo step.</summary>
    internal static async Task RenameAsync(IEditableDocument document, TimelineView timeline, ContextMenuHost menus, Func<string, string, string, Task<string?>> askText)
    {
        if (Clicked(document, menus) is not { } hit) return;
        var (track, lane, _) = hit;
        var current = lane < track.Lanes.Count ? track.Lanes[lane].Name ?? "" : "";
        if (await askText("Rename lane", "Lane name", current) is not { } name || !document.Project.Tracks.Contains(track)) return;
        MainWindowClipKeys.Edit(document, timeline, () => ClipLaneEdits.Rename(track, lane, name), "Rename lane");
    }

    /// <summary>Moves the clicked lane one place up (-1) or down (+1) with its clips, as one undo step.</summary>
    internal static void MoveLane(IEditableDocument document, TimelineView timeline, ContextMenuHost menus, int delta)
    {
        if (Clicked(document, menus) is not { } hit) return;
        var (track, lane, _) = hit;
        MainWindowClipKeys.Edit(document, timeline, () => ClipLaneEdits.Move(track, lane, delta), delta < 0 ? "Move lane up" : "Move lane down");
    }

    /// <summary>Deletes the clicked lane and its clips as one undo step; a lane that holds clips asks first (the lane name and clip count in the question).</summary>
    internal static async Task DeleteLaneAsync(IEditableDocument document, TimelineView timeline, ContextMenuHost menus, Func<string, string, Task<bool>> confirm)
    {
        if (Clicked(document, menus) is not { } hit) return;
        var (track, lane, _) = hit;
        var clips = ClipLaneEdits.ClipsOn(track, lane);
        if (clips > 0)
        {
            var label = lane < track.Lanes.Count && track.Lanes[lane].Name is { Length: > 0 } laneName ? "lane " + laneName : "lane " + (lane + 1);
            var what = clips == 1 ? "clip" : clips + " clips";
            if (!await confirm("Delete " + label + " of " + track.Name + " and its " + what + "? Undo brings them back.", "Delete lane")) return;
            if (!document.Project.Tracks.Contains(track)) return;
        }
        MainWindowClipKeys.Edit(document, timeline, () => ClipLaneEdits.Delete(track, lane), "Delete lane");
        timeline.SelectedClip = null;
    }
}
