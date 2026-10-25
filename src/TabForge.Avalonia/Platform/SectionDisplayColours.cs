// Owns: the display colour of each section flag on the timeline ruler and of each section in the Sections pane, resolved by one rule so
//   both show the same colour (own colour tamed unless chosen exactly, one colour per section family, distinct palette colours when families look alike).
// Does not own: the colour rules (Core SectionColourPresenter), the marker storage, the flag drawing (TimelineView) or the list rows (SectionsPanelView).
// Tests: SectionColourPresenterTests (rules).
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>Resolves the section colours the timeline and the Sections pane draw. Thread: UI thread; holds no state.</summary>
public static class SectionDisplayColours
{
    /// <summary>Palette key of the colour a section takes when it has no stored colour.</summary>
    private const string SectionDefaultKey = "SectionDefault";

    /// <summary>Display colour per section, index-aligned with <paramref name="sortedSections"/> (sections in bar order).</summary>
    public static Rgba[] Resolve(IReadOnlyList<MarkerModel> sortedSections, bool matchSimilarNames = true) =>
        SectionColourPresenter.Resolve(sortedSections, matchSimilarNames, PaletteRgba(SectionDefaultKey), SectionColourPresenter.DistinctPaletteNames.Select(PaletteRgba).ToList());

    /// <summary>
    /// Writes each section's currently displayed colour into the section as an explicit colour, so the colours survive a reorder.
    /// Call inside the same edit as the reorder (one undo step). Mutates the markers; UI thread.
    /// </summary>
    public static void FreezeDisplayedColours(SongProject project)
    {
        var sorted = SectionListPresenter.InBarOrder(project.Markers);
        var colours = Resolve(sorted);
        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i].ColorHex = ColourHex.Format(colours[i]);
            sorted[i].ColorIsExplicit = true;
        }
    }

    private static Rgba PaletteRgba(string name)
    {
        var colour = Palette.Get(name);
        return new Rgba(colour.R, colour.G, colour.B, colour.A);
    }
}
