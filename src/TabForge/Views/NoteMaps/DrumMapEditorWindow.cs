using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views.Presets;

namespace TabForge.Views.NoteMaps;

// Owns: the drum map editor window: the map drop-down with New, Duplicate and Manage, the remap list (one row per drum the song's
//   map names, with the note sent to the instrument), the piece-name grid under Advanced, and Save / Cancel.
// Does not own: checking and writing a map (DrumMapDraft), the map files (DrumMapStore), the note table (DrumMapLibrary).
// Tests: TestDrumMapEditorStore (the checks and the store); capture step "note-map-editor".
internal sealed class DrumMapEditorWindow : Window
{
    private readonly DrumMap? _source;
    private readonly DrumMapStore _store = new();
    private readonly ComboBox _maps = new() { Width = 190 };
    private readonly PresetRows _mapRows;
    private readonly TextBox _name = new() { Width = 220, MaxLength = PresetRules.MaxNameLength };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed, FontWeight = FontWeights.SemiBold };
    private readonly CheckBox _onlyChanged = new() { Content = "Show only changed", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    private readonly StackPanel _rows = new(), _pieces = new();
    private readonly Button _save = Push("Save", "Save this map to your drum maps and use it");
    private readonly Button _addPiece = Push("Add a piece", "Add a row to name another note of this map");
    private readonly List<(int Note, TextBox Box, TextBlock Heard, Border Row)> _sends = new();
    private readonly List<(TextBox Note, TextBox Piece)> _pieceRows = new();
    private readonly Expander _advanced = new() { Header = "Advanced: the piece names of this map", Margin = new Thickness(0, 10, 0, 0), ToolTip = "Name the notes of this map. Two maps pair the pieces that carry the same name; the list above overrides that per note" };
    private DrumMap? _map;
    private bool _readOnly, _filling, _dirty, _modal;

    /// <summary>The name of the map that was saved; null while nothing was.</summary>
    public string? Saved { get; private set; }

    /// <summary>Opens the editor on a map (null: a new one), with the remap rows of the song's map. Returns the saved map's name, or null.</summary>
    public static string? Edit(Window owner, DrumMap? map, DrumMap? source)
    {
        var window = new DrumMapEditorWindow(map, source) { Owner = owner, _modal = true };
        return window.ShowDialog() == true ? window.Saved : null;
    }

    internal DrumMapEditorWindow(DrumMap? map, DrumMap? source)
    {
        _source = source;
        Title = "Drum map";
        Width = 720; Height = 660; MinWidth = 620; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var create = Push("New", "Start a new, empty map");
        var duplicate = Push("Duplicate", "Make a copy of this map that you can change");
        UiIds.Id(_maps, "NoteMap.Editor.Maps", "Map to edit");
        TooltipShortcuts.Bind(_maps, "The map to look at or change. Maps that come with the app are read-only: duplicate one to change it. Rename, duplicate, delete or drag your own maps in the open list", null);
        UiIds.Id(_name, "NoteMap.Editor.Name", "Map name");
        TooltipShortcuts.Bind(_name, "The name this map is saved under", null);
        TooltipShortcuts.Bind(_onlyChanged, "List only the drums whose note changes", null);
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        foreach (var element in new FrameworkElement[] { _maps, create, duplicate }) { DockPanel.SetDock(element, Dock.Left); bar.Children.Add(element); }
        DockPanel.SetDock(_count, Dock.Right); bar.Children.Add(_count);
        DockPanel.SetDock(_onlyChanged, Dock.Right); bar.Children.Add(_onlyChanged);

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        nameRow.Children.Add(Muted("Name", 60));
        nameRow.Children.Add(_name);

        var advanced = _advanced;
        advanced.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        var pieceBox = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pieceBox.Children.Add(Cells(null, Muted("Note", 0), Muted("Name", 0), Muted("Piece", 0), null));
        pieceBox.Children.Add(_pieces);
        _addPiece.HorizontalAlignment = HorizontalAlignment.Left; _addPiece.Margin = new Thickness(0, 6, 0, 0);
        pieceBox.Children.Add(_addPiece);
        advanced.Content = pieceBox;

        var list = new StackPanel();
        list.Children.Add(_rows);
        list.Children.Add(advanced);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false };

        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 3, 14, 3), ToolTip = "Close without saving (Esc)" };
        _save.IsDefault = true; _save.Padding = new Thickness(14, 3, 14, 3); _save.Margin = new Thickness(0, 0, 8, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(_save); buttons.Children.Add(cancel);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");

        var root = new DockPanel { Margin = new Thickness(12) };
        foreach (var top in new FrameworkElement[] { bar, nameRow, _hint, Header() }) { DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); }
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        root.Children.Add(scroll);
        Content = root;

        _mapRows = PresetRows.Attach(_maps, PresetKind.DrumMap, () => _store, o => o is ComboBoxItem { Tag: DrumMap { IsUser: true } map } ? map.Name : null);
        _maps.SelectionChanged += (_, _) =>
        {
            if (_filling || _maps.SelectedItem is not ComboBoxItem { Tag: DrumMap picked }) return;
            if (KeepEdits()) { FillMaps(); return; }
            Load(picked, picked.Name, !picked.IsUser);
        };
        create.Click += (_, _) => { if (!KeepEdits()) Load(null, Unused("My drum map"), false); };
        duplicate.Click += (_, _) => { Load(_map, PresetRules.CopyName(_map?.Name ?? _name.Text.Trim(), AllNames()), false); _dirty = true; };
        _store.Changed += () => { if (_map is { IsUser: true } && DrumMapLibrary.Find(_map.Name) is null) Load(null, Unused("My drum map"), false); else FillMaps(); };
        _onlyChanged.Click += (_, _) => Recount();
        _addPiece.Click += (_, _) => { AddPiece("", ""); _pieceRows[^1].Note.Focus(); };
        _save.Click += (_, _) => Save();
        Load(map, map?.Name ?? Unused("My drum map"), map is { IsUser: false });
    }

    private static IReadOnlyList<string> AllNames() => DrumMapLibrary.All().Select(m => m.Name).ToList();
    private static string Unused(string name) => AllNames().Contains(name, StringComparer.OrdinalIgnoreCase) ? PresetRules.CopyName(name, AllNames()) : name;

    /// <summary>True when the user has unsaved changes and chooses to stay on them.</summary>
    private bool KeepEdits() =>
        _dirty && ThemedMessageBox.Show(this, "Discard the changes to this map?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes;

    private void FillMaps()
    {
        _filling = true;
        try
        {
            _maps.Items.Clear();
            foreach (var map in DrumMapLibrary.All())
                _maps.Items.Add(_mapRows.Item(map.IsUser ? $"★ {map.Name}" : map.Name, map, map.IsUser ? "Your own map" : "Comes with the app (read-only)"));
            var current = _maps.Items.Cast<ComboBoxItem>().FirstOrDefault(i => _map is not null && ((DrumMap)i.Tag).Name == _map.Name && _name.Text.Trim() == _map.Name);
            if (current is null) _maps.Items.Add(current = new ComboBoxItem { Content = "(new map, not saved yet)" });
            _mapRows.AddManage(_maps.Items);
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
            _save.IsEnabled = _addPiece.IsEnabled = !readOnly;
            _hint.Text = readOnly
                ? "This map comes with the app and cannot be changed. Press Duplicate to make your own copy, then change the Send as numbers."
                : $"Each row is a drum the song plays ({_source?.Name ?? "no song map"}). Type the note your instrument needs under Send as; x means the note is not sent.";
            _error.Visibility = Visibility.Collapsed;
            _pieces.Children.Clear(); _pieceRows.Clear();
            foreach (var (note, piece) in (map?.Notes ?? new Dictionary<int, string>()).OrderBy(p => p.Key)) AddPiece(note.ToString(CultureInfo.InvariantCulture), piece);
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

    // ---------- the remap list ----------
    private static FrameworkElement Header()
    {
        var header = Cells(null, Muted("Drum in the tab", 0), Muted("Note", 0), null, Muted("Send as", 0), Muted("Heard as", 0));
        header.Margin = new Thickness(12, 0, 12, 2);   // a row's frame padding plus its own margin
        return header;
    }

    private void AddSend(int note, string piece, string text)
    {
        var box = new TextBox { Text = text, MaxLength = 3, Width = 52, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_readOnly };
        AutomationProperties.SetName(box, $"Send note {note} {piece} as");
        TooltipShortcuts.Bind(box, $"The note your instrument receives when the song plays {note} {piece} (0 to 127; x: not sent)", null);
        var heard = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var arrow = new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center };
        var border = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(3) };
        border.Child = Cells(null, Plain(piece), Plain($"{note}  {MusicTheoryService.NoteName(note)}"), arrow, box, heard);
        var row = (note, box, heard, border);
        _sends.Add(row);
        _rows.Children.Add(border);
        box.TextChanged += (_, _) => { if (_filling) return; _dirty = true; Refresh(row); Recount(); };
    }

    /// <summary>The note a row sends: null when its text is no note, -1 for not sent.</summary>
    private static int? Sent(int note, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return note;
        if (text is "x" or "X" or "-") return -1;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= 127 ? n : null;
    }

    private void Refresh((int Note, TextBox Box, TextBlock Heard, Border Row) row)
    {
        var sent = Sent(row.Note, row.Box.Text);
        var changed = sent != row.Note;
        if (changed) row.Row.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); else row.Row.Background = null;
        row.Heard.SetResourceReference(TextBlock.ForegroundProperty, changed ? "TextBrush" : "MutedBrush");
        row.Heard.FontWeight = changed ? FontWeights.SemiBold : FontWeights.Normal;
        row.Heard.Text = sent switch
        {
            null => "not a note (0 to 127, or x)",
            -1 => "not sent",
            _ when !changed => "same",
            { } n => $"{MusicTheoryService.NoteName(n)}  {_pieceRows.FirstOrDefault(p => p.Note.Text.Trim() == n.ToString(CultureInfo.InvariantCulture)).Piece?.Text}".TrimEnd(),
        };
    }

    private void Recount()
    {
        var changed = _sends.Count(r => Sent(r.Note, r.Box.Text) != r.Note);
        _count.Text = changed == 1 ? "1 note changed" : $"{changed} notes changed";
        foreach (var row in _sends)
            row.Row.Visibility = _onlyChanged.IsChecked == true && Sent(row.Note, row.Box.Text) == row.Note ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- the piece grid ----------
    private void AddPiece(string note, string piece)
    {
        var noteBox = new TextBox { Text = note, MaxLength = 3, Width = 52, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_readOnly, ToolTip = "Note number, 0 to 127" };
        var pieceBox = new TextBox { Text = piece, MaxLength = DrumMapDraft.MaxPieceLength, Width = 230, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_readOnly, ToolTip = "The piece this note plays, e.g. Bass Drum 1. Maps pair pieces that carry the same name" };
        AutomationProperties.SetName(noteBox, "Note number");
        AutomationProperties.SetName(pieceBox, "Piece name");
        var name = Plain(NameOf(note));
        var remove = new Button { Content = "×", Width = 24, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_readOnly, ToolTip = "Remove this row" };
        AutomationProperties.SetName(remove, "Remove this piece");
        var line = Cells(new Thickness(6, 2, 6, 2), noteBox, name, pieceBox, remove);
        var row = (noteBox, pieceBox);
        _pieceRows.Add(row);
        _pieces.Children.Add(line);
        void Edited() { if (_filling) return; _dirty = true; foreach (var send in _sends) Refresh(send); }
        noteBox.TextChanged += (_, _) => { name.Text = NameOf(noteBox.Text); Edited(); };
        pieceBox.TextChanged += (_, _) => Edited();
        remove.Click += (_, _) => { _pieceRows.Remove(row); _pieces.Children.Remove(line); Edited(); };
    }

    private static string NameOf(string text) => Sent(0, text) is { } n and >= 0 && text.Trim().Length > 0 ? MusicTheoryService.NoteName(n) : "";

    // ---------- save ----------
    private (DrumMapDraft? Draft, string? Error) Draft() =>
        DrumMapDraft.Build(_name.Text, _pieceRows.Select(r => (r.Note.Text, r.Piece.Text)), _sends.Select(r => (r.Note, r.Box.Text)), _source, _map?.Remap);

    /// <summary>Shows why the rows cannot be saved (nothing when they can); the capture path shows a refused map with it.</summary>
    internal bool Check()
    {
        var error = Draft().Error;
        _error.Text = error ?? "";
        _error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        return error is null;
    }

    private void Save()
    {
        if (_readOnly || !Check() || Draft().Draft is not { } draft) return;
        var replaces = _store.FileOfMap(draft.Name) is not null && !(_map is { IsUser: true } && string.Equals(_map.Name, draft.Name, StringComparison.OrdinalIgnoreCase));
        if (replaces && ThemedMessageBox.Show(this, $"Replace your map \"{draft.Name}\"?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { draft.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _error.Text = "The map could not be saved: " + ex.Message;   // Not logged: the message is shown in the window
            _error.Visibility = Visibility.Visible;
            return;
        }
        Saved = draft.Name;
        if (_modal) DialogResult = true; else Close();
    }

    /// <summary>The capture path's states: "shipped" (read-only), "copy" (a duplicate with one more change), "advanced" (changed rows and the piece grid), "error" (a refused map).</summary>
    internal void CaptureShow(string kind)
    {
        if (kind == "shipped") return;
        Load(_map, PresetRules.CopyName(_map?.Name ?? "My drum map", AllNames()), false);
        if (_sends.FirstOrDefault(r => r.Note == 46).Box is { } openHat) openHat.Text = "26";
        if (kind is "advanced" or "error") { _onlyChanged.IsChecked = true; Recount(); _advanced.IsExpanded = true; }
        if (kind == "error") { AddPiece("38", "Rim Shot"); Check(); }
    }

    // ---------- small pieces ----------
    private static Button Push(string text, string tip)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        TooltipShortcuts.Bind(button, tip, null);
        return button;
    }

    private static TextBlock Plain(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private static TextBlock Muted(string text, double width)
    {
        var block = Plain(text);
        if (width > 0) block.Width = width;
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return block;
    }

    /// <summary>One line of the list: five columns of the same widths in every row (a null cell stays empty).</summary>
    private static Grid Cells(Thickness? margin, params UIElement?[] cells)
    {
        var grid = new Grid { Margin = margin ?? new Thickness(6, 0, 6, 0) };
        // Remap rows: drum, note, arrow, send as, heard as. Piece rows use the first four: note, name, piece, remove.
        var widths = cells.Length == 5 ? new[] { 190.0, 90, 30, 80, 0 } : new[] { 80.0, 60, 250, 0 };
        foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width > 0 ? new GridLength(width) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < cells.Length; i++)
            if (cells[i] is { } cell) { Grid.SetColumn(cell, i); grid.Children.Add(cell); }
        return grid;
    }
}
