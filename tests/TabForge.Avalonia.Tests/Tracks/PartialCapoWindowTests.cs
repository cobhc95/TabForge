using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Track properties partial-capo card as a real model edit: loading the track's partials, OK writing them with the
// full capo in one undo step, Cancel leaving the track alone, and a string-count change moving/dropping the string flags.
// Does not own: the model rules (PartialCapoTests), the fretboard drawing (InstrumentPanelViewTests) or the import.
public sealed class PartialCapoWindowTests
{
    private static TrackModel Guitar()
    {
        var track = new TrackModel
        {
            Name = "Capo guitar", Kind = TrackKind.Guitar, InstrumentName = "Electric Guitar",
            MidiChannel = 0, MidiProgram = 29, MidiOutputDeviceId = -1, NumberOfFrets = 22,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1),
        };
        for (var s = 0; s < track.StringTunings.Count; s++)
            track.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = s, Fret = 0, MidiValue = track.PitchOf(s, 0) });
        return track;
    }

    private static TrackPropertiesWindow Open(TrackModel track, ScorePreviewEditor.PreviewDocument? document = null)
    {
        var window = new TrackPropertiesWindow(track, document);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Discard(TrackPropertiesWindow window)
    {
        DialogTestKit.Click(Named<Button>(window, "CancelButton"));
        Dispatcher.UIThread.RunJobs();
        if (window.IsVisible) DialogTestKit.Click(Named<Button>(window.OwnedWindows.OfType<SavePromptWindow>().Single(), "YesButton"));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void OpeningShowsTheTracksPartialCapos()
    {
        var track = Guitar();
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000101 } });
        var window = Open(track);
        try
        {
            Assert.Single(window.PartialCapos);
            Assert.Equal(5, window.PartialCapos[0].Fret);
            Assert.True(window.PartialCapos[0].Strings[0]);
            Assert.False(window.PartialCapos[0].Strings[1]);
            Assert.True(window.PartialCapos[0].Strings[2]);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Control>(), c => c.Name == "CapoStringTicks");
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void OkWritesThePartialAndRepitchesItsStringsAsOneUndoStep()
    {
        var track = Guitar();
        var doc = new ScorePreviewEditor.PreviewDocument(new SongProject { Tracks = new List<TrackModel> { track } });
        var window = Open(track, doc);
        try
        {
            CapoPlacementTests.PlacePartial(window, 1);
            // Exercise the working-copy persistence seam for a saved custom mask (the card no longer has string ticks).
            for (var s = 0; s < 6; s++) window.PartialCapos[0].Strings[s] = s == 0;
            window.SyncCapoCard();
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Applied);
            var written = doc.Project.Tracks[0];
            Assert.Equal(1, Assert.Single(written.PartialCapos!).Fret);
            Assert.Equal(0b000001, written.PartialCapos![0].StringMask);
            Assert.Equal(65, written.Measures[0].Cells[0].Notes[0].MidiValue);
            Assert.Equal(40, written.Measures[0].Cells[0].Notes[5].MidiValue);
            Assert.Equal(1, doc.Undo.UndoCount);

            Assert.NotNull(DocumentEdits.Undo(doc));
            Assert.Null(doc.Project.Tracks[0].PartialCapos);
            Assert.Equal(64, doc.Project.Tracks[0].Measures[0].Cells[0].Notes[0].MidiValue);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public void CancelLeavesTheTracksPartialCaposAlone()
    {
        var track = Guitar();
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } });
        var soundingBefore = track.Measures[0].Cells[0].Notes[0].MidiValue;
        var window = Open(track);
        try
        {
            var board = Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard");
            Assert.True(board.BeginDrag(new global::Avalonia.Point(board.FretX(5), board.StringY(1))));
            board.Drag(new global::Avalonia.Point(board.FretX(6), board.Bounds.Height / 2));
            board.EndDrag();
            DialogTestKit.Click(Named<Button>(window, "RemoveSelectedCapoButton"));
            Discard(window);
            var partial = Assert.Single(track.PartialCapos!);
            Assert.Equal(5, partial.Fret);
            Assert.Equal(0b000111, partial.StringMask);
            Assert.Equal(soundingBefore, track.Measures[0].Cells[0].Notes[0].MidiValue);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public void AddingAHighStringMovesTheFlagsAndRemovingItBringsThemBack()
    {
        var track = Guitar();
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b100000 } });
        var window = Open(track);
        try
        {
            Assert.True(window.PartialCapos[0].Strings[5]);
            // v7 (remapped): + Add string adds a LOW string (there is no add-high), so the flag keeps its slot 5 and the new slot 6 is unticked;
            // the string-count box takes the added string away again.
            DialogTestKit.Click(Named<Button>(window, "AddStringButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(7, window.PartialCapos[0].Strings.Count);
            Assert.True(window.PartialCapos[0].Strings[5]);
            Assert.False(window.PartialCapos[0].Strings[6]);

            var count = Named<ComboBox>(window, "StringCountBox");
            count.SelectedIndex -= 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(6, window.PartialCapos[0].Strings.Count);
            Assert.True(window.PartialCapos[0].Strings[5]);
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void APartialOnANewHighStringSurvivesOk()
    {
        var track = Guitar();
        var doc = new ScorePreviewEditor.PreviewDocument(new SongProject { Tracks = new List<TrackModel> { track } });
        var window = Open(track, doc);
        try
        {
            // Two low strings, then a working custom partial on the newest string.
            DialogTestKit.Click(Named<Button>(window, "AddStringButton"));
            DialogTestKit.Click(Named<Button>(window, "AddStringButton"));
            Dispatcher.UIThread.RunJobs();
            CapoPlacementTests.PlacePartial(window, 3);
            Assert.Equal(8, window.PartialCapos[0].Strings.Count);
            for (var s = 0; s < 8; s++) window.PartialCapos[0].Strings[s] = s == 7;
            window.SyncCapoCard();
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Applied);
            var written = doc.Project.Tracks[0];
            Assert.Equal(8, written.StringTunings.Count);
            var partial = Assert.Single(written.PartialCapos!);
            Assert.Equal(3, partial.Fret);
            Assert.Equal(0b10000000, partial.StringMask);
        }
        finally { if (window.IsVisible) window.Close(); }
    }
}
