using System;
using System.Collections.Generic;
using Avalonia;
using TabForge.Avalonia.Views.Dialogs;
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
        Width = 680;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(14), Spacing = 10 };
        root.Children.Add(new TextBlock
        {
            Text = "Retunes every pitched tab track in the song together. Fret numbers stay as written; each string's sounding pitch shifts (drum tracks are untouched).",
            TextWrapping = TextWrapping.Wrap, Opacity = ChromeMetrics.GlobalTuningDescriptionOpacity,
        });
        root.Children.Add(TuningCard());
        root.Children.Add(ButtonRow());
        Content = root;

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => PaintStrings();
        Refresh();
    }

    private Border TuningCard()
    {
        _presetBox.ItemsSource = _presenter.PresetChoices;
        _presetBox.SelectionChanged += (_, _) => { if (!_syncing && _presenter.ApplyPreset(_presetBox.SelectedIndex)) Refresh(); };
        ToolTip.SetTip(_presetBox, "Apply a preset tuning to every pitched tab track in the song");

        var allDown = StepButton("AllDownButton", "↓ All −1", () => _presenter.NudgeAll(-1));
        allDown.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(allDown, "Lower every string on every pitched track by 1 semitone");
        var allUp = StepButton("AllUpButton", "↑ All +1", () => _presenter.NudgeAll(1));
        allUp.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(allUp, "Raise every string on every pitched track by 1 semitone");
        var nudgeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*") };
        nudgeGrid.Children.Add(allDown);
        Grid.SetColumn(allUp, 2);
        nudgeGrid.Children.Add(allUp);

        _shiftBox.Name = "ShiftBox";
        _shiftBox.Text = "0";
        _shiftBox.Width = 56;
        _shiftBox.VerticalAlignment = VerticalAlignment.Center;
        _shiftBox.VerticalContentAlignment = VerticalAlignment.Center;
        _shiftBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyManualShift(); e.Handled = true; } };
        ToolTip.SetTip(_shiftBox, "Semitones to shift every string on every pitched track (negative lowers, positive raises)");
        var shiftApply = new Button { Name = "ShiftButton", Content = "Shift", Padding = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        shiftApply.Click += (_, _) => ApplyManualShift();
        ToolTip.SetTip(shiftApply, "Shift every string on every pitched track by the entered number of semitones");
        var shiftGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,6,Auto,6,*") };
        shiftGrid.Children.Add(Label("Shift"));
        Grid.SetColumn(_shiftBox, 2);
        shiftGrid.Children.Add(_shiftBox);
        Grid.SetColumn(shiftApply, 4);
        shiftGrid.Children.Add(shiftApply);

        var reset = new Button { Name = "StandardEButton", Content = "Reset to Standard E", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        reset.Click += (_, _) => { _presenter.ResetToStandard(); Refresh(); };
        ToolTip.SetTip(reset, "Reset all strings back to Standard E (E2–A2–D3–G3–B3–E4)");

        var leftCol = new StackPanel { Spacing = 8, Children = { _presetBox, nudgeGrid, shiftGrid, reset } };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("185,16,*") };
        body.Children.Add(leftCol);
        var strings = StringGrid();
        Grid.SetColumn(strings, 2);
        body.Children.Add(strings);

        var heading = new TextBlock { Text = "GLOBAL TUNING", FontSize = 10.5, FontWeight = FontWeight.Bold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 8) };
        var stack = new StackPanel { Children = { heading, body } };
        var card = new Border { Child = stack, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return card;
    }

    // Per string: number, the drawn string, − / +, rectangular note badge (matching Track properties), and offset from standard E.
    private Grid StringGrid()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,30,30,58,74") };
        for (var s = 0; s < StringCount; s++)
        {
            var index = s;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var number = Label((s + 1).ToString());
            number.Opacity = ChromeMetrics.GlobalTuningStringLabelOpacity;
            var line = new Rectangle { Name = $"StringLine{s}", Height = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            _stringLines[s] = line;
            var note = new TextBlock
            {
                Name = $"NoteLabel{s}", FontWeight = FontWeight.SemiBold, FontSize = ChromeMetrics.TextEmphasis,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextStrong");
            _noteLabels[s] = note;
            var badge = new Border
            {
                Width = 50, Height = 24, CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(1), Child = note,
                VerticalAlignment = VerticalAlignment.Center,
            };
            badge[!Border.BackgroundProperty] = new DynamicResourceExtension("TuningPillBackground");
            badge[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
            ToolTip.SetTip(badge, $"Target pitch for string {s + 1} across all pitched tracks");
            var offset = new TextBlock { Name = $"OffsetLabel{s}", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(6, 0, 0, 0), Opacity = ChromeMetrics.GlobalTuningStringLabelOpacity };
            _offsetLabels[s] = offset;
            var down = StepButton($"StringDown{s}", "−", () => _presenter.NudgeString(index, -1));
            ToolTip.SetTip(down, $"Lower string {s + 1} by 1 semitone across all pitched tracks");
            var up = StepButton($"StringUp{s}", "+", () => _presenter.NudgeString(index, 1));
            ToolTip.SetTip(up, $"Raise string {s + 1} by 1 semitone across all pitched tracks");
            Control[] cells = { number, line, down, up, badge, offset };
            for (var c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], s);
                Grid.SetColumn(cells[c], c);
                grid.Children.Add(cells[c]);
            }
        }
        return grid;
    }

    private StackPanel ButtonRow()
    {
        var apply = DialogButtons.Apply();
        apply.Click += (_, _) => Close(_presenter.Tuning);
        var cancel = DialogButtons.Cancel();
        cancel.Click += (_, _) => Close(null);
        return DialogButtons.Row(apply, cancel);
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
