using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The track's custom drum map (the drum map window): for every GM percussion sound the TAB line, the TAB text,
/// the staff position and the notehead, starting from the track's current mapping; "Reset from" refills every row from a preset.
/// Owns: building the rows and returning the edited entries on OK. Does not own: the rows' data and choices (Core CustomDrumMapRows),
/// applying the map (DrumMaps.Apply in TrackPropertiesWindow), or the MIDI drum map files (NoteMaps/DrumMapEditorWindow).
/// Tests: TabForge.Avalonia.Tests.TrackDrumMapWindowTests.
/// </summary>
public sealed class TrackDrumMapWindow : Window
{
    private const double LineBoxWidth = 70, StepBoxWidth = 90, HeadBoxWidth = 110, PresetBoxWidth = 150, FooterButtonWidth = 80, Gap = 14;
    private const string ColumnWidths = "220,90,90,110,*";
    private const string Intro = "Choose how each drum sound is written. TAB line 1 is the top line; staff position counts half-spaces down from the top staff line (0 = top line, 7 = kick space, negative = above the staff).";

    private readonly StackPanel _rows = new() { Name = "DrumMapRows" };
    private readonly ComboBox _basePreset = new() { Name = "ResetPresetBox", Width = PresetBoxWidth, Margin = new Thickness(0, 0, 8, 0) };
    private List<DrumMapEntry> _entries;

    public TrackDrumMapWindow(TrackModel track)
    {
        _entries = CustomDrumMapRows.FromTrack(track);
        Title = "Custom drum map";
        Width = 720; Height = 640; MinWidth = 560; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new DockPanel { Margin = new Thickness(Gap) };
        var intro = new TextBlock { Text = Intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(ColumnWidths), Margin = new Thickness(0, 0, 18, 4) };
        string[] titles = { "Sound", "TAB line", "TAB text", "Staff position", "Notehead" };
        for (var c = 0; c < titles.Length; c++)
        {
            var title = new TextBlock { Text = titles[c], FontWeight = FontWeight.SemiBold };
            Grid.SetColumn(title, c);
            header.Children.Add(title);
        }
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        BuildRows();
        root.Children.Add(new ScrollViewer { Content = _rows });
        Content = root;
    }

    /// <summary>The working rows; OK returns them.</summary>
    internal IReadOnlyList<DrumMapEntry> Entries => _entries;

    private StackPanel Footer()
    {
        ToolTip.SetTip(_basePreset, "Reset every row from a preset");
        AutomationProperties.SetName(_basePreset, "Reset preset");
        foreach (var preset in DrumMaps.Presets.Where(p => p != DrumMaps.Custom)) _basePreset.Items.Add(new ComboBoxItem { Content = DrumMaps.DisplayName(preset), Tag = preset });
        var reset = new Button { Name = "ResetFromButton", Content = "Reset from", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 0) };
        reset.Click += (_, _) => ResetFromPicked();
        var ok = new Button { Name = "OkButton", Content = "OK", Width = FooterButtonWidth, IsDefault = true, Margin = new Thickness(20, 0, 6, 0) };
        ok.Click += (_, _) => Close(_entries);
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", Width = FooterButtonWidth, IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = DialogMetrics.FooterGap, Children = { reset, _basePreset, ok, cancel } };
    }

    // "Reset from": every row as the picked preset writes it; nothing happens until a preset is picked.
    private void ResetFromPicked()
    {
        if ((_basePreset.SelectedItem as ComboBoxItem)?.Tag is not string preset) return;
        _entries = CustomDrumMapRows.FromPreset(preset);
        BuildRows();
    }

    private void BuildRows()
    {
        _rows.Children.Clear();
        foreach (var entry in _entries) _rows.Children.Add(Row(entry));
    }

    private static Grid Row(DrumMapEntry entry)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(ColumnWidths), Margin = new Thickness(0, 1) };
        var name = new TextBlock { Text = CustomDrumMapRows.SoundLabel(entry.Midi), VerticalAlignment = VerticalAlignment.Center };
        var lines = Enumerable.Range(CustomDrumMapRows.FirstTabLine, CustomDrumMapRows.LastTabLine - CustomDrumMapRows.FirstTabLine + 1).Cast<object>();
        var line = Choices(LineBoxWidth, lines, entry.TabLine + 1);
        line.SelectionChanged += (_, _) => { if (line.SelectedItem is int v) entry.TabLine = v - 1; };
        var text = new TextBox { Text = entry.Label, Width = LineBoxWidth, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = CustomDrumMapRows.LabelMaxLength };
        text.TextChanged += (_, _) => entry.Label = (text.Text ?? "").Trim();
        var steps = Enumerable.Range(CustomDrumMapRows.LowestStaffStep, CustomDrumMapRows.HighestStaffStep - CustomDrumMapRows.LowestStaffStep + 1).Cast<object>();
        var step = Choices(StepBoxWidth, steps, CustomDrumMapRows.ClampStaffStep(entry.StaffStep));
        step.SelectionChanged += (_, _) => { if (step.SelectedItem is int v) entry.StaffStep = v; };
        var head = Choices(HeadBoxWidth, CustomDrumMapRows.Heads, entry.Head);
        head.SelectionChanged += (_, _) => { if (head.SelectedItem is string v) entry.Head = v; };
        Control[] cells = { name, line, text, step, head };
        for (var c = 0; c < cells.Length; c++) { Grid.SetColumn(cells[c], c); row.Children.Add(cells[c]); }
        return row;
    }

    private static ComboBox Choices(double width, IEnumerable<object> items, object selected)
    {
        var box = new ComboBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var item in items) box.Items.Add(item);
        box.SelectedItem = selected;
        return box;
    }
}
