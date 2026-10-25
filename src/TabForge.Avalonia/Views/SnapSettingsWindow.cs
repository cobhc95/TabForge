using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Snap / grid settings: Enable snapping, the grid size, the "Snap to" switches and the snap distance, then Close. Every change applies at
/// once and calls the change callback; the distance commits on Enter, on leaving the box and on closing.
/// Owns: the window's controls and layout. Does not own: the texts, switches and distance rule (<see cref="SnapSettingsPresenter"/>) or
/// saving (the caller's change callback).
/// Tests: TabForge.Avalonia.Tests.ClipSnapPluginDialogTests.
/// </summary>
public sealed class SnapSettingsWindow : Window
{
    public SnapSettingsWindow(SnapSettings snap, Action changed)
    {
        Title = SnapSettingsPresenter.Title;
        Width = 430; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        var root = new StackPanel { Margin = new Thickness(16) };

        void Switch(SnapSettingsPresenter.Switch spec)
        {
            var box = new CheckBox { Content = spec.Label, IsChecked = spec.Get(snap), Margin = new Thickness(spec.Indent, 4, 0, 4) };
            ToolTip.SetTip(box, spec.Tip);
            box.IsCheckedChanged += (_, _) => { spec.Set(snap, box.IsChecked == true); changed(); };
            root.Children.Add(box);
        }
        Switch(SnapSettingsPresenter.Enable);

        var grid = new ComboBox { ItemsSource = SnapSettings.Grids, SelectedItem = snap.Grid, Width = 90 };
        ToolTip.SetTip(grid, SnapSettingsPresenter.GridSizeTip);
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is string g) { snap.Grid = g; changed(); } };
        root.Children.Add(Row(SnapSettingsPresenter.GridSizeLabel, grid));

        root.Children.Add(new TextBlock { Text = SnapSettingsPresenter.SnapToHeading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
        foreach (var spec in SnapSettingsPresenter.SnapTo) Switch(spec);

        var distance = new TextBox { Text = snap.DistancePx.ToString(), Width = 44, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(distance, SnapSettingsPresenter.DistanceTip);
        void Commit()
        {
            var committed = SnapSettingsPresenter.CommitDistance(snap, distance.Text ?? "");
            distance.Text = snap.DistancePx.ToString();
            if (committed) changed();
        }
        distance.LostFocus += (_, _) => Commit();
        distance.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        var distanceRow = Row(SnapSettingsPresenter.DistanceLabel, distance);
        distanceRow.Children.Add(new TextBlock { Text = SnapSettingsPresenter.DistanceUnit, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(distanceRow);

        var close = new Button { Content = SnapSettingsPresenter.CloseButton, IsDefault = true, IsCancel = true, MinWidth = DialogMetrics.DialogButtonMinWidth, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        Content = root;
        Closed += (_, _) => Commit();
    }

    private static StackPanel Row(string label, Control editor) => new()
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4),
        Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, editor },
    };
}
