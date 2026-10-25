using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: changing a track to the other family (a guitar to a drum kit) through the Track properties Change instrument button: the picker's Select
/// converts the track at once as one undo step (kind drums, strings on the drum lines, notes kept on those lines) and Undo brings the guitar back.
/// Does not own: the family rules (TrackSetup), the picker run (InstrumentPickerRunTests) or the picker window (InstrumentPickerWindow).
/// Tests: TabForge.Avalonia.Tests.TrackFamilyChangeTests.
/// </summary>
public sealed class TrackFamilyChangeTests
{
    [AvaloniaFact]
    public void DrumKitSelectConvertsTheTrackInOneUndoStepAndKeepsItsNotes()
    {
        var project = DemoSongFactory.Create();
        var track = project.Tracks[0];
        var notesBefore = NoteCount(track);
        var strings = track.StringTunings.Count;
        Assert.True(notesBefore > 0 && !TrackSetup.IsDrumFamily(track), "the demo's first track is a pitched track with notes");
        var doc = new SongDocument(() => null) { Project = project };
        var undo = doc.Undo.UndoCount;
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();

        InstrumentPickerRunTests.PickThroughTheChooser(window, InstrumentCatalog.All.First(entry => entry.IsDrumKit).Name);
        var ask = window.OwnedWindows.OfType<AvaloniaMessageBox>().Single();   // a pitched track with notes: the conversion asks first
        Assert.Contains("remaps the notes to drum sounds", DialogTestKit.Named<TextBlock>(ask, "MessageText").Text);
        DialogTestKit.Click(ask.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Yes"));
        DialogTestKit.RunQueuedUiWork();

        Assert.True(track.Kind == TrackKind.Drums, "kind " + track.Kind + " instrument " + track.InstrumentName + " undo " + (doc.Undo.UndoCount - undo));
        Assert.Equal(5, track.StringTunings.Count);
        Assert.Equal(notesBefore, NoteCount(track));
        Assert.All(track.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes), note => Assert.True(note.StringIndex < track.StringTunings.Count));
        Assert.Equal(undo + 1, doc.Undo.UndoCount);
        Assert.True(DialogTestKit.Named<Border>(window, "DrumCard").IsVisible);

        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal(strings, doc.Project.Tracks[0].StringTunings.Count);
        Assert.NotEqual(TrackKind.Drums, doc.Project.Tracks[0].Kind);
        window.Close();
    }

    private static int NoteCount(TrackModel track) => track.Measures.Sum(m => m.Cells.Concat(m.Voice2Cells).Sum(cell => cell.Notes.Count));
}
