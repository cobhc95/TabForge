using TabForge.Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the bar menu's Copy bar rows: each row maps to its command id (ContextMenuRows) and that id is one the window handles (MainWindowTimelineBars.Commands).
// Does not own: the copy itself (TimelineClips in Core) or the menu's contents (TimelineMenus in Core).
// Tests: this file.
public sealed class TimelineBarCopyMenuTests
{
    [Fact]
    public void CopyBarRowsRunTheirHandledCommands()
    {
        Assert.Equal(ContextMenuRows.BarCopyId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.CopyBar }));
        Assert.Equal(ContextMenuRows.BarCopyAllTracksId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.CopyBarAllTracks }));
        Assert.Contains(ContextMenuRows.BarCopyId, MainWindowTimelineBars.Commands);
        Assert.Contains(ContextMenuRows.BarCopyAllTracksId, MainWindowTimelineBars.Commands);
    }

    [Fact]
    public void PasteBarRowsRunTheirHandledCommands()
    {
        Assert.Equal(ContextMenuRows.BarPasteId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.PasteBar }));
        Assert.Equal(ContextMenuRows.BarPasteAllTracksId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.PasteBarAllTracks }));
        Assert.Contains(ContextMenuRows.BarPasteId, MainWindowTimelineBars.Commands);
        Assert.Contains(ContextMenuRows.BarPasteAllTracksId, MainWindowTimelineBars.Commands);
    }

    [Fact]
    public void DeleteBarRowsRunTheirHandledCommands()
    {
        Assert.Equal(ContextMenuRows.BarDeleteId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.DeleteBar }));
        Assert.Equal(ContextMenuRows.BarDeleteAllTracksId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.DeleteBarAllTracks }));
        Assert.Contains(ContextMenuRows.BarDeleteId, MainWindowTimelineBars.Commands);
        Assert.Contains(ContextMenuRows.BarDeleteAllTracksId, MainWindowTimelineBars.Commands);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteBarRunsTheRememberedAnswerWithoutAsking()
    {
        var project = new SongProject();
        var track = new TrackModel();
        for (var bar = 0; bar < 3; bar++) track.Measures.Add(new MeasureModel { Number = bar + 1 });
        project.Tracks.Add(track);
        var document = new TabForge.Documents.SongDocument(() => null) { Project = project };
        var menus = new ContextMenuHost { ClickedBar = 1, ClickedTrack = 0 };
        var editing = new EditingSettings { BarRangeDelete = BarRangePromptText.Store(BarRangeAction.Remove, allTracks: true) };
        var asked = false;

        await MainWindowTimelineBars.DeleteBar(document, menus, allTracks: true, _ => { }, (_, _, _) => { asked = true; return System.Threading.Tasks.Task.FromResult<BarRangeAnswer?>(null); },
            () => editing, () => { });

        Assert.False(asked);
        Assert.Equal(2, track.Measures.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteBarStoresTheAnswerWhenRememberIsTicked()
    {
        var project = new SongProject();
        var track = new TrackModel();
        for (var bar = 0; bar < 3; bar++) track.Measures.Add(new MeasureModel { Number = bar + 1 });
        project.Tracks.Add(track);
        var document = new TabForge.Documents.SongDocument(() => null) { Project = project };
        var menus = new ContextMenuHost { ClickedBar = 0, ClickedTrack = 0 };
        var editing = new EditingSettings();
        var saved = 0;

        await MainWindowTimelineBars.DeleteBar(document, menus, allTracks: false, _ => { },
            (_, _, _) => System.Threading.Tasks.Task.FromResult<BarRangeAnswer?>(new BarRangeAnswer(BarRangeAction.Remove, true, Remember: true)), () => editing, () => saved++);

        Assert.Equal(1, saved);
        Assert.Equal(BarRangePromptText.Store(BarRangeAction.Remove, true), editing.BarRangeDelete);
        Assert.Equal(nameof(BarRangeAction.Remove), editing.BarRangeLastChoice);
    }
}
