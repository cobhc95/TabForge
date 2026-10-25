using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the master track staying hidden until the track list's right-click row shows it: a fresh pane hides both the
/// timeline lane and the header's Master row, the View.MasterTrack row shows both and saves the Timeline setting, and a
/// later settings Apply or timeline reload keeps the saved state.
/// Does not own: the lane's drawing (MasterLaneStrip, MasterLaneTests), the track list fit (MasterLaneFitTests) or the
/// other settings rows (TimelineSettingsBindingTests).
/// Tests: TimelineView.MasterLaneShown, ArrangementView.ShowMasterLane, TimelineSettingsBinding, MainWindowTrackListRows.
/// </summary>
public class MasterTrackDefaultHiddenTests
{
    private static (MainWindowTabs Tabs, MainWindowDocumentPanes Panes) NewShell()
    {
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        return (tabs, new MainWindowDocumentPanes(tabs, new ScorePreviewView()));
    }

    /// <summary>The centre of the Master row's volume cell: inside the lane once shown, above the track rows when hidden.</summary>
    private static Point MasterBandPoint(TrackHeaderColumn header)
    {
        var volume = header.CellRect(TrackHeaderColumn.MasterRow, TrackRowCells.Cell.Volume);
        return volume.Center;
    }

    [AvaloniaFact]
    public void NewPanesStartWithTheMasterLaneHidden()
    {
        var (_, panes) = NewShell();
        var settings = new AppSettings();
        Assert.False(settings.Timeline.ShowMasterTrack);
        var binding = new TimelineSettingsBinding(settings);
        panes.ArrangementCreated += binding.Attach;
        var arrangement = panes.NewArrangement();

        Assert.False(arrangement.Timeline.MasterLaneShown);
        Assert.Equal(0, arrangement.Timeline.MasterLaneHeight);
        Assert.NotEqual(TrackHeaderColumn.MasterRow, arrangement.Header.CellAt(MasterBandPoint(arrangement.Header))?.Row);
    }

    [AvaloniaFact]
    public void ToggleShowsBothSidesAndSavesTheSetting()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-master-track-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            var (tabs, panes) = NewShell();
            var arrangement = panes.NewArrangement();
            var owner = new Window();
            var commands = new MainWindowCommands(owner, () => { });
            MainWindowTrackListRows.Register(commands, tabs, panes, settings, owner, _ => { }, () => { });
            var binding = new TimelineSettingsBinding(settings.Settings);
            binding.Attach(arrangement);

            commands.Run(TrackRowMenus.MasterLaneCommandId);
            Assert.True(arrangement.Timeline.MasterLaneShown);
            Assert.Equal(MasterLaneRules.Height(MasterLaneRules.Rows(true, false), arrangement.Timeline.RowHeight), arrangement.Timeline.MasterLaneHeight);
            Assert.Equal(TrackHeaderColumn.MasterRow, arrangement.Header.CellAt(MasterBandPoint(arrangement.Header))?.Row);
            Assert.True(settings.Settings.Timeline.ShowMasterTrack);

            arrangement.Timeline.Reload(arrangement.Timeline.Song);
            arrangement.Header.Reload(arrangement.Timeline.Song);
            Assert.True(arrangement.Timeline.MasterLaneShown);

            settings.Settings.Timeline.ShowMasterTrack = false;
            binding.Apply(settings.Settings);
            Assert.False(arrangement.Timeline.MasterLaneShown);
            Assert.Equal(0, arrangement.Timeline.MasterLaneHeight);
            Assert.NotEqual(TrackHeaderColumn.MasterRow, arrangement.Header.CellAt(MasterBandPoint(arrangement.Header))?.Row);

            commands.Run(TrackRowMenus.MasterLaneCommandId);
            Assert.True(arrangement.Timeline.MasterLaneShown);
            Assert.True(settings.Settings.Timeline.ShowMasterTrack);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
