using System.Collections.Generic;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the document-level events a view listens to: timeline marks that survive an undo's song swap, the swap itself, and one Restored per undo/redo.
// Does not own: the undo engine (UndoController) or the edit pipeline's transaction rules (DocumentEdits).
// Tests: this class. Headless, in-memory, no engine.
public sealed class DocumentWatchTests
{
    private static SongDocument NewSong()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        song.MarkClean();
        return song;
    }

    private static void AddTrack(SongDocument song) =>
        DocumentEdits.Run(song, p => { p.Tracks.Add(NewTrackFactory.CreateTrack(p, TrackKind.Audio)); return true; });

    private static DocumentWatch Watch(SongDocument song, List<string> log, List<SongProject>? marks = null)
    {
        var watch = new DocumentWatch
        {
            Marked = p => { marks?.Add(p); log.Add("marked"); },
            ProjectChanged = (_, _) => log.Add("changed"),
            Restored = () => log.Add("restored"),
        };
        watch.Bind(song);
        return watch;
    }

    [Fact]
    public void ProjectSwapRaisesProjectChangedOnce()
    {
        var song = NewSong();
        var old = song.Project;
        SongProject? seenOld = null, seenNew = null;
        var count = 0;
        using var watch = new DocumentWatch { ProjectChanged = (o, n) => { count++; seenOld = o; seenNew = n; } };
        watch.Bind(song);
        var next = DemoSongFactory.Create();

        song.Project = next;
        song.Project = next;   // same instance: no second event

        Assert.Equal(1, count);
        Assert.Same(old, seenOld);
        Assert.Same(next, seenNew);
    }

    [Fact]
    public void DocumentTimelineMarkedKeepsFiringAfterUndoSwap()
    {
        var song = NewSong();
        AddTrack(song);
        var log = new List<string>();
        using var watch = Watch(song, log);
        DocumentEdits.Undo(song);
        log.Clear();

        song.Project.MarkTimelineChanged();

        Assert.Contains("marked", log);
    }

    [Fact]
    public void OldProjectMarksDoNotLeakAfterSwap()
    {
        var song = NewSong();
        var old = song.Project;
        var log = new List<string>();
        using var watch = Watch(song, log);
        song.Project = DemoSongFactory.Create();
        log.Clear();

        old.MarkTimelineChanged();

        Assert.Empty(log);
    }

    [Fact]
    public void UndoRaisesRestoredAfterFinalState()
    {
        var song = NewSong();
        AddTrack(song);
        var tracksAtRestore = -1;
        var count = 0;
        using var watch = new DocumentWatch { Restored = () => { count++; tracksAtRestore = song.Project.Tracks.Count; } };
        watch.Bind(song);
        var before = song.Project.Tracks.Count;

        DocumentEdits.Undo(song);

        Assert.Equal(1, count);
        Assert.Equal(before - 1, tracksAtRestore);
        Assert.False(song.IsDirty);
    }

    [Fact]
    public void RedoRaisesRestoredOnce()
    {
        var song = NewSong();
        AddTrack(song);
        DocumentEdits.Undo(song);
        var count = 0;
        using var watch = new DocumentWatch { Restored = () => count++ };
        watch.Bind(song);

        DocumentEdits.Redo(song);

        Assert.Equal(1, count);
    }

    [Fact]
    public void NothingToUndoRaisesNoEvent()
    {
        var song = NewSong();
        var log = new List<string>();
        using var watch = Watch(song, log);

        Assert.Null(DocumentEdits.Undo(song));
        Assert.Null(DocumentEdits.Redo(song));

        Assert.Empty(log);
    }

    [Fact]
    public void RebindToOtherDocumentMovesSubscription()
    {
        var first = NewSong();
        var second = NewSong();
        var log = new List<string>();
        using var watch = Watch(first, log);

        watch.Bind(second);
        first.Project.MarkTimelineChanged();
        first.NotifyRestored();
        Assert.Empty(log);

        second.Project.MarkTimelineChanged();
        second.NotifyRestored();
        Assert.Equal(new[] { "marked", "restored" }, log);
    }

    [Fact]
    public void DisposeStopsAll()
    {
        var song = NewSong();
        var log = new List<string>();
        var watch = Watch(song, log);

        watch.Dispose();
        song.Project.MarkTimelineChanged();
        song.Project = DemoSongFactory.Create();
        song.NotifyRestored();

        Assert.Empty(log);
    }
}
