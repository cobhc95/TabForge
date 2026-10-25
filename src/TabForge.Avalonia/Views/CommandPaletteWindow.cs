using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Command palette: type to filter the commands, Up and Down move the selection, Enter runs the chosen command, Esc closes.
/// Owns: the window's controls, key and double-click routing, and closing when focus is lost. Does not own: the filter, ranking, selection
/// and chosen command (CommandPalettePresenter). TabForge has no command table here yet, so the window lists <see cref="SampleEntries"/> in place
/// of the WPF HotkeyCatalog; the app passes its own entries through the constructor.
/// Tests: TabForge.Avalonia.Tests.CommandPaletteSavePromptTuningTests.
/// </summary>
public sealed class CommandPaletteWindow : Window
{
    private readonly CommandPalettePresenter _presenter;
    private readonly TextBox _query = new();
    private readonly ListBox _list = new();
    private bool _closing;
    // Perf: typing filters at once on the first keystroke and once more when it rests (~100 ms).
    private readonly TabForge.Platform.IUiTimer _filterDebounce = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication()
        .CreateTimer(System.TimeSpan.FromMilliseconds(TabForge.Avalonia.Platform.UiTiming.FilterDebounceMs));
    private bool _filterDirty;
    // True while the window writes the presenter's selection into the list, so the list's own change events are not fed back.
    private bool _mirroringSelection;

    /// <summary>Screenshot tour: do not close when the window loses focus.</summary>
    public bool KeepOpen { get; set; }

    /// <summary>The command id the user picked, or null when cancelled.</summary>
    public string? ChosenId => _presenter.ChosenId;

    /// <summary>Sample commands for the preview, with the "Category: Name" titles the app's palette shows. Key texts are the catalogue defaults (the preview keeps no bindings), read through the same lookup as the app.</summary>
    public static IReadOnlyList<CommandPaletteEntry> SampleEntries { get; } = BuildSampleEntries();

    private static IReadOnlyList<CommandPaletteEntry> BuildSampleEntries()
    {
        var defaults = new HotkeySettings();
        return
        [
            new("File.Save", "File: Save", HotkeyCatalog.DisplayAll(defaults, "File.Save"), "Save the song"),
            new("File.SaveAs", "File: Save as…", HotkeyCatalog.DisplayAll(defaults, "File.SaveAs"), "Save the song under a new name"),
            new("Edit.Undo", "Edit: Undo", HotkeyCatalog.DisplayAll(defaults, "Edit.Undo"), "Undo the last edit"),
            new("Edit.Redo", "Edit: Redo", HotkeyCatalog.DisplayAll(defaults, "Edit.Redo"), "Redo the last undone edit"),
            new("Tools.ChordFinder", "Tools: Chord finder", HotkeyCatalog.DisplayAll(defaults, "Tools.ChordFinder"), "Show the notes of a chord"),
            new("Tools.GlobalTuning", "Tools: Global tuning", HotkeyCatalog.DisplayAll(defaults, "Tools.GlobalTuning"), "Retune every instrument track"),
        ];
    }

    public CommandPaletteWindow(IEnumerable<CommandPaletteEntry> entries)
    {
        _presenter = new CommandPalettePresenter(entries);
        Title = "Command palette";
        Width = 520;
        Height = 420;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        AutomationProperties.SetName(_query, "Search commands");
        AutomationProperties.SetName(_list, "Matching commands");
        _query.Margin = new Thickness(8, 8, 8, 4);
        _query.Padding = new Thickness(4);
        // TextChanged is posted to the dispatcher; the property change filters at once, as the WPF box does, so Enter sees the new rows.
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.Stop();
            if (!_filterDirty) return;
            _filterDirty = false;
            Refresh();
        };
        _query.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (_filterDebounce.IsEnabled) { _filterDirty = true; return; }   // a trailing pass is already queued
            Refresh();
            _filterDebounce.Start();
        };
        _list.Margin = new Thickness(8, 4, 8, 8);
        _list.HorizontalAlignment = HorizontalAlignment.Stretch;
        _list[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        _list[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        // Perf: virtualized like the preset and plug-in lists (recycling template over a VirtualizingStackPanel).
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<CommandPaletteEntry>((entry, _) => BuildRow(entry), supportsRecycling: true);
        _list.DoubleTapped += (_, _) => Choose();
        _list.SelectionChanged += (_, _) => { if (!_mirroringSelection) _presenter.Select(_list.SelectedIndex); };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(_list, 1);
        grid.Children.Add(_query);
        grid.Children.Add(_list);
        Content = grid;

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => _query.Focus();
        // Closing deactivates the window; the flag keeps the focus-loss close from running a second time.
        Closing += (_, _) => { _closing = true; _filterDebounce.Stop(); };
        Deactivated += (_, _) => { if (IsVisible && !_closing && !KeepOpen) Close(); };
        Refresh();
    }

    // The key text sits at the right edge, as in the WPF row: the title is the left-hand, trimmed part.
    // The list builds a container with a null item while it swaps ItemsSource; that container gets an empty row.
    private static Control BuildRow(CommandPaletteEntry? entry)
    {
        if (entry is null) return new DockPanel();
        var row = new DockPanel();
        var key = new TextBlock { Text = entry.Key, Opacity = ChromeMetrics.DimTextOpacity, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(key, Dock.Right);
        row.Children.Add(key);
        row.Children.Add(new TextBlock { Text = entry.Title, TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }

    private void Refresh()
    {
        _presenter.SetQuery(_query.Text ?? "");
        _mirroringSelection = true;
        try
        {
            _list.ItemsSource = _presenter.Shown;
            _list.SelectedIndex = _presenter.SelectedIndex;
        }
        finally { _mirroringSelection = false; }
    }

    /// <summary>Runs a queued trailing filter now (Enter, arrows: the list shows the latest text).</summary>
    private void FlushFilter()
    {
        _filterDebounce.Stop();
        if (!_filterDirty) return;
        _filterDirty = false;
        Refresh();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter: FlushFilter(); Choose(); break;
            case Key.Down: FlushFilter(); Move(1); break;
            case Key.Up: FlushFilter(); Move(-1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move(int delta)
    {
        _presenter.Move(delta);
        _mirroringSelection = true;
        try { _list.SelectedIndex = _presenter.SelectedIndex; }
        finally { _mirroringSelection = false; }
        if (_list.SelectedItem is not null) _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Choose()
    {
        // The list's selection is mirrored into the presenter, so the presenter's choice is the row the user sees selected.
        if (_presenter.Choose()) Close();
    }
}
