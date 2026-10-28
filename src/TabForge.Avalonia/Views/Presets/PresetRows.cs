using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Plugins;
using TabForge.Presenters;

// Owns: the organisable rows of a preset ComboBox (or list): a "Built-in" and a "Yours" group, the pencil and bin of a user's row (shown on hover and on
//   the selected row only, PresetRowIcons), the drag grip that reorders (PresetRows.Drag), the inline rename box with the name rules, the replace
//   question, the delete that is done at once and offered back with Undo (PresetUndoToast), Alt+Up / Alt+Down reorder, F2 / Delete, the trailing
//   Manage row's selection guard, and listing again when the window is used after the manager was open.
// Does not own: the item types each window keeps (they carry per-window payloads: a program index, a Build function, a map tag), what
//   picking a preset does, or where presets live (IPresetStore). That is why the item types are not unified here: the row layer
//   takes a text, a user-name selector and a built-in test instead, the same shape.
// Tests: PresetRowsTests, PresetManagerOpenTests, PresetUndoTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>One rendered preset row: the text the box shows, and the preset name it edits when it is the user's own.</summary>
public sealed record PresetRow(string Text, string? Name, bool BuiltIn = false);

/// <summary>The group a row belongs to in the open list: none for a hint, a Manage row or a preset name that came with an imported chain.</summary>
public enum PresetGroup { None, BuiltIn, Yours }

/// <summary>The organisable rows of one preset ComboBox.</summary>
public sealed partial class PresetRows
{
    private const string ReservedText = "A preset that comes with the app has this name. Choose another name.";
    private const string ReplaceQuestionFormat = "A preset named \"{0}\" already exists. Replace it?";
    private const string ChangeFailed = "The preset could not be changed: ";
    private const string DeletedFormat = "Deleted \"{0}\"";

    private readonly SelectingItemsControl _host;
    private readonly Func<IPresetStore?> _store;
    private readonly Func<object, string?> _userName;
    private readonly Func<object?, bool> _isManage;
    private readonly Func<object?, bool> _isBuiltIn;
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
    /// <summary>The "Deleted ... Undo" message of this box's window.</summary>
    public PresetUndoToast Toast { get; }
    /// <summary>Why the last change was refused; empty when it was not.</summary>
    public string Message { get; private set; } = "";
    public bool IsEditing => _editor is not null;
    /// <summary>Names a user preset may not take (the ones that come with the app); empty by default.</summary>
    public Func<IEnumerable<string>> Reserved { get; set; } = () => Array.Empty<string>();

    private PresetRows(SelectingItemsControl host, Func<IPresetStore?> store, Func<object, string?> userName, Func<PresetCatalogKind> kind, Func<string?> key,
        Action? changed, Func<object?, bool> isManage, Func<object?, string> text, Func<object?, bool> isBuiltIn)
    {
        _host = host;
        _store = store;
        _userName = userName;
        _isManage = isManage;
        _isBuiltIn = isBuiltIn;
        _text = text;
        _kind = kind;
        _key = key;
        _changed = changed;
        Toast = new PresetUndoToast(() => Owner());
        Confirm = async text => await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), Owner()!, text, PresetManagerWindow.Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes;
        var template = new FuncDataTemplate<object>((item, _) => new PresetRowView(this), supportsRecycling: true);
        host.ItemTemplate = template;
        // A window that lists ready-made ComboBoxItems (note maps, drum maps) gets no item template from the box: give each its content template.
        host.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ContentControl { ContentTemplate: null } own && e.Index < host.ItemCount && ReferenceEquals(host.Items[e.Index], own)) own.ContentTemplate = template;
        };
        host.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        host.SelectionChanged += OnSelectionChanged;
        host.DetachedFromVisualTree += (_, _) => { EndEdit(); Toast.Dismiss(); };
        host.AttachedToVisualTree += (_, _) => WatchWindow();
    }

    /// <summary>Makes a ComboBox's (or list's) rows organisable; <paramref name="userName"/> gives the preset name of an item, null for a built-in or hint row.
    /// <paramref name="isBuiltIn"/> marks the rows that come with the app (they sit under the "Built-in" header); every other non-user row stays plain.</summary>
    public static PresetRows Attach(SelectingItemsControl host, Func<IPresetStore?> store, Func<object, string?> userName,
        Func<PresetCatalogKind> kind, Func<string?> key, Action? changed = null, Func<object?, bool>? isManage = null, Func<object?, string>? textOf = null,
        Func<object?, bool>? isBuiltIn = null) =>
        new(host, store, userName, kind, key, changed, isManage ?? PresetRowText.IsManage, textOf ?? (item => item?.ToString() ?? ""), isBuiltIn ?? (_ => false));

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
        }.AsPresetEditor();
        AutomationProperties.SetName(editor, $"New name for {match.Key}");
        Grid.SetColumn(editor, LabelColumn);
        Grid.SetRow(editor, ContentRow);
        Grid.SetColumnSpan(editor, ToolsColumn - LabelColumn + 1);
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

    /// <summary>Deletes one user preset at once and offers Undo for a few seconds; null when nothing was deleted.</summary>
    public PresetDeleteUndo? Delete(string name)
    {
        if (_store() is not { } store) return null;
        PresetDeleteUndo? undo = null;
        if (!Try(() => undo = PresetDeleteUndo.Delete(store, new[] { name }))) return null;
        if (_host is ComboBox box) box.IsDropDownOpen = false;   // the message would sit under the open list otherwise
        if (undo is { CanUndo: true } done) Toast.Show(string.Format(DeletedFormat, name), () => Try(done.Undo));
        return undo;
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

    /// <summary>Drops a user preset on another user preset: it takes the place of that one (what releasing the grip over a row does).</summary>
    public void DropOn(string name, string target)
    {
        if (_store() is not { } store || Same(name, target)) return;
        var to = store.Names().ToList().FindIndex(existing => Same(existing, target));
        if (to >= 0) Try(() => store.Move(name, to));
    }

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
            case Key.Delete: Delete(name); break;
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
            switch (_store())
            {
                case PresetFolderStore files: files.RaiseChanged(); break;
                case DrumMapStore maps: maps.RaiseChanged(); break;
                case TabForge.Views.EffectEditors.EffectPresetListStore effects: effects.RaiseChanged(); break;
            }
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
}
