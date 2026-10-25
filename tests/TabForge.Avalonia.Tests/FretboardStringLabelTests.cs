using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the labels beside the nut: the string numbers 1 to 6 with the note names setting off, and each string's open note with its octave (as the
/// WPF panel writes it) with the setting on.
/// Does not own: the note-name setting's row in the menu (FretboardContextMenuTests) or the markers' note names (InstrumentPanelView.Labels).
/// Tests: TabForge.Avalonia.Tests.FretboardStringLabelTests.
/// </summary>
public sealed class FretboardStringLabelTests
{
    [AvaloniaFact]
    public void StringLabelsAreNumbersOrOpenNotesWithTheNoteNamesSetting()
    {
        var view = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard };
        view.Show(DemoSongFactory.Create().Tracks[0], System.Array.Empty<InstrumentLitNotes.Lit>());
        var settings = new AppSettings();

        view.Look = FretboardSettingsBinding.Look.From(settings);
        Assert.Equal("1", view.StringLabel(0));
        Assert.Equal("6", view.StringLabel(5));

        settings.Editing.ShowNoteNames = true;
        view.Look = FretboardSettingsBinding.Look.From(settings);
        Assert.Equal("E4", view.StringLabel(0));
        Assert.Equal("E2", view.StringLabel(5));
    }
}
