using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the column checks of the Mixer's one column layout: the widths of the header grid, and that every row grid shares them.
/// Does not own: the window, the rows, or the layout itself (MixerColumnLayout).
/// Tests: this class.
/// </summary>
public class MixerColumnLayoutTests
{
    /// <summary>The song as the window edits it: a document of its own, as MixerWindowTests makes it.</summary>
    private static SongDocument DocumentOf(SongProject project) => new(() => null) { Project = project };

    [AvaloniaFact]
    public void ColumnsAreTheMixerWidths()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 1000 };
        window.Show();
        window.EnsureStripsBuilt();

        var header = HeaderGrid(window);
        var pan = MixerColumnLayout.Columns[MixerColumnLayout.Pan];
        var volume = MixerColumnLayout.Columns[MixerColumnLayout.Volume];
        Assert.Equal((1, 80d, 240d), (pan.Weight, pan.MinWidth, pan.MaxWidth));
        Assert.Equal((2, 100d, 480d), (volume.Weight, volume.MinWidth, volume.MaxWidth));
        Assert.All(
            new[] { MixerColumnLayout.Name, MixerColumnLayout.Fx, MixerColumnLayout.Midi, MixerColumnLayout.Pitch, MixerColumnLayout.MuteSolo },
            column => Assert.Equal(0, MixerColumnLayout.Columns[column].Weight));
        Assert.Equal(
            new double[] { 190, 66, 58, 112, 84 },
            new[] { MixerColumnLayout.Name, MixerColumnLayout.Fx, MixerColumnLayout.Midi, MixerColumnLayout.Pitch, MixerColumnLayout.MuteSolo }
                .Select(column => MixerColumnLayout.Columns[column].Width));
        Assert.Equal(
            new[]
            {
                (GridUnitType.Pixel, 190d), (GridUnitType.Pixel, 66d), (GridUnitType.Pixel, 58d), (GridUnitType.Pixel, 112d),
                (GridUnitType.Star, 1d), (GridUnitType.Star, 2d), (GridUnitType.Pixel, 84d),
            },
            header.ColumnDefinitions.Select(Width));
        Assert.Equal((80d, 240d), (header.ColumnDefinitions[MixerColumnLayout.Pan].MinWidth, header.ColumnDefinitions[MixerColumnLayout.Pan].MaxWidth));
        Assert.Equal((100d, 480d), (header.ColumnDefinitions[MixerColumnLayout.Volume].MinWidth, header.ColumnDefinitions[MixerColumnLayout.Volume].MaxWidth));
        window.Close();
    }

    [AvaloniaFact]
    public void HeaderAndTrackRowCellsAlignAtMediumWidth()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 900 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();

        var header = HeaderGrid(window);
        var trackRow = window.GetLogicalDescendants().OfType<Border>().First(b => b.Name?.StartsWith("Mixer strip: ") == true);
        var rowGrid = Assert.IsType<Grid>(trackRow.Child);
        var headerX = header.TranslatePoint(default, window)!.Value.X;
        var rowX = rowGrid.TranslatePoint(default, window)!.Value.X;
        // The header sits 9 DIP in and the rows 11 DIP (panel padding); move the rows onto the header before comparing centres.
        var inset = headerX - rowX;
        foreach (var column in new[] { MixerColumnLayout.Name, MixerColumnLayout.Fx, MixerColumnLayout.Pan, MixerColumnLayout.Volume, MixerColumnLayout.MuteSolo })
        {
            var headerCentre = CenterX(header, column) + headerX;
            var rowCentre = CenterX(rowGrid, column) + rowX + inset;
            // The header and the rows have the same grid width (the header's extra 2 DIP on the left is balanced on the right),
            // so every column, the two star columns included, gets the same width in both and the cells share their centres.
            Assert.InRange(Math.Abs(headerCentre - rowCentre), 0, 1.5);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void PanAndVolumeAreCappedAtWideWidth()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 1400 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();

        var header = HeaderGrid(window);
        Assert.InRange(header.ColumnDefinitions[MixerColumnLayout.Pan].ActualWidth, 0, 241);
        Assert.InRange(header.ColumnDefinitions[MixerColumnLayout.Volume].ActualWidth, 0, 481);
        window.Close();
    }

    [AvaloniaFact]
    public void HeaderAndTrackRowShareTheColumnDefinitions()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create()));
        window.Show();
        window.EnsureStripsBuilt();

        var header = HeaderGrid(window);
        var trackRow = window.GetLogicalDescendants().OfType<Border>().First(b => b.Name?.StartsWith("Mixer strip: ") == true);
        var rowGrid = Assert.IsType<Grid>(trackRow.Child);
        Assert.Equal(header.ColumnDefinitions.Select(Width), rowGrid.ColumnDefinitions.Select(Width));
        window.Close();
    }

    [AvaloniaFact]
    public void WideModeAtOneThousandKeepsTheMidiAndPitchColumns()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 1000 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();
        window.UpdateLayout();

        Assert.False(window.IsCompact);
        var header = HeaderGrid(window);
        Assert.Equal(58d, header.ColumnDefinitions[MixerColumnLayout.Midi].ActualWidth);
        Assert.Equal(112d, header.ColumnDefinitions[MixerColumnLayout.Pitch].ActualWidth);
        Assert.Equal(0d, MixerColumnLayout.Columns[MixerColumnLayout.RowMenu].Width);
        window.Close();
    }

    [AvaloniaFact]
    public void RowsFitAtOneThousandWithoutAHorizontalScrollbar()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 1000 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();
        window.UpdateLayout();

        var viewport = RowsViewport(window);
        Assert.False(window.IsCompact);
        Assert.True(viewport.Extent.Width <= viewport.Viewport.Width);
        window.Close();
    }

    [AvaloniaFact]
    public void RowsContentKeepsTheCompactMinimumAndScrollsSideways()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 560 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();

        var viewport = RowsViewport(window);
        Assert.Equal(MixerColumnLayout.MinCompactWidth, Assert.IsType<StackPanel>(viewport.Content).MinWidth);
        Assert.Equal(ScrollBarVisibility.Auto, viewport.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, viewport.VerticalScrollBarVisibility);
        window.Close();
    }

    [AvaloniaFact]
    public void CompactModeAtFiveSixtyCollapsesMidiAndPitchAndAddsTheRowMenu()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 560 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();
        window.UpdateLayout();

        Assert.True(window.IsCompact);
        var header = HeaderGrid(window);
        Assert.Equal(0d, header.ColumnDefinitions[MixerColumnLayout.Midi].ActualWidth);
        Assert.Equal(0d, header.ColumnDefinitions[MixerColumnLayout.Pitch].ActualWidth);
        Assert.Equal(28d, header.ColumnDefinitions[MixerColumnLayout.RowMenu].ActualWidth);

        var trackRow = window.GetLogicalDescendants().OfType<Border>().First(b => b.Name?.StartsWith("Mixer strip: ") == true);
        var rowGrid = Assert.IsType<Grid>(trackRow.Child);
        Assert.Equal(header.ColumnDefinitions.Select(Width), rowGrid.ColumnDefinitions.Select(Width));
        Assert.Equal(28d, rowGrid.ColumnDefinitions[MixerColumnLayout.RowMenu].ActualWidth);

        var menu = trackRow.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RowMenu");
        Assert.True(menu.IsVisible);
        Assert.False(trackRow.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Name == "MidiSound").IsVisible);
        Assert.False(trackRow.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "Semitones").IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void WideTopBarShowsTheTrackListCheckboxesAndNoTopBarMenu()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 1000 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();
        window.UpdateLayout();

        Assert.False(window.IsCompact);
        var boxes = TrackListBoxes(window);
        Assert.Equal(3, boxes.Length);
        Assert.All(boxes, box => Assert.True(box.IsEffectivelyVisible));
        Assert.False(TopBarMenu(window).IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void CompactTopBarHidesTheTrackListCheckboxesBehindATopBarMenu()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 560 };
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();
        window.UpdateLayout();

        Assert.True(window.IsCompact);
        var boxes = TrackListBoxes(window);
        Assert.Equal(3, boxes.Length);
        Assert.All(boxes, box => Assert.False(box.IsEffectivelyVisible));
        var button = TopBarMenu(window);
        Assert.True(button.IsEffectivelyVisible);
        Assert.NotNull(button.ContextMenu); // the three check items are built on opening, which headless windows do not raise: checked by hand
        window.Close();
    }

    [AvaloniaFact(Skip = "Headless window resize after Show does not change the viewport; checked by hand in the real window (see Queue.md)")]
    public void LayoutModeFollowsTheAvailableWidthBothWays()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create())) { Width = 1000 };
        window.Show();
        window.UpdateLayout();
        window.UpdateLayout();
        Assert.False(window.IsCompact);

        window.Width = 560; window.Arrange(new global::Avalonia.Rect(0, 0, 560, window.Height));
        window.UpdateLayout();
        window.UpdateLayout();
        Assert.True(window.IsCompact);

        window.Width = 1000; window.Arrange(new global::Avalonia.Rect(0, 0, 1000, window.Height));
        window.UpdateLayout();
        window.UpdateLayout();
        Assert.False(window.IsCompact);
        window.Close();
    }

    private static double CenterX(Grid grid, int column) =>
        grid.ColumnDefinitions.Take(column).Sum(c => c.ActualWidth) + grid.ColumnDefinitions[column].ActualWidth / 2;

    private static Grid HeaderGrid(Window window) =>
        window.GetLogicalDescendants().OfType<Grid>().Single(g => g.Name == "ColumnHeader");

    private static ScrollViewer RowsViewport(Window window) =>
        window.GetLogicalDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel { Name: "MixerGroups" });

    private static CheckBox[] TrackListBoxes(Window window) =>
        window.GetLogicalDescendants().OfType<CheckBox>().Where(c => c.Name is "Show-volume" or "Show-pan" or "Show-groups").ToArray();

    private static Button TopBarMenu(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "TopBarMenu");

    private static (GridUnitType Kind, double Value) Width(ColumnDefinition column) =>
        (column.Width.GridUnitType, column.Width.Value);
}
