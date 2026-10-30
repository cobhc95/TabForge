using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

/// <summary>A section a host adds at the bottom of the master scale panel (or in its second column): a caps title and the controls it builds.</summary>
/// <param name="Title">The caps title ("OPTIONS", "FRETBOARD LOOK").</param>
/// <param name="Build">Builds the content; the argument closes the panel (a Done button).</param>
public sealed record ScaleExtraSection(string Title, Func<Action, Control> Build);

/// <summary>A section that belongs to another host: shown muted with its title, a line of description and a link that opens that host's panel.</summary>
/// <param name="Id">Short id for control names ("Fretboard", "PianoRoll").</param>
/// <param name="LinkText">The link text when the other host can be opened ("Open the fretboard look").</param>
/// <param name="WaitText">The disabled link text when it cannot ("Open the fretboard first").</param>
/// <param name="CanOpen">True when the other host's control is on screen.</param>
/// <param name="Open">Opens the other host's panel.</param>
public sealed record ScaleGreyedSection(string Id, string Title, string Description, string LinkText, string WaitText, Func<bool> CanOpen, Action Open);

/// <summary>What the master scale panel shows and changes. The status bar, the fretboard and the piano roll each implement it; they differ only in the extras and the greyed sections.</summary>
public interface IScalePanelHost
{
    /// <summary>The readout the panel shows now.</summary>
    ScaleReadoutResult Current { get; }

    /// <summary>The line under the title ("Verse 1 · Lead Gtr · bars 1–8").</summary>
    string ContextLine { get; }

    /// <summary>The saved scale readout settings the panel edits.</summary>
    ScaleReadoutSettings Settings { get; }

    /// <summary>True when the song key is a flat key, so the panel spells roots with flats.</summary>
    bool Flats { get; }

    /// <summary>The user chose a scale (a detected card or the pick-another list).</summary>
    void Pick(ScaleMatch scale);

    /// <summary>Auto: forget the pick and go back to detection.</summary>
    void UseAuto();

    /// <summary>A setting changed in the panel: save it and read again.</summary>
    void SettingChanged();

    /// <summary>Raised after the readout changed, so an open panel shows it.</summary>
    event Action? Refreshed;

    /// <summary>The notes the readout reads, for the fit shown in the full list; null when there are none.</summary>
    PitchHistogram? Histogram { get; }

    /// <summary>True when the song has a selected bar range the readout can read instead of the section.</summary>
    bool HasSelection { get; }

    /// <summary>True when the panel puts its extras in a second column (the fretboard look on a wide window).</summary>
    bool TwoColumns { get; }

    /// <summary>The sections this host adds below the shared ones.</summary>
    IReadOnlyList<ScaleExtraSection> Extras { get; }

    /// <summary>The sections of the other hosts, shown muted with a link.</summary>
    IReadOnlyList<ScaleGreyedSection> Greyed { get; }
}
