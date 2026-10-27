using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: in the Band view, each card's first system starts at the card's left padding, so the staff fills the card width and no empty band
/// sits on the card's left.
/// Does not own: the bar widths the cards share (BandView.ShareBars), the staff's page margin and centring (ScorePreviewView, ScoreGeometryProbe).
/// Tests: TabForge.Avalonia.Tests.BandCardStartTests.
/// </summary>
public sealed class BandCardStartTests
{
    /// <summary>The largest distance allowed between a card's left padding and its first system, in DIP.</summary>
    private const double StartTolerance = 30;

    private static void Pick(MainWindow main, string commandId)
    {
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == commandId).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
    }

    [AvaloniaFact]
    public void FirstSystemOfEachCardStartsAtItsLeftPadding()
    {
        var main = new MainWindow { Width = 2000, Height = 1100 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        Pick(main, "View.BandView");
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();

        var cards = main.GetVisualDescendants().OfType<BandTrackPanel>().ToList();
        Assert.NotEmpty(cards);
        foreach (var card in cards)
        {
            var staff = card.Staff;
            var viewer = staff.FindAncestorOfType<ScrollViewer>()!;
            var systems = staff.Probe.Layout.GetLayout(staff.Probe.Track!).Systems;
            var pageWidth = staff.Probe.ContentSize().Width * staff.Zoom;
            var pageOffset = staff.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Center ? Math.Max(0, (viewer.Viewport.Width - pageWidth) / 2) : 0;
            var staffLeft = staff.TranslatePoint(default, main)!.Value.X;
            var firstSystemLeft = staffLeft + pageOffset + systems[0].X * staff.Zoom;
            var paddingLeft = card.TranslatePoint(new Point(card.BorderThickness.Left + card.Padding.Left, 0), main)!.Value.X;
            Assert.True(Math.Abs(firstSystemLeft - paddingLeft) <= StartTolerance,
                $"{card.TrackIndex}: the first system starts at {firstSystemLeft - paddingLeft:F1} px from the card's left padding (staff {staffLeft:F1}, page offset {pageOffset:F1}, system {systems[0].X * staff.Zoom:F1}, viewport {viewer.Viewport.Width:F1})");
        }
        main.Close();
    }
}
