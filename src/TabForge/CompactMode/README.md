# CompactMode

Compact mode hides every track row of the arrangement and shows the master track lane at twice its height. Per window, never saved.

- `CompactModeFeatureModule.cs`: the Compact mode command, hotkey row and View menu row; checks the menu row.
- `CompactModeController.cs`: the on/off state, the track-list pane height (shrunk while on, put back after) and the side panel (hidden while on; restored on leaving only when compact mode hid it and the user did not show it meanwhile).
- `CompactModeButton.cs`: the toolbar button; a click runs the menu row, and it is lit while the row is checked.
- `CompactModeStrip.cs`: the header strip under the top bar while compact mode is on: caption, one pill per track (the selected track lit; a click selects that track) and the close button. Hidden while the Band view shows its own header. The pill parts are shared with the Band view (`TrackPillStrip`).
- `CompactModeMenu.cs`: the strip's right-click menu. While compact mode is on, the window's menu row is hidden in every layout; the strip's caption (with a chevron) and a right-click open the menu, and with the Band view on its header does the same and its roll-down gets the rows too. The menu offers that row's icon buttons (same icon, key and lit state) and the main menus as sub-menus, whose real items are lent to it while it is open.
- `CompactColumnLayout.cs`: the track list's left column while compact mode is on: the toolbar's own buttons in rows of five in a slim column (no Add track button, no column header row, no inline Master row), and everything put back on leaving.
- `ArrangementPanel.CompactMode.cs`: the panel's flag; hides the Add-track strip and keeps the scroll position.

The rows are hidden by the row geometry (`ArrangementPanel.RowGeometry.cs` reads `MasterLane.IsCompact`), the same path collapsed groups use. Test: `TestCompactMode`.
