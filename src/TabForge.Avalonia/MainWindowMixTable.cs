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
//   and the bar's tempo as one undo step, as WPF does. Clear removes the mix point.
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
        var cell = measure.Cells[cellIndex];
        var presenter = new MixTablePresenter(cell.Mix, track.MidiProgram, track.Volume, track.Pan, measure.TempoChange, project.Tempo,
            InstrumentCatalog.All.Select(e => (e.Program, e.Name, e.IsDrumKit)));
        var result = await DialogLauncher.Ask<MixTableWindow, MixTablePresenter.Result?>(owner, () => new MixTableWindow(presenter));
        if (result is null) return;
        Apply(document, trackIndex, bar, cellIndex, result);
        score.InvalidateVisual();
    }

    /// <summary>Writes the window's result to the beat (and the bar's tempo to every track) as one undo step.</summary>
    private static void Apply(TabForge.Documents.IEditableDocument document, int trackIndex, int bar, int cellIndex, MixTablePresenter.Result result) =>
        DocumentEdits.Run(document, project =>
        {
            var measure = project.Tracks[trackIndex].Measures[bar];
            measure.Cells[cellIndex].Mix = result.Mix is { IsEmpty: false } mix ? mix : null;
            if (result.Mix is { IsEmpty: true } && result.Tempo is null) measure.TempoChange = null;
            if (result.Tempo is int tempo)
                foreach (var track in project.Tracks) if (bar < track.Measures.Count) track.Measures[bar].TempoChange = tempo;
            return true;
        });
}
