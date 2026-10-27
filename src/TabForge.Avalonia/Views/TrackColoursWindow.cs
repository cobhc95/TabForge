using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Colour tracks window: tick track rows (or give a range), then click a palette colour to colour the ticked rows. Each change
/// shows at once; the caller's undo capture runs before the first one.
/// Owns: building the window (intro, the range pair with Select range, one check box and swatch per track, the palette buttons, Close),
///     and wiring its ticks and colouring to the tracks it is given.
/// Does not own: the tick and colour rules and the palette (TrackColoursPresenter), the track model and the undo stack (the caller).
/// Live data: the track list row passes the shown song's tracks, its undo capture and its change mark.
/// Tests: TabForge.Avalonia.Tests.TrackColoursWindowTests.
/// </summary>
public sealed class TrackColoursWindow : Window
{
    private readonly TrackColoursPresenter _presenter;
    private readonly IReadOnlyList<TrackModel> _tracks;
    private readonly Action? _beforeFirstChange;
    private readonly Action? _changed;
    private readonly List<CheckBox> _boxes = new();
    private readonly List<Border> _swatches = new();

    public TrackColoursWindow(IReadOnlyList<TrackModel> tracks, Action? beforeFirstChange = null, Action? changed = null)
    {
        _tracks = tracks;
        _beforeFirstChange = beforeFirstChange;
        _changed = changed;
        _presenter = new TrackColoursPresenter(tracks.Count);

        Title = "Colour tracks";
        Width = 460;
        Height = 540;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var root = new DockPanel { Margin = new Thickness(14) };
        var intro = new TextBlock
        {
            Name = "IntroText", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "Tick the tracks to colour (or choose a range), then click a colour. Repeat for other tracks.",
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        var range = BuildRangeRow();
        DockPanel.SetDock(range, Dock.Top);
        root.Children.Add(range);
        var bottom = BuildBottom();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Name = "TrackList", Content = BuildTrackRows() });
        Content = root;
    }

    // Tracks [from] to [to]: the Select range button ticks those rows and unticks the rest.
    private StackPanel BuildRangeRow()
    {
        var count = _tracks.Count;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var from = new ComboBox { Name = "RangeFrom", Width = 60, ItemsSource = Enumerable.Range(1, count).ToList(), SelectedIndex = 0 };
        var to = new ComboBox { Name = "RangeTo", Width = 60, ItemsSource = Enumerable.Range(1, count).ToList(), SelectedIndex = Math.Max(0, count - 1) };
        var select = new Button { Name = "SelectRange", Content = "Select range", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        select.Click += (_, _) => TickRange(from.SelectedIndex, to.SelectedIndex);
        row.Children.Add(new TextBlock { Text = "Tracks", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(from);
        row.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        row.Children.Add(to);
        row.Children.Add(select);
        return row;
    }

    private StackPanel BuildBottom()
    {
        var bottom = new StackPanel();
        var palette = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        foreach (var (name, hex) in TrackColoursPresenter.Palette)
        {
            var button = new Button { Width = 30, Height = 24, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(0) };
            button.Content = new Border { Width = 22, Height = 16, CornerRadius = new CornerRadius(3), Background = SwatchBrush(hex) };
            ToolTip.SetTip(button, $"{name}: colour the ticked tracks");
            button.Click += (_, _) => ColourTicked(hex);
            palette.Children.Add(button);
        }
        bottom.Children.Add(palette);
        var close = new Button
        {
            Name = "CloseButton", Content = "Close", IsCancel = true, IsDefault = true, MinWidth = DialogMetrics.DialogButtonMinWidth, HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();
        bottom.Children.Add(close);
        return bottom;
    }

    private StackPanel BuildTrackRows()
    {
        var list = new StackPanel();
        for (var i = 0; i < _tracks.Count; i++)
        {
            var index = i;
            var track = _tracks[i];
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            box.IsCheckedChanged += (_, _) => _presenter.SetTicked(index, box.IsChecked == true);
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Margin = new Thickness(6, 0, 8, 0), Background = SwatchBrush(track.ColorHex) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = DialogMetrics.RowMargin };
            row.Children.Add(box);
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock { Text = $"{i + 1}. {track.Name}", VerticalAlignment = VerticalAlignment.Center });
            list.Children.Add(row);
            _boxes.Add(box);
            _swatches.Add(swatch);
        }
        return list;
    }

    private void TickRange(int fromIndex, int toIndex)
    {
        _presenter.TickRange(fromIndex, toIndex);
        for (var i = 0; i < _boxes.Count; i++) _boxes[i].IsChecked = _presenter.IsTicked(i);
    }

    // Colours the ticked rows; the first change captures undo through the caller. Nothing happens when no row is ticked.
    private void ColourTicked(string hex)
    {
        var change = _presenter.ColourTicked();
        if (change is null) return;
        if (change.IsFirstChange) _beforeFirstChange?.Invoke();
        foreach (var index in change.TrackIndexes)
        {
            _tracks[index].ColorFollowsInstrument = false;
            _tracks[index].ColorHex = hex;
            _swatches[index].Background = SwatchBrush(hex);
        }
        _changed?.Invoke();
    }

    // The colour a track or palette hex paints; null (no fill) when the text is not a colour.
    private static IBrush? SwatchBrush(string hex) =>
        ColourHex.TryParse(hex, out var colour) ? new SolidColorBrush(AvaloniaPalette.ColorOf(colour.ToUi())) : null;
}
