using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: reordering the manager's list by dragging the grip of a user preset: the pointer capture, the row under the pointer, the shaded row that
//   shows where it lands, and the drop (PresetManagerActions.Drop). The pointer shape never changes; the shade is the only feedback.
// Does not own: the grip's drawing (PresetRowIcons), the rows (PresetManagerWindow.Rows.cs) or moving the preset in the store (the actions).
// Tests: PresetUndoTests.
namespace TabForge.Avalonia.Views.Presets;

public sealed partial class PresetManagerWindow
{
    private const string DropSurface = "HoverSurface";

    private PresetEntry? _dragging, _target;
    private ListBoxItem? _marked;

    private void BeginDrag(Border grip, PresetEntry entry)
    {
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || _editor is not null) return;
            e.Pointer.Capture(grip);
            _dragging = entry;
            _target = null;
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (_dragging != entry) return;
            _target = EntryUnder(grip, e.GetPosition(grip));
            Mark(_target);
        };
        grip.PointerReleased += (_, e) =>
        {
            if (_dragging != entry) return;
            var target = _target;
            e.Handled = true;
            e.Pointer.Capture(null);
            EndDrag();
            if (target is not null) Drop(entry, target);
        };
        grip.PointerCaptureLost += (_, _) => EndDrag();
    }

    /// <summary>Drops a user preset on another of the same place: it takes that one's position.</summary>
    public void Drop(PresetEntry entry, PresetEntry target)
    {
        var result = _actions.Drop(entry, target);
        if (result.Error is { } error) Say(error);
        if (result.Ok) FillList(new[] { entry });
    }

    // The listed user preset under the pointer (the first or last shown one when it is above or below them all); built-in rows are not drop places.
    private PresetEntry? EntryUnder(Visual from, Point at)
    {
        var rows = _list.GetRealizedContainers().OfType<ListBoxItem>().Select(item => (Item: item, Top: item.TranslatePoint(default, from)))
            .Where(row => row.Top is not null && row.Item.Content is PresetEntry { BuiltIn: false }).OrderBy(row => row.Top!.Value.Y).ToList();
        if (rows.Count == 0) return null;
        var hit = rows.FirstOrDefault(row => at.Y < row.Top!.Value.Y + row.Item.Bounds.Height);
        return (hit.Item ?? rows[^1].Item).Content as PresetEntry;
    }

    private void Mark(PresetEntry? entry)
    {
        var item = entry is null ? null : _list.GetRealizedContainers().OfType<ListBoxItem>().FirstOrDefault(c => Equals(c.Content, entry));
        if (ReferenceEquals(item, _marked)) return;
        _marked?.ClearValue(BackgroundProperty);
        _marked = item;
        if (item is not null) item.Background = BandBrush.Of(item, DropSurface);
    }

    private void EndDrag()
    {
        Mark(null);
        _dragging = null;
        _target = null;
    }
}
