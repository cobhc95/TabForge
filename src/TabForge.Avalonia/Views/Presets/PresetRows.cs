using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Plugins;
using TabForge.Presenters;

// Owns: the organisable rows of a preset ComboBox: the two icon buttons (PresetRowIcons) of a user's row, the inline rename box with the name
//   rules, the replace and delete questions, Alt+Up / Alt+Down reorder, F2 / Delete, the trailing Manage row's selection guard, and listing
//   again when the window is used after the manager was open. Built-in and hint rows stay plain text.
// Does not own: the item types each window keeps (they carry per-window payloads: a program index, a Build function, a map tag), what
//   picking a preset does, or where presets live (IPresetStore). That is why the three item types are not unified here: the row layer
//   takes a text and a user-name selector instead, the same shape as the WPF PresetRows.
// Tests: PresetRowsTests, PresetManagerOpenTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>One rendered preset row: the text the box shows, and the preset name it edits when it is the user's own.</summary>
public sealed record PresetRow(string Text, string? Name, bool BuiltIn = false);

/// <summary>The organisable rows of one preset ComboBox.</summary>
public sealed class PresetRows
{
    private const string DeleteQuestionCaption = "Presets";
    private const string ReservedText = "A preset that comes with the app has this name. Choose another name.";
    private const string ReplaceQuestionFormat = "A preset named \"{0}\" already exists. Replace it?";
    private const string ChangeFailed = "The preset could not be changed: ";
    private const double RowMinHeight = 22, LabelGap = 6;

    private readonly ComboBox _host;
    private readonly Func<IPresetStore?> _store;
    private readonly Func<object, string?> _userName;
    private readonly Func<object?, bool> _isManage;
    private readonly Func<object?, string> _text;
    private readonly Func<PresetCatalogKind> _kind;
    private readonly Func<string?> _key;
    private readonly Action? _changed;
    private readonly Dictionary<string, (Grid Grid, TextBlock Label, TextBlock Error)> _rows = new(StringComparer.OrdinalIgnoreCase);
    private TextBox? _editor;
    private string? _editing;
    private bool _restoring, _stale;

    /// <summary>Asks a yes / no question (a replace); tests answer it directly.</summary>
    public Func<string, Task<bool>> Confirm { get; set; }
    /// <summary>Asks the delete question (text in; answer and tick out); tests answer it directly.</summary>
    public Func<string, Task<(bool Yes, bool Remember)>> AskDelete { get; set; }
    /// <summary>Why the last change was refused; empty when it was not.</summary>
    public string Message { get; private set; } = "";
    public bool IsEditing => _editor is not null;
    /// <summary>Names a user preset may not take (the ones that come with the app); empty by default.</summary>
    public Func<IEnumerable<string>> Reserved { get; set; } = () => Array.Empty<string>();

    private PresetRows(ComboBox host, Func<IPresetStore?> store, Func<object, string?> userName, Func<PresetCatalogKind> kind, Func<string?> key, Action? changed, Func<object?, bool> isManage, Func<object?, string> text)
    {
        _host = host;
        _store = store;
        _userName = userName;
        _isManage = isManage;
        _text = text;
        _kind = kind;
        _key = key;
        _changed = changed;
        Confirm = async text => await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), Owner()!, text, DeleteQuestionCaption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes;
        AskDelete = text => PresetDeleteQuestion.AskAsync(new AvaloniaDialogHost(), Owner()!, text);
        host.ItemTemplate = new FuncDataTemplate<object>((item, _) => new PresetRowView(this), supportsRecycling: true);
        host.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        host.SelectionChanged += OnSelectionChanged;
        host.DetachedFromVisualTree += (_, _) => EndEdit();
        host.AttachedToVisualTree += (_, _) => WatchWindow();
    }

    /// <summary>Makes a ComboBox's rows organisable; <paramref name="userName"/> gives the preset name of an item, null for a built-in or hint row.</summary>
    public static PresetRows Attach(ComboBox host, Func<IPresetStore?> store, Func<object, string?> userName,
        Func<PresetCatalogKind> kind, Func<string?> key, Action? changed = null, Func<object?, bool>? isManage = null, Func<object?, string>? textOf = null) => new(host, store, userName, kind, key, changed, isManage ?? PresetRowText.IsManage, textOf ?? (item => item?.ToString() ?? ""));

    /// <summary>True when the row is one of the user's presets (never a built-in, hint or Manage row).</summary>
    public bool IsPreset(object? item) => _userName(item!) is not null;

    /// <summary>Starts renaming a user row in place.</summary>
    public void BeginRename(string name)
    {
        EndEdit();
        if (_store() is not { } store || !store.Names().Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        var match = _rows.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
        if (match.Value.Grid is not { } grid || match.Value.Label is not { } label) return;
        _editing = match.Key;
        label.IsVisible = false;
        var editor = _editor = new TextBox
        {
            Text = match.Key, MaxLength = PresetRules.MaxNameLength, VerticalAlignment = VerticalAlignment.Center,
            Height = 22, Padding = new Thickness(2, 0, 2, 0),
        };
        AutomationProperties.SetName(editor, $"New name for {match.Key}");
        Grid.SetColumn(editor, 0);
        grid.Children.Add(editor);
        var from = match.Key;
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Platform.UiTasks.Forget(CommitRenameAsync(from, editor.Text ?? ""), "Rename preset", ShowMessage); }
            else if (e.Key == Key.Escape) { e.Handled = true; EndEdit(); }
        };
        editor.Focus();
        editor.SelectAll();
    }

    /// <summary>Applies an inline rename; false when the name is refused (the reason stays on the row) or the user keeps the other preset.</summary>
    public async Task<bool> CommitRenameAsync(string from, string text)
    {
        if (_store() is not { } store) return false;
        var name = text.Trim();
        if ((PresetRules.Error(name) ?? (Reserved().Any(reserved => Same(reserved, name)) ? ReservedText : null)) is { } error)
        {
            ShowMessage(error);
            return false;
        }
        if (Same(name, from)) { EndEdit(); return true; }
        var clash = store.Names().FirstOrDefault(existing => Same(existing, name) && !Same(existing, from));
        if (clash is not null && !await Confirm(string.Format(ReplaceQuestionFormat, clash))) return false;
        if (!Try(() => store.Rename(from, name, replace: clash is not null))) return false;
        EndEdit();
        return true;
    }

    /// <summary>Deletes one user preset after the one delete question.</summary>
    public async Task DeleteAsync(string name)
    {
        if (_store() is not { } store) return;
        if (!await PresetDeleteQuestion.ConfirmAsync(new[] { name }, AskDelete, PresetManager.ConfirmDeletePresets, value => PresetManager.RememberDeletePresets(value)))
            return;
        Try(() => store.Delete(name));
    }

    /// <summary>Moves a user preset one row up (-1) or down (+1); a move past either end does nothing.</summary>
    public void Move(string name, int delta)
    {
        if (_store() is not { } store) return;
        var names = store.Names().ToList();
        var from = names.FindIndex(existing => Same(existing, name));
        var to = from + delta;
        if (from < 0 || to < 0 || to >= names.Count) return;
        Try(() => store.Move(name, to));
    }

    /// <summary>What a row renders: the organisable row for a user's preset, plain text otherwise.</summary>
    public Control View(object? item)
    {
        var text = _text(item);
        return _userName(item!) is { } name ? Row(name, text) : Plain(text);
    }

    private Control Row(string name, string text)
    {
        var grid = new Grid { MinHeight = RowMinHeight, ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        var label = Plain(text);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = PresetRowIcons.ButtonGap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(LabelGap, 0, 0, 0),
        };
        tools.Children.Add(PresetRowIcons.Rename(name, () => BeginRename(name)));
        tools.Children.Add(PresetRowIcons.Delete(name, () => Platform.UiTasks.Forget(DeleteAsync(name), "Delete preset", ShowMessage)));
        Grid.SetColumn(tools, 1);
        grid.Children.Add(tools);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 2, 0, 2) };
        error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");
        Grid.SetRow(error, 1);
        Grid.SetColumnSpan(error, 2);
        grid.Children.Add(error);
        grid.RowDefinitions = RowDefinitions.Parse("Auto,Auto");
        _rows[name] = (grid, label, error);
        return new Border { Child = grid, Padding = new Thickness(2, 1), Tag = name };
    }

    private static TextBlock Plain(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_restoring) { e.Handled = true; return; }
        if (!_isManage(_host.SelectedItem)) return;
        e.Handled = true;
        var previous = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
        _restoring = true;
        try { _host.SelectedItem = previous; }
        finally { _restoring = false; }
        if (Owner() is not { } owner) return;
        Dispatcher.UIThread.Post(() => PresetManager.Open(owner, _kind(), _key()), DispatcherPriority.Background);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_editor is not null || _host.SelectedItem is not { } item || !IsPreset(item)) return;
        var name = _userName(item);
        if (name is null) return;
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.F2: BeginRename(name); break;
            case Key.Delete: Platform.UiTasks.Forget(DeleteAsync(name), "Delete preset", ShowMessage); break;
            case Key.Up when alt: Move(name, -1); break;
            case Key.Down when alt: Move(name, 1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void WatchWindow()
    {
        if (TopLevel.GetTopLevel(_host) is not Window window) return;
        window.Deactivated += (_, _) => _stale |= PresetManager.IsOpen;
        window.Activated += (_, _) =>
        {
            if (!_stale) return;
            _stale = false;
            if (_store() is PresetFolderStore files) files.RaiseChanged();
            else if (_store() is DrumMapStore maps) maps.RaiseChanged();
            _changed?.Invoke();
        };
    }

    private Window? Owner() => TopLevel.GetTopLevel(_host) as Window;

    private bool Try(Action change)
    {
        EndEdit();
        Message = "";
        foreach (var row in _rows.Values) { row.Error.Text = ""; row.Error.IsVisible = false; }
        try { change(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "presets: change: " + ex.Message);
            ShowMessage(ChangeFailed + ex.Message);
            _changed?.Invoke();
            return false;
        }
        _changed?.Invoke();
        return true;
    }

    private void ShowMessage(string text)
    {
        Message = text;
        var name = _editing ?? (_host.SelectedItem is { } item ? _userName(item!) : null);
        if (name is { } key && _rows.GetValueOrDefault(key) is { } row)
        {
            row.Error.Text = text;
            row.Error.IsVisible = text.Length > 0;
        }
    }

    private void EndEdit()
    {
        Message = "";
        if (_editing is { } name && _rows.GetValueOrDefault(name) is { } row)
        {
            row.Error.Text = "";
            row.Error.IsVisible = false;
            row.Label.IsVisible = true;
        }
        if (_editor is { } editor && _editing is { } current && _rows.GetValueOrDefault(current) is { } entry)
            entry.Grid.Children.Remove(editor);
        _editor = null;
        _editing = null;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The row content: the organisable row inside the open list, plain text in the closed box.</summary>
    private sealed class PresetRowView : Decorator
    {
        private readonly PresetRows _rows;
        public PresetRowView(PresetRows rows)
        {
            _rows = rows;
            Loaded += (_, _) => Build();
            DataContextChanged += (_, _) => { if (IsLoaded) Build(); };
        }

        private void Build()
        {
            var text = DataContext?.ToString() ?? "";
            var inList = this.GetVisualAncestors().OfType<ComboBoxItem>().Any();
            Child = inList ? _rows.View(DataContext) : Plain(text);
        }
    }
}
