using System;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the capos on the fretboard: the full capo (and each partial capo, clipped to the strings it covers) at its fret, in the chosen finish,
//   with its cast shadow, overhanging the board's edges as a real clamp wraps the neck. The bars are drawn through the shared
//   <see cref="FretboardBoardPainter"/> (the Capo card's board draws the same bars); the artwork ratios live there too.
// Does not own: the wood, binding, inlays, wires and strings (FretboardArtist), the board geometry (Core FretboardGeometryRules) or the capo
//   drag (InstrumentPanelView.CapoDrag.cs).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public sealed partial class InstrumentPanelView
{
    /// <summary>This panel decodes its own textures, and only the ones its look uses.</summary>
    private readonly FretboardAssets _assets = new();
    private readonly FretboardInlayPlan _inlays = new();

    private void DrawCapo(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout)
    {
        if (_track is not { } track) return;
        int? badge = null;
        var badgePartial = -1;
        if (_capoDrag is not null)
        {
            badge = _capoDrag.CurrentFret;
            badgePartial = _partialPreviewIndex;
        }
        FretboardBoardPainter.DrawCapos(d, ink, layout, _look.Style, FretInput.Bass, _assets,
            _capoPreview ?? track.Capo, track.PartialCapos, _partialPreviewIndex, _partialPreviewFret,
            badge, badgePartial, _look.FretNumberScale, Font);
    }

    /// <summary>The drag landing badge: a solid accent pill above the bar with the landing fret's real number.</summary>
    internal static void DrawLandingBadge(IDrawContext d, Func<string, UiColor> ink, UiColor accent, UiRect bar, int fret, double numberScale) =>
        FretboardBoardPainter.DrawLandingBadge(d, ink, accent, bar, fret, numberScale, Font);
}
