using System;
using System.Collections.Generic;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the part of a scale host that reads the status bar's scale readout (the section at the playhead, the one shared pick): the readout, the settings, the
//   refresh event and the pick calls. The status bar and the fretboard hosts derive from it and add their extras, their greyed sections and how a pick is kept.
// Does not own: the detection and picks (StatusScaleReadout, ScaleReadoutEngine), the panel (ScalePanel).
// Tests: TabForge.Avalonia.Tests.ScalePanelHostTests.
internal abstract class ReadoutScaleHost : IScalePanelHost
{
    protected ReadoutScaleHost(StatusScaleReadout readout, ScaleHostLinks links) => (Readout, Links) = (readout, links);

    protected StatusScaleReadout Readout { get; }

    protected ScaleHostLinks Links { get; }

    public ScaleReadoutResult Current => Readout.Current;

    public string ContextLine => Readout.Current.Context.Text;

    public ScaleReadoutSettings Settings => Readout.Settings;

    public bool Flats => Readout.Flats;

    public PitchHistogram? Histogram => Readout.Histogram;

    public bool HasSelection => Readout.HasSelection;

    public virtual bool TwoColumns => false;

    public event Action? Refreshed
    {
        add => Readout.Refreshed += value;
        remove => Readout.Refreshed -= value;
    }

    public abstract void Pick(ScaleMatch scale);

    public void UseAuto() => Readout.UseAuto();

    public void SettingChanged() => Readout.SettingChanged();

    public abstract IReadOnlyList<ScaleExtraSection> Extras { get; }

    public abstract IReadOnlyList<ScaleGreyedSection> Greyed { get; }

    /// <summary>The muted fretboard look section with a link that opens the fretboard host.</summary>
    protected ScaleGreyedSection FretboardLink() => new("Fretboard", "FRETBOARD LOOK", "How the scale is drawn on the fretboard and keyboard.",
        "Open the fretboard look", "Open the fretboard first", Links.FretboardAvailable, Links.OpenFretboard);

    /// <summary>The muted piano roll section with a link that opens the piano roll host.</summary>
    protected ScaleGreyedSection PianoRollLink() => new("PianoRoll", "PIANO ROLL OPTIONS", "Highlight, rows and lock on the piano roll grid.",
        "Open the piano roll scale options", "Open the piano roll first", Links.PianoRollAvailable, Links.OpenPianoRoll);
}
