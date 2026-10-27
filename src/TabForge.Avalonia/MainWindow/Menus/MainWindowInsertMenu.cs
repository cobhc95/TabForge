using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Lyrics;
using TabForge.Avalonia.Views.Docking;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Insert menu commands that are new: a bar after the selection (Bar.InsertAfter), several bars (Bar.InsertSeveral, one dialog, one undo step), a bar at the
//   end (Bar.Append, the Add bar at the end tool), Lyrics (Insert.Lyrics), an audio or MIDI file at the cursor (Insert.AudioFile, Insert.MidiFile) and an empty MIDI clip
//   at the cursor (Insert.EmptyMidiClip). Each ends in the one shared refresh (MainWindowInsertRefresh). The cursor time is the score cursor bar and beat.
// Does not own: the Insert rows (MainMenuTable), the track adds (MainWindowTrackMenu), the existing bar, section, chord and text commands, the audio import (MainWindowAudioDrop),
//   the bar insert rules (BarStructureEdits), the dialogs (InsertBarsWindow, the file picker).
// Tests: TabForge.Avalonia.Tests.InsertMenuTests.
/// <summary>Registers the new Insert menu commands on the shown song.</summary>
public static class MainWindowInsertMenu
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "Bar.InsertAfter", "Bar.InsertSeveral", "Bar.Append", "Insert.Lyrics", "Insert.AudioFile", "Insert.MidiFile", "Insert.EmptyMidiClip",
    };

    /// <summary>The window questions: the Insert several bars dialog (given the song bar count; null on Cancel) and the file picker (true: a MIDI file, false: an audio file; null on Cancel).</summary>
    public sealed record Prompts(Func<int, Task<InsertBarsAnswer?>> AskBars, Func<bool, string?> PickFile);

    private const string NoTrackText = "Add a track first";

    /// <param name="status">The notice row (what was inserted, or why nothing was).</param>
    /// <param name="showLyrics">Brings the Lyrics pane into view and focuses it.</param>
    /// <param name="importFiles">Lands the files from a song time on the track (null: a new audio / MIDI track); the task ends when they are in (MainWindowAudioDrop.InsertAtAsync).</param>
    internal static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, ScorePreviewView score, Prompts prompts,
        Action<string> status, Action showLyrics, Func<IEnumerable<string>, TrackModel?, double, Task> importFiles)
    {
        void Landed(TrackModel? newTrack = null) => MainWindowInsertRefresh.Landed(panes, score, songs.Active, newTrack);
        commands.Add("Bar.InsertAfter", () =>
        {
            var after = (score.Editor.SelectedBarRange()?.Last ?? score.Editor.SelectedMeasure) + 1;
            if (score.InsertBarAt(after)) { Landed(); status($"Inserted bar {after + 1}"); }
        });
        commands.Add("Bar.InsertSeveral", () => UiTasks.Forget(InsertSeveralAsync(score, prompts, status, Landed), "Insert several bars", status));
        commands.Add("Bar.Append", () =>
        {
            if (score.Run(PaletteToolCatalog.CommandIdFor("gp:append_bar")!)) Landed();
        });
        commands.Add("Insert.Lyrics", showLyrics);
        commands.Add("Insert.AudioFile", () => UiTasks.Forget(InsertFileAsync(songs, score, prompts, false, importFiles, Landed), "Insert audio file", status));
        commands.Add("Insert.MidiFile", () => UiTasks.Forget(InsertFileAsync(songs, score, prompts, true, importFiles, Landed), "Insert MIDI file", status));
        commands.Add("Insert.EmptyMidiClip", () => InsertEmptyMidiClip(songs, panes, score, status, Landed));
    }

    // The dialog asks how many bars and where; the insert is one undo step and the cursor lands on the first new bar.
    private static async Task InsertSeveralAsync(ScorePreviewView score, Prompts prompts, Action<string> status, Action<TrackModel?> landed)
    {
        var answer = await prompts.AskBars(score.Editor.BarCount);
        if (answer is null) return;
        var at = InsertBarsPresenter.InsertionBar(answer.Place, score.Editor.SelectedMeasure, score.Editor.SelectedBarRange(), score.Editor.BarCount);
        if (!score.InsertBarsAt(at, answer.Count)) return;
        landed(null);
        status(answer.Count == 1 ? $"Inserted bar {at + 1}" : $"Inserted {answer.Count} bars from bar {at + 1}");
    }

    // The file lands at the cursor time on the selected audio / MIDI track, or on a new audio / MIDI track when the selected track holds notation.
    private static async Task InsertFileAsync(MainWindowTabs songs, ScorePreviewView score, Prompts prompts, bool midi,
        Func<IEnumerable<string>, TrackModel?, double, Task> importFiles, Action<TrackModel?> landed)
    {
        if (prompts.PickFile(midi) is not { } path) return;
        var song = songs.Active;
        var tracksBefore = song.Project.Tracks.Count;
        await importFiles(new[] { path }, SelectedAudioTrack(song), CursorSeconds(song, score));
        if (!ReferenceEquals(songs.Active, song)) return;
        landed(song.Project.Tracks.Count > tracksBefore ? song.Project.Tracks[^1] : null);
    }

    // The one-bar empty MIDI clip at the cursor, on the first free lane of the selected audio / MIDI track (a new one when the selected track holds notation); one undo step.
    private static void InsertEmptyMidiClip(MainWindowTabs songs, MainWindowDocumentPanes panes, ScorePreviewView score, Action<string> status, Action<TrackModel?> landed)
    {
        if (panes.Arrangement?.Timeline is not { } timeline || !ReferenceEquals(timeline.Song, songs.Active.Project)) return;
        var song = songs.Active;
        if (song.Project.Tracks.Count == 0) { status(NoTrackText); return; }
        var sec = CursorSeconds(song, score);
        var target = SelectedAudioTrack(song);
        TrackModel? added = null;
        AudioClip? made = null;
        MainWindowClipKeys.Edit(song, timeline, () =>
        {
            var track = target;
            if (track is null)
            {
                track = added = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
                song.Project.Tracks.Add(track);
            }
            made = ClipLaneEdits.NewMidiClip(song.Project, track, 0, sec);
            target = track;
            return true;
        }, "New MIDI clip", () => made is null ? Array.Empty<AudioClip>() : new[] { made });
        if (made is null) return;
        timeline.SelectedClip = new TimelineClipSelection(target!, made);
        landed(added);
        status($"Added an empty MIDI clip to {target!.Name} at {sec:0.00} s");
    }

    /// <summary>Insert > Lyrics: brings the Lyrics pane into view (a closed one at its default place, one behind another tab as the shown tab) and puts the keyboard focus in its text box once it is on screen.</summary>
    internal static void ShowLyrics(Window owner, DockWorkspaceController dock)
    {
        dock.RevealPanel(LyricsPaneId);
        Dispatcher.UIThread.Post(() => owner.GetVisualDescendants().OfType<LyricsSongPane>().FirstOrDefault()?.FocusEditor(), DispatcherPriority.Loaded);
    }

    private const string LyricsPaneId = "lyrics";

    /// <summary>The song second at the score cursor.</summary>
    internal static double CursorSeconds(SongDocument song, ScorePreviewView score) =>
        AudioClipTimelineRules.SecondsAtCursor(song.Project, score.Editor.SelectedMeasure, score.Editor.SelectedCell);

    /// <summary>The selected track when it is an audio / MIDI track, else null (the file or clip then goes on a new audio / MIDI track).</summary>
    private static TrackModel? SelectedAudioTrack(SongDocument song)
    {
        var index = MainWindowTrackMenu.SelectedIndex(song);
        return index >= 0 && index < song.Project.Tracks.Count && song.Project.Tracks[index].IsAudio ? song.Project.Tracks[index] : null;
    }
}
