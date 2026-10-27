using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Choose instrument tile building: the window shows with its first family built and the rest in background slices; the search filters
/// tiles built late; each tile has the hover shade and no cursor; a second open reuses the badge cache.
/// Owns: the checks of those behaviours. Does not own: the window (InstrumentPickerWindow), the hover shade (HoverShade) or the badge cache
/// (InstrumentBadgeView).
/// Tests: this class.
/// </summary>
public class InstrumentPickerTileBuildTests
{
    /// <summary>The most tiles the window may build before it is shown: the first family only.</summary>
    private const int MaxTilesBeforeFirstPaint = 40;

    [AvaloniaFact]
    public void OnlyTheFirstFamilyIsBuiltWhenTheWindowShowsAndTheSearchFiltersTilesBuiltLate()
    {
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), null);
        window.Show();
        var builtAtShow = BuiltTiles(window).Count;
        Assert.InRange(builtAtShow, 1, MaxTilesBeforeFirstPaint);

        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "drum";
        RunQueuedUiWork();

        var tiles = BuiltTiles(window);
        Assert.Equal(InstrumentCatalog.All.Count, tiles.Count);
        var drumMatches = InstrumentPickerWindow.CatalogueEntries().Count(e => (e.Name + " " + e.Family).Contains("drum", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(drumMatches, tiles.Count(t => t.IsVisible));
        window.Close();
    }

    [AvaloniaFact]
    public void InstrumentTileShowsTheHoverShadeAndNoTileHasACursor()
    {
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.SampleEntries, null);
        window.Show();
        RunQueuedUiWork();
        window.UpdateLayout();

        var tiles = BuiltTiles(window);
        Assert.All(tiles, tile => Assert.Null(tile.Cursor));

        var tile = tiles.First();
        var rest = ColourOf(tile.Background);
        Assert.Equal(0, rest.A);
        var centre = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;
        window.MouseMove(centre);
        Assert.Equal(ColourOf(HoverShade.Brush(tile)), ColourOf(tile.Background));
        Assert.NotEqual(rest, ColourOf(tile.Background));
        window.Close();
    }

    [AvaloniaFact]
    public void SecondOpenReusesTheBadgeCache()
    {
        var first = OpenWithEveryBadgeRead();
        var readsAfterFirstOpen = InstrumentBadgeView.PreparedCount;
        first.Close();

        var second = OpenWithEveryBadgeRead();
        Assert.Equal(readsAfterFirstOpen, InstrumentBadgeView.PreparedCount);
        second.Close();
    }

    private static InstrumentPickerWindow OpenWithEveryBadgeRead()
    {
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), null);
        window.Show();
        RunQueuedUiWork();
        var badges = window.GetLogicalDescendants().OfType<InstrumentBadgeView>().ToList();
        Assert.True(badges.Count(badge => badge.HasArt) > 0, "no instrument badge has art");
        return window;
    }

    private static List<Border> BuiltTiles(Window window) =>
        window.GetLogicalDescendants().OfType<Border>().Where(b => ToolTip.GetTip(b) is string).ToList();

    private static Color ColourOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;
}
