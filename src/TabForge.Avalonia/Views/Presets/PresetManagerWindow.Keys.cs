using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;

// Owns: the preset manager's keys: F2 rename, Delete, Alt+Up / Alt+Down move, Enter show in tree, Esc clears the search then closes,
//   Ctrl+F search, Ctrl+A select all, and Left/Right (and double-click) on a group in the tree.
// Does not own: what each key does (PresetManagerWindow) or the rename box (the window).
// Tests: PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

public sealed partial class PresetManagerWindow
{
    private void OnKey(object? sender, KeyEventArgs e)
    {
        var key = e.Key;
        if (_editor is not null)
        {
            if (key == Key.Escape) { e.Handled = true; EndEdit(); UpdateTools(); }   // the first Escape cancels the rename
            return;
        }
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var inList = _list.IsKeyboardFocusWithin;
        var inTree = _tree.IsKeyboardFocusWithin;
        if (ctrl && key == Key.F) { _search.Focus(); e.Handled = true; return; }
        if (ctrl && key == Key.A) { _list.SelectAll(); e.Handled = true; return; }
        switch (key)
        {
            case Key.Escape when SearchText.Length > 0: _search.Text = ""; break;
            case Key.Escape: Close(); break;
            case Key.Enter when SearchText.Length > 0: ShowInTree(); break;
            case Key.Down when _search.IsKeyboardFocusWithin: _list.Focus(); break;
            case Key.F2 when inList: BeginRename(); break;
            case Key.Delete when inList: UiTasks.Forget(DeleteSelectedAsync(), "Delete preset", Say); break;
            case Key.Up when inList && alt: MoveSelected(-1); break;
            case Key.Down when inList && alt: MoveSelected(1); break;
            case Key.Left when inTree && _node.IsGroup && _expanded.Contains(_node.Kind): Toggle(_node.Kind, false); break;
            case Key.Right when inTree && _node.IsGroup && !_expanded.Contains(_node.Kind): Toggle(_node.Kind, true); break;
            default: return;
        }
        e.Handled = true;
    }
}
