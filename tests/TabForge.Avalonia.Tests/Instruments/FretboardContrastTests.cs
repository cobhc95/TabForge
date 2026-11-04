using System;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: regression checks for independent neck, inlay and note contrast, persistence, the settings controls and note labels.
// Does not own: playback-role selection or layout rules; these tests inspect the actual painter output, not only its Look.
// Tests: FretboardArtist, InstrumentPanelView, FretboardSettingsWindow and the settings catalogue/validator.
public sealed class FretboardContrastTests
{
    [Fact]
    public void ContrastDefaultsPersistAndInvalidValuesAreClamped()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(65, settings.Editing.FretboardBrightness);
        Assert.Equal(100, settings.Editing.FretboardNoteBrightness);
        var rows = SettingsCatalog.Build(settings);
        rows.Single(r => r.Key == "fretboard.brightness").Set(60);
        rows.Single(r => r.Key == "fretboard.notebrightness").Set(145);
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(60, copy.Editing.FretboardBrightness);
        Assert.Equal(145, copy.Editing.FretboardNoteBrightness);
        copy.Editing.FretboardBrightness = -50;
        copy.Editing.FretboardNoteBrightness = 999;
        SettingsValidator.Normalize(copy);
        Assert.Equal(FretboardStyles.MinBrightness, copy.Editing.FretboardBrightness);
        Assert.Equal(FretboardStyles.MaxBrightness, copy.Editing.FretboardNoteBrightness);
    }

    private static InstrumentPanelView Panel(EditingSettings editing, bool playing = false)
    {
        var panel = new InstrumentPanelView { Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing }) };
        panel.Show(DemoSongFactory.Create().Tracks[0], new[] { new InstrumentLitNotes.Lit(1, 3, 62) });
        panel.Measure(new Size(1300, 180));
        panel.Arrange(new Rect(0, 0, 1300, 180));
        if (playing) panel.ShowPlayback(new[] { new NoteEvent { StringIndex = 1, Fret = 3, Midi = 62, OnsetMs = 0, DurationMs = 1000 } }, 200);
        return panel;
    }

    private static InstrumentFretNumberTests.RecordingDrawContext Draw(InstrumentPanelView panel, bool light = false)
    {
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(draw, light);
        return draw;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoardVeilReachesThePhysicalNeckButLeavesNotesAndScaleColoursAlone(bool light)
    {
        var editing = new EditingSettings();
        var panel = Panel(editing);
        var darker = Draw(panel, light);
        var shade = (AvaloniaPalette.Get("FretboardWoodShade", light) with { A = 255 }).WithOpacity(FretboardStyles.BrightnessVeil(editing.FretboardBrightness));
        var veil = Assert.Single(darker.Rects, r => r.Fill == shade);
        Assert.True(veil.Width >= panel.FretboardLayout().Board.Width);
        editing.FretboardBrightness = 100;
        panel.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing });
        var original = Draw(panel, light);
        Assert.DoesNotContain(original.Rects, r => r.Fill == shade);
        Assert.Equal(original.Ellipses, darker.Ellipses);
        editing.FretboardBrightness = 130;
        panel.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing });
        Assert.Contains(Draw(panel, light).Rects, r => r.Fill == AvaloniaPalette.Get("InstrumentWhite", light).WithOpacity(0.3));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NoteBrightnessChangesActualCursorAndPlaybackFillsWithoutChangingInlays(bool playing, bool light)
    {
        var editing = new EditingSettings();
        var panel = Panel(editing, playing);
        var original = Draw(panel, light);
        editing.FretboardNoteBrightness = 160;
        panel.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing });
        var brighter = Draw(panel, light);
        var at = FretboardGeometryRules.PositionOfTab(panel.FretboardLayout(), 1, 3);
        static UiColor Fill(InstrumentFretNumberTests.RecordingDrawContext d, UiPoint p) =>
            d.Ellipses.Last(e => e.X == p.X && e.Y == p.Y && e.Fill?.A == 255).Fill!.Value;
        Assert.Equal(ThemeColours.Brighten(Fill(original, at), 0.6), Fill(brighter, at));
        Assert.Contains(brighter.Texts, t => t.Text == "3" && t.Colour == ThemeColours.InkOn(Fill(brighter, at)));
        Assert.Equal(original.PathFills, brighter.PathFills);
        editing.FretboardNoteBrightness = 40;
        panel.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing });
        Assert.True(Fill(Draw(panel, light), at).G < Fill(original, at).G);
    }

    [AvaloniaFact]
    public void EveryInlayColourAndBrightnessLevelChangesTheActualInlayPathsNotNoteDots()
    {
        foreach (var colour in FretMarkerLevels.Colours)
        {
            var editing = new EditingSettings { FretMarkerColour = colour };
            var panel = Panel(editing);
            var original = Draw(panel);
            var tint = AvaloniaPalette.Get(FretboardSettingsBinding.ColourKey(colour), false).WithOpacity(0.45);
            Assert.Equal(12, original.PathFills.Count(c => c == tint));
            foreach (var level in FretMarkerLevels.All.Skip(1))
            {
                editing.FretMarkerBrightness = level;
                panel.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing });
                var brighter = Draw(panel);
                var lift = AvaloniaPalette.Get("InstrumentWhite", false).WithOpacity(FretMarkerLevels.Level(level) * 0.65);
                Assert.Equal(12, brighter.PathFills.Count(c => c == lift));
                Assert.Equal(original.Ellipses, brighter.Ellipses);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoteNameSettingChangesActualCursorAndPlaybackDotLabels(bool playing)
    {
        var editing = new EditingSettings();
        var panel = Panel(editing, playing);
        var original = Draw(panel);
        editing.ShowNoteNames = true;
        panel.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing });
        var named = Draw(panel);
        var pitch = MusicTheoryService.NoteName(DemoSongFactory.Create().Tracks[0].StringTunings[1] + 3).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');
        Assert.DoesNotContain(original.Texts, t => t.Text == pitch);
        Assert.Contains(named.Texts, t => t.Text == pitch);
        Assert.Equal(original.Ellipses, named.Ellipses);
    }

    [AvaloniaFact]
    public void PointerPreviewUsesTheChosenNoteLabelToo()
    {
        var panel = Panel(new EditingSettings { ShowNoteNames = true });
        panel.Show(DemoSongFactory.Create().Tracks[0], Array.Empty<InstrumentLitNotes.Lit>());
        var window = new Window { Width = 1300, Height = 180, Content = panel };
        window.Show();
        var at = panel.FretCentre(1, 3);
        window.MouseMove(new Point(at.X, at.Y));
        Assert.Contains(Draw(panel).Texts, t => t.Text == "D");
        window.Close();
    }

    [AvaloniaFact]
    public void SettingsMenuSeparatesInlaysAndNotesAndResetsTheirOwnContrast()
    {
        var settings = new AppSettings();
        var saves = 0;
        var window = new FretboardSettingsWindow(settings, () => saves++, () => { });
        T Find<T>(string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click("FretboardBrightnessMinus");
        Assert.Equal(60, settings.Editing.FretboardBrightness);
        Find<ToggleButton>("FretboardMarkerColourBlue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Blue", settings.Editing.FretMarkerColour);
        Dispatcher.UIThread.RunJobs();
        window.Select(1);
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Control>(), c => c.Name == "FretboardMarkerColourBlue");
        Click("FretboardNoteBrightnessPlus");
        Assert.Equal(105, settings.Editing.FretboardNoteBrightness);
        window.ResetSection(1);
        Assert.Equal(100, settings.Editing.FretboardNoteBrightness);
        Assert.Equal(60, settings.Editing.FretboardBrightness);
        Assert.Equal("Blue", settings.Editing.FretMarkerColour);
        window.ResetSection(0);
        Assert.Equal(65, settings.Editing.FretboardBrightness);
        Assert.Equal("White", settings.Editing.FretMarkerColour);
        Assert.True(saves >= 5);
    }
}
