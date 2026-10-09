# Docking

The dockable panel workspace: layout tree, drag and drop, floating windows.

## How to change me
1. Entry files: `DockLayoutTree.cs` / `DockDragController.cs`.
2. Owner class: `DockLayoutTree` (layout), `DockWorkspaceState` (saved).
3. Tests to run: `TestDockRatioNotRewrittenByAutoFit` (full-suite build); `--areas ui`, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: this README, the feature's page in `docs/feature-map/`.

## Key types
- `DockLayoutTree`: the layout as a tree of panels and splits.
- `DockWorkspaceState`: the saved layout.
- `DockDragController`: drag, drop zones and the drag ghost.
- `DockHostView`: the control that shows the tree.
- `FloatingWindowPlacement`: where a floating panel opens on screen.

A layout holds exactly one editor node, except a layout that holds the `band` panel: the Band layout has no score. A panel registered with `startsClosed` stays closed in a saved layout that predates it.

## Pathway
Layout changes are made on `DockLayoutTree` and saved through `DockWorkspaceState` (settings go through the shared `AppSettingsStore`). The view only displays the tree.

## Must not depend on
Song data, playback or the audio engine.

## Tests
`TestDockRatioNotRewrittenByAutoFit`, `TestBrowserTabShell`, `TestTabUi`.
