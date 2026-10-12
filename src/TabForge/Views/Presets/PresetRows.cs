using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Plugins;

namespace TabForge.Views.Presets;

// Owns: the organisable rows of a preset drop-down. Every row of a user's preset shows, inside the open list, a grip, the name and
//   rename and delete buttons (a copy is made in the preset manager); built-in rows stay plain text. Inline rename with the name rules (Enter applies, Esc
//   cancels), the overwrite and delete questions, reorder by the grip or Alt+Up / Alt+Down, and F2 / Delete on the highlighted row.
//   A button or the grip never picks the preset and leaves the list open.
// Does not own: where presets live (IPresetStore), what picking a preset does, or filling the drop-down (the window that shows it;
//   it lists again when the store changes).
// Tests: TestPresetManagePanel.
internal sealed class PresetRows
{
    private static readonly DependencyProperty RowsProperty = DependencyProperty.RegisterAttached("Rows", typeof(PresetRows), typeof(PresetRows));
    private static readonly DataTemplate Template = new() { VisualTree = new FrameworkElementFactory(typeof(PresetRowView)) };

    private readonly ItemsControl _host;
    private readonly Func<IPresetStore?> _store;
    private readonly Func<object, string?> _userName;
    private readonly Action? _changed;
    private Window? _window;   // the window whose return from the preset manager refreshes the list
    private bool _stale;
    private readonly Action _manage;
    private readonly ComboBoxItem _manageItem = new() { Content = ManageText, ToolTip = "Open the preset manager" };
    private bool _restoring;
    internal const string ManageText = "Manage presets…";
    private readonly Dictionary<string, Border> _rows = new(StringComparer.OrdinalIgnoreCase);
    private TextBox? _editor;
    private string? _editing, _dragging;
    private Border? _indicated;

    /// <summary>Asks a yes / no question; the list opens again afterwards. Tests answer it directly.</summary>
    internal Func<string, bool> Confirm;
    /// <summary>Asks the delete question (text in; answer and "don't ask again" tick out). Tests answer it directly.</summary>
    internal Func<string, (bool Yes, bool Remember)> AskDelete;
    /// <summary>Shows a failure (a file could not be changed). Tests collect it.</summary>
    internal Action<string> ShowError;
    /// <summary>Starts a rename somewhere else than in the row (a menu, where typing belongs to the menu).</summary>
    internal Action<string>? RenameElsewhere;
    /// <summary>Names a preset may not take (the presets that come with the app).</summary>
    internal Func<IEnumerable<string>>? Reserved;
    internal const string ReservedText = "A preset that comes with the app has this name. Choose another name.";
    /// <summary>Why the last rename was refused ("" when it was not).</summary>
    internal string Message { get; private set; } = "";
    internal bool IsEditing => _editor is not null;

    private PresetRows(ItemsControl host, Func<IPresetStore?> store, Func<object, string?> userName, Action? changed, PresetKind kind, Func<string?>? key)
    {
        _host = host; _store = store; _userName = userName; _changed = changed;
        _manage = () => PresetManager.Open(Window.GetWindow(host), kind, key?.Invoke());
        // The preset manager edits the same folders through its own stores: when this window is used again after the manager was open, list again.
        host.Loaded += (_, _) =>
        {
            if (_window is not null || Window.GetWindow(host) is not { } window) return;
            _window = window;
            window.Deactivated += (_, _) => _stale |= Application.Current?.Windows.OfType<PresetManagerWindow>().Any() == true;
            window.Activated += (_, _) =>
            {
                if (!_stale) return;
                _stale = false;
                if (_store() is PresetFolderStore files) files.RaiseChanged(); else if (_store() is DrumMapStore maps) maps.RaiseChanged();
                _changed?.Invoke();
            };
        };
        Confirm = text => Modal(text, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        ShowError = text => Modal(text, MessageBoxButton.OK, MessageBoxImage.Warning);
        AskDelete = text =>
        {
            var answer = PresetDeleteQuestion.Ask(Window.GetWindow(host), text, "Presets");
            if (host is ComboBox combo) combo.IsDropDownOpen = true;   // the question took the focus, which closes the list
            return answer;
        };
    }

    /// <summary>Makes a drop-down's (or menu's) user rows organisable. <paramref name="userName"/> gives the preset name of an item, null for a built-in
    /// or hint row; <paramref name="changed"/> runs after a change when the store's own Changed does not reach the window. Attach before the window's
    /// own SelectionChanged handler: picking the Manage row is undone here and never reaches it.</summary>
    internal static PresetRows Attach(ItemsControl host, PresetKind kind, Func<IPresetStore?> store, Func<object, string?> userName, Action? changed = null, Func<string?>? key = null)
    {
        var rows = new PresetRows(host, store, userName, changed, kind, key);
        host.SetValue(RowsProperty, rows);
        if (host is ComboBox combo)
        {
            combo.ItemTemplate = Template;   // rows that come from ItemsSource
            combo.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            combo.PreviewKeyDown += rows.OnKey;
            combo.DropDownClosed += (_, _) => rows.EndEdit();
            combo.SelectionChanged += (_, e) =>
            {
                if (rows._restoring) { e.Handled = true; return; }
                if (!ReferenceEquals(combo.SelectedItem, rows._manageItem)) return;
                e.Handled = true;
                var open = combo.IsDropDownOpen;   // a click or Enter in the open list; stepping onto the row with the arrow keys of the closed box only steps back
                rows._restoring = true;
                try { combo.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null; }
                finally { rows._restoring = false; }
                if (open) combo.Dispatcher.BeginInvoke(rows._manage);
            };
        }
        return rows;
    }

    internal static PresetRows? Of(ItemsControl host) => host.GetValue(RowsProperty) as PresetRows;

    /// <summary>A row of the drop-down: its text stays the item's content, so the closed box, typing to jump and screen readers are unchanged.</summary>
    internal ComboBoxItem Item(string text, object? tag, string? tip = null) =>
        new() { Content = text, Tag = tag, ToolTip = tip, ContentTemplate = Template, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    /// <summary>Ends a list with a line and the Manage presets… row, which is never a preset.</summary>
    internal void AddManage(System.Collections.IList items)
    {
        items.Add(new Separator());
        items.Add(_manageItem);
    }

    /// <summary>The Manage presets… entry of a menu.</summary>
    internal MenuItem ManageMenuItem()
    {
        var item = new MenuItem { Header = ManageText, ToolTip = "Open the preset manager" };
        item.Click += (_, _) => _manage();
        return item;
    }

    /// <summary>Opens the drop-down with that preset's name being edited.</summary>
    internal void OpenRename(string name)
    {
        if (_host is ComboBox combo) combo.IsDropDownOpen = true;
        _host.Dispatcher.BeginInvoke(new Action(() => BeginRename(name)), System.Windows.Threading.DispatcherPriority.Input);
    }

    private MessageBoxResult Modal(string text, MessageBoxButton buttons, MessageBoxImage image)
    {
        var result = ThemedMessageBox.Show(Window.GetWindow(_host), text, "Presets", buttons, image);
        if (_host is ComboBox combo) combo.IsDropDownOpen = true;   // the question took the focus, which closes the list
        return result;
    }


    // ---------- the four actions ----------
    /// <summary>Applies an inline rename. False when the name is refused (the reason shows in the row) or the user keeps the other preset.</summary>
    internal bool Rename(string from, string text)
    {
        if (_store() is not { } store) return false;
        var name = text.Trim();
        if ((PresetRules.Error(name) ?? (Reserved?.Invoke().Any(r => Same(r, name)) == true ? ReservedText : null)) is { } error) { Say(error); return false; }
        if (name == from) { EndEdit(); return true; }
        var clash = store.Names().FirstOrDefault(n => Same(n, name) && !Same(n, from));
        if (clash is not null && !Confirm($"A preset named \"{clash}\" already exists. Replace it?")) return false;
        return Try(() => store.Rename(from, name));
    }

    internal void Duplicate(string name)
    {
        if (_store() is { } store) Try(() => store.Duplicate(name, PresetRules.CopyName(name, store.Names())));
    }

    internal void Delete(string name)
    {
        if (_store() is { } store && PresetDeleteQuestion.Confirm(new[] { name }, AskDelete)) Try(() => store.Delete(name));
    }

    /// <summary>Moves a preset up (-1) or down (+1) one row.</summary>
    internal void Move(string name, int delta)
    {
        if (_store() is not { } store) return;
        var to = store.Names().ToList().FindIndex(n => Same(n, name)) + delta;
        if (to >= 0 && to < store.Names().Count) Try(() => store.Move(name, to));
    }

    /// <summary>Runs a store change; a file failure is shown, and the window lists the presets again either way.</summary>
    private bool Try(Action change)
    {
        EndEdit();
        var ok = true;
        try { change(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "presets: change: " + ex.Message);
            ok = false;
            ShowError("The preset could not be changed: " + ex.Message);
        }
        _changed?.Invoke();
        return ok;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Say(string text)
    {
        Message = text;
        if (_editor is not null) { _editor.ToolTip = text; _editor.SetResourceReference(Control.BorderBrushProperty, "AccentBrush"); }
        if (_editing is not null && _rows.GetValueOrDefault(_editing) is { Child: Grid grid } && grid.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetRow(t) == 1) is { } line)
        { line.Text = text; line.Visibility = Visibility.Visible; }
    }

    // ---------- rows ----------
    /// <summary>What a row of the list shows for an item: the organisable row for a user's preset, plain text otherwise.</summary>
    internal FrameworkElement View(object item, string text) => _userName(item) is { } name ? Row(name, text) : Plain(text);

    internal static TextBlock Plain(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>The organisable row: grip, name, rename, delete.</summary>
    internal Border Row(string name, string text)
    {
        var grid = new Grid { MinHeight = 22, MinWidth = 250 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // A click in the row being renamed belongs to the text box, not to the list.
        grid.MouseLeftButtonDown += (_, e) => { if (_editing is not null && Same(_editing, name)) e.Handled = true; };
        grid.MouseLeftButtonUp += (_, e) => { if (_editing is not null && Same(_editing, name)) e.Handled = true; };

        var grip = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 0, 6, 0), ToolTip = "Drag to reorder", Child = PresetManagePanel.Glyph("") };
        grip.MouseLeftButtonDown += (_, e) => { _dragging = name; grip.CaptureMouse(); e.Handled = true; };
        grip.MouseMove += (_, e) => { if (_dragging is not null) Indicate(DropIndex(e)); };
        grip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_dragging is not { } moving || _store() is not { } store) return;
            _dragging = null;
            grip.ReleaseMouseCapture();
            var to = DropIndex(e);
            Indicate(-1);
            var names = store.Names();
            var from = names.ToList().FindIndex(n => Same(n, moving));
            if (to > from) to--;   // the row leaves its place first
            to = Math.Clamp(to, 0, names.Count - 1);
            if (to != from) Try(() => store.Move(moving, to));
        };
        grid.Children.Add(grip);

        var label = Plain(text);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(Tool("", "Rename", $"Rename {name}", () => { if (RenameElsewhere is { } elsewhere) elsewhere(name); else BeginRename(name); }));
        buttons.Children.Add(Tool("", "Delete", $"Delete {name}", () => Delete(name)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        Grid.SetRow(line, 1); Grid.SetColumnSpan(line, 3);
        grid.Children.Add(line);
        return _rows[name] = new Border { Child = grid, Tag = name, Background = Brushes.Transparent };

        static Button Tool(string glyph, string tip, string spoken, Action run)
        {
            var button = new Button { Content = PresetManagePanel.Glyph(glyph), Width = 26, Height = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), Focusable = false };
            TooltipShortcuts.Bind(button, tip, null);
            AutomationProperties.SetName(button, spoken);
            button.Click += (_, e) => { e.Handled = true; run(); };
            return button;
        }
    }

    /// <summary>The name of a row becomes a text box: Enter applies, Esc cancels.</summary>
    internal void BeginRename(string name)
    {
        EndEdit();
        if (_rows.GetValueOrDefault(name) is not { Child: Grid grid } row) return;
        var label = grid.Children.OfType<TextBlock>().First(t => Grid.GetRow(t) == 0 && Grid.GetColumn(t) == 1);
        label.Visibility = Visibility.Collapsed;
        _editing = (string)row.Tag;
        var editor = _editor = new TextBox { Text = _editing, MaxLength = PresetRules.MaxNameLength, VerticalContentAlignment = VerticalAlignment.Center, Height = 22, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(editor, $"New name for {_editing}");
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        var from = _editing;
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; if (!Rename(from, editor.Text) && ReferenceEquals(_editor, editor)) { editor.Focus(); editor.SelectAll(); } }
            else if (e.Key == Key.Escape) { e.Handled = true; EndEdit(); }
        };
        editor.Dispatcher.BeginInvoke(new Action(() => { editor.Focus(); Keyboard.Focus(editor); editor.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void EndEdit()
    {
        Message = "";
        if (_editor is { } editor && _editing is { } name && _rows.GetValueOrDefault(name) is { Child: Grid grid })
        {
            grid.Children.Remove(editor);
            foreach (var text in grid.Children.OfType<TextBlock>()) text.Visibility = Grid.GetRow(text) == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        _editor = null; _editing = null;
    }

    // ---------- keys and drag ----------
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_editor is not null || _host is not ComboBox { IsDropDownOpen: true } combo) return;
        var highlighted = combo.Items.Cast<object>().FirstOrDefault(i => combo.ItemContainerGenerator.ContainerFromItem(i) is ComboBoxItem { IsHighlighted: true });
        if (highlighted is null || _userName(highlighted) is not { } name) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        if (key == Key.F2) BeginRename(name);
        else if (key == Key.Delete) Delete(name);
        else if (alt && key == Key.Up) Move(name, -1);
        else if (alt && key == Key.Down) Move(name, 1);
        else return;
        e.Handled = true;
    }

    /// <summary>The position among the user's presets a dragged row would take: before the first row whose middle is below the pointer.</summary>
    private int DropIndex(MouseEventArgs e)
    {
        var index = 0;
        foreach (var name in _store()?.Names() ?? Array.Empty<string>())
        {
            if (_rows.GetValueOrDefault(name) is { IsVisible: true } row && e.GetPosition(row).Y < row.ActualHeight / 2) break;
            index++;
        }
        return index;
    }

    /// <summary>A line where the dragged row will land (above the row at <paramref name="index"/>, or below the last one); -1 clears it.</summary>
    private void Indicate(int index)
    {
        if (_indicated is not null) { _indicated.BorderThickness = new Thickness(0); _indicated = null; }
        var names = _store()?.Names() ?? Array.Empty<string>();
        if (index < 0 || names.Count == 0) return;
        var last = index >= names.Count;
        if (_rows.GetValueOrDefault(names[last ? names.Count - 1 : index]) is not { } row) return;
        _indicated = row;
        row.BorderThickness = last ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
        row.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
    }

    /// <summary>The content of one row of the list. In the closed box (no list row around it) it is the plain text.</summary>
    private sealed class PresetRowView : Decorator
    {
        public PresetRowView()
        {
            Loaded += (_, _) => Build();
            DataContextChanged += (_, _) => { if (IsLoaded) Build(); };
        }

        private void Build()
        {
            if (DataContext is Separator)
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 3, 0, 3) };
                line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                Child = line;
                return;
            }
            var text = DataContext?.ToString() ?? "";
            DependencyObject? up = this;
            while (up is not null and not ComboBoxItem) up = VisualTreeHelper.GetParent(up);
            Child = up is ComboBoxItem container && ItemsControl.ItemsControlFromItemContainer(container) is { } list && list.GetValue(RowsProperty) is PresetRows rows
                ? rows.View(list.ItemContainerGenerator.ItemFromContainer(container), text)
                : Plain(text);
        }
    }
}
