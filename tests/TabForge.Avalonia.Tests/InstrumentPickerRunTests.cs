using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Choose instrument run in the real windows: the track row icon and the Track properties button open the same picker, on the current
///     family with every other family collapsed and the current tile selected and in view; a family header toggles from its whole row (click or Enter)
///     with the theme's hover shade; the icon keeps its hover and its click after OK, Cancel and the close box, and a change commits as one undo step.
/// Does not own: the sound side (InstrumentChoiceTests), the presenter's rules (TrackIconInstrumentPickerTests).
/// Tests: InstrumentPickerWindow, InstrumentFamilyHeader, InstrumentPickerFlow, MainWindowTrackRowOptions, TrackHeaderColumn, TrackPropertiesWindow.
/// </summary>
public class InstrumentPickerRunTests
{
    private const string CommitLabel = InstrumentPreviewSession.CommitLabel;

    [AvaloniaTheory]
    [InlineData("OK")]
    [InlineData("Cancel")]
    [InlineData("X")]
    public void TheIconReturnsToItsNormalLookAndStaysClickableAfterEachWayOfClosing(string way)
    {
        var (main, arrangement, row) = OpenMain();
        var at = IconPoint(main, arrangement, row);
        main.MouseMove(at);
        main.MouseDown(at, MouseButton.Left);   // the dialog opens on the press, so the release never reaches the header
        var picker = Picker(main);
        Assert.Equal(TrackRowCells.CellPaint.Pressed, arrangement.Header.PaintOf(row, TrackRowCells.Cell.Kind));

        Close(picker, way);

        Assert.Contains(arrangement.Header.PaintOf(row, TrackRowCells.Cell.Kind), new[] { TrackRowCells.CellPaint.Idle, TrackRowCells.CellPaint.Hover });
        main.MouseUp(at, MouseButton.Left);
        main.MouseDown(at, MouseButton.Left);
        main.MouseUp(at, MouseButton.Left);
        Assert.Single(main.OwnedWindows.OfType<InstrumentPickerWindow>());
        Finish(main);
    }

    [AvaloniaFact]
    public void ChangingTheInstrumentFromTheIconCommitsOneStepAndKeepsTheIconHoverableAndClickable()
    {
        var (main, arrangement, row) = OpenMain();
        var doc = main.Songs.Active;
        var track = doc.Project.Tracks[row];
        var undo = doc.Undo.UndoCount;
        var at = IconPoint(main, arrangement, row);
        main.MouseDown(at, MouseButton.Left);
        var picker = Picker(main);
        var target = OtherInSameFamily(track);

        ClickTile(picker, target);
        Assert.Equal(target.Name, track.InstrumentName);
        Assert.Equal(undo, doc.Undo.UndoCount);
        Close(picker, "OK");

        Assert.Equal(target.Name, track.InstrumentName);
        Assert.Equal(undo + 1, doc.Undo.UndoCount);
        Assert.Equal(CommitLabel, doc.Undo.Latest?.Label);
        main.MouseMove(new Point(at.X + 400, at.Y + 400));
        main.MouseMove(at);
        Assert.Equal((row, TrackRowCells.Cell.Kind), arrangement.Header.HoverCell);
        main.MouseUp(at, MouseButton.Left);
        main.MouseDown(at, MouseButton.Left);
        main.MouseUp(at, MouseButton.Left);
        Assert.Single(main.OwnedWindows.OfType<InstrumentPickerWindow>());
        Finish(main);
    }

    [AvaloniaFact]
    public void CancelFromTheIconRestoresTheTrackExactlyWithNoStep()
    {
        var (main, arrangement, row) = OpenMain();
        var doc = main.Songs.Active;
        var track = doc.Project.Tracks[row];
        var before = DocumentEdits.Fingerprint(doc);
        var undo = doc.Undo.UndoCount;
        var name = track.InstrumentName;
        main.MouseDown(IconPoint(main, arrangement, row), MouseButton.Left);
        var picker = Picker(main);

        ClickTile(picker, OtherInSameFamily(track));
        Assert.NotEqual(name, track.InstrumentName);
        Close(picker, "Cancel");

        Assert.Equal(name, track.InstrumentName);
        Assert.Equal(before, DocumentEdits.Fingerprint(doc));
        Assert.Equal(undo, doc.Undo.UndoCount);
        Finish(main);
    }

    [AvaloniaFact]
    public void ThePickerOpensOnTheCurrentFamilyWithTheCurrentTileSelectedAndInView()
    {
        var current = InstrumentCatalog.All.Last();   // a drum kit: the last family, far below the first screen
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), current.Name, focusFamily: true);
        window.Show();
        RunQueuedUiWork();
        window.UpdateLayout();

        var open = window.GetLogicalDescendants().OfType<InstrumentFamilyHeader>().Where(h => h.IsOpen).Select(h => h.Family).ToList();
        Assert.Equal(new[] { current.Category }, open);
        var tile = Tiles(window).Single(t => ((string)ToolTip.GetTip(t)!).StartsWith(current.Name + "  ·"));
        Assert.True(tile.IsVisible);
        Assert.True(((ISolidColorBrush)tile.Background!).Color.A > 0, "the current tile carries the selection fill");
        var viewer = window.GetLogicalDescendants().OfType<ScrollViewer>().First();
        var top = tile.TranslatePoint(default, viewer)!.Value.Y;
        Assert.InRange(top, 0, viewer.Viewport.Height - tile.Bounds.Height + 1);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePickerFindsTheCurrentTileByProgramWhenTheNameCarriesASuffix()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var entry = InstrumentCatalog.All.First(e => !e.IsDrumKit && e.Program == 24);
        track.MidiProgram = entry.Program;
        track.InstrumentName = entry.Name + " (6 strings)";

        var current = new InstrumentChoice(song, track, null).Current;
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), current?.Name, focusFamily: true);
        window.Show();
        RunQueuedUiWork();

        Assert.Equal(new[] { entry.Category }, window.GetLogicalDescendants().OfType<InstrumentFamilyHeader>().Where(h => h.IsOpen).Select(h => h.Family));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFamilyHeaderTogglesFromItsWholeRowWithTheThemesHoverAndPressShades(bool light)
    {
        using var theme = UseTheme(light);
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.SampleEntries, "Fretless Bass", focusFamily: true);
        window.Show();
        RunQueuedUiWork();
        window.UpdateLayout();
        var header = window.GetLogicalDescendants().OfType<InstrumentFamilyHeader>().First(h => h.Family == "Piano");
        Assert.False(header.IsOpen);
        Assert.Equal("2", header.CountText);
        var farRight = header.TranslatePoint(new Point(header.Bounds.Width - 4, header.Bounds.Height / 2), window)!.Value;

        window.MouseMove(farRight);
        Assert.Equal(ColourOf(HoverShade.Brush(header)), ColourOf(header.Background));
        window.MouseDown(farRight, MouseButton.Left);
        Assert.True(header.IsOpen, "a press anywhere on the row opens the family");
        Assert.Equal(ColourOf(AvaloniaPalette.PaletteBrush(HoverShade.PressPaletteKey, light)), ColourOf(header.Background));
        window.MouseUp(farRight, MouseButton.Left);
        Assert.Null(header.Cursor);

        header.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.False(header.IsOpen, "Enter closes it again");
        header.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        Assert.True(header.IsOpen, "Space opens it");
        window.Close();
    }

    [AvaloniaFact]
    public void TheTrackPropertiesButtonRunsTheSamePickerAndCommitsOneStep()
    {
        var (doc, track, window) = OpenProperties();
        var undo = doc.Undo.UndoCount;
        var picker = OpenPicker(window);
        var family = InstrumentCatalog.ForTrack(track.InstrumentName, track.MidiProgram, false)!.Category;
        Assert.Equal(new[] { family }, picker.GetLogicalDescendants().OfType<InstrumentFamilyHeader>().Where(h => h.IsOpen).Select(h => h.Family));
        var target = OtherInSameFamily(track);

        ClickTile(picker, target);
        Assert.Equal(target.Name, track.InstrumentName);
        Assert.Equal(target.Name, Named<TextBlock>(window, "ChooserName").Text);
        Assert.Equal(undo, doc.Undo.UndoCount);
        Close(picker, "OK");

        Assert.Equal(undo + 1, doc.Undo.UndoCount);
        Assert.Equal(CommitLabel, doc.Undo.Latest?.Label);
        Assert.Equal(target.Name, Named<TextBlock>(window, "ChooserName").Text);
        Assert.Equal(target.Program.ToString(), Named<TextBox>(window, "ProgramBox").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void CancelInTheTrackPropertiesPickerRestoresTheTrackAndTheForm()
    {
        var (doc, track, window) = OpenProperties();
        var before = DocumentEdits.Fingerprint(doc);
        var name = track.InstrumentName;
        var program = Named<TextBox>(window, "ProgramBox").Text;
        var picker = OpenPicker(window);

        ClickTile(picker, OtherInSameFamily(track));
        Close(picker, "X");

        Assert.Equal(before, DocumentEdits.Fingerprint(doc));
        Assert.Equal(name, Named<TextBlock>(window, "ChooserName").Text);
        Assert.Equal(program, Named<TextBox>(window, "ProgramBox").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void ATrackThatPlaysThroughAVstInstrumentShowsTheRouteNoteAtTheTopOfThePicker()
    {
        var (_, track, window) = OpenProperties(t =>
        {
            t.SoundSource = SoundSources.Plugins;
            t.Rig.Plugins.Add(new PluginSlot { Name = "Sylenth1", Type = PluginSlotType.Instrument });
        });
        var picker = OpenPicker(window);

        var note = Named<TextBlock>(picker, "RouteNote");

        Assert.Equal(InstrumentRouteNote.For(track), note.Text);
        Assert.Contains("Sylenth1", note.Text);
        Close(picker, "Cancel");
        window.Close();
    }

    [AvaloniaFact]
    public void APlainTrackShowsNoRouteNote()
    {
        var (_, _, window) = OpenProperties();
        var picker = OpenPicker(window);
        Assert.DoesNotContain(picker.GetLogicalDescendants().OfType<TextBlock>(), t => t.Name == "RouteNote");
        Close(picker, "Cancel");
        window.Close();
    }

    private static (MainWindow Main, ArrangementView Arrangement, int Row) OpenMain()
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song);
        main.Show();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var row = song.Project.Tracks.FindIndex(t => !t.IsAudio && t.MidiChannel != 9);
        return (main, arrangement, row);
    }

    private static (SongDocument Doc, TrackModel Track, TrackPropertiesWindow Window) OpenProperties(Action<TrackModel>? setup = null)
    {
        var project = FullDemoSongFactory.Create();
        var track = project.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        setup?.Invoke(track);
        var doc = new SongDocument(() => null) { Project = project };
        project.IsDirty = false;
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        return (doc, track, window);
    }

    /// <summary>Picks a sound through the Change instrument button of the window: opens the picker, opens the family of the sound, clicks its tile and Select.</summary>
    internal static void PickThroughTheChooser(TrackPropertiesWindow window, string name)
    {
        var picker = OpenPicker(window);
        var entry = InstrumentCatalog.Find(name)!;
        var header = picker.GetLogicalDescendants().OfType<InstrumentFamilyHeader>().Single(h => h.Family == entry.Category);
        if (!header.IsOpen)
        {
            header.BringIntoView();
            picker.UpdateLayout();
            var centre = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), picker)!.Value;
            picker.MouseDown(centre, MouseButton.Left);
            picker.MouseUp(centre, MouseButton.Left);
        }
        ClickTile(picker, entry);
        Close(picker, "OK");
    }

    private static InstrumentPickerWindow OpenPicker(TrackPropertiesWindow window)
    {
        Click(Named<Button>(window, "InstrumentChooserButton"));
        RunQueuedUiWork();
        return window.OwnedWindows.OfType<InstrumentPickerWindow>().Single();
    }

    private static InstrumentPickerWindow Picker(MainWindow main)
    {
        RunQueuedUiWork();
        return main.OwnedWindows.OfType<InstrumentPickerWindow>().Single();
    }

    private static Point IconPoint(MainWindow main, ArrangementView arrangement, int row)
    {
        var local = arrangement.Header.CellRect(row, TrackRowCells.Cell.Kind).Center;
        var origin = arrangement.Header.TranslatePoint(default, arrangement) ?? default;
        return arrangement.TranslatePoint(new Point(local.X + origin.X, local.Y + origin.Y), main) ?? throw new InvalidOperationException("The arrangement is not in the main window.");
    }

    /// <summary>A sound of the own family of the track, other than the one it has: its tile is built and visible when the picker opens.</summary>
    private static InstrumentEntry OtherInSameFamily(TrackModel track)
    {
        var current = InstrumentCatalog.ForTrack(track.InstrumentName, track.MidiProgram, track.MidiChannel == 9)!;
        return InstrumentCatalog.All.First(e => e.Category == current.Category && e.Name != current.Name);
    }

    private static List<Border> Tiles(Window window) => window.GetLogicalDescendants().OfType<Border>().Where(b => ToolTip.GetTip(b) is string).ToList();

    private static void ClickTile(InstrumentPickerWindow picker, InstrumentEntry entry)
    {
        picker.UpdateLayout();
        var tile = Tiles(picker).Single(t => ((string)ToolTip.GetTip(t)!).StartsWith(entry.Name + "  ·"));
        Assert.True(tile.IsVisible, entry.Name + " should be on show in the opened family");
        tile.BringIntoView();
        picker.UpdateLayout();
        var centre = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), picker)!.Value;
        picker.MouseDown(centre, MouseButton.Left);
        picker.MouseUp(centre, MouseButton.Left);
    }

    private static void Close(InstrumentPickerWindow picker, string way)
    {
        if (way == "X") picker.Close();
        else Click(picker.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == (way == "OK" ? "Select" : "Cancel")));
        RunQueuedUiWork();
    }

    private static void Finish(MainWindow main)
    {
        foreach (var open in main.OwnedWindows.ToList()) open.Close();
        main.Songs.Active.MarkClean();   // a committed change is an edit, and a dirty song would open a save prompt on close that nothing answers here
        main.Close();
    }

    private static Color ColourOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;
}
