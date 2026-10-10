# Note right-click menu

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md). The fretboard, keyboard and drum panel and its own right-click menu are in [instrument-views.md](instrument-views.md).

Three menus open from the score and the panel: the note menu (right-click on a note, or inside the selection), the score's empty-area menu (right-click on a blank part of the score), and the fretboard menu (right-click on the panel, or Shift+F10 / the Menu key on it). This page covers the first two. It names where the fretboard menu lives.

- **Main code:** `MainWindow.ContextMenus.cs` (ShowNoteContextMenu, ShowScoreContextMenu); `ContextMenuLayouts` (the note menu's order and its fixed texts); `ScoreMenus` (the empty-area menu as data); `InstrumentMenus` (the fretboard menu as data).
- **Fretboard menu:** ShowInstrumentContextMenu and RunInstrumentCommand are in `src/TabForge/MainWindow.View.cs`, not in `MainWindow.ContextMenus.cs`. Its data is `InstrumentMenus` in `src/TabForge/Views/ContextMenuSpecs.cs`.
- **Owner folders:** `src/TabForge/Views/` (menu data and the popup helper) and the right-click hit test in `src/TabForge/Views/Score/`. The window hooks are `src/TabForge/MainWindow.ContextMenus.cs` and `src/TabForge/MainWindow.xaml.cs`.
- **Tests:** `--areas ui`. Narrow names (confirmed with `--only` on a full-suite build): `TestContextMenuLayouts`, `TestContextMenuLean`, `TestNoteMenuClipboardActions`, `TestNoteMenuEditActions`, `TestNoteMenuItemsRouted`, `TestScoreContextMenuByKeyboard`, `TestKeyboardContextMenuPlacement`, `TestTimelineAndInstrumentContextMenuByKeyboard`, `TestMenuPopupWarmup`, `TestMenuGestureTextFollowsBindings`, and `TestMainMenuTreeGolden` for the main menu and the menus built from `MenuSpec` data. Tests live in `tests/full-suite/Editor/` (`SelfTestSelection.cs`, `SelfTestKeyboardContextMenu.cs`), `tests/full-suite/Views/` and `tests/full-suite/Menus/`.

## Pathway to use

1. A right-click on the score is hit-tested by `EditorInputController`: it finds the measure, cell and string, and sets OnNote (a note on that string), OverBeat and InsideSelection. It raises ContextMenuRequested and does not move the cursor or seek playback.
2. Shift+F10 or the Menu key on the score calls `EditorInputController.RequestContextMenuAtCaret`. It raises the same event from the caret, with FromKeyboard set and the menu anchored at the caret cell.
3. `MainWindow.xaml.cs` routes the event: OnNote or InsideSelection goes to ShowNoteContextMenu, anything else to ShowScoreContextMenu. A right-click on the score area outside the editor goes to ShowScoreContextMenu directly.
4. ShowNoteContextMenu keeps the selection when the click is inside it, else selects the clicked beat. It builds the items in the order `ContextMenuLayouts.NoteMenu` gives (paste only with a clip). Duration, Dynamics, Effects and Beat are sub-menus of the tool palette's tools for that group (only tools marked Supported), with their state and enabled flags. Pitch and string call Editor.Effects directly.
5. ShowScoreContextMenu builds `ScoreMenus.Empty` from a `ScoreEmptyState`. Paste appears only over a beat with a clip. The switch runs the command; "Score settings" opens the Score settings page at its first row.
6. Shortcut text comes from MenuKey, which reads the user's bindings from `HotkeyCatalog`. The Backspace text of Delete is fixed (`ContextMenuLayouts.FixedKeys`), not a bindable command.
7. `SpecMenus.New` and `SpecMenus.Open` (`src/TabForge/Views/SpecMenus.cs`) turn the `MenuSpec` data into a WPF ContextMenu and place it at the mouse point, or at the anchor when opened from the keyboard.

## Where to add or change things

- **A note menu item:** add its id to `ContextMenuLayouts.NoteMenu` (the top-level count in `TestContextMenuLayouts` then changes with it), add an Item to the parts table in ShowNoteContextMenu, and read its shortcut with MenuKey if it is a bindable command (hotkey presets and `TOOLS_AND_HOTKEYS.md`, as the project agent rules say).
- **A score empty-area item:** a `MenuSpec` in `ScoreMenus.Empty`, and a case in the switch in ShowScoreContextMenu.
- **A fretboard menu item:** a `MenuSpec` in `InstrumentMenus.Build` (`src/TabForge/Views/ContextMenuSpecs.cs`), its state in ShowInstrumentContextMenu (`src/TabForge/MainWindow.View.cs`), and a case in RunInstrumentCommand.
- **A setting-like option in a menu:** it needs a settings row. `TestContextMenuLean` checks that every tick maps to a settings row, or to a stated reason.
- **Golden menus:** an intended change to a `MenuSpec` menu changes `tests/full-suite/Menus/code-menus.golden.txt`. Re-record it once with `TABFORGE_RECORD_MENU_GOLDEN=1` on a full-suite build (see `docs/RECIPES.md`), and review the diff.

## Classes

| File | Purpose |
| --- | --- |
| `src/TabForge/MainWindow.ContextMenus.cs` | ShowScoreContextMenu and ShowNoteContextMenu: build the menus from the data, run the commands, and the score view choices (a partial) |
| `src/TabForge/MainWindow.xaml.cs` | Wiring: the editor's ContextMenuRequested routing and the right-click outside the editor |
| `src/TabForge/MainWindow.View.cs` | ShowInstrumentContextMenu and RunInstrumentCommand: the fretboard menu |
| `src/TabForge/MainWindow.MenuPrewarm.cs` | Once at idle, builds and discards one hidden menu with every item kind, so the first right-click opens quickly |
| `src/TabForge/Views/Score/EditorInputController.cs` | The right-click hit test and RequestContextMenuAtCaret (Shift+F10 on the score) |
| `src/TabForge/Views/EditorEvents.cs` | `ContextMenuEventArgs`: position, measure, cell, string, OnNote, OverBeat, InsideSelection, FromKeyboard, anchor |
| `src/TabForge/Views/ContextMenuLayouts.cs` | The note menu's order (NoteMenu), its separator, the top-level names and the fixed Delete text |
| `src/TabForge/Views/ContextMenuSpecs.cs` | `MenuMarks`, `ScoreMenus` (empty-area menu data), `InstrumentMenus` (fretboard menu data) |
| `src/TabForge/Views/TimelineContextMenus.cs` | Declares `MenuSpec`, the data type every menu here is built from |
| `src/TabForge/Views/SpecMenus.cs` | Builds, places and opens a ContextMenu from `MenuSpec` data |
| `src/TabForge/Views/ToolPaletteController.cs` | PaletteToolState and PaletteToolEnabled, which the sub-menus read; the tool list is in `ToolPaletteController.Tools.cs` |
| `src/TabForge/Views/MenuPopupWarmup.cs` | Temporary popup used by the warm-up and the diagnostics |
| `src/TabForge/Services/HotkeyCatalog.cs` | The bindings behind the shortcut text (`HotkeyCatalog.DisplayAll`) |

## Debugging

- Render a menu off-screen: a capture script step `{"context":"score"|"note"|"timeline"|"fretboard"}` with the target `menu` (see the capture script reference). The step is ShowContext in `src/TabForge/Diagnostics/WindowProbes.CaptureShots.cs`.
- The fretboard menu in each view: `TabForge.exe --probe-instrument-menu <report>`.
- Wrong menu chosen on a right-click: check OnNote and InsideSelection in `ContextMenuEventArgs`, then the routing in `src/TabForge/MainWindow.xaml.cs`.
- Keyboard placement: `TestKeyboardContextMenuPlacement` and `TestScoreContextMenuByKeyboard` show the expected anchors.

## Known limits

- The note menu's actions run through their item clicks in `TestNoteMenuClipboardActions`, `TestNoteMenuEditActions` and `TestNoteMenuItemsRouted`. Not covered: Paste special and the editor dialogs (Bend, Natural harmonic, Grace note, Trill, Tremolo bar, Chord, Text); Voice is not in this menu; a palette id missing from the handler switch is ignored silently, which the sweep cannot see. `code-menus.golden.txt` does not cover the note menu (see `docs/RECIPES.md`).
- Paste items are hidden without a clip, not greyed out.
- The sub-menus list only the palette tools marked Supported.
- A right-click inside the selection keeps the selection. Outside it, the clicked beat becomes the selection, without a seek.
- The fretboard menu opened from the keyboard appears at the panel's top-left with its first item focused.
