using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the track properties window (structure, presenter texts, the name rule, the string tools, OK validation,
/// the discard question, dark and light PNGs, contrast).
/// Does not own: the window, TrackPropertiesPresenter or the palette. The track is the demo song's first track, as the preview opens it.
/// Tests: this class.
/// </summary>
public class TrackPropertiesWindowTests
{
    private static TrackModel DemoTrack() => DemoSongFactory.Create().Tracks[0];

    private static TrackPropertiesWindow Open(TrackModel track)
    {
        var window = new TrackPropertiesWindow(track);
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void TitleSizeFieldsAndFooterMatchWpf()
    {
        var track = DemoTrack();
        var window = Open(track);

        Assert.Equal(TrackPropertiesPresenter.Title(track.Name, adding: false), window.Title);
        Assert.Equal((800d, 640d, 0d, SizeToContent.Height), (window.Width, window.MinWidth, window.MinHeight, window.SizeToContent));
        // Order: details (name, played by, colour, notes), instrument (program, bank MSB/LSB, channel), tuning (frets, capo).
        var fields = window.GetLogicalDescendants().OfType<TextBox>().Where(b => b.Name?.StartsWith("NoteBox") != true).Select(b => (b.Name, AutomationProperties.GetName(b))).ToList();
        Assert.Equal(new (string?, string?)[]
        {
            ("NameBox", "Track name"), ("PerformerBox", "Played by"), ("NotesBox", "Notes"),
            ("ProgramBox", "MIDI program"), ("BankMsbBox", "Bank MSB (-1 is off)"), ("BankLsbBox", "Bank LSB (-1 is off)"), ("ChannelBox", "Channel"), ("FretsBox", "Frets"),
        }, fields);
        var tools = Buttons(window).Where(b => b is not CheckBox && b is not ToggleButton && b is not ColourButton && b.Name is not null && !b.Name.StartsWith("String") && b.Name != "InstrumentChooserButton" && b.Content is string).Select(b => (b.Name, (string)b.Content!)).ToList();
        Assert.Equal(new (string?, string)[]
        {
            ("CancelButton", "Cancel"), ("OkButton", "OK"), ("FxChainButton", "Effects & instruments (FX)…"), ("EditCustomMapButton", "Edit custom map…"),
            ("TuningPresetButton", "Standard E ▾"), ("AllDownButton", "All −1"), ("AllUpButton", "All +1"), ("AddStringButton", "+ Add string"),
            ("RemoveSelectedCapoButton", "Remove selected"), ("RemoveAllCaposButton", "Remove all capos"),
        }, tools);
        var ok = Named<Button>(window, "OkButton");
        var cancel = Named<Button>(window, "CancelButton");
        Assert.Equal((true, false, 92d), (ok.IsDefault, ok.IsCancel, ok.Width));
        Assert.Equal((false, true, 92d), (cancel.IsDefault, cancel.IsCancel, cancel.Width));
        Assert.Equal(track.StringTunings.Count - TrackPropertiesPresenter.FewestStrings, Named<ComboBox>(window, "StringCountBox").SelectedIndex);
        window.Close();
    }

    // The instrument cover is gone from the left column (Details moved up); the drum and preset boxes take the widths.
    [AvaloniaFact]
    public void NoCoverAndBoxSizesMatchWpf()
    {
        var window = Open(DemoTrack());

        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Control>(), c => c.Name == "InstrumentCover");
        Assert.Equal((200d, 116d), (Named<ComboBox>(window, "DrumMapBox").Width, Named<Button>(window, "TuningPresetButton").MinWidth));
        window.Close();
    }

    // Each MIDI label sits above its box (a label row, then the box row), so the text never runs into the next cell.
    [AvaloniaFact]
    public void MidiLabelsSitAboveTheirBoxes()
    {
        var window = Open(DemoTrack());
        Dispatcher.UIThread.RunJobs();

        var card = Named<Border>(window, "MidiCard");
        foreach (var (label, box) in new[] { ("Program", "ProgramBox"), ("Bank MSB", "BankMsbBox"), ("Bank LSB", "BankLsbBox"), ("Channel", "ChannelBox") })
        {
            var caption = card.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == label);
            var input = Named<TextBox>(window, box);
            var captionBottom = caption.TranslatePoint(new Point(0, caption.Bounds.Height), window)!.Value.Y;
            var inputTop = input.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            Assert.True(captionBottom <= inputTop, $"{label} label overlaps {box}");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void FxChainButtonIsEnabledOnlyForAnExistingTrack()
    {
        var window = Open(DemoTrack());
        Assert.True(Named<Button>(window, "FxChainButton").IsEnabled);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("ProgramBox", "128")]
    [InlineData("BankMsbBox", "128")]
    [InlineData("BankMsbBox", "-2")]
    [InlineData("BankLsbBox", "128")]
    [InlineData("BankLsbBox", "-2")]
    [InlineData("ChannelBox", "16")]
    [InlineData("FretsBox", "11")]
    [InlineData("ProgramBox", "abc")]
    public void OutOfRangeOkShowsThePresenterMessageAndStaysOpen(string field, string typed)
    {
        var track = DemoTrack();
        var before = (track.Name, track.MidiProgram, track.MidiBankMsb, track.MidiBankLsb, track.MidiChannel, track.NumberOfFrets, track.Capo);
        var window = Open(track);
        Named<TextBox>(window, field).Text = typed;
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        var expected = TrackPropertiesPresenter.FieldError(Text(window, "ProgramBox"), Text(window, "BankMsbBox"), Text(window, "BankLsbBox"), Text(window, "ChannelBox"), Text(window, "FretsBox"), track.Capo.ToString(), track.IsDrumTrack);
        Assert.NotNull(expected);
        Assert.Equal(expected, Named<TextBlock>(window, "ErrorText").Text);
        Assert.True(window.IsVisible);
        Assert.False(window.Applied);
        Assert.Equal(before, (track.Name, track.MidiProgram, track.MidiBankMsb, track.MidiBankLsb, track.MidiChannel, track.NumberOfFrets, track.Capo));
        Discard(window);
    }

    [AvaloniaTheory]
    [InlineData("   ")]
    [InlineData("  Solo guitar  ")]
    public void OkStoresTheResolvedName(string typed)
    {
        var track = DemoTrack();
        var expected = TrackPropertiesPresenter.ResolvedName(typed, track.Name);
        var window = Open(track);
        Named<TextBox>(window, "NameBox").Text = typed;
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Applied);
        Assert.Equal(expected, track.Name);
    }

    [AvaloniaFact]
    public void ValidOkWritesTheTrackAndCloses()
    {
        var track = DemoTrack();
        var expectedTunings = track.StringTunings.ToList();
        expectedTunings[0] = TrackPropertiesPresenter.Step(expectedTunings[0], 1);
        var window = Open(track);
        Named<TextBox>(window, "ProgramBox").Text = "31";
        Named<TextBox>(window, "BankMsbBox").Text = "2";
        Named<TextBox>(window, "BankLsbBox").Text = "5";
        Named<TextBox>(window, "ChannelBox").Text = "2";
        Named<TextBox>(window, "FretsBox").Text = "22";
        CapoUp(window);
        CapoUp(window);
        DialogTestKit.Click(Named<Button>(window, "StringUp0"));
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Applied);
        Assert.False(window.IsVisible);
        Assert.Equal((31, 2, 5, 2, 22, 2), (track.MidiProgram, track.MidiBankMsb, track.MidiBankLsb, track.MidiChannel, track.NumberOfFrets, track.Capo));
        Assert.Equal(expectedTunings, track.StringTunings);
    }

    [AvaloniaFact]
    public void OkRetunesTheWrittenNotesAsOneUndoStep()
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var track = doc.Project.Tracks[0];
        track.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = track.PitchOf(0, 3) });
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        Named<ColourButton>(window, "ColourBox").Show("#123456");
        DialogTestKit.Click(Named<Button>(window, "StringUp0"));
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Applied);
        var retuned = doc.Project.Tracks[0];
        var note = retuned.Measures[0].Cells[0].Notes.Last();
        Assert.Equal((0, 3, retuned.PitchOf(0, 3)), (note.StringIndex, note.Fret, note.MidiValue));
        Assert.Equal("#123456", retuned.ColorHex);
        Assert.Equal(1, doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void RetuningBeforeRemovingUsesTheFinalTuningAndOkDoesNotAskAgain()
    {
        var track = GuitarWithLowNotes();   // A2 and B2 on the low E
        var doc = new ScorePreviewEditor.PreviewDocument(new SongProject { Tracks = new List<TrackModel> { track } });
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        TuningRemovalImpact? removal = null;
        var askedAtCommit = false;
        window.ConfirmNoteMove = impact => { removal = impact; return System.Threading.Tasks.Task.FromResult(true); };
        window.ConfirmUndisclosedPitchChange = _ => { askedAtCommit = true; return System.Threading.Tasks.Task.FromResult(true); };

        DialogTestKit.Click(Named<Button>(window, "StringUp4"));
        DialogTestKit.Click(Named<Button>(window, "StringUp4"));   // A2 string becomes B2
        RemoveLowString(window);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(removal);
        Assert.Equal((2, 1), (removal!.Total, removal.Unplayable));   // A2 no longer fits, B2 still does
        Assert.Same(track.Measures[0].Cells[0].Notes[0], Assert.Single(removal.UnplayableNotes));

        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.False(askedAtCommit);   // the removal warning already used the final tuning
        Assert.True(window.Applied);
        Assert.Equal(1, doc.Undo.UndoCount);
        Assert.Equal(new[] { 64, 59, 55, 50, 47 }, doc.Project.Tracks[0].StringTunings);
        Assert.Equal((4, 0, 47), (track.Measures[0].Cells[0].Notes[0].StringIndex, track.Measures[0].Cells[0].Notes[0].Fret, track.Measures[0].Cells[0].Notes[0].MidiValue));
        Assert.Equal((4, 0, 47), (track.Measures[0].Cells[1].Notes[0].StringIndex, track.Measures[0].Cells[1].Notes[0].Fret, track.Measures[0].Cells[1].Notes[0].MidiValue));
    }

    [AvaloniaFact]
    public void CancellingTheRemovalWarningKeepsTheRetunedDraftAndTheSong()
    {
        var track = GuitarWithLowNotes();
        var doc = new ScorePreviewEditor.PreviewDocument(new SongProject { Tracks = new List<TrackModel> { track } });
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        window.ConfirmNoteMove = _ => System.Threading.Tasks.Task.FromResult(false);

        DialogTestKit.Click(Named<Button>(window, "StringUp4"));
        DialogTestKit.Click(Named<Button>(window, "StringUp4"));   // keep this retune when the removal is refused
        RemoveLowString(window);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.False(window.Applied);
        Assert.Equal(3, Named<ComboBox>(window, "StringCountBox").SelectedIndex);   // still the six strings
        Assert.Equal("B2", Named<TextBox>(window, "NoteBox4").Text);
        Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, track.StringTunings);
        Assert.Equal((5, 5, 45), (track.Measures[0].Cells[0].Notes[0].StringIndex, track.Measures[0].Cells[0].Notes[0].Fret, track.Measures[0].Cells[0].Notes[0].MidiValue));
        Assert.Equal(0, doc.Undo.UndoCount);
        Discard(window);
    }

    [AvaloniaFact]
    public void RemovingThenRetuningWarnsBeforeOkAndCancelKeepsDraftAndSong()
    {
        var track = GuitarWithLowNotes();
        var doc = new ScorePreviewEditor.PreviewDocument(new SongProject { Tracks = new List<TrackModel> { track } });
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        TuningRemovalImpact? removal = null;
        TuningRemovalImpact? commit = null;
        window.ConfirmNoteMove = impact => { removal = impact; return System.Threading.Tasks.Task.FromResult(true); };
        window.ConfirmUndisclosedPitchChange = impact => { commit = impact; return System.Threading.Tasks.Task.FromResult(false); };

        RemoveLowString(window);
        DialogTestKit.Click(Named<Button>(window, "StringUp4"));
        DialogTestKit.Click(Named<Button>(window, "StringUp4"));   // A2 string becomes B2, after the removal was accepted
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(removal);
        Assert.Equal((2, 0), (removal!.Total, removal.Unplayable));   // the removal question could not know about this retune

        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(commit);
        Assert.Equal((2, 1), (commit!.Total, commit.Unplayable));
        Assert.Same(track.Measures[0].Cells[0].Notes[0], Assert.Single(commit.UnplayableNotes));   // A2 is the newly unplayable note
        Assert.True(window.IsVisible);
        Assert.False(window.Applied);
        Assert.Equal(0, doc.Undo.UndoCount);
        Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, track.StringTunings);
        Assert.Equal((5, 5, 45), (track.Measures[0].Cells[0].Notes[0].StringIndex, track.Measures[0].Cells[0].Notes[0].Fret, track.Measures[0].Cells[0].Notes[0].MidiValue));
        Assert.Equal(2, Named<ComboBox>(window, "StringCountBox").SelectedIndex);   // the five-string draft survives No
        Assert.Equal("B2", Named<TextBox>(window, "NoteBox4").Text);

        window.ConfirmUndisclosedPitchChange = _ => System.Threading.Tasks.Task.FromResult(true);
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Applied);
        Assert.False(window.IsVisible);
        Assert.Equal(1, doc.Undo.UndoCount);
        Assert.Equal(new[] { 64, 59, 55, 50, 47 }, doc.Project.Tracks[0].StringTunings);
        Assert.Equal((4, 0, 47), (track.Measures[0].Cells[0].Notes[0].StringIndex, track.Measures[0].Cells[0].Notes[0].Fret, track.Measures[0].Cells[0].Notes[0].MidiValue));
        Assert.Equal((4, 0, 47), (track.Measures[0].Cells[1].Notes[0].StringIndex, track.Measures[0].Cells[1].Notes[0].Fret, track.Measures[0].Cells[1].Notes[0].MidiValue));
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, doc.Project.Tracks[0].StringTunings);
    }

    [AvaloniaFact]
    public void OkClosesTheWindowBeforeTheEditRunsAndMakesOneUndoStep()
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var track = doc.Project.Tracks[0];
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        Named<TextBox>(window, "NameBox").Text = "Closed first";
        DialogTestKit.Click(Named<Button>(window, "OkButton"));

        Assert.False(window.IsVisible);
        Assert.NotEqual("Closed first", track.Name);
        Assert.Equal(0, doc.Undo.UndoCount);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Closed first", track.Name);
        Assert.Equal(1, doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void InvalidColourShowsItsMessageAndStaysOpen()
    {
        var window = Open(DemoTrack());
        Named<ColourButton>(window, "ColourBox").Show("blue");
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.Applied);
        Assert.Equal("Colour must be a hex value like #4C9AFF.", Named<TextBlock>(window, "ErrorText").Text);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task TheColourIsTheSharedColourButtonAndAPickedColourIsStored()
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var window = new TrackPropertiesWindow(doc.Project.Tracks[0], doc);
        window.Show();
        var button = Named<ColourButton>(window, "ColourBox");
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<WrapPanel>(), p => p.Name == "QuickPicks");
        Assert.Equal("Track colour", AutomationProperties.GetName(button));
        button.Chooser = _ => System.Threading.Tasks.Task.FromResult<string?>("#F76B15");
        await button.OpenAsync();
        Assert.Equal("#F76B15", button.Value);
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("#F76B15", doc.Project.Tracks[0].ColorHex);
    }

    [AvaloniaFact]
    public void CancelWithoutEditsClosesAtOnce()
    {
        var window = Open(DemoTrack());
        DialogTestKit.Click(Named<Button>(window, "CancelButton"));

        Assert.False(window.IsVisible);
        Assert.False(window.Applied);
    }

    [AvaloniaFact]
    public void ClosingWithEditsAsksTheWpfQuestionAndNoReturnsToEditing()
    {
        var track = DemoTrack();
        var name = track.Name;
        var window = Open(track);
        Named<TextBox>(window, "NameBox").Text = "Renamed";
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = window });

        var prompt = Prompt(window);
        Assert.Equal("Unsaved track changes", prompt.Title);
        Assert.Equal("Discard the changes made to this track?", Named<TextBlock>(prompt, "MessageText").Text);
        Assert.Equal("▸ Show changes (1)", Named<TextBlock>(prompt, "DetailsLink").Text);
        Assert.DoesNotContain(prompt.GetLogicalDescendants().OfType<Button>(), b => b.Name == "CancelButton");
        DialogTestKit.Click(Named<Button>(prompt, "NoButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.Equal("Renamed", Named<TextBox>(window, "NameBox").Text);
        Discard(window);
        Assert.False(window.IsVisible);
        Assert.Equal(name, track.Name);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RendersPngInEachTheme(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new TrackPropertiesWindow(DemoTrack());
        try
        {
            var path = DialogTestKit.SavePng(window, nameof(TrackPropertiesWindowTests), $"track-properties-{(light ? "light" : "dark")}.png");
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnThePanel(bool light)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), panel) >= 4.5, $"{text} on PanelBackground (light: {light})");
    }

    // Cancel, then Yes on the discard question: the window closes without applying.
    private static void Discard(TrackPropertiesWindow window)
    {
        DialogTestKit.Click(Named<Button>(window, "CancelButton"));
        if (window.IsVisible) DialogTestKit.Click(Named<Button>(Prompt(window), "YesButton"));
        Dispatcher.UIThread.RunJobs();
    }

    private static SavePromptWindow Prompt(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.OwnedWindows.OfType<SavePromptWindow>().Single();
    }

    private static string Text(Window window, string name) => Named<TextBox>(window, name).Text ?? "";

    [AvaloniaFact]
    public void ChannelNineShowsDrumCardAndTintHasTip()
    {
        var track = DemoTrack();
        track.Kind = TrackKind.Drums;
        track.MidiChannel = 9;
        var window = Open(track);
        Assert.True(Named<Border>(window, "DrumCard").IsVisible);
        Assert.NotNull(ToolTip.GetTip(Named<CheckBox>(window, "TintBox")));
        window.Close();
    }

    [AvaloniaFact]
    public void PickingBassOnEmptyTrackTakesBassStrings()
    {
        var track = new TrackModel { Name = "Empty", Kind = TrackKind.Guitar, InstrumentName = "Distortion Guitar", StringTunings = TabForge.Services.TrackSetup.DefaultStrings(TrackKind.Guitar)! };
        var window = Open(track);
        InstrumentPickerRunTests.PickThroughTheChooser(window, "Electric Bass (Pick)");
        Assert.Equal(TabForge.Services.TrackSetup.DefaultStrings(TrackKind.Bass)!.Count, Named<StackPanel>(window, "StringRows").Children.Count);
    }

    [AvaloniaFact]
    public void PickingBassOnTrackWithNotesKeepsItsStrings()
    {
        var track = DemoTrack();
        Assert.True(TabForge.Services.TrackSetup.HasNotes(track));
        var window = Open(track);
        var rows = Named<StackPanel>(window, "StringRows").Children.Count;
        InstrumentPickerRunTests.PickThroughTheChooser(window, "Electric Bass (Pick)");
        Assert.Equal(rows, Named<StackPanel>(window, "StringRows").Children.Count);
    }


    private static TrackModel GuitarWithLowNotes()
    {
        var track = new TrackModel
        {
            Name = "Tuning test", Kind = TrackKind.Guitar, NumberOfFrets = 24,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
        };
        var measure = new MeasureModel();
        track.Measures.Add(measure);
        measure.Cells[0].Notes.Add(new TabNote { StringIndex = 5, Fret = 5, MidiValue = track.PitchOf(5, 5) });   // A2
        measure.Cells[1].Notes.Add(new TabNote { StringIndex = 5, Fret = 7, MidiValue = track.PitchOf(5, 7) });   // B2
        return track;
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    // Removal goes through the string-count box; capo placement goes through the board.
    private static void RemoveLowString(Window window) { var box = Named<ComboBox>(window, "StringCountBox"); box.SelectedIndex -= 1; }
    private static void CapoUp(Window window) { var card = (TrackPropertiesWindow)window; CapoPlacementTests.ClickFret(card, card.WorkingEffectiveCapo(0) + 1); }

    private static List<Button> Buttons(Window window) => window.GetLogicalDescendants().OfType<Button>().ToList();
}
