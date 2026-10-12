# Views/IconRow

The icon row at the right of the top bar: its order, its group separators and dragging an icon to another place.

| File | Role |
|---|---|
| `IconRowOrder.cs` | Pure order rules: the default sequence, restoring a saved sequence (unknown ids dropped, missing icons appended), moving an icon, and which separators show (never first, last or doubled). |
| `IconRowDragController.cs` | Attached to the row's panel in `MainWindow.xaml` (`IconRowDragController.Reorder="True"`). Restores the saved order at load, handles the mouse drag with its insertion marker, hides dangling separators, saves the order, and restores the default when `ResetEvent` reaches the window. |

How it fits together:

- Each item of the row carries `IconRowDragController.Id` in XAML; a separator carries `|`. The order the panel is declared in is the default order, so a button added to the XAML later is a known icon without further code.
- The order is stored per user in `AppearanceSettings.IconRowOrder` (a list of ids; empty means the default).
- `DockWorkspace.ResetAllPanels` raises `IconRowDragController.ResetEvent`, so "Reset all panels" also restores the default icon order.
- Dragging is mouse only; the buttons keep their commands, tooltips, keyboard and screen-reader behaviour.

Tests: `TestIconRowOrder`.
