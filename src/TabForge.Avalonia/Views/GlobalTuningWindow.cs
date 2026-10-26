using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Global tuning for every pitched track at once: pick a preset or set each string of a six-string reference (high to low).
/// Apply closes with the tuning as its result; Cancel and Esc close with null.
/// Owns: the window's controls and layout, and refreshing their texts from the presenter. Does not own: the tuning logic (GlobalTuningPresenter)
/// or the preset catalogue, which the caller passes in (the Tools command passes the Core <c>TuningPresets.SixStringPresets</c>).
/// Deviation: each string is a flat fill of the light string tone; the string line is a gradient.
/// Tests: TabForge.Avalonia.Tests.CommandPaletteSavePromptTuningTests.
/// </summary>
public sealed class GlobalTuningWindow : Window
{
    private const int StringCount = 6;

    // Gap between the "Shift all by (semitones)" label and its value box.
    private const double ShiftLabelGap = 8;

    private readonly GlobalTuningPresenter _presenter;
    private readonly ComboBox _presetBox = new() { Name = "PresetBox", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _shiftBox = new();
    private readonly TextBlock[] _noteLabels = new TextBlock[StringCount];
    private readonly TextBlock[] _offsetLabels = new TextBlock[StringCount];
    private readonly Rectangle[] _stringLines = new Rectangle[StringCount];
    private bool _syncing;

    public GlobalTuningWindow(int[] current, IEnumerable<(string Name, int[] HighToLow)> presets)
    {
        _presenter = new GlobalTuningPresenter(current, presets);
        Title = "Global tuning (all instruments)";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock
        {
            Text = "Retunes every instrument track. Fret numbers stay; the sounding pitch of each string moves.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Opacity = ChromeMetrics.GlobalTuningDescriptionOpacity,
        });
        root.Children.Add(PresetRow());
        root.Children.Add(StringGrid());
        root.Children.Add(ShiftRow());
        root.Children.Add(ManualShiftRow());
        root.Children.Add(ButtonRow());
        Content = root;

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => PaintStrings();
        Refresh();
    }

    private DockPanel PresetRow()
    {
        _presetBox.ItemsSource = _presenter.PresetChoices;
        _presetBox.SelectionChanged += (_, _) => { if (!_syncing && _presenter.ApplyPreset(_presetBox.SelectedIndex)) Refresh(); };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(Label("Preset", 60));
        row.Children.Add(_presetBox);
        return row;
    }

    // Per string: number, the drawn string, − / note pill / +, and the offset from standard E.
    private Grid StringGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10), ColumnDefinitions = new ColumnDefinitions("26,*,34,64,34,120") };
        for (var s = 0; s < StringCount; s++)
        {
            var index = s;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            var number = Label((s + 1).ToString());
            number.Opacity = ChromeMetrics.GlobalTuningStringLabelOpacity;
            var line = new Rectangle { Name = $"StringLine{s}", Height = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            _stringLines[s] = line;
            var note = new TextBlock
            {
                Name = $"NoteLabel{s}", FontWeight = FontWeight.SemiBold, FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextStrong");
            _noteLabels[s] = note;
            var pill = new Border
            {
                Width = 56, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(4, 0), BorderThickness = new Thickness(1), Child = note,
                VerticalAlignment = VerticalAlignment.Center,
            };
            pill[!Border.BackgroundProperty] = new DynamicResourceExtension("TuningPillBackground");
            pill[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
            var offset = new TextBlock { Name = $"OffsetLabel{s}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = ChromeMetrics.GlobalTuningStringLabelOpacity };
            _offsetLabels[s] = offset;
            Control[] cells =
            {
                number, line,
                StepButton($"StringDown{s}", "−", () => _presenter.NudgeString(index, -1)),
                pill,
                StepButton($"StringUp{s}", "+", () => _presenter.NudgeString(index, 1)),
                offset,
            };
            for (var c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], s);
                Grid.SetColumn(cells[c], c);
                grid.Children.Add(cells[c]);
            }
        }
        return grid;
    }

    private StackPanel ShiftRow()
    {
        var reset = new Button { Name = "StandardEButton", Content = "Standard E", Margin = new Thickness(12, 2, 0, 2), Padding = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        reset.Click += (_, _) => { _presenter.ResetToStandard(); Refresh(); };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                Label("All strings", 70),
                StepButton("AllDownButton", "−", () => _presenter.NudgeAll(-1)),
                StepButton("AllUpButton", "+", () => _presenter.NudgeAll(1)),
                reset,
            },
        };
    }

    private StackPanel ManualShiftRow()
    {
        _shiftBox.Name = "ShiftBox";
        _shiftBox.Text = "0";
        _shiftBox.Width = 56;
        _shiftBox.Margin = new Thickness(0, 2, 6, 2);
        _shiftBox.VerticalAlignment = VerticalAlignment.Center;
        _shiftBox.VerticalContentAlignment = VerticalAlignment.Center;
        _shiftBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyManualShift(); e.Handled = true; } };
        var apply = new Button { Name = "ShiftButton", Content = "Shift", Padding = new Thickness(10, 0), Margin = new Thickness(0, 2), VerticalAlignment = VerticalAlignment.Center };
        apply.Click += (_, _) => ApplyManualShift();
        var shiftLabel = Label("Shift all by (semitones)");
        shiftLabel.Margin = new Thickness(0, 0, ShiftLabelGap, 0);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12),
            Children = { shiftLabel, _shiftBox, apply },
        };
    }

    private StackPanel ButtonRow()
    {
        var apply = new Button { Name = "ApplyButton", Content = "Apply", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        apply.Click += (_, _) => Close(_presenter.Tuning);
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", Width = 80, IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { apply, cancel } };
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close(null);
        e.Handled = true;
    }

    private void ApplyManualShift()
    {
        if (!_presenter.TryShiftAll(_shiftBox.Text ?? "")) return;
        _shiftBox.Text = "0";
        Refresh();
    }

    // The string gradient reads the palette for the window's theme, so it is painted once the window shows its theme.
    private void PaintStrings()
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        foreach (var line in _stringLines) line.Fill = AvaloniaPalette.TuningStringBrush(light);
    }

    private void Refresh()
    {
        _syncing = true;
        try
        {
            for (var s = 0; s < StringCount; s++)
            {
                _noteLabels[s].Text = _presenter.NoteOf(s);
                // Thicker for lower pitches, like real wound strings.
                var thickness = _presenter.StringThickness(s);
                _stringLines[s].Height = thickness;
                _stringLines[s].RadiusX = _stringLines[s].RadiusY = thickness / 2;
                _offsetLabels[s].Text = _presenter.OffsetText(s);
            }
            _presetBox.SelectedIndex = _presenter.MatchingPresetIndex;
        }
        finally { _syncing = false; }
    }

    private Button StepButton(string name, string text, Action step)
    {
        var button = new Button { Name = name, Content = text, Width = 28, Height = 24, Margin = new Thickness(2), Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { TabForge.Avalonia.Platform.AvaloniaFormControlStyles.StepButtonClass } };
        button.Click += (_, _) => { step(); Refresh(); };
        return button;
    }

    private static TextBlock Label(string text, double width = double.NaN) =>
        new() { Text = text, Width = width, VerticalAlignment = VerticalAlignment.Center };
}
