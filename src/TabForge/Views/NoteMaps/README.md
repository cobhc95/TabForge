# Views/NoteMaps

The drum map rows of the "Note map (drum map)" MIDI processor and the drum map editor. The MIDI processing window only creates the pane and asks it for the two map rows.

## Files
| File | Purpose |
| --- | --- |
| `NoteMapPane.cs` | The "Song plays notes of" and "Instrument expects" drop-downs (shipped maps, then the user's with a star; an unreadable file is listed greyed out), Edit map…, New map…, the Manage menu (manage panel, open the folder, reload) and the summary of the resulting note changes |
| `DrumMapEditorWindow.cs` | The editor: map drop-down, New, Duplicate, Manage…, the remap list (drum, note, Send as, Heard as; changed rows tinted; Show only changed), the piece grid under Advanced, Save / Cancel |

## How a map is stored
A map is a JSON file (`Plugins/DrumMapLibrary.cs`): `Name`, `Notes` (note number to piece name) and the optional `Remap` (incoming note number to the note sent, -1 for not sent). Two maps pair pieces by name; the target map's `Remap` is applied after that, and the processor's Overrides after that. The editor's Send as numbers are saved in `Remap` only where piece names do not already give that note. `Plugins/DrumMapFiles.cs` holds the checks and the atomic write (`DrumMapDraft`) and the user's folder as an `IPresetStore` (`DrumMapStore`), which the preset manage panel uses.

Maps that come with the app are read-only: Duplicate makes a user copy. The folder name is deliberately not `DrumMaps`: `Services.DrumMaps` is a class, and a namespace of that name under `Views` would hide it.

## Not here
A per-row test hit: the window has no path to play one note through the track's plug-in.

## Tests
`TestDrumMapRemap` (the note table through save, load and the processor's engine parameters; the summary text), `TestDrumMapEditorStore` (the editor's checks; rename, duplicate, delete in a scratch folder; an unreadable file). Capture steps: `midi-add` with `noteMap`, `note-map-editor`.
