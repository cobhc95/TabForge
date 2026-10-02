# Docking

The dockable panel workspace: layout tree, drag and drop, floating windows.

## Key types
- `DockLayoutTree`: the layout as a tree of panels and splits.
- `DockWorkspaceState`: the saved layout.
- `DockDragController`: drag, drop zones and the drag ghost.
- `DockHostView`: the control that shows the tree.
- `FloatingWindowPlacement`: where a floating panel opens on screen.

## Pathway
Layout changes are made on `DockLayoutTree` and saved through `DockWorkspaceState` (settings go through the shared `AppSettingsStore`). The view only displays the tree.

## Must not depend on
Song data, playback or the audio engine.

## Tests
`TestDockRatioNotRewrittenByAutoFit`, `TestBrowserTabShell`, `TestTabUi`.
