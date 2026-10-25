# Core/Docking

The pure data of the dock: the saved workspace layout, the layout tree that edits it, and the table of dock panes. No WPF and no UI types.

| File | Owns |
|---|---|
| `DockWorkspaceState.cs` | The saved layout (`DockWorkspaceState`), its split and tab nodes (`DockNodeState`), floating roots (`DockFloatingState`) and the drop zones (`DockDropZone`). |
| `DockLayoutTree.cs` | Building, cloning, searching, editing and validating the tree; `IsFixedPanel` (the Band pane stays where its layout puts it). |
| `DockDropRules.cs` | The drop-zone maths of a tab drag (zone from a point, split fit and fraction, preview rectangle) and `FloatingPlacementRules` (finite starting bounds, work-area fit), in DIPs on `UiPoint`/`UiRect`. |
| `DockPaneTable.cs` | One row per dock pane: id, titles, default placement, side-panel membership, registration order. |
| `DockWorkspaceRules.cs` | Restore (saved state to a valid tree), capture (tree to the saved state), visibility, the show / hide / close / reset changes with `ClosedPanels`, and `DropPanel` (a panel dropped into a zone; both front ends call it). |
| `DockSizeRules.cs` | The height maths in plain pixels: a tab host's maximum and locked height, the fit ratio, the splitter-drag clamp and the main dock overflow. |
| `DockLayoutCatalog.cs` | The built-in layouts, saved-layout lookup and naming, the Layouts menu rows as data, the score-back and side-panel rules. |
| `FloatingSessionRules.cs` | A floating pane's life as state changes: open floating, user close or redock (panels return to their default host), refit restored bounds to the work area (`IMonitorWorkAreas`). |
| `WorkspaceNormalizer.cs` | Validating and clamping a saved workspace from settings (fail closed); removing the Keyboard mode pane from a saved layout. |

Does not own: the views and drag handling (`Source/src/TabForge.Avalonia/Views/Dock/`), or the saved file (`AppSettingsStore`).
Tests: `TestDockPaneTable`, `TestDockDropRules`, `TestDockWorkspaceRulesRoundTrip`, `TestDockDropPanelCharacterization`, `TestDockSizeRulesCharacterization`, `TestDockLayoutCatalog`, `TestDockRatioNotRewrittenByAutoFit`, `TestFloatingSessionRules`.
