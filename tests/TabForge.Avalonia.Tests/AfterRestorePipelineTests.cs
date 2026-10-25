using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the one after-restore pipeline (MainWindow AfterUndoRedo, bound to the shown document's Restored): it runs once for every undo path (the score's own key,
//   the Edit menu key, a direct DocumentEdits call as the mixer and FX windows make), for Redo, and not for a tab in the background.
//   Its visible effect here: a clip selection the restore invalidated is cleared (the engine sync runs in the same call).
// Does not own: the engine sync itself (AvaloniaPlaybackSessions), the arrangement's own re-read (ArrangementUndoRebindTests).
// Tests: this class.
public class AfterRestorePipelineTests
{
    private static (MainWindow Window, SongDocument Song, TimelineView Timeline) Open()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: song);
        window.Show();
        window.UpdateLayout();
        return (window, song, window.GetLogicalDescendants().OfType<ArrangementView>().First().Timeline);
    }

    private static void Edit(SongDocument song) => Assert.True(DocumentEdits.Run(song, project => { project.Tempo += 1; return true; }).Changed);

    // A selection naming a clip no track holds: only the pipeline clears it.
    private static void SelectStaleClip(SongDocument song, TimelineView timeline) =>
        timeline.SelectedClip = new TimelineClipSelection(song.Project.Tracks[0], new AudioClip { File = "gone.wav", Name = "gone" });

    private static void Finish(MainWindow window, SongDocument song)
    {
        song.MarkClean();
        window.Close();
    }

    [AvaloniaFact]
    public void ScoreKeyUndoSyncsEngineAndReloadsRows()
    {
        var (window, song, timeline) = Open();
        Edit(song);
        SelectStaleClip(song, timeline);
        var score = window.GetLogicalDescendants().OfType<ScorePreviewView>().First();
        score.Focus();
        score.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control, Source = score });
        Assert.Null(timeline.SelectedClip);
        Assert.Same(song.Project, timeline.Song);
        Finish(window, song);
    }

    [AvaloniaFact]
    public void MenuUndoRunsPipelineExactlyOnce()
    {
        var (window, song, timeline) = Open();
        Edit(song);
        var restored = 0;
        song.Restored += () => restored++;
        SelectStaleClip(song, timeline);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control, Source = window });
        Assert.Equal(1, restored);
        Assert.Null(timeline.SelectedClip);
        Finish(window, song);
    }

    [AvaloniaFact]
    public void RedoRunsPipeline()
    {
        var (window, song, timeline) = Open();
        Edit(song);
        Assert.NotNull(DocumentEdits.Undo(song));
        SelectStaleClip(song, timeline);
        Assert.NotNull(DocumentEdits.Redo(song));
        Assert.Null(timeline.SelectedClip);
        Finish(window, song);
    }

    [AvaloniaFact]
    public void BackgroundTabUndoDoesNotRunActiveWindowPipeline()
    {
        var (window, first, timeline) = Open();
        Edit(first);
        window.Songs.New();
        var shown = window.Songs.Active;
        Assert.NotSame(first, shown);
        timeline = window.GetLogicalDescendants().OfType<ArrangementView>().First().Timeline;
        SelectStaleClip(shown, timeline);
        Assert.NotNull(DocumentEdits.Undo(first));
        Assert.NotNull(timeline.SelectedClip);
        shown.MarkClean();
        Finish(window, first);
    }
}
