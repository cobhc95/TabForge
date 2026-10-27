using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track list fit runs when a track or a group row is added or removed, and not on edits that leave the row
/// count alone.
/// Does not own: the fit maths (DockSizeRules), the fit itself (MainWindowTrackListFit), the master lane fit (MasterLaneFitTests).
/// Tests: TabForge.Avalonia.Tests.TrackListCountFitTests.
/// </summary>
public class TrackListCountFitTests
{
    [AvaloniaFact]
    public void AddingAndRemovingATrackRunsTheTrackListFitButARenameDoesNot()
    {
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var panes = new MainWindowDocumentPanes(tabs, new ScorePreviewView());
        var arrangement = panes.NewArrangement();
        var window = new Window { Width = 1000, Height = 400, Content = arrangement };
        window.Show();
        var fits = 0;
        arrangement.TrackRowsCountChanged += () => fits++;
        var song = tabs.Active;

        DocumentEdits.Run(song, project => { project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio)); return true; });
        Assert.Equal(1, fits);

        DocumentEdits.Run(song, project => { project.Tracks.RemoveAt(project.Tracks.Count - 1); return true; });
        Assert.Equal(2, fits);

        DocumentEdits.Run(song, project => { project.Tracks[0].Name = "Renamed"; return true; });
        Assert.Equal(2, fits);
        window.Close();
    }
}
