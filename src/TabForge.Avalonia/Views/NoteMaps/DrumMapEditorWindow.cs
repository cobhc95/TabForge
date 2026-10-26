using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.NoteMaps;

/// <summary>
/// The drum map editor, the drum map editor window: the map drop-down with New and Duplicate,
/// the name, the remap list (one row per drum the song's map names, with the note it is sent as and the note it is heard as),
/// Show only changed, the count of changed notes, and Save / Cancel.
/// Owns: building the rows, refreshing the changed rows and the count, and saving the checked draft through the Core draft.
/// Does not own: checking and writing a map (DrumMapDraft in Core), the map files and the note table (DrumMapLibrary).
/// Not ported yet: the piece grid under Advanced, Manage, the replace prompt (a name that another user map holds is refused)
/// and the discard prompt (switching maps while edits are unsaved is refused). The error line uses the text colour, as the
/// preview palette has no danger colour.
/// Tests: TabForge.Avalonia.Tests.DrumMapEditorWindowTests.
/// </summary>
public sealed class DrumMapEditorWindow : Window
{
    private const double WindowWidth = 720;
    private const double WindowHeight = 660;
    private const double WindowMinWidth = 620;
    private const double WindowMinHeight = 400;
    private const double MapBoxWidth = 190;
    private const double NameBoxWidth = 220;
    private const double NameLabelWidth = 60;
    private const double SendBoxWidth = 52;
    private const int SendMaxLength = 3;
    private const string SendRowColumns = "190,90,30,80,*";
    private const string NewMapName = "My drum map";
    private const string ReadOnlyHint = "This map comes with the app and cannot be changed. Press Duplicate to make your own copy, then change the Send as numbers.";
    private const string UserNameTakenText = "A map of that name already exists. Choose another name.";

    private readonly DrumMap? _source;
    private readonly ComboBox _maps = new() { Width = MapBoxWidth, Name = "MapBox" };
    private readonly TextBox _name = new() { Width = NameBoxWidth, MaxLength = PresetRules.MaxNameLength, Name = "NameBox" };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false, FontWeight = FontWeight.SemiBold, Name = "ErrorText" };
    private readonly CheckBox _onlyChanged = new() { Content = "Show only changed", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    private readonly StackPanel _rows = new();
    private readonly Button _save = Push("Save", "Save this map to your drum maps and use it");
    private readonly List<SendRow> _sends = new();
    private DrumMap? _map;
    private bool _readOnly, _filling, _dirty;

    /// <summary>One remap row: the source note, its send-as box, the heard-as text and the row's frame.</summary>
    private sealed record SendRow(int Note, TextBox Box, TextBlock Heard, Border Row);

    /// <summary>The name of the map that was saved; null while nothing was.</summary>
    public string? Saved { get; private set; }

    /// <summary>Opens the editor on a map (null: a new one), with the remap rows of the song's map.</summary>
    public DrumMapEditorWindow(DrumMap? map, DrumMap? source)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _source = source;
        Title = "Drum map";
        Width = WindowWidth; Height = WindowHeight; MinWidth = WindowMinWidth; MinHeight = WindowMinHeight;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush");

        var create = Push("New", "Start a new, empty map");
        var duplicate = Push("Duplicate", "Make a copy of this map that you can change");
        ToolTip.SetTip(_maps, "The map to look at or change. Maps that come with the app are read-only: duplicate one to change it.");
        ToolTip.SetTip(_name, "The name this map is saved under");
        ToolTip.SetTip(_onlyChanged, "List only the drums whose note changes");
        _save.Name = "SaveButton";

        var mapBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        mapBar.Children.Add(_maps); mapBar.Children.Add(create); mapBar.Children.Add(duplicate);
        var optionsBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        optionsBar.Children.Add(_onlyChanged); optionsBar.Children.Add(_count);
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        nameRow.Children.Add(Muted("Name", NameLabelWidth)); nameRow.Children.Add(_name);

        var list = new StackPanel();
        list.Children.Add(Header());
        list.Children.Add(_rows);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false };

        var cancel = Push("Cancel", "Close without saving");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(_save); buttons.Children.Add(cancel);

        var root = new DockPanel { Margin = new Thickness(12) };
        foreach (var top in new Control[] { mapBar, optionsBar, nameRow, _hint }) { DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); }
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        root.Children.Add(scroll);
        Content = root;

        _maps.SelectionChanged += (_, e) =>
        {
            if (_filling) return;
            if (PresetRowText.IsManage(_maps.SelectedItem))
            {
                _filling = true;
                try { if (e is SelectionChangedEventArgs args && args.RemovedItems.Count > 0) _maps.SelectedItem = args.RemovedItems[0]; }
                finally { _filling = false; }
                Dispatcher.UIThread.Post(() => PresetManager.Open(this, PresetCatalogKind.DrumMap, null), DispatcherPriority.Background);
                return;
            }
            if (_maps.SelectedItem is ComboBoxItem { Tag: DrumMap picked }) PickMap(picked, (e as SelectionChangedEventArgs)?.RemovedItems is { Count: > 0 } removed ? removed[0] : null);
        };
        create.Click += (_, _) => { if (!_dirty) Load(null, Unused(NewMapName), false); };
        duplicate.Click += (_, _) => { Load(_map, PresetRules.CopyName(_map?.Name ?? (_name.Text ?? "").Trim(), AllNames()), false); _dirty = true; };
        cancel.Click += (_, _) => Close();
        _save.Click += (_, _) => Save();
        _name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !_filling) _dirty = true; };
        _onlyChanged.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Recount(); };

        Load(map, map?.Name ?? Unused(NewMapName), map is { IsUser: false });
    }

    /// <summary>The checks the Save button runs; shows the first problem in the error line. True when the map can be saved.</summary>
    public bool Check()
    {
        var error = Draft().Error ?? UserNameTakenError();
        ShowError(error ?? "");
        return error is null;
    }

    private static IReadOnlyList<string> AllNames() => DrumMapLibrary.All().Select(m => m.Name).ToList();

    private static string Unused(string name) =>
        AllNames().Contains(name, StringComparer.OrdinalIgnoreCase) ? PresetRules.CopyName(name, AllNames()) : name;

    private void PickMap(DrumMap picked, object? previous)
    {
        if (_dirty)
        {
            _filling = true;
            try { if (previous is not null) _maps.SelectedItem = previous; }
            finally { _filling = false; }
            return;
        }
        Load(picked, picked.Name, !picked.IsUser);
    }

    private void FillMaps()
    {
        _filling = true;
        try
        {
            _maps.Items.Clear();
            foreach (var map in DrumMapLibrary.All())
                _maps.Items.Add(new ComboBoxItem { Content = map.IsUser ? $"★ {map.Name}" : map.Name, Tag = map });
            var current = _maps.Items.Cast<ComboBoxItem>().FirstOrDefault(i => _map is { } shown && ((DrumMap)i.Tag!).Name == shown.Name && _name.Text?.Trim() == shown.Name);
            if (current is null) _maps.Items.Add(current = new ComboBoxItem { Content = "(new map, not saved yet)" });
            PresetRowText.AppendManage(_maps.Items);
            _maps.SelectedItem = current;
        }
        finally { _filling = false; }
    }

    /// <summary>Shows a map's rows (null: an empty map) under a name; a read-only map cannot be typed into or saved.</summary>
    private void Load(DrumMap? map, string name, bool readOnly)
    {
        _map = map; _readOnly = readOnly; _filling = true;
        try
        {
            _name.Text = name; _name.IsEnabled = !readOnly;
            _save.IsEnabled = !readOnly;
            _hint.Text = readOnly ? ReadOnlyHint : $"Each row is a drum the song plays ({_source?.Name ?? "no song map"}). Type the note your instrument needs under Send as; x means the note is not sent.";
            ShowError("");
            _rows.Children.Clear(); _sends.Clear();
            var pieces = map?.Notes ?? new Dictionary<int, string>();
            foreach (var (note, piece) in (_source?.Notes ?? new Dictionary<int, string>()).OrderBy(p => p.Key))
            {
                var sent = map?.Remap is { } remap && remap.TryGetValue(note, out var mapped) ? mapped : DrumMapDraft.Paired(_source, pieces, note);
                AddSend(note, piece, sent < 0 ? "x" : sent.ToString(CultureInfo.InvariantCulture));
            }
        }
        finally { _filling = false; }
        _dirty = false;
        FillMaps();
        foreach (var row in _sends) Refresh(row);
        Recount();
    }

    private static Grid Header() =>
        Cells(new Thickness(12, 0, 12, 2), Muted("Drum in the tab"), Muted("Note"), null, Muted("Send as"), Muted("Heard as"));

    private void AddSend(int note, string piece, string text)
    {
        var box = new TextBox { Text = text, MaxLength = SendMaxLength, Width = SendBoxWidth, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_readOnly };
        ToolTip.SetTip(box, $"The note your instrument receives when the song plays {note} {piece} (0 to 127; x: not sent)");
        var heard = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var arrow = new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center };
        var border = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(3) };
        border.Child = Cells(new Thickness(6, 0, 6, 0), Plain(piece), Plain($"{note}  {MusicTheoryService.NoteName(note)}"), arrow, box, heard);
        var row = new SendRow(note, box, heard, border);
        _sends.Add(row);
        _rows.Children.Add(border);
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !_filling) { _dirty = true; Refresh(row); Recount(); } };
    }

    /// <summary>The note a row sends: null when its text is no note, -1 for not sent, the row's own note for unchanged.</summary>
    private static int? Sent(int note, string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return note;
        if (text is "x" or "X" or "-") return -1;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= MidiLimits.Max ? n : null;
    }

    private void Refresh(SendRow row)
    {
        var sent = Sent(row.Note, row.Box.Text);
        var changed = sent != row.Note;
        if (changed) row.Row[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
        else row.Row.ClearValue(Border.BackgroundProperty);
        row.Heard[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(changed ? "TextBrush" : "MutedBrush");
        row.Heard.FontWeight = changed ? FontWeight.SemiBold : FontWeight.Normal;
        row.Heard.Text = sent switch
        {
            null => "not a note (0 to 127, or x)",
            -1 => "not sent",
            _ when !changed => "same",
            { } n => $"{MusicTheoryService.NoteName(n)}  {PieceOf(n)}".TrimEnd(),
        };
    }

    private string PieceOf(int note) => _map?.Notes.GetValueOrDefault(note) ?? "";

    private void Recount()
    {
        var changed = _sends.Count(r => Sent(r.Note, r.Box.Text) != r.Note);
        _count.Text = changed == 1 ? "1 note changed" : $"{changed} notes changed";
        foreach (var row in _sends)
            row.Row.IsVisible = !(_onlyChanged.IsChecked == true && Sent(row.Note, row.Box.Text) == row.Note);
    }

    private (DrumMapDraft? Draft, string? Error) Draft() =>
        DrumMapDraft.Build(_name.Text ?? "", (_map?.Notes ?? new Dictionary<int, string>())
            .Select(p => (Note: p.Key.ToString(CultureInfo.InvariantCulture), Piece: p.Value)),
            _sends.Select(r => (r.Note, r.Box.Text ?? "")), _source, _map?.Remap);

    /// <summary>A user's map of another name already holds the chosen name (the edited map itself may keep it).</summary>
    private string? UserNameTakenError()
    {
        var name = (_name.Text ?? "").Trim();
        var editsThisMap = _map is { IsUser: true } && string.Equals(_map.Name, name, StringComparison.OrdinalIgnoreCase);
        return DrumMapLibrary.Find(name) is { IsUser: true } && !editsThisMap ? UserNameTakenText : null;
    }

    private void Save()
    {
        if (_readOnly || !Check() || Draft().Draft is not { } draft) return;
        try { draft.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowError("The map could not be saved: " + ex.Message);   // Not logged: the message is shown in the window
            return;
        }
        Saved = draft.Name;
        Close();
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.IsVisible = text.Length > 0;
    }

    private static Button Push(string text, string tip)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static TextBlock Plain(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Muted(string text, double width = 0)
    {
        var block = Plain(text);
        if (width > 0) block.Width = width;
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        return block;
    }

    /// <summary>One line of the list: five columns of the same widths in every row (a null cell stays empty).</summary>
    private static Grid Cells(Thickness margin, params Control?[] cells)
    {
        var grid = new Grid { Margin = margin, ColumnDefinitions = ColumnDefinitions.Parse(SendRowColumns) };
        for (var i = 0; i < cells.Length; i++)
            if (cells[i] is { } cell) { Grid.SetColumn(cell, i); grid.Children.Add(cell); }
        return grid;
    }
}
