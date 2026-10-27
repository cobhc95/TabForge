using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the score right-click menu of a Band card in the real main window: it offers the score rows, every row acts on the Band view's own
/// settings (lane content, lane zoom, outline, page layout) and none changes the main score.
/// Does not own: the rows (ScoreContextMenuTests), the cards' layout (BandCardTests).
/// Tests: BandScoreMenu.
/// </summary>
public class BandScoreMenuTests
{
    private static (MainWindow Main, BandView Band, ScorePreviewView MainScore) OpenBand()
    {
        var main = new MainWindow { Width = 2000, Height = 1100 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var score = main.GetVisualDescendants().OfType<ScorePreviewView>().First(s => s.FindAncestorOfType<BandView>() is null);
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == "View.BandView").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        return (main, main.GetVisualDescendants().OfType<BandView>().Single(), score);
    }

    private static void Pick(ScorePreviewView staff, string id, string? arg = null)
    {
        var row = staff.ContextRowsAt(new global::Avalonia.Point(5, 5)).SelectMany(r => r.Children ?? new System.Collections.Generic.List<MenuSpec> { r }).Single(r => r.Id == id && r.Arg == arg);
        staff.ContextMenus.Commands.Run(ContextMenuRows.CommandIdOf(row)!, row.Arg);
    }

    [AvaloniaFact]
    public void CardMenuChangesTheBandSettingsAndNotTheMainScore()
    {
        var (main, band, mainScore) = OpenBand();
        var staff = band.PanelFor(band.ShownTrackIndices[0])!.Staff;
        var rows = staff.ContextRowsAt(new global::Avalonia.Point(5, 5)).ToList();
        Assert.Contains(rows, r => r.Id == ScoreMenus.NotationId);
        var mainNotation = mainScore.Probe.Notation;
        var mainZoom = mainScore.Zoom;
        var mainOutline = mainScore.Probe.Appearance.ShowNoteOutline;

        Pick(staff, ScoreMenus.NotationId);
        Pick(staff, ScoreMenus.OutlineId);
        Pick(staff, ScoreMenus.ZoomInId);
        Pick(staff, ScoreMenus.LayoutId, "continuous");

        Assert.NotEqual(NotationMode.TabOnly, staff.Notation);
        Assert.All(band.ShownTrackIndices, i => Assert.Equal(staff.Notation, band.PanelFor(i)!.Staff.Notation));
        Assert.False(staff.Probe.Appearance.ShowNoteOutline);
        Assert.True(staff.Probe.Appearance.CenterSystems);
        Assert.True(band.ScoreMenu!.LaneZoom > 1);
        Assert.Equal(mainNotation, mainScore.Probe.Notation);
        Assert.Equal(mainZoom, mainScore.Zoom);
        Assert.Equal(mainOutline, mainScore.Probe.Appearance.ShowNoteOutline);
        main.Close();
    }

    [AvaloniaFact]
    public void CtrlWheelChangesTheLaneZoomAndOnlyTheFirstCardMayShowTwoLines()
    {
        var (main, band, mainScore) = OpenBand();
        var cards = band.ShownTrackIndices.Select(i => band.PanelFor(i)!).ToList();
        Assert.InRange(cards[0].SystemCount, BandTrackPanel.OtherCardSystems, BandTrackPanel.ShownSystems);
        Assert.All(cards.Skip(1), c => Assert.Equal(1, c.SystemCount));
        var before = cards[0].Staff.Zoom;
        band.ScoreMenu!.WheelZoom(true);
        Assert.True(cards[0].Staff.Zoom > before);
        Assert.Equal(1, mainScore.Zoom);
        main.Close();
    }

    [Fact]
    public void BandScoreRowsAreInPreferencesAndSetOnlyTheBandBlock()
    {
        var settings = new TabForge.Services.AppSettings();
        var rows = PreferencesCatalog.Build(settings).ToList();
        var outline = rows.Single(r => r.Key == "band.noteoutline");
        var continuous = rows.Single(r => r.Key == "band.continuous");
        Assert.Contains(rows, r => r.Key == BandScoreMenu.SettingsRow);
        outline.Set(false);
        continuous.Set(false);
        Assert.False(settings.Timeline.Band.ShowNoteOutline);
        Assert.False(settings.Timeline.Band.ContinuousLayout);
        Assert.True(settings.Appearance.ShowNoteOutline);
    }
}
