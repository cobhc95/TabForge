using System;
using Avalonia.Controls;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The one column layout of the Mixer: the column ids, the widths, and the grid every header and row is built with.
/// Pan and volume share the leftover width (weights 1:2) between their minimum and maximum widths; the other columns are fixed.
/// Wide and Medium share one set of columns; Compact drops MIDI and pitch, hides their cells and adds the row menu column.
/// Owns: the columns of the mixer grids and the width at which Compact begins. Does not own: the cells placed in them (MixerWindow).
/// Tests: TabForge.Avalonia.Tests.MixerColumnLayoutTests.
/// </summary>
public static class MixerColumnLayout
{
    /// <summary>The column ids, in order: name | FX | MIDI | pitch | pan | volume | mute and solo | row menu.</summary>
    public const int Name = 0, Fx = 1, Midi = 2, Pitch = 3, Pan = 4, Volume = 5, MuteSolo = 6, RowMenu = 7;

    /// <summary>One column: a fixed <see cref="Width"/> in DIP, or a star <see cref="Weight"/> between <see cref="MinWidth"/> and <see cref="MaxWidth"/>.</summary>
    public readonly record struct ColumnSpec(double Width, int Weight, double MinWidth, double MaxWidth);

    /// <summary>The row border's padding and thickness, the group panel's border and padding, and the header grid's margins: the horizontal insets the grids sit in.</summary>
    public const double RowBorderPadding = 4, RowBorderThickness = 2, PanelBorderThickness = 1, PanelPadding = 4;
    public const double HeaderMarginLeft = 9, HeaderMarginRight = 13;

    /// <summary>The width of the row menu column in Compact (a "..." button at the row end); zero in Wide and Medium.</summary>
    public const double CompactRowMenuWidth = 28;

    /// <summary>The horizontal inset of a row grid: the group panel and row border on both sides.</summary>
    public static readonly double RowHorizontalInset = 2 * (PanelBorderThickness + PanelPadding + RowBorderPadding + RowBorderThickness);

    /// <summary>The horizontal inset of the header grid: its left and right margins.</summary>
    public static readonly double HeaderHorizontalInset = HeaderMarginLeft + HeaderMarginRight;

    /// <summary>The columns in order; pan and volume split the leftover width at 1:2, capped at 240 and 480 DIP. RowMenu is 0 wide outside Compact.</summary>
    public static readonly ColumnSpec[] Columns =
    {
        new(190, 0, 0, 0), new(66, 0, 0, 0), new(58, 0, 0, 0), new(112, 0, 0, 0),
        new(0, 1, 80, 240), new(0, 2, 100, 480), new(84, 0, 0, 0), new(0, 0, 0, 0),
    };

    /// <summary>Wide and Medium: the shared columns without the row menu column (there is no room and no button there).</summary>
    public static readonly ColumnSpec[] WideColumns = Columns[..^1];

    /// <summary>Compact: MIDI and pitch collapse to 0 and the row menu column takes its 28 DIP at the row end.</summary>
    public static readonly ColumnSpec[] CompactColumns = BuildCompactColumns();

    private static ColumnSpec[] BuildCompactColumns()
    {
        var columns = (ColumnSpec[])Columns.Clone();
        columns[Midi] = columns[Midi] with { Width = 0 };
        columns[Pitch] = columns[Pitch] with { Width = 0 };
        columns[RowMenu] = new ColumnSpec(CompactRowMenuWidth, 0, 0, 0);
        return columns;
    }

    /// <summary>The columns of a mode: Wide and Medium share one grid, Compact its own.</summary>
    public static ColumnSpec[] ColumnsFor(bool compact) => compact ? CompactColumns : WideColumns;

    /// <summary>
    /// The smallest rows-area width that still holds Medium: every fixed column plus the pan and volume minimums, plus the
    /// larger of the row and header horizontal insets (the grids sit inside them, so a narrower rows area cannot fit the columns).
    /// </summary>
    public static readonly double MinMediumWidth =
        Columns[Name].Width + Columns[Fx].Width + Columns[Midi].Width + Columns[Pitch].Width + Columns[MuteSolo].Width
        + Columns[Pan].MinWidth + Columns[Volume].MinWidth
        + Math.Max(RowHorizontalInset, HeaderHorizontalInset);

    /// <summary>
    /// The smallest rows-area width that still holds the Compact always-visible set: name, FX, pan and volume minimums,
    /// mute/solo and the row menu, plus the larger of the row and header horizontal insets.
    /// </summary>
    public static readonly double MinCompactWidth =
        Columns[Name].Width + Columns[Fx].Width + Columns[Pan].MinWidth + Columns[Volume].MinWidth + Columns[MuteSolo].Width + CompactRowMenuWidth
        + Math.Max(RowHorizontalInset, HeaderHorizontalInset);

    /// <summary>A grid with exactly the Mixer columns: the header grid and every row grid is made with this.</summary>
    public static Grid NewGrid() => NewGrid(compact: false);

    /// <summary>A grid with exactly the Mixer columns of the mode: the header grid and every row grid is made with this.</summary>
    public static Grid NewGrid(bool compact)
    {
        var grid = new Grid();
        foreach (var column in ColumnsFor(compact))
        {
            var definition = new ColumnDefinition(column.Weight > 0
                ? new GridLength(column.Weight, GridUnitType.Star)
                : new GridLength(column.Width));
            if (column.Weight > 0)
            {
                definition.MinWidth = column.MinWidth;
                definition.MaxWidth = column.MaxWidth;
            }
            grid.ColumnDefinitions.Add(definition);
        }
        return grid;
    }
}
