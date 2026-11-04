using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the JOB B gap checks for the track properties redesign: string-row order (speaker, label, -, +, note box),
// the window-local partial capos (full capo still writes the track), Add-track parity, and the preset catalog rules.
// Does not own: the window, the draft, or the preset data. No modals: loops and questions are untouched here.
public class TrackPropertiesJobBTests
{
    private static TrackModel GuitarTrack() => new()
    {
        Name = "Strings", Kind = TrackKind.Guitar, InstrumentName = "Electric Guitar",
        MidiChannel = 0, MidiProgram = 29, MidiOutputDeviceId = -1, NumberOfFrets = 22,
        StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
    };

    private static TrackPropertiesWindow Open(TrackModel track, bool adding = false)
    {
        var window = new TrackPropertiesWindow(track, adding: adding);
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
    public void StringRowOrderIsSpeakerLabelMinusPlusNoteBox()
    {
        var window = Open(GuitarTrack());
        try
        {
            var rows = Named<StackPanel>(window, "StringRows").Children.OfType<TrackStringRow>().ToList();
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                var ordered = row.Children.OrderBy(Grid.GetColumn).ToList();
                Assert.IsType<ToggleButton>(ordered[0]);
                Assert.Equal($"SpeakerToggle{row.Index}", ((Control)ordered[0]).Name);
                Assert.IsType<TextBlock>(ordered[1]);
                Assert.Equal((row.Index + 1).ToString(), ((TextBlock)ordered[1]).Text);
                Assert.Equal($"StringDown{row.Index}", ((Control)ordered[3]).Name);
                Assert.Equal($"StringUp{row.Index}", ((Control)ordered[4]).Name);
                Assert.IsType<TextBox>(ordered[5]);
                Assert.Equal($"NoteBox{row.Index}", ((Control)ordered[5]).Name);
            }
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void PartialCapoDefaultsToOneOffRowAndFullCapoSteps()
    {
        var window = Open(GuitarTrack());
        try
        {
            Assert.Empty(window.PartialCapos);
            Assert.True(Named<ToggleButton>(window, "CapoPlacementFull").IsChecked);
            Assert.Equal(0, window.WorkingEffectiveCapo(0));
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<ToggleButton>(), t => (t.Name ?? "").StartsWith("CapoTick"));
            CapoPlacementTests.ClickFret(window, 1);
            Assert.Equal(1, window.WorkingEffectiveCapo(0));
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void PartialCapoAddStepsClampAndFollowStringCount()
    {
        var window = Open(GuitarTrack());
        try
        {
            CapoPlacementTests.PlacePartial(window, 4);
            CapoPlacementTests.ClickFret(window, 2);
            Assert.Equal(2, window.PartialCapos.Count);
            Assert.Equal(2, window.PartialCapos[1].Fret);
            var board = Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard");
            Assert.True(board.BeginDrag(new global::Avalonia.Point(board.FretX(2), board.Bounds.Height / 2)));
            board.Drag(new global::Avalonia.Point(board.FretX(12) + 100, board.Bounds.Height / 2));
            board.EndDrag();
            Assert.Equal(TrackPropertiesPresenter.MaxCapo, window.PartialCapos[1].Fret);
            window.PartialCapos[0].Strings[5] = true;
            Assert.True(window.PartialCapos[0].Strings[5]);
            Named<ComboBox>(window, "StringCountBox").SelectedIndex = 0; // 3 strings at the low end
            Dispatcher.UIThread.RunJobs();
            Assert.All(window.PartialCapos, p => Assert.Equal(3, p.Strings.Count));
            Assert.Equal(2, Named<Panel>(window, "CapoChips").Children.Count);
        }
        finally { Discard(window); }
    }

    [AvaloniaFact]
    public void PartialCaposNeverReachTheTrack()
    {
        var track = GuitarTrack();
        var window = Open(track);
        try
        {
            Assert.Null(typeof(TrackModel).GetProperty("PartialCapo"));
            CapoPlacementTests.PlacePartial(window, 2);
            Assert.Null(track.PartialCapos);
            DialogTestKit.Click(Named<ToggleButton>(window, "CapoPlacementFull"));
            CapoPlacementTests.ClickFret(window, 1);
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Applied);
            Assert.Equal(1, track.Capo);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public void AddModeHasTheSameTuningLayout()
    {
        var track = GuitarTrack();
        var window = Open(track, adding: true);
        try
        {
            Assert.Equal("Add track", window.Title);
            Assert.Equal(8, Named<ComboBox>(window, "StringCountBox").Items.Count); // 3 to 10 strings
            Assert.NotNull(Named<Button>(window, "TuningPresetButton"));
            Assert.NotNull(Named<Button>(window, "AllDownButton"));
            Assert.NotNull(Named<Button>(window, "AllUpButton"));
            Assert.NotNull(Named<ToggleButton>(window, "CapoPlacementFull"));
            Assert.NotNull(Named<ToggleButton>(window, "CapoPlacementPartial"));
            Assert.NotNull(Named<Panel>(window, "CapoChips"));            // v7: replaces the PartialCapoRows panel
            Assert.Equal(track.StringTunings.Count, Named<StackPanel>(window, "StringRows").Children.Count);
        }
        finally { Discard(window); }
    }

    [Fact]
    public void PresetCatalogMeetsJobBRules()
    {
        var catalog = TuningPresets.Catalog.ToList();
        Assert.True(catalog.Count(e => e.Group == TuningPresets.GuitarGroup && e.Preset.HighToLow.Length == 6) >= 20);
        for (var strings = TrackPropertiesPresenter.FewestStrings; strings <= TrackPropertiesPresenter.MostStrings; strings++)
            Assert.Contains(catalog, e => e.Preset.HighToLow.Length == strings);
        foreach (var (_, preset) in catalog)
        {
            Assert.InRange(preset.HighToLow.Length, TrackPropertiesPresenter.FewestStrings, TrackPropertiesPresenter.MostStrings);
            Assert.All(preset.HighToLow, m => Assert.InRange(m, 0, 127));
            Assert.All(preset.HighToLow, m => Assert.InRange(m, TrackPropertiesPresenter.LowestNote, TrackPropertiesPresenter.HighestNote));
        }
        var keys = catalog.GroupBy(e => (e.Group, Count: e.Preset.HighToLow.Length, e.Preset.Name));
        Assert.All(keys, g => Assert.Single(g));
        Assert.DoesNotContain(catalog, e => e.Preset.Name.Contains("Drop Db") || e.Preset.Name.Contains("Drop Bb"));
    }
}
