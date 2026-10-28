using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;

// Owns: reordering by dragging the grip of a user row: the pointer capture, finding the row under the pointer, the shaded row that shows where the
//   preset will land, and the drop (PresetRows.DropOn). The pointer shape never changes; the shade is the only feedback.
// Does not own: the grip's drawing (PresetRowIcons) or moving the preset in the store (PresetRows.DropOn).
// Tests: PresetRowsTests, PresetUndoTests.
namespace TabForge.Avalonia.Views.Presets;

public sealed partial class PresetRows
{
    private const string DropSurface = "HoverSurface";

    private string? _dragging, _target;
    private Border? _marked;

    private void BeginDrag(Border grip, string name)
    {
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || _editor is not null) return;
            e.Pointer.Capture(grip);
            _dragging = name;
            _target = null;
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!Same(_dragging ?? "", name)) return;
            _target = RowUnder(grip, e.GetPosition(grip));
            Mark(_target);
        };
        grip.PointerReleased += (_, e) =>
        {
            if (!Same(_dragging ?? "", name)) return;
            var target = _target;
            e.Handled = true;
            e.Pointer.Capture(null);
            End();
            if (target is not null) DropOn(name, target);
        };
        grip.PointerCaptureLost += (_, _) => End();
    }

    /// <summary>The user row the pointer is over (the first or last when it is above or below them all); null when no row is shown.</summary>
    private string? RowUnder(Visual from, Point at)
    {
        var shown = _rows.Select(pair => (Name: pair.Key, Top: pair.Value.Grid.TranslatePoint(default, from), pair.Value.Grid.Bounds.Height))
            .Where(row => row.Top is not null).OrderBy(row => row.Top!.Value.Y).ToList();
        if (shown.Count == 0) return null;
        if (at.Y < shown[0].Top!.Value.Y) return shown[0].Name;
        foreach (var row in shown)
            if (at.Y < row.Top!.Value.Y + row.Height) return row.Name;
        return shown[^1].Name;
    }

    private void Mark(string? name)
    {
        var row = name is not null && _rows.TryGetValue(name, out var entry) ? entry.Grid.Parent as Border : null;
        if (ReferenceEquals(row, _marked)) return;
        if (_marked is not null) _marked.Background = AvaloniaPalette.TransparentBrush;
        _marked = row;
        if (row is not null) row.Background = BandBrush.Of(row, DropSurface);
    }

    private void End()
    {
        Mark(null);
        _dragging = null;
        _target = null;
    }
}
