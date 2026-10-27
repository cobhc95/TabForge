using System;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the rows of the snap settings popup: the On/Off switch (Enable snapping), the grid size, the "Snap to" check boxes and the snap distance.
//     Every change applies at once and calls the change callback; the distance commits on Enter, on leaving the box and when the popup closes.
// Does not own: the popup's look and opening (TransportPopupFrame), the texts, switches and distance rule (SnapSettingsPresenter), saving (the callback).
// Tests: TabForge.Avalonia.Tests.SnapSettingsPopupTests.
public static class SnapSettingsPopup
{
    public const string Title = "SNAP SETTINGS";
    private const string CloseTip = "Close snap settings";
    private const double DistanceTopMargin = 10;
    private const double DistanceBoxSize = 44;
    private const double DistanceUnitMargin = 8;
    private const double GridBoxSize = 90;
    private const double ChoiceTopMargin = 2;
    private const double CheckTopMargin = 2;
    private const double ChoiceRowMargin = 10;

    /// <param name="snap">The snap settings the rows read and write.</param>
    /// <param name="changed">Runs after every change (the caller refreshes the magnet and saves).</param>
    public static TransportPopupFrame Create(SnapSettings snap, Action changed)
    {
        var frame = new TransportPopupFrame(Title, CloseTip, TransportPopupFrame.WideWidth);
        frame.AddPower(SnapSettingsPresenter.EnableLabel, snap.Enabled, value => SnapSettingsPresenter.Enable.Set(snap, value), changed);

        var grid = new ComboBox { ItemsSource = SnapSettings.Grids, SelectedItem = snap.Grid, Width = GridBoxSize, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(grid, SnapSettingsPresenter.GridSizeTip);
        AutomationProperties.SetName(grid, SnapSettingsPresenter.GridSizeLabel);
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is string g) { snap.Grid = g; changed(); } };
        DockPanel.SetDock(grid, Dock.Right);
        frame.Rows.Children.Add(new DockPanel
            { Children = { grid, new TextBlock { Text = SnapSettingsPresenter.GridSizeLabel, VerticalAlignment = VerticalAlignment.Center } } }.WithMargin(0, ChoiceTopMargin, 0, 0));

        frame.Rows.Children.Add(new TextBlock { Text = SnapSettingsPresenter.SnapToHeading, FontWeight = global::Avalonia.Media.FontWeight.SemiBold }.WithMargin(0, ChoiceRowMargin, 0, 0));
        foreach (var spec in SnapSettingsPresenter.SnapTo)
        {
            var box = frame.AddCheck(spec.Label, spec.Get(snap), value => spec.Set(snap, value), changed, CheckTopMargin);
            box.WithMargin(spec.Indent, CheckTopMargin, 0, 0);
            ToolTip.SetTip(box, spec.Tip);
        }

        var distance = new TextBox { Text = snap.DistancePx.ToString(), Width = DistanceBoxSize, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(distance, SnapSettingsPresenter.DistanceTip);
        AutomationProperties.SetName(distance, SnapSettingsPresenter.DistanceLabel);
        void Commit()
        {
            var committed = SnapSettingsPresenter.CommitDistance(snap, distance.Text ?? "");
            distance.Text = snap.DistancePx.ToString();
            if (committed) changed();
        }
        distance.LostFocus += (_, _) => Commit();
        distance.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        var unit = new TextBlock { Text = SnapSettingsPresenter.DistanceUnit, VerticalAlignment = VerticalAlignment.Center }.WithMargin(DistanceUnitMargin, 0, 0, 0);
        var distanceLabel = new TextBlock { Text = SnapSettingsPresenter.DistanceLabel, VerticalAlignment = VerticalAlignment.Center }.WithMargin(0, 0, ChoiceRowMargin, 0);
        frame.Rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { distanceLabel, distance, unit } }.WithMargin(0, DistanceTopMargin, 0, 0));
        frame.Closed += () => { if (distance.Text != snap.DistancePx.ToString()) Commit(); };
        return frame;
    }
}
