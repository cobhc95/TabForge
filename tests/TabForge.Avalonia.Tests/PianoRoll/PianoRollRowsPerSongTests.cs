using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: SHOW ROWS as a choice of each song: All pitches for a song that never chose one (new, imported or an old file, whatever another song chose), the choice
//   stored in the song (SongProject.PianoRollRows, written only when it is not All pitches, one undo step), restored when the song's tab is shown, and never
//   changed for another song. The Reset all settings path is covered by PreferencesWindowResetTests.
// Does not own: the scale panel's sections (ScalePanelHostTests) or the row rules themselves (PianoRollScaleDetectTests, PianoRollMoreEditTests).
// Tests: this class.
public sealed class PianoRollRowsPerSongTests
{
    /// <summary>A piano roll window whose host shows the song <c>show</c> was last given (the song tab switch).</summary>
    private static (Window Window, PianoRollView View, Action<SongDocument> Show) Roll()
    {
        SongDocument? current = null;
        var editing = new EditingSettings();
        var view = new PianoRollView(new PianoRollHost { Document = () => current, Editing = () => editing });
        var window = new Window { Width = 2000, Height = 700, Content = view };
        window.Show();
        void Show(SongDocument document)
        {
            current = document;
            view.Show(document, 0, newVisit: true);
            window.UpdateLayout();
        }
        return (window, view, Show);
    }

    [AvaloniaFact]
    public void ANewSongShowsAllPitchesAndStoresNothing()
    {
        var (window, view, show) = Roll();
        var song = PianoRollSongs.Midi();
        try
        {
            show(song);
            Assert.Equal(PianoRollScale.FoldNone, PianoRollScale.RowsOf(song.Project));
            Assert.Null(song.Project.PianoRollRows);
            Assert.Equal(128, view.Surface.Rows.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PickingScalePitchesInOneSongLeavesAnotherSongOnAllPitches()
    {
        var (window, view, show) = Roll();
        var a = PianoRollSongs.Midi();
        var b = PianoRollSongs.Midi();
        try
        {
            show(a);
            view.ScaleHost.Pick(ScaleReadoutEngine.Parse("A Natural Minor")!);
            view.ScaleHost.ChooseRows(PianoRollScale.FoldScale);
            Assert.Equal(PianoRollScale.FoldScale, PianoRollScale.RowsOf(a.Project));
            Assert.True(view.Surface.Rows.Count < 128, "the scale pitches only: fewer rows");
            show(b);
            Assert.Null(b.Project.PianoRollRows);
            Assert.Equal(PianoRollScale.FoldNone, PianoRollScale.RowsOf(b.Project));
            Assert.Equal(128, view.Surface.Rows.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SwitchingSongTabsSwapsTheRows()
    {
        var (window, view, show) = Roll();
        var a = PianoRollSongs.Midi();
        var b = PianoRollSongs.Midi();
        try
        {
            show(a);
            view.ScaleHost.ChooseRows(PianoRollScale.FoldUsed);
            var usedRows = view.Surface.Rows.Count;
            Assert.True(usedRows < 128, "the pitches used by the notes: fewer rows");
            show(b);
            Assert.Equal(128, view.Surface.Rows.Count);
            show(a);
            Assert.Equal(usedRows, view.Surface.Rows.Count);
            show(b);
            Assert.Equal(128, view.Surface.Rows.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheChoiceRoundTripsThroughTheFileAndAllPitchesWritesNoField()
    {
        var song = PianoRollSongs.Midi();
        var folder = Path.Combine(Path.GetTempPath(), "tf-rows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            Assert.True(PianoRollScale.StoreRows(song.Project, PianoRollScale.FoldUsed));
            ProjectService.Save(path, song.Project);
            Assert.Equal(PianoRollScale.FoldUsed, PianoRollScale.RowsOf(ProjectService.Load(path)));

            Assert.True(PianoRollScale.StoreRows(song.Project, PianoRollScale.FoldNone));
            ProjectService.Save(path, song.Project);
            Assert.Equal(PianoRollScale.FoldNone, PianoRollScale.RowsOf(ProjectService.Load(path)));
            using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            var json = JsonNode.Parse(gzip)!.AsObject();
            Assert.False(json.ContainsKey(nameof(SongProject.PianoRollRows)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void AnOldFileWithoutTheFieldLoadsAllPitches()
    {
        var song = PianoRollSongs.Midi();
        var folder = Path.Combine(Path.GetTempPath(), "tf-rows-old-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            Assert.True(PianoRollScale.StoreRows(song.Project, PianoRollScale.FoldScale));
            ProjectService.Save(path, song.Project);
            using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            var json = JsonNode.Parse(gzip)!.AsObject();
            Assert.True(json.Remove(nameof(SongProject.PianoRollRows)));
            var old = Path.Combine(folder, "old.tforge");
            File.WriteAllText(old, json.ToJsonString());
            Assert.Null(ProjectService.Load(old).PianoRollRows);
            Assert.Equal(PianoRollScale.FoldNone, PianoRollScale.RowsOf(ProjectService.Load(old)));
        }
        finally { Directory.Delete(folder, true); }
    }
}
