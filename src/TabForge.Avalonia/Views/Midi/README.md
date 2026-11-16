# Views/Midi: the MIDI processing window's parts

The window itself is `Views/MidiProcessingWindow.cs` with its partials `.Catalog` (grouped, searchable catalog; add and drag in), `.Detail` (header and parameter cards), `.ChainSelect` (selection, drag to reorder, right-click menu, keys) and `.Log`.
This folder holds the parts it is built from.

| Class | Job |
|---|---|
| `MidiParamEditors` | The control of each kind of processor parameter (a knob with its value text, a drop-down, a tick, a text box, the note chips with an add box and Learn), each storing its value through the edit callback; a knob drag is one undo step with a light engine message while it turns. Also the `MidiLearn` state. |
| `MidiNoteMapRows` | The note-map processor's two map rows ("Song plays notes of", "Instrument expects"), the Edit map… / New map… buttons, and the summary of the note changes. |
| `MidiPresetBars` | The two preset rows (the whole list's presets and the selected processor's parameter presets), Save preset… and Delete (icon buttons with tooltips and accessible names), and reading/writing a preset file. |
| `MidiCardLayout` | The adaptive editor layout measured from the real text: `MidiFieldRow` (label beside its control, or above it when they do not fit), `MidiCardPanel` (one shared label column per card; knobs two per line when they fit) and `MidiHeaderRow` (name and preset row on one line, or the preset row under the name). Holds at every UI scale, density and font size. |
| `MidiIcons` | Which `IconGeometry` icon each processor type, catalog group and parameter section shows, and drawing one in a theme brush. |
| `MidiSharedRows`, `MidiSmallLayout` | Shared rows (Learn toggle, chips, entry row, icon button, tooltips) and the three-column body grid. |

Rules: the processors and their parameters come from `MidiProcessingListPresenter` / the engine's processor types; the window is the only
caller; the preset rows use the shared `PresetRows` layer (see `Views/Presets/README.md`).

Tests: `MidiProcessingWindowTests`, `MidiSmallLayoutTests` (every processor at several widths and UI scales), `NoteMapEditEntryTests`, `PresetManagerOpenTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~MidiProcessingWindowTests"`.
