# Views/Midi: the MIDI processing window's parts

The window itself is `Views/MidiProcessingWindow.cs` (the processor list, add/remove/move, the parameter area, the note map and the preset rows).
This folder holds the parts it is built from.

| Class | Job |
|---|---|
| `MidiParamEditors` | The control of each kind of processor parameter (a knob with its value text, a drop-down, a tick, a text box, the note chips with an add box and Learn), each storing its value through the edit callback; a knob drag is one undo step with a light engine message while it turns. Also the `MidiLearn` state. |
| `MidiNoteMapRows` | The note-map processor's two map rows ("Song plays notes of", "Instrument expects"), the Edit map… / New map… buttons, and the summary of the note changes. |
| `MidiPresetBars` | The two preset rows (the whole list's presets and the selected processor's parameter presets), Save preset…, Delete, and reading/writing a preset file. |

Rules: the processors and their parameters come from `MidiProcessingListPresenter` / the engine's processor types; the window is the only
caller; the preset rows use the shared `PresetRows` layer (see `Views/Presets/README.md`).

Tests: `MidiProcessingWindowTests`, `NoteMapEditEntryTests`, `PresetManagerOpenTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~MidiProcessingWindowTests"`.
