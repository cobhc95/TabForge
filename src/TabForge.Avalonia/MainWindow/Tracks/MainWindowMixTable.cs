using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Beat.MixTable command (F10): opens the Mix Table for the cursor's beat of the selected track and, on OK, sets the beat's mix point
// and the bar's tempo as one undo step. Clear removes the mix point.
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

    /// <summary>A click on a bar's red button on the timeline: the cursor goes to the bar's first changed beat and the Mix Table opens on it.</summary>
    public static void OpenBar(MainWindowTabs songs, ScorePreviewView score, Window owner, int bar)
    {
        var document = songs.Active;
        if (BeatMix.FirstChange(document.Project, bar, MainWindowTrackMenu.SelectedIndex(document)) is not { } at) return;
        document.TrackIndex = at.Track;
        score.Editor.SelectedMeasure = bar;
        score.Editor.SelectedCell = at.Cell;
        _ = EditBeatAsync(document, score, owner);
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
        // F10 on a beat with nothing, in a bar the timeline marks: the table opens on the bar's first changed beat, where the changes are.
        if (!BeatMix.BeatHasChange(project, trackIndex, bar, cellIndex) && BeatMix.FirstChange(project, bar, trackIndex) is { } first)
        {
            (trackIndex, cellIndex) = first;
            document.TrackIndex = trackIndex;
            score.Editor.SelectedCell = cellIndex;
            track = project.Tracks[trackIndex];
            measure = track.Measures[bar];
        }
        var slot = BeatMix.SlotOf(measure, cellIndex);
        // The beat shows every change in force there, whichever track it was set from: its mix point (or one set for all tracks) and the bar's tempo with its ramp.
        var tempo = BeatMix.TempoAt(project, bar, slot);
        var presenter = new MixTablePresenter(BeatMix.MixAt(project, trackIndex, bar, cellIndex), track.MidiProgram, track.Volume, track.Pan, tempo?.Tempo, project.Tempo,
            InstrumentCatalog.All.Select(e => (e.Program, e.Name, e.IsDrumKit)), tempo?.TransitionBeats ?? 0);
        var result = await DialogLauncher.Ask<MixTableWindow, MixTablePresenter.Result?>(owner, () => new MixTableWindow(presenter));
        if (result is null) return;
        Apply(document, trackIndex, bar, cellIndex, result);
        score.InvalidateVisual();
    }

    /// <summary>Writes the window's result to the beat (and its tempo to every track) as one undo step.</summary>
    private static void Apply(TabForge.Documents.IEditableDocument document, int trackIndex, int bar, int cellIndex, MixTablePresenter.Result result) =>
        DocumentEdits.Run(document, project =>
        {
            var measure = project.Tracks[trackIndex].Measures[bar];
            measure.Cells[cellIndex].Mix = result.Mix is { IsEmpty: false } mix ? mix : null;
            BeatMix.SetTempo(project, bar, BeatMix.SlotOf(measure, cellIndex),
                result.Tempo is int bpm ? new BeatTempo(bpm, result.Mix?.TransitionBeats ?? 0) : null);
            return true;
        });
}
