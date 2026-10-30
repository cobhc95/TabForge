using System;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: how one host's greyed section reaches another host's panel: whether the other area is on screen and how to open its panel. The window sets them once.
// Does not own: the panels (ScalePanel) or the hosts' own state.
// Tests: TabForge.Avalonia.Tests.ScalePanelHostTests.
/// <summary>UI thread only.</summary>
public sealed class ScaleHostLinks
{
    /// <summary>True when the fretboard's Scales button is on screen.</summary>
    public Func<bool> FretboardAvailable { get; set; } = () => false;

    /// <summary>Opens the fretboard host's panel above its Scales button.</summary>
    public Action OpenFretboard { get; set; } = () => { };

    /// <summary>True when the piano roll shows (its scale button is on screen).</summary>
    public Func<bool> PianoRollAvailable { get; set; } = () => false;

    /// <summary>Opens the piano roll host's panel above its scale arrow.</summary>
    public Action OpenPianoRoll { get; set; } = () => { };
}
