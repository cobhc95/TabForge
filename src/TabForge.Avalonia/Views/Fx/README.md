# Views/Fx: the FX chain window's parts

The window itself is `Views/FxChainWindow*.cs` (layout, options row, chain commands, plug-in bar, menu, editor area). This folder holds what it is built from.

| Class | Job |
|---|---|
| `IFxChainHost`, `SongFxChainHost`, `StandaloneFxChainHost` | What the window needs of the app. `Edit` is the one way a chain changes: one `DocumentEdits` step (undo, dirty mark), then the engine sync. The standalone host serves the live input chain and the preview (no undo). |
| `IFxChainEngine`, `AudioEngineFxAdapter` | The engine calls and events the window uses; the adapter forwards to `AudioEngineClient`, a test supplies a recording fake. |
| `FxChainWindows` | The open windows of a main window: one per track, the master, group and monitor stand-ins, closing the window of a removed track, refresh after the FX power switch. Found through `FxChainWindows.For(window)`. |
| `FxChainList`, `FxChainRows`, `FxChainContainers`, `FxContainerPresets` | The rows (several selected, rubber band, drag with gap and line, Esc), their look, the containers and their pane and presets. Drop rules are `FxChainDropMath` in Core. |
| `FxEditorController`, `FxEditorArea`, `PluginEditorSurface` | The plug-in's own editor: docked in a native child window the engine parents it to, or floating; the notice for every state (`FxEditorPlan` in Core); the scroll bars and window growth (`PluginEditorFitMath` in Core); the BYPASSED stamp (the native area is hidden under it, so the engine hides its editor). Windows calls: `Shell/Win32/PluginEditorWin32`. |
| `FxChainKeys` | The FX chain scope of the key map and the gesture text of the menu items. |
| `FxChainFiles`, `FxRfxChainFlows` | Load, add to the end, save, the Chains menu, and the `.RfxChain` import and export. |
| `FxAddPlugin`, `PluginBrowserSession` | The Add path: the browser on the real scan, approval, identify, the new slot. |
| `FxListSplitter`, `CommonFoldersWindow`, `LiveInputHint`, `FxChainDialogs`, `FxDefaultChains`, `FxEngineSetup` | The list width divider, the common folders editor, the live input note, the questions and pickers (replaceable in tests), the default chains applied to a song, and the crash list and pitch matcher set once on the engine. |

Midi processing has its own folder: `Views/Midi` (`MidiParamEditors`, `MidiNoteMapRows`, `MidiPresetBars`) around `Views/MidiProcessingWindow.cs`.

Rules: colours are theme keys, sizes and timings are named constants, the pointer never changes shape, no per-frame layout (the list moves rows with render transforms only while a drag is on).

Tests: `FxChainWindowTests`, `FxChainEditsTests`, `FxChainKeysTests`, `FxChainListTests`, `FxChainContainersTests`, `FxEngineCallTests`, `FxAddPluginTests`, `FxChainOptionsTests`, `FxChainWindowsTests`, `FxChainSupportTests`, `MidiProcessingWindowTests`, `PluginBrowserWindowTests`, `LiveInputFxTitleTests`.
