using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the fretboard's view of the master scale panel: its FRETBOARD LOOK extra (show on / off, five styles, colours, opacity, extras, Clear highlight) and the
//   muted piano roll section with its link. A pick made here always sticks to the section and track and lights the fretboard at once.
// Does not own: the shared sections (ScalePanel), the look rows (FretboardLookSection), the readout (StatusScaleReadout).
// Tests: TabForge.Avalonia.Tests.ScalePanelHostTests, FretboardScalePopupTests.
internal sealed class FretboardScaleHost : ReadoutScaleHost
{
    private const string LookTitle = "FRETBOARD LOOK";
    private readonly bool _wide;

    /// <param name="wide">True on a wide window: the look takes a second column.</param>
    public FretboardScaleHost(StatusScaleReadout readout, ScaleHostLinks links, bool wide = false) : base(readout, links) => _wide = wide;

    public override bool TwoColumns => _wide;

    public override void Pick(ScaleMatch scale) => Readout.PickFor(scale, keep: true, fromFretboard: true);

    public override IReadOnlyList<ScaleExtraSection> Extras => Readout.Editing is { } editing
        ? new[] { new ScaleExtraSection(LookTitle, close => new FretboardLookSection(this, editing, close, Readout.ClearHighlight, Readout.LookChanged).Build()) }
        : System.Array.Empty<ScaleExtraSection>();

    public override IReadOnlyList<ScaleGreyedSection> Greyed => new[] { PianoRollLink() };
}
