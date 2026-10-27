using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the snap settings popup: it opens above the magnet button, its On/Off switch sets snapping, the grid size, the Snap to boxes and the distance
///     write the settings and report each change, and Escape closes it.
/// Does not own: the popup's frame (TransportSettingsPopupTests) or the snap rules (SnapSettingsPresenter).
/// Tests: this class.
/// </summary>
public class SnapSettingsPopupTests
{
    private static (Window Window, TransportBarView Bar) Open()
    {
        var bar = new TransportBarView { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom };
        var window = new Window { Width = 900, Height = 600, Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, bar);
    }

    [AvaloniaFact]
    public void OpeningSnapSettingsShowsThePopupAboveTheMagnetButton()
    {
        var (window, bar) = Open();
        var popup = bar.OpenSnapSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.Contains(popup.Rows.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == SnapSettingsPopup.Title);
        var magnet = bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Single(b => b.Item.Name == TransportBarView.SnapItem.Name);
        Assert.True(popup.Panel.PointToScreen(new Point(0, popup.Panel.Bounds.Height)).Y <= magnet.PointToScreen(new Point(0, 0)).Y);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheSwitchGridSizeAndBoxesWriteTheSettingsAndEscapeCloses()
    {
        var snap = new SnapSettings { DistancePx = 8, Enabled = false };
        var changes = 0;
        var window = new Window { Width = 900, Height = 600, Content = new Border() };
        window.Show();
        var popup = SnapSettingsPopup.Create(snap, () => changes++);
        popup.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        var power = popup.Rows.GetLogicalDescendants().OfType<ToggleSwitch>().Single();
        power.IsChecked = true;
        Assert.True(snap.Enabled);
        var grid = popup.Rows.GetLogicalDescendants().OfType<ComboBox>().Single();
        var other = SnapSettings.Grids.First(g => g != snap.Grid);
        grid.SelectedItem = other;
        Assert.Equal(other, snap.Grid);
        var boxes = popup.Rows.GetLogicalDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(SnapSettingsPresenter.SnapTo.Select(s => s.Label), boxes.Select(b => ((TextBlock)b.Content!).Text));
        boxes[3].IsChecked = false;
        Assert.False(snap.ToPlayhead);
        Assert.Equal(3, changes);
        popup.Rows.GetLogicalDescendants().OfType<TextBox>().Single().Text = "99";
        popup.Panel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
        Assert.Equal((40, 4), (snap.DistancePx, changes));
        window.Close();
    }
}
