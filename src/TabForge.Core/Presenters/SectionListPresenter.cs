// Owns: the section list's rules on plain values: the rows in bar order, and each row's fill and opacity in its rest, hover and selected state.
// Does not own: the colours themselves (SectionColourPresenter), the list control, the hover and selection events, or drawing.
// Tests: SectionColourPresenterTests.
using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>The three looks a section row has in the list.</summary>
public enum SectionRowState
{
    Rest,
    Hover,
    Selected,
}

/// <summary>Section list rules. Thread: any; holds no state.</summary>
public static class SectionListPresenter
{
    /// <summary>Alpha of a resting row's fill (over the desaturated colour).</summary>
    public const byte RestAlpha = 170;

    /// <summary>Alpha of a hovered row's fill.</summary>
    public const byte HoverAlpha = 215;

    /// <summary>Alpha of a selected row's fill.</summary>
    public const byte SelectedAlpha = 255;

    /// <summary>Opacity of a resting row; hovered and selected rows are fully opaque.</summary>
    public const double RestOpacity = 0.82;

    /// <summary>The sections in bar order (a stable sort on the bar each starts at).</summary>
    public static List<MarkerModel> InBarOrder(IEnumerable<MarkerModel> markers) => markers.OrderBy(marker => marker.MeasureIndex).ToList();

    /// <summary>The fill of a row whose section has the display colour <paramref name="colour"/>, in the given state.</summary>
    public static Rgba RowFill(Rgba colour, SectionRowState state) => state switch
    {
        SectionRowState.Hover => new Rgba(colour.R, colour.G, colour.B, HoverAlpha),
        SectionRowState.Selected => new Rgba(colour.R, colour.G, colour.B, SelectedAlpha),
        _ => Dimmed(colour) with { A = RestAlpha },
    };

    /// <summary>The opacity of a row in the given state.</summary>
    public static double RowOpacity(SectionRowState state) => state == SectionRowState.Rest ? RestOpacity : 1.0;

    /// <summary>A colour desaturated and darkened for resting rows: 45 % of the colour is replaced by its grey, then the result is scaled to 80 %.</summary>
    public static Rgba Dimmed(Rgba colour)
    {
        var grey = colour.R * 0.3 + colour.G * 0.59 + colour.B * 0.11;
        byte Mix(byte channel) => (byte)Math.Round((channel * 0.55 + grey * 0.45) * 0.8);
        return new Rgba(Mix(colour.R), Mix(colour.G), Mix(colour.B), colour.A);
    }
}
