using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the drum key map labels each sound with the selected drum track's own drum-map label, and a pitched track gives none.
/// Does not own: the drum maps (DrumMaps) or the key map's layout (PercussionKeyMapRules).
/// Tests: TabForge.Avalonia.Tests.FretboardDrumLabelTests.
/// </summary>
public sealed class FretboardDrumLabelTests
{
    /// <summary>The General MIDI acoustic snare: a sound every drum map has.</summary>
    private const int SnareMidi = 38;

    [AvaloniaFact]
    public void KeyMapShowsTheDrumTracksOwnLabelAndAPitchedTrackNone()
    {
        var project = DemoSongFactory.Create();
        var drums = NewTrackFactory.CreateTrack(project, TrackKind.Drums);
        var keyMap = new InstrumentPanelView { Kind = InstrumentPanelKind.Drums };
        keyMap.Show(drums, System.Array.Empty<InstrumentLitNotes.Lit>());
        Assert.Equal(DrumMaps.For(drums, SnareMidi).Label, keyMap.DrumLabelOf(SnareMidi));

        var pitched = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard };
        pitched.Show(project.Tracks[0], System.Array.Empty<InstrumentLitNotes.Lit>());
        Assert.Null(pitched.DrumLabelOf(SnareMidi));
    }
}
