using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the rebuilt Track properties window: the + Add string button (replaces Advanced tuning), the capo chips and string ticks,
// the capo board drag (previews only, one commit on release, nothing in the song until OK) and where the cards sit.
// Does not own: the old string/capo behaviour that did not change (TrackPropertiesWindowTests, TrackPropertiesJobBTests, PartialCapoWindowTests).
public sealed class TrackPropertiesRedesignTests
{
    private static TrackModel Guitar()
    {
        var track = new TrackModel
        {
            Name = "Redesign guitar", Kind = TrackKind.Guitar, InstrumentName = "Electric Guitar",
            MidiChannel = 0, MidiProgram = 29, MidiOutputDeviceId = -1, NumberOfFrets = 22,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1),
        };
        track.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 0, MidiValue = track.PitchOf(0, 0) });
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

    private static bool Has(Window window, string name) => window.GetLogicalDescendants().OfType<Control>().Any(c => c.Name == name);

    private static void Discard(TrackPropertiesWindow window)
    {
        DialogTestKit.Click(Named<Button>(window, "CancelButton"));
        Dispatcher.UIThread.RunJobs();
        if (window.IsVisible) DialogTestKit.Click(Named<Button>(window.OwnedWindows.OfType<SavePromptWindow>().Single(), "YesButton"));
        Dispatcher.UIThread.RunJobs();
    }

    private static int RowNote(TrackPropertiesWindow window, int row)
    {
        Assert.True(TrackPropertiesPresenter.TryParseNote(Named<TextBox>(window, $"NoteBox{row}").Text ?? "", out var midi));
        return midi;
    }

    [AvaloniaFact]
    public void AddStringAppendsALowerStringSyncsTheCountAndStopsAtTheMost()
    {
        var window = Open(Guitar());
        try
        {
            Assert.False(Has(window, "AdvancedTuningButton"));
            var add = Named<Button>(window, "AddStringButton");
            DialogTestKit.Click(add);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(7, Named<StackPanel>(window, "StringRows").Children.Count);
            Assert.Equal("7 strings", Named<ComboBox>(window, "StringCountBox").SelectedItem);
            Assert.True(RowNote(window, 6) < RowNote(window, 5));
            Assert.Equal(35, RowNote(window, 6));
            Named<Control>(window, "SpeakerToggle6");   // the new row has the same speaker as the others

            for (var n = 7; n < TrackPropertiesPresenter.MostStrings; n++) DialogTestKit.Click(add);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TrackPropertiesPresenter.MostStrings, Named<StackPanel>(window, "StringRows").Children.Count);
            Assert.False(add.IsEnabled);
            DialogTestKit.Click(add);
            Assert.Equal(TrackPropertiesPresenter.MostStrings, Named<StackPanel>(window, "StringRows").Children.Count);
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void CapoCapsulesChoosePlacementWhileBoardClicksSelectTheClippedBar()
    {
        var track = Guitar();
        TrackSetup.SetCapoAndPartials(track, 2, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } }, track.StringTunings.Count);
        var window = Open(track);
        try
        {
            var board = Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard");
            Assert.Equal(2, board.ShownFret);
            Assert.All(board.Covered, covered => Assert.True(covered));
            Assert.False(Has(window, "CapoStringTicks") || Has(window, "CapoSettings"));
            CapoPlacementTests.ClickFret(window, 5);
            Assert.Equal(5, board.ShownFret);
            Assert.Equal(new[] { true, true, true, false, false, false }, board.Covered);
            CapoPlacementTests.ClickFret(window, 2);
            Assert.Equal(2, board.ShownFret);
            Assert.All(board.Covered, covered => Assert.True(covered));

            CapoPlacementTests.PlacePartial(window, 7);
            Assert.Equal(2, window.PartialCapos.Count);
            Assert.Equal(7, board.ShownFret);
            Assert.Equal(new[] { false, true, true, true, false, false }, board.Covered);
            DialogTestKit.Click(Named<Button>(window, "RemoveSelectedCapoButton"));
            Assert.Single(window.PartialCapos);
            Assert.Equal(2, board.ShownFret);
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void ABoardDragPreviewsOnlyThenCommitsOnceOnReleaseAndOkIsOneUndoStep()
    {
        var track = Guitar();
        TrackSetup.SetCapoAndPartials(track, 2, null, track.StringTunings.Count);
        var doc = new ScorePreviewEditor.PreviewDocument(new SongProject { Tracks = new List<TrackModel> { track } });
        var window = Open(track, doc);
        try
        {
            var board = Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard");
            var commits = 0;
            board.CommitRequested += _ => { commits++; return true; };
            Assert.True(board.Bounds.Width > 0);

            var y = board.Bounds.Height / 2;
            Assert.True(board.BeginDrag(new Point(board.FretX(2), y)));
            foreach (var fret in new[] { 3, 5, 7, 4 }) board.Drag(new Point(board.FretX(fret), y));
            Assert.Equal(4, board.ShownFret);
            Assert.Equal(2, window.WorkingEffectiveCapo(0));
            Assert.Equal(0, commits);
            Assert.Equal(2, track.Capo);
            Assert.Equal(0, doc.Undo.UndoCount);

            board.EndDrag();
            Assert.Equal(1, commits);
            Assert.Equal(4, window.WorkingEffectiveCapo(0));
            Assert.Equal(4, board.ShownFret);
            Assert.Equal(2, track.Capo);       // still nothing in the song until OK
            Assert.Equal(0, doc.Undo.UndoCount);

            board.EndDrag();
            Assert.Equal(1, commits);

            CapoPlacementTests.ClickFret(window, 6);
            Assert.Equal(6, board.ShownFret);

            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Applied);
            Assert.Equal(6, doc.Project.Tracks[0].Capo);
            Assert.Equal(1, doc.Undo.UndoCount);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public void TheCardsSitWhereTheDrawingPutsThem()
    {
        var window = Open(Guitar());
        try
        {
            Assert.False(Has(window, "AdvancedTuningButton") || Has(window, "AdvancedTuningRow"));
            Assert.True(Has(window, "AddStringButton"));
            var mixer = Named<Border>(window, "MixerCard");
            var midi = Named<Border>(window, "MidiCard");
            var name = Named<TextBox>(window, "NameBox");
            Assert.True(mixer.TranslatePoint(default, window)!.Value.Y > name.TranslatePoint(default, window)!.Value.Y);   // mixer under the details
            Assert.True(mixer.TranslatePoint(default, window)!.Value.X < midi.TranslatePoint(default, window)!.Value.X);    // MIDI is in the right column
            Assert.True(midi.TranslatePoint(default, window)!.Value.Y < Named<Border>(window, "TuningCard").TranslatePoint(default, window)!.Value.Y);
            foreach (var box in new[] { "ProgramBox", "BankMsbBox", "BankLsbBox", "ChannelBox" })
                Assert.Contains(midi, Named<TextBox>(window, box).GetLogicalAncestors());
            Assert.DoesNotContain(midi, Named<Button>(window, "InstrumentChooserButton").GetLogicalAncestors());
            Assert.Contains(mixer, Named<Button>(window, "FxChainButton").GetLogicalAncestors());
            Assert.True(Named<Border>(window, "CapoCard").TranslatePoint(default, window)!.Value.Y > Named<Border>(window, "TuningCard").TranslatePoint(default, window)!.Value.Y);
        }
        finally { Discard(window); }
    }
}
