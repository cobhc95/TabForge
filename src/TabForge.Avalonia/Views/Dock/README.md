# Views/Dock: the dockable workspace

The dock tree, its saved state, layouts and pane table are Core (`DockPaneTable`, `DockLayoutTree`, `DockLayoutCatalog`, `DockWorkspaceRules`);
this folder turns that state into controls, handles tab drags/floats, and hosts each pane's content. Panes are registered by id through
`DockPaneSetup`, never found by element name.

| Class (files) | Job |
|---|---|
| `DockWorkspaceView` | Turns a `DockNodeState` tree into controls (splits, tab groups, the editor node), the host lookup by id and by point, and the drop preview. |
| `DockWorkspaceController` | The live layout of a view: restore, the edits a tab drag asks for, drop, close/reset panel/all, layout capture, and the floating windows. |
| `DockTabSurface`, `DockSplitPanel`, `DockSplitter` | One tab group (strip, selected content, context-menu hook, drag gesture), the two halves of a split from its ratio, and the draggable splitter bar. |
| `DockPaneSetup` | The registry: pane id → content factory, the editor content, natural-height panes, and placeholder panes for anything unregistered. |
| `DockPaneDragFlow`, `DockDragGhost` | A tab drag across windows: drop-zone preview over the dock, ghost outside it, dock or float on drop, Escape cancels. |
| `DockFloatingWindow`, `DockPanelMenu`, `DockViewMenus`, `DockPreviewWindow` | The floating pane window, the tab's context menu, the View > Layouts/Panels submenus, and the "Dock preview" window. |

## Pane ids

From Core `DockPaneTable.Rows` (Panels-menu order). The registration is in `MainWindow.cs` unless a factory column names another file.
Retired ids `practice` and `playback` are still accepted from old saved layouts, the editor id `editor` is not a pane.

| Pane id | Factory (registered at) | Content |
|---|---|---|
| `tools`, `structure`, `rhythm`, `layout` | `MainWindow.cs` `RegisterShared` | `Views/ToolsPaneView.cs` (one tab per Tools pane page) |
| `sections` | `MainWindow.cs` `SectionLinks.NewPanel` | `Views/SectionLinks.cs` (`SectionsPanelView`) |
| `lyrics` | `Lyrics/LyricsPaneFlow.Register` | `Lyrics/LyricsSongPane.cs` |
| `viewtempo` | `MainWindow.cs`, `DockPaneTable.ViewTempoId` | `Views/ViewTempoBarView.cs` |
| `instrument` | `MainWindow.cs`, `setup.Register("instrument", …)` | `Platform/InstrumentPanelStrip.cs` |
| `timeline` | `MainWindowDocumentPanes.NewArrangement` | `Views/ArrangementView.cs` |
| `band` | `MainWindowDocumentPanes.NewBand` | `Platform/BandView.cs` |
| `learn` | `MainWindowDocumentPanes.NewKeyboardMode` | `Views/KeyboardModePane.cs` |
| `editor` (not a pane) | `MainWindow.cs` `RegisterEditor` | `Platform/ScorePreviewView.cs` |

Rules: pane ids are stable (they are in saved layouts); a factory makes a fresh control per rebuild except the shared ones; a drag never
changes the model mid-gesture; no per-frame layout while dragging (render transforms only).

Tests: `DockWorkspaceControllerTests`, `DockWorkspaceViewTests`, `DockFloatingWindowTests`, `DockDefaultLayoutTests`, `DockLayoutSnapshotTests`,
`DockStartupLayoutTests`, `TabTearOffTests`, `MainWindowMenuTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~DockWorkspaceControllerTests"`.
