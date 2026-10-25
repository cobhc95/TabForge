using System;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the size a Band card's fretboard keeps: the user's chosen width (BandSettings.InstrumentWidth, saved with the settings, 0 = automatic), and the height-to-width
//   rule that keeps the board's real proportions (the card is as wide as the board needs at the chosen height: its real width, the close-button reserve).
// Does not own: the card that shows it (BandTrackPanel), the automatic size (BandCardFit), the drag (BandFretboardGrip), the settings file, the board's proportions (FretboardGeometryRules).
// Tests: TabForge.Avalonia.Tests.BandCardFitTests, TabForge.Avalonia.Tests.FretboardRealProportionTests.
internal sealed class BandFretboardSize
{
    private readonly Func<BandSettings> _band;
    private readonly Action _save;

    /// <param name="band">The band settings that hold the width.</param>
    /// <param name="save">Writes the settings after the user picks a size.</param>
    public BandFretboardSize(Func<BandSettings> band, Action save)
    {
        _band = band;
        _save = save;
    }

    /// <summary>The smallest and largest drawing scale of a card's fretboard: half size (the fret and string numbers stay apart) to the board's own full size.</summary>
    public const double MinScale = 0.5, MaxScale = 1;

    /// <summary>The height a card's fretboard starts at (and the automatic size never passes): the dock panel's default height.</summary>
    public const double AutoMaxHeight = InstrumentPanelView.PanelHeight;

    /// <summary>The smallest height a board of <paramref name="strings"/> strings is drawn at (the smallest drawing scale).</summary>
    public static double MinHeight(int strings) => MinScale * FretboardScaleRules.NaturalHeight(strings, 1);

    /// <summary>The largest height a board of <paramref name="strings"/> strings grows to (the largest drawing scale).</summary>
    public static double MaxHeight(int strings) => MaxScale * FretboardScaleRules.NaturalHeight(strings, 1);

    /// <summary>The pane width that draws a board of this shape at <paramref name="height"/> with its real proportions, not squeezed.</summary>
    public static double WidthFor(double height, FretboardGeometryRules.Input shape)
    {
        var natural = FretboardScaleRules.NaturalHeight(shape.Strings, 1);
        return height / natural * FretboardGeometryRules.PaneWidthForHeight(shape, natural) + InstrumentPanelView.CloseButtonReserve;
    }

    /// <summary>The height whose proportional pane is <paramref name="width"/> wide (the inverse of <see cref="WidthFor"/>).</summary>
    private static double HeightFor(double width, FretboardGeometryRules.Input shape)
    {
        var natural = FretboardScaleRules.NaturalHeight(shape.Strings, 1);
        return (width - InstrumentPanelView.CloseButtonReserve) / FretboardGeometryRules.PaneWidthForHeight(shape, natural) * natural;
    }

    /// <summary>The height the user chose for a board of this shape, or null while the size is automatic.</summary>
    public double? UserHeight(FretboardGeometryRules.Input shape) =>
        _band().InstrumentWidth is > 0 and var width ? Math.Clamp(HeightFor(width, shape), MinHeight(shape.Strings), MaxHeight(shape.Strings)) : null;

    /// <summary>Keeps <paramref name="height"/> as the user's size for every board; <see cref="Commit"/> saves it.</summary>
    public void Choose(double height, FretboardGeometryRules.Input shape) =>
        _band().InstrumentWidth = Math.Round(WidthFor(Math.Clamp(height, MinHeight(shape.Strings), MaxHeight(shape.Strings)), shape));

    /// <summary>Goes back to the automatic size and saves it.</summary>
    public void Reset()
    {
        _band().InstrumentWidth = 0;
        _save();
    }

    /// <summary>Saves the size the user chose.</summary>
    public void Commit() => _save();
}
