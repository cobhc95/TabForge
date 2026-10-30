using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the "Preview previous notes" toggle: its default, the menu row under "Preview next notes", and the recent trail in the fretboard,
/// keyboard and drum map and the legend, in all four combinations with "Preview next notes".
/// Tests: EditingSettings, FretboardContextMenu, InstrumentPanelView.
/// </summary>
public class PreviewPreviousNotesTests
{
    private static NoteEvent N(double onset, double duration, int midi, int fret = 0, int str = 0) =>
        new() { OnsetMs = onset, DurationMs = duration, StringIndex = str, Fret = fret, Midi = midi };

    private static NoteEvent[] Notes(bool drums) => drums
        ? new[] { N(0, 400, 36), N(1000, 800, 38), N(2000, 300, 42) }
        : new[] { N(0, 400, 62, 1, 1), N(1000, 800, 67, 3), N(2000, 300, 64, 2, 2), N(3000, 300, 69, 5, 4) };

    private static InstrumentPanelView Panel(InstrumentPanelKind kind, bool next, bool previous)
    {
        var settings = new AppSettings();
        settings.Editing.PreviewNotesEnabled = next;
        settings.Editing.PreviewPreviousNotesEnabled = previous;
        var panel = new InstrumentPanelView { Kind = kind, Look = FretboardSettingsBinding.Look.From(settings) };
        panel.Show(DemoSongFactory.Create().Tracks[0], System.Array.Empty<InstrumentLitNotes.Lit>());
        panel.Measure(new Size(1100, 170));
        panel.Arrange(new Rect(0, 0, 1100, 170));
        panel.ShowPlayback(Notes(kind == InstrumentPanelKind.Drums), 1200);
        return panel;
    }

    [Fact]
    public void DefaultsAreNextOnAndPreviousOff()
    {
        var fresh = new AppSettings();
        Assert.True(fresh.Editing.PreviewNotesEnabled);
        Assert.False(fresh.Editing.PreviewPreviousNotesEnabled);
    }

    [Fact]
    public void MissingKeysInALoadedFileGiveTheDefaults()
    {
        var withEditing = LoadJson("{\"Editing\":{\"FretboardFrets\":12}}");
        Assert.True(withEditing.Editing.PreviewNotesEnabled);
        Assert.False(withEditing.Editing.PreviewPreviousNotesEnabled);

        var withoutEditing = LoadJson("{}");
        Assert.True(withoutEditing.Editing.PreviewNotesEnabled);
        Assert.False(withoutEditing.Editing.PreviewPreviousNotesEnabled);
    }

    [Fact]
    public void SavedValuesInALoadedFileAreKept()
    {
        var loaded = LoadJson("{\"Editing\":{\"PreviewNotesEnabled\":false,\"PreviewPreviousNotesEnabled\":true}}");
        Assert.False(loaded.Editing.PreviewNotesEnabled);
        Assert.True(loaded.Editing.PreviewPreviousNotesEnabled);
    }

    private static AppSettings LoadJson(string json)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tf-preview-{System.Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, json);
        try { return SettingsFileService.Load(path); }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void MenuRowSitsUnderPreviewNextAndTogglesTheSetting()
    {
        var settings = new AppSettings();
        var saves = 0;
        var menu = new FretboardContextMenu(() => settings, () => saves++, () => { }, () => null);
        var ids = menu.RowsFor(InstrumentPanelKind.Fretboard).Select(r => r.Id).ToList();
        Assert.Equal(ids.IndexOf(FretboardContextMenu.PreviewId) + 1, ids.IndexOf(FretboardContextMenu.PreviewPreviousId));
        Assert.False(menu.RowsFor(InstrumentPanelKind.Keyboard).Single(r => r.Id == FretboardContextMenu.PreviewPreviousId).Checked);

        menu.Run(FretboardContextMenu.PreviewPreviousId, null);
        Assert.True(settings.Editing.PreviewPreviousNotesEnabled);
        Assert.True(settings.Editing.PreviewNotesEnabled);
        Assert.Equal(1, saves);
        Assert.True(menu.RowsFor(InstrumentPanelKind.Fretboard).Single(r => r.Id == FretboardContextMenu.PreviewPreviousId).Checked);

        var row = SettingsCatalogRow(settings);
        row.set(false);
        Assert.False(menu.RowsFor(InstrumentPanelKind.Fretboard).Single(r => r.Id == FretboardContextMenu.PreviewPreviousId).Checked);
    }

    private static (System.Action<bool> set, bool value) SettingsCatalogRow(AppSettings settings) => (v => settings.Editing.PreviewPreviousNotesEnabled = v, settings.Editing.PreviewPreviousNotesEnabled);

    [AvaloniaTheory]
    [InlineData(InstrumentPanelKind.Fretboard, true, true)]
    [InlineData(InstrumentPanelKind.Fretboard, true, false)]
    [InlineData(InstrumentPanelKind.Fretboard, false, true)]
    [InlineData(InstrumentPanelKind.Fretboard, false, false)]
    [InlineData(InstrumentPanelKind.Keyboard, true, true)]
    [InlineData(InstrumentPanelKind.Keyboard, true, false)]
    [InlineData(InstrumentPanelKind.Keyboard, false, true)]
    [InlineData(InstrumentPanelKind.Keyboard, false, false)]
    [InlineData(InstrumentPanelKind.Drums, true, true)]
    [InlineData(InstrumentPanelKind.Drums, true, false)]
    public void RecentNotesFollowTheToggleIndependentlyOfNext(InstrumentPanelKind kind, bool next, bool previous)
    {
        var panel = Panel(kind, next, previous);
        var drums = kind == InstrumentPanelKind.Drums;
        var recent = drums ? 36 : 62;
        Assert.Equal(previous, panel.LitMidis().Contains(recent));
        Assert.Equal(1 + (next ? 2 : 0) + (previous ? 1 : 0), panel.LegendRowCount);
        if (kind != InstrumentPanelKind.Fretboard) return;
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(draw, light: false);
        var labels = draw.Texts.Select(t => t.Text).ToList();
        Assert.Equal(previous, labels.Contains("recent"));
        Assert.Equal(next, labels.Contains("next"));
    }

    [AvaloniaFact]
    public void TurningItOffLiveDropsTheTrailAtOnce()
    {
        var panel = Panel(InstrumentPanelKind.Keyboard, next: true, previous: true);
        Assert.Contains(62, panel.LitMidis());
        var off = new AppSettings();
        off.Editing.PreviewPreviousNotesEnabled = false;
        panel.Look = FretboardSettingsBinding.Look.From(off);
        Assert.DoesNotContain(62, panel.LitMidis());
    }
}
