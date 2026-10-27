# TabForge.Avalonia

The Avalonia app for TabForge: an application with the Core theme and a main window titled "TabForge".

- Owns: the Avalonia start-up (`Program`, `App`) and the audio engine start (the same internal flag the WPF app uses), which hands off to `EngineHost` before Avalonia starts (Windows only), the preview window (`MainWindow`: menu bar, tab strip, View and tempo bar, the dock workspace and the status notice), the main menu (`MainWindowMenu`, built from `MainMenuTable`; `MainWindowCommands` maps command ids to handlers and a row with no handler is disabled), the window lifetime (`MainWindowLifetime`: key routing and the close question), the window key table (`MainWindowKeyboard`, from `HotkeyCatalog`), the document tabs (`MainWindowTabs`: new, switch, close with the save question, whose Save saves first), File > Save and Save As (`MainWindowSaving`, through `DocumentSaveFlow`), File > Open and Export MIDI (`MainWindowFile`, through `DocumentController`), the Edit, Note and Bar commands the score editor runs (`MainWindowEdit`) and the palette tools that ask a question first (`MainWindowBarTools`, windows built on `BarDialogWindow`), Edit Copy, Cut, Paste and Paste Special (`MainWindowClipboard`) and the panes following the shown tab (`MainWindowDocumentPanes`) and the preview dialog list (`PreviewToolsMenu`), and the Avalonia adapters of the Core ports (the files below).
- Does not own: any feature logic. The Chord finder view (`Source/src/TabForge.Avalonia/Views/ChordFinderWindow.axaml`) is layout and wiring only; its choices, notes and status texts come from `ChordFinderPresenter` in Core. It reaches the shared ports (in `Source/src/TabForge.Core/Platform`) through the Core project only.
- Tests: the headless test project in the tests folder (`dotnet test`).
- References: `Source/src/TabForge.Core` (its `net10.0` build).

## Layout

Only the project files, `app.manifest`, `AREA-MAP.md` and this README sit at the root. `App/` holds `Program` and `App`; `MainWindow/` holds the `MainWindow` partials (its subfolders group them: `Menus/`, `Timeline/`, `Clips/`, `Editing/`, `Tracks/`, `Transport/`, `File/`, `Band/`, `Tools`); `Settings/` the settings bindings; `Metrics/` window and density sizing.

## Shell

The title row and caption buttons are in `Source/src/TabForge.Avalonia/Shell/README.md`.

## Views

The ported windows and the preview Tools menu are listed in `Source/src/TabForge.Avalonia/Views/README.md`.

## Adapters

The adapters and drawing hosts are listed in `Source/src/TabForge.Avalonia/Platform/README.md`.

Not added: an `IAppServices` adapter. Its property bag has no Avalonia equivalent, so an adapter would invent its semantics.

## Score opening

`ScoreOpenFlow` (`Source/src/TabForge.Avalonia/MainWindow/File/ScoreOpenFlow.cs`) opens a score file for the shell: a Guitar Pro file is parsed in the import worker, and a file whose worker cannot start is refused. The tabs and dialogs stay in `MainWindowFile`.

## Theme gaps

`AvaloniaLooks.Build().Unsupported` lists the look rules not mapped yet: part setters of non-border parts or unmapped brushes, data-bound conditions and the rules of control kinds not mapped yet. The ComboBox, ComboBoxItem, CheckBox, TextBox, TextBlock, ListBoxItem, ListViewItem (an Avalonia ListBoxItem), MenuItem, TabItem, ScrollBar, Thumb and Slider kinds are mapped; form, menu and tab parts map to their Fluent template names in `AvaloniaLooks.AvaloniaPartNames`, and the ComboBoxItem item background has no Avalonia border part to paint. The list, thumb and button-family looks use the border template. The grid view column header has no Avalonia class in this project (the DataGrid package is not referenced), so its rules stay out. The look table has no effect or transform setters. Windows render text with greyscale anti-aliasing (the render options text mode, set in `App`), as WPF does. The WPF app repaints some brushes from the user's appearance settings; the preview shows the Core defaults. The Core palette has no light surfaces or text, so the light render keeps the dark palette surfaces under the Fluent light controls; the Chord finder's buttons therefore set the primary text colour.
