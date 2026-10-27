using System;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Avalonia;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the track multi-selection surviving removal and undo (rows, anchor), the copied tracks' take groups, the long delete question, and the settings store's longest wait for a save.
// Does not own: the pure selection rules (TrackGroupEditsTests) or the pointer wiring.
// Tests: this file.
public sealed class TrackSelectionFollowUpTests
{
    private static SongDocument Document(int count = 5)
    {
        var song = DemoSongFactory.Create();
        while (song.Tracks.Count < count) song.Tracks.Add(NewTrackFactory.CreateTrack(song, TrackKind.Audio));
        song.Tracks.RemoveRange(count, song.Tracks.Count - count);
        return new SongDocument(() => null) { Project = song };
    }

    [Fact]
    public void RemovingTracksDropsThemFromTheSelectionAndUndoBringsRowsAndAnchorBack()
    {
        var doc = Document();
        var tracks = doc.Project.Tracks;
        doc.Selection.Tracks.Click(tracks, 3, TrackPick.Plain);
        doc.Selection.Tracks.Click(tracks, 1, TrackPick.Range);   // rows 1..3, anchor track 3
        var anchor = tracks[3].Id;
        Assert.Equal(anchor, doc.Selection.Tracks.Anchor);

        Assert.True(MainWindowTrackMenu.RemoveTracks(doc, new[] { 2, 3 }).Changed);
        Assert.Equal(new[] { tracks[1].Id }, doc.Selection.Rows);
        Assert.Null(doc.Selection.Tracks.Anchor);

        DocumentEdits.Undo(doc);
        Assert.Equal(3, doc.Selection.Rows.Count);
        Assert.Equal(anchor, doc.Selection.Tracks.Anchor);
    }

    [Fact]
    public void ACopiedTrackLeavesTheOriginalsTakeGroup()
    {
        var doc = Document(2);
        var clip = new AudioClip { Id = "a", TakeGroupId = "group", TakeGroupKind = TakeGroupKind.Loop, TakeGroupPass = 2 };
        doc.Project.Tracks[0].AudioClips.Add(clip);
        var clipboard = new ClipboardService(null);
        clipboard.CopyTracks(new[] { doc.Project.Tracks[0] });
        var copy = clipboard.TryGetTracks().Single().AudioClips.Single();
        Assert.NotEqual("a", copy.Id);
        Assert.Null(copy.TakeGroupId);
        Assert.Equal((default(TakeGroupKind), 0), (copy.TakeGroupKind, copy.TakeGroupPass));
    }

    [Fact]
    public void ALongDeleteQuestionNamesFiveTracksAndCountsTheRest()
    {
        var names = Enumerable.Range(1, 8).Select(i => $"T{i}").ToList();
        var text = PromptTextsPresenter.DeleteTracks(names, "Ctrl+Z").Message;
        Assert.Contains("T5 and 3 more", text);
        Assert.DoesNotContain("T6", text);
        Assert.Contains("T1, T2", PromptTextsPresenter.DeleteTracks(names.Take(3).ToList(), "Ctrl+Z").Message);
    }

    [Fact]
    public void ASettingsChangeIsWrittenEvenWhileMoreChangesKeepArriving()
    {
        var dir = Directory.CreateTempSubdirectory("store-ceiling");
        try
        {
            using var store = AppSettingsStore.Open(Path.Combine(dir.FullName, "settings.json"), TimeSpan.FromMilliseconds(300));
            store.MaxPendingDelay = TimeSpan.FromMilliseconds(500);
            var written = false;
            for (var i = 0; i < 40 && !written; i++)
            {
                store.MarkChanged(null, notifyObservers: false);   // a resize step: the 300 ms debounce restarts every 50 ms and would never fire
                Thread.Sleep(50);
                written = !store.HasPendingSave;
            }
            Assert.True(written);
        }
        finally { dir.Delete(true); }
    }
}
