using System.Collections.Generic;
using System;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Beat.MixTable command (F10): opens the Mix Table for the cursor's beat with a tab per track that changes there (the selected track's
// tab picked) and, on OK, sets every tab's mix point and the beat's tempo as one undo step. Clear removes the selected tab's mix point.
// Does not own: the window and its controls (MixTableWindow), the start values and result rules (MixTablePresenter), the instrument list (InstrumentCatalog),
//   the track selection (MainWindowTrackMenu).
// Tests: no dedicated test class.
/// <summary>The F10 Mix Table command, bound to the shown song and its cursor beat.</summary>
public static class MainWindowMixTable
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Beat.MixTable" };

    /// <param name="songs">The tabs; the command acts on the shown song.</param>
    /// <param name="score">The score whose cursor beat is edited.</param>
    /// <param name="owner">The window the dialog opens over.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, ScorePreviewView score, Window owner) =>
        commands.Add("Beat.MixTable", () => _ = EditBeatAsync(songs.Active, score, owner));

    /// <summary>
    /// A click on a bar's red button on the timeline: the cursor goes to the bar's first changed beat, on the selected track, and the Mix Table opens on it
    /// with the selected track's tab picked.
    /// </summary>
    public static void OpenBar(MainWindowTabs songs, ScorePreviewView score, Window owner, int bar)
    {
        var document = songs.Active;
        var trackIndex = MainWindowTrackMenu.SelectedIndex(document);
        if (FirstChangeCell(document.Project, bar, trackIndex) is not { } cell) return;
        score.Editor.SelectedMeasure = bar;
        score.Editor.SelectedCell = cell;
        _ = EditBeatAsync(document, score, owner);
    }

    /// <summary>The selected track's cell at the bar's first changed beat (of any track), or null when the bar changes nothing.</summary>
    private static int? FirstChangeCell(SongProject project, int bar, int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= project.Tracks.Count || bar >= project.Tracks[trackIndex].Measures.Count) return null;
        if (BeatMix.FirstChange(project, bar, trackIndex) is not { } at || bar >= project.Tracks[at.Track].Measures.Count) return null;
        var slot = BeatMix.SlotOf(project.Tracks[at.Track].Measures[bar], at.Cell);
        return BeatMix.CellAt(project.Tracks[trackIndex].Measures[bar], slot);
    }

    private static async Task EditBeatAsync(TabForge.Documents.SongDocument document, ScorePreviewView score, Window owner)
    {
        var project = document.Project;
        var trackIndex = MainWindowTrackMenu.SelectedIndex(document);
        if (trackIndex < 0 || trackIndex >= project.Tracks.Count) return;
        var track = project.Tracks[trackIndex];
        var bar = score.Editor.SelectedMeasure;
        if (bar < 0 || bar >= track.Measures.Count) return;
        var measure = track.Measures[bar];
        if (measure.Cells.Count == 0) return;
        var cellIndex = Math.Clamp(score.Editor.SelectedCell, 0, measure.Cells.Count - 1);
        var slot = BeatMix.SlotOf(measure, cellIndex);
        // F10 on a beat where no track changes anything, in a bar the timeline marks: the table opens on the bar's first changed beat. The selected
        // track stays selected (its own tab), so another track's change is never shown as this track's.
        if (!AnyChangeAt(project, bar, slot, trackIndex) && FirstChangeCell(project, bar, trackIndex) is { } first)
        {
            cellIndex = first;
            score.Editor.SelectedCell = cellIndex;
            slot = BeatMix.SlotOf(measure, cellIndex);
        }
        var tempo = BeatMix.TempoAt(project, bar, slot);
        var instruments = InstrumentCatalog.All.Select(e => (e.Program, e.Name, e.IsDrumKit)).ToList();
        var tabs = BeatMix.TracksAt(project, bar, slot, trackIndex).Select(at =>
        {
            var t = project.Tracks[at.Track];
            // Unticked rows show what the track plays at this beat (earlier mix points included), not its starting sound.
            var now = BeatMix.InForce(project, at.Track, bar, slot);
            var presenter = new MixTablePresenter(BeatMix.OwnMix(project, at.Track, bar, at.Cell), now.Program, now.Volume, now.Pan, tempo?.Tempo, project.Tempo,
                instruments, tempo?.TransitionBeats ?? 0);
            return new MixTableTab(at.Track, at.Cell, $"{at.Track + 1}. {t.Name}", at.HasChange, presenter, t.ColorHex, t.IsDrumTrack);
        }).ToList();
        if (tabs.Count == 0) return;
        var selected = Math.Max(0, tabs.FindIndex(t => t.TrackIndex == trackIndex));
        var result = await DialogLauncher.Ask<MixTableWindow, MixTableOutcome?>(owner, () => new MixTableWindow(tabs, selected, tempo?.TransitionBeats ?? 0));
        if (result is null) return;
        Apply(document, bar, slot, result);
        score.InvalidateVisual();
    }

    private static bool AnyChangeAt(SongProject project, int bar, double slot, int trackIndex) =>
        BeatMix.TempoAt(project, bar, slot) is not null || BeatMix.TracksAt(project, bar, slot, trackIndex).Any(t => t.HasChange);

    /// <summary>Writes every tab's mix point and the beat's tempo (to every track) as one undo step.</summary>
    private static void Apply(TabForge.Documents.IEditableDocument document, int bar, double slot, MixTableOutcome result) =>
        DocumentEdits.Run(document, project =>
        {
            foreach (var (tab, mix) in result.Mixes)
            {
                if (tab.TrackIndex >= project.Tracks.Count || bar >= project.Tracks[tab.TrackIndex].Measures.Count) continue;
                var cells = project.Tracks[tab.TrackIndex].Measures[bar].Cells;
                if (tab.Cell < cells.Count) cells[tab.Cell].Mix = mix is { IsEmpty: false } ? mix : null;
            }
            BeatMix.SetTempo(project, bar, slot, result.Tempo is int bpm ? new BeatTempo(bpm, result.TempoTransitionBeats) : null);
            return true;
        });
}
