using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
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
        Assert.Equal((800d, 640d, 420d, SizeToContent.Height), (window.Width, window.MinWidth, window.MinHeight, window.SizeToContent));
        // Order: details (name, played by, colour, notes), instrument (program, channel), tuning (frets, capo).
        var fields = window.GetLogicalDescendants().OfType<TextBox>().Select(b => (b.Name, AutomationProperties.GetName(b))).ToList();
        Assert.Equal(new (string?, string?)[]
        {
            ("NameBox", "Track name"), ("PerformerBox", "Played by"), ("NotesBox", "Notes"),
            ("ProgramBox", "MIDI program"), ("ChannelBox", "Channel"), ("FretsBox", "Frets"), ("CapoBox", "Capo"),
        }, fields);
        var tools = Buttons(window).Where(b => b is not CheckBox && b is not ColourButton && b.Name is not null && !b.Name.StartsWith("String") && b.Name != "InstrumentChooserButton").Select(b => (b.Name, (string)b.Content!)).ToList();
        Assert.Equal(new (string?, string)[]
        {
            ("CancelButton", "Cancel"), ("OkButton", "OK"), ("EditCustomMapButton", "Edit custom map…"), ("FxChainButton", "Effects & instruments (FX)…"),
            ("AllDownButton", "All −1"), ("AllUpButton", "All +1"), ("AddLowStringButton", "+ Low string"), ("RemoveLowStringButton", "− Low string"),
        }, tools);
        var ok = Named<Button>(window, "OkButton");
        var cancel = Named<Button>(window, "CancelButton");
        Assert.Equal((true, false, 92d), (ok.IsDefault, ok.IsCancel, ok.Width));
        Assert.Equal((false, true, 92d), (cancel.IsDefault, cancel.IsCancel, cancel.Width));
        Assert.Equal($"{track.StringTunings.Count} strings", Named<TextBlock>(window, "StringsLabel").Text);
        window.Close();
    }

    // The cover is a 190 DIP picture with the instrument name below it (outside the picture); the drum and preset boxes take the WPF widths.
    [AvaloniaFact]
    public void CoverCaptionAndBoxSizesMatchWpf()
    {
        var window = Open(DemoTrack());

        var cover = Named<Border>(window, "InstrumentCover");
        Assert.Equal(190d, cover.Height);
        Assert.DoesNotContain(cover.GetLogicalDescendants(), d => d is TextBlock { Name: "InstrumentCaption" });
        Assert.Equal((200d, 220d), (Named<ComboBox>(window, "DrumMapBox").Width, Named<ComboBox>(window, "TuningPresetBox").MinWidth));
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
    [InlineData("ChannelBox", "16")]
    [InlineData("FretsBox", "11")]
    [InlineData("CapoBox", "13")]
    [InlineData("ProgramBox", "abc")]
    public void OutOfRangeOkShowsThePresenterMessageAndStaysOpen(string field, string typed)
    {
        var track = DemoTrack();
        var before = (track.Name, track.MidiProgram, track.MidiChannel, track.NumberOfFrets, track.Capo);
        var window = Open(track);
        Named<TextBox>(window, field).Text = typed;
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        var expected = TrackPropertiesPresenter.FieldError(Text(window, "ProgramBox"), Text(window, "ChannelBox"), Text(window, "FretsBox"), Text(window, "CapoBox"), track.IsDrumTrack);
        Assert.NotNull(expected);
        Assert.Equal(expected, Named<TextBlock>(window, "ErrorText").Text);
        Assert.True(window.IsVisible);
        Assert.False(window.Applied);
        Assert.Equal(before, (track.Name, track.MidiProgram, track.MidiChannel, track.NumberOfFrets, track.Capo));
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
    public void LowStringButtonsStopAtNineAndThreeStrings()
    {
        var window = Open(DemoTrack());
        var expected = DemoTrack().StringTunings.ToList();
        for (var i = 0; i < 5; i++)
        {
            DialogTestKit.Click(Named<Button>(window, "AddLowStringButton"));
            TrackPropertiesPresenter.WithLowString(expected);
        }
        Assert.Equal(TrackPropertiesPresenter.MostStrings, expected.Count);
        Assert.Equal("9 strings", Named<TextBlock>(window, "StringsLabel").Text);
        Assert.Equal(TabForge.Services.MusicTheoryService.NoteName(expected[^1]), Named<TextBlock>(window, "NoteLabel8").Text);
        for (var i = 0; i < 8; i++)
        {
            DialogTestKit.Click(Named<Button>(window, "RemoveLowStringButton"));
            TrackPropertiesPresenter.WithoutLowString(expected);
        }
        Assert.Equal(TrackPropertiesPresenter.FewestStrings, expected.Count);
        Assert.Equal("3 strings", Named<TextBlock>(window, "StringsLabel").Text);
        Assert.Equal(3, window.GetLogicalDescendants().OfType<TextBlock>().Count(t => t.Name?.StartsWith("NoteLabel") == true));
        Discard(window);
    }

    [AvaloniaFact]
    public void ValidOkWritesTheTrackAndCloses()
    {
        var track = DemoTrack();
        var expectedTunings = track.StringTunings.ToList();
        expectedTunings[0] = TrackPropertiesPresenter.Step(expectedTunings[0], 1);
        var window = Open(track);
        Named<TextBox>(window, "ProgramBox").Text = "31";
        Named<TextBox>(window, "ChannelBox").Text = "2";
        Named<TextBox>(window, "FretsBox").Text = "22";
        Named<TextBox>(window, "CapoBox").Text = "2";
        DialogTestKit.Click(Named<Button>(window, "StringUp0"));
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Applied);
        Assert.False(window.IsVisible);
        Assert.Equal((31, 2, 22, 2), (track.MidiProgram, track.MidiChannel, track.NumberOfFrets, track.Capo));
        Assert.Equal(expectedTunings, track.StringTunings);
    }

    [AvaloniaFact]
    public void PickingDropDFillsTheLowStringAndOkWritesIt()
    {
        var track = DemoTrack();
        var window = Open(track);
        var presets = Named<ComboBox>(window, "TuningPresetBox");
        presets.SelectedItem = "Drop D";

        Assert.Equal("D2", Named<TextBlock>(window, "NoteLabel5").Text);
        DialogTestKit.Click(Named<Button>(window, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Applied);
        Assert.Equal(new[] { 64, 59, 55, 50, 45, 38 }, track.StringTunings);
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

    [AvaloniaFact]
    public void DoubleClickedChipTakesATypedNoteOnEnter()
    {
        var window = Open(DemoTrack());
        window.TypeNote(Named<Border>(window, "NoteChip0"), 0);
        var entry = Named<TextBox>(window, "NoteEntry0");
        entry.Text = "D2";
        entry.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal("D2", Named<TextBlock>(window, "NoteLabel0").Text);
        Assert.IsType<global::Avalonia.Media.LinearGradientBrush>(Named<global::Avalonia.Controls.Shapes.Rectangle>(window, "StringLine0").Fill);
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static List<Button> Buttons(Window window) => window.GetLogicalDescendants().OfType<Button>().ToList();
}
