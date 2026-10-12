using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Plugins;

namespace TabForge.Views.Presets;

// Owns: the preset manage panel, a popup under a preset drop-down that lists the user's saved presets of one kind, one row each
//   (grip, name, rename, delete): inline rename with its name rules, the overwrite and delete questions, reorder by the
//   grip or Alt+Up / Alt+Down, and the keys (Up, Down, F2, Delete, Esc).
// Does not own: where presets live or how they are loaded (IPresetStore and the window that shows the drop-down), or built-in entries.
// Tests: TestPresetManagePanel, TestPresetStoresOnDisk.
public sealed class PresetManagePanel
{
    private const string Icons = "Segoe MDL2 Assets";
    private readonly IPresetStore _store;
    private readonly UIElement _target;
    private readonly string _title;
    private readonly Popup _popup;
    private readonly Border _frame;
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), MaxHeight = 320, Background = Brushes.Transparent };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 6, 2), Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty = new() { Text = "No saved presets yet", Margin = new Thickness(6, 6, 6, 6) };
    private TextBox? _editor;
    private string? _editing, _dragging;
    private Border? _indicated;

    /// <summary>Asks a yes / no question (the panel steps aside while the themed message box is up). Tests answer it directly.</summary>
    internal Func<string, bool> Confirm;
    /// <summary>Asks the delete question (text in; answer and "don't ask again" tick out). Tests answer it directly.</summary>
    internal Func<string, (bool Yes, bool Remember)> AskDelete;
    /// <summary>Shows a failure (a file could not be changed). Tests collect it.</summary>
    internal Action<string> ShowError;

    public PresetManagePanel(UIElement target, IPresetStore store, string title)
    {
        _target = target; _store = store; _title = title;
        var header = new TextBlock { Text = title, FontSize = Services.ThemeService.MinFontSize, Margin = new Thickness(6, 2, 6, 4) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _message.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(_list, title);
        var stack = new StackPanel();
        stack.Children.Add(header); stack.Children.Add(_list); stack.Children.Add(_empty); stack.Children.Add(_message);
        _frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(4), Child = stack, MinWidth = 300, MaxWidth = 420 };
        _frame.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        _frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _frame.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _frame.SetResourceReference(FrameworkElement.LayoutTransformProperty, "UiScaleTransform");
        _frame.PreviewKeyDown += OnKey;
        _popup = new Popup { Child = _frame, PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        _popup.Opened += (_, _) => { _store.Changed += Rebuild; Rebuild(); FocusList(); };
        _popup.Closed += (_, _) => _store.Changed -= Rebuild;
        Confirm = text =>
        {
            _popup.IsOpen = false;
            var yes = ThemedMessageBox.Show(Window.GetWindow(_target), text, _title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            _popup.IsOpen = true;
            return yes;
        };
        ShowError = text =>
        {
            _popup.IsOpen = false;
            ThemedMessageBox.Show(Window.GetWindow(_target), text, _title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _popup.IsOpen = true;
        };
        AskDelete = text =>
        {
            _popup.IsOpen = false;
            var answer = PresetDeleteQuestion.Ask(Window.GetWindow(_target), text, _title);
            _popup.IsOpen = true;
            return answer;
        };
        Rebuild();
    }

    /// <summary>Opens a panel under the drop-down once the menu or button that asked for it has let go; <paramref name="rename"/> starts with that preset's name being edited.</summary>
    public static PresetManagePanel Open(UIElement target, IPresetStore store, string title, string? rename = null)
    {
        var panel = new PresetManagePanel(target, store, title);
        target.Dispatcher.BeginInvoke(new Action(() =>
        {
            panel._popup.IsOpen = true;
            if (rename is not null) panel.BeginRename(rename);
        }), System.Windows.Threading.DispatcherPriority.Input);
        return panel;
    }

    /// <summary>The panel's content (the capture path draws it without a popup).</summary>
    internal FrameworkElement TakeContent() { _popup.Child = null; return _frame; }
    internal IReadOnlyList<string> Rows => _list.Items.Cast<ListBoxItem>().Select(i => (string)i.Tag).ToList();
    internal string? Selected { get => (_list.SelectedItem as ListBoxItem)?.Tag as string; set => Select(value); }
    internal string Message => _message.Visibility == Visibility.Visible ? _message.Text : "";
    internal bool IsEmptyShown => _empty.Visibility == Visibility.Visible;

    // ---------- the four actions ----------
    /// <summary>Applies an inline rename. False when the name is refused (the reason shows in the panel) or the user keeps the other preset.</summary>
    internal bool Rename(string from, string text)
    {
        var name = text.Trim();
        if (PresetRules.Error(name) is { } error) { Say(error); return false; }
        if (name == from) { Rebuild(); return true; }
        var clash = _store.Names().FirstOrDefault(n => Same(n, name) && !Same(n, from));
        if (clash is not null && !Confirm($"A preset named \"{clash}\" already exists. Replace it?")) return false;
        return Try(() => _store.Rename(from, name), name);
    }

    internal void Duplicate(string name)
    {
        var copy = PresetRules.CopyName(name, _store.Names());
        Try(() => _store.Duplicate(name, copy), copy);
    }

    /// <summary>Deletes one preset after the delete question (the setting may skip it).</summary>
    internal void Delete(string name) => Delete(new[] { name });

    /// <summary>Deletes presets of this store after one question; false when the question is answered No.</summary>
    internal bool Delete(IReadOnlyList<string> names) => PresetDeleteQuestion.Confirm(names, AskDelete) && Remove(names);

    /// <summary>Deletes presets of this store without asking (the caller has asked). True when they were deleted.</summary>
    internal bool Remove(IReadOnlyList<string> names)
    {
        var index = _list.SelectedIndex;
        var done = Try(() => { foreach (var name in names) _store.Delete(name); }, null);
        if (done && _list.Items.Count > 0) _list.SelectedIndex = Math.Clamp(index, 0, _list.Items.Count - 1);
        return done;
    }

    /// <summary>Moves a preset up (-1) or down (+1) one row.</summary>
    internal void Move(string name, int delta) => MoveTo(name, Rows.ToList().FindIndex(n => Same(n, name)) + delta);

    /// <summary>Moves the named presets one row (delta) as a block, keeping their order. False when none can move.</summary>
    internal bool MoveBlock(IReadOnlyList<string> names, int delta)
    {
        var steps = BlockSteps(_store.Names().ToList(), names, delta);
        if (steps.Count == 0) return false;
        return Try(() => { foreach (var (name, index) in steps) _store.Move(name, index); }, null);
    }

    /// <summary>
    /// The moves that shift the chosen names one row (delta) as a block: each moves past the row beside it when that row is not chosen,
    /// so a name at the edge, or behind a chosen one that cannot move, stays. Empty when nothing can move.
    /// </summary>
    internal static List<(string Name, int Index)> BlockSteps(IReadOnlyList<string> order, IReadOnlyList<string> chosen, int delta)
    {
        var list = order.ToList();
        var set = new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);
        var moving = list.Where(set.Contains).ToList();
        if (delta > 0) moving.Reverse();   // the row nearest the way it moves goes first
        var steps = new List<(string Name, int Index)>();
        foreach (var name in moving)
        {
            var from = list.FindIndex(n => Same(n, name));
            var to = from + delta;
            if (to < 0 || to >= list.Count || set.Contains(list[to])) continue;
            list.RemoveAt(from);
            list.Insert(to, name);
            steps.Add((name, to));
        }
        return steps;
    }

    private void MoveTo(string name, int index)
    {
        if (index < 0 || index >= _list.Items.Count) return;
        Try(() => _store.Move(name, index), name);
    }

    /// <summary>Runs a store change; a file failure is shown and the list is read again, so it always shows what is on disk.</summary>
    private bool Try(Action change, string? select)
    {
        Say(null);
        try { change(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "presets: change: " + ex.Message);
            Rebuild();
            ShowError("The preset could not be changed: " + ex.Message);
            return false;
        }
        Rebuild();
        if (select is not null) Select(select);
        FocusList();
        return true;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Say(string? text)
    {
        _message.Text = text ?? "";
        _message.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- rows ----------
    private void Rebuild()
    {
        var selected = Selected;
        _editor = null; _editing = null;
        _list.Items.Clear();
        foreach (var name in _store.Names()) _list.Items.Add(Row(name));
        _empty.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = _list.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Select(selected);
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Select(string? name) => _list.SelectedItem = _list.Items.Cast<ListBoxItem>().FirstOrDefault(i => name is not null && Same((string)i.Tag, name)) ?? _list.SelectedItem;

    private void FocusList() => _list.Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_editor is not null) return;
        if ((_list.SelectedItem ?? (_list.Items.Count > 0 ? _list.Items[0] : null)) is ListBoxItem item) item.Focus(); else _frame.Focus();
    }), System.Windows.Threading.DispatcherPriority.Input);

    private ListBoxItem Row(string name)
    {
        var grid = new Grid { MinHeight = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var item = new ListBoxItem { Content = new Border { Child = grid }, Tag = name, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(2, 1, 2, 1) };
        AutomationProperties.SetName(item, name);

        var grip = new Border { Background = Brushes.Transparent, Padding = new Thickness(4, 0, 6, 0), ToolTip = "Drag to reorder", Child = Glyph("") };
        grip.MouseLeftButtonDown += (_, e) => { _list.SelectedItem = item; _dragging = name; grip.CaptureMouse(); e.Handled = true; };
        grip.MouseMove += (_, e) => { if (_dragging is not null) Indicate(DropIndex(e)); };
        grip.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragging is not { } moving) return;
            _dragging = null;
            grip.ReleaseMouseCapture();
            var to = DropIndex(e);
            Indicate(-1);
            var from = Rows.ToList().FindIndex(n => Same(n, moving));
            if (to > from) to--;   // the row leaves its place first
            if (to != from) MoveTo(moving, Math.Clamp(to, 0, _list.Items.Count - 1));
        };
        grid.Children.Add(grip);

        var text = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = "Double-click to rename" };
        text.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { e.Handled = true; BeginRename(name); } };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(Tool("", "Rename", $"Rename {name}", () => BeginRename(name)));
        buttons.Children.Add(Tool("", "Delete", $"Delete {name}", () => Delete(name)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return item;

        Button Tool(string glyph, string tip, string spoken, Action run)
        {
            var button = new Button { Content = Glyph(glyph), Width = 26, Height = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), Focusable = false };
            TooltipShortcuts.Bind(button, tip, null);
            AutomationProperties.SetName(button, spoken);
            button.Click += (_, _) => { _list.SelectedItem = item; run(); };
            return button;
        }
    }

    internal static TextBlock Glyph(string glyph) => new() { Text = glyph, FontFamily = new FontFamily(Icons), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>The name of a row becomes a text box: Enter applies, Esc cancels.</summary>
    internal void BeginRename(string name)
    {
        Rebuild();
        Select(name);
        if (_list.SelectedItem is not ListBoxItem { Content: Border { Child: Grid grid } } item || !Same((string)item.Tag, name)) return;
        var old = grid.Children.OfType<TextBlock>().First();
        grid.Children.Remove(old);
        _editing = (string)item.Tag;
        _editor = new TextBox { Text = _editing, MaxLength = PresetRules.MaxNameLength, VerticalContentAlignment = VerticalAlignment.Center, Height = 22, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(_editor, $"New name for {_editing}");
        Grid.SetColumn(_editor, 1);
        grid.Children.Add(_editor);
        var editor = _editor;
        editor.Dispatcher.BeginInvoke(new Action(() => { editor.Focus(); Keyboard.Focus(editor); editor.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---------- keys and drag ----------
    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_editor is { } editor && _editing is { } editing)
        {
            if (key == Key.Enter) { e.Handled = true; if (!Rename(editing, editor.Text)) { editor.Focus(); editor.SelectAll(); } }
            else if (key == Key.Escape) { e.Handled = true; Say(null); Rebuild(); FocusList(); }
            return;
        }
        if (key == Key.Escape) { e.Handled = true; _popup.IsOpen = false; return; }
        if (Selected is not { } name) return;
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        if (key == Key.F2) BeginRename(name);
        else if (key == Key.Delete) Delete(name);
        else if (alt && key == Key.Up) Move(name, -1);
        else if (alt && key == Key.Down) Move(name, 1);
        else return;
        e.Handled = true;
    }

    /// <summary>The position a dragged row would take: before the first row whose middle is below the pointer.</summary>
    private int DropIndex(MouseEventArgs e)
    {
        var index = 0;
        foreach (ListBoxItem item in _list.Items)
        {
            if (e.GetPosition(item).Y < item.ActualHeight / 2) break;
            index++;
        }
        return index;
    }

    /// <summary>A line where the dragged row will land (above the row at <paramref name="index"/>, or below the last one); -1 clears it.</summary>
    private void Indicate(int index)
    {
        if (_indicated is not null) { _indicated.BorderThickness = new Thickness(0); _indicated = null; }
        if (index < 0 || _list.Items.Count == 0) return;
        var last = index >= _list.Items.Count;
        _indicated = (Border)((ListBoxItem)_list.Items[last ? _list.Items.Count - 1 : index]).Content;
        _indicated.BorderThickness = last ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
        _indicated.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
    }
}
