# LiveInput

The square microphone button at the left of the track list toolbar: hear the chosen audio input, a MIDI keyboard, or both through an app-wide live FX chain and the master, without a track and without recording. One arm for the whole program: it stays on across tabs and windows until the button or its command turns it off, and it is never on at start.

- `LiveInputSettings.cs`: what the app settings keep: the last chain, the audio input (or none), the MIDI device (any, one, or none), "open the FX window when turned on", the restore choice (Ask, Always, Never).
- `LiveInputArm.cs`: the program's one live input: the stand-in track (the session's chain), whether it is on, and what the engine sync asks about it. One instance per `AudioEngineClient`, which every window shares.
- `LiveInputController.cs`: one window's side: on / off, the restore question, saving the chain, the menu's choices, opening the chain window and being its `IFxChainHost` (chain edits go to the app settings, never to a song).
- `LiveInputButton.cs`: the button: red while on in every window, tooltip, the right-click menu.
- `LiveInputMenu.cs`: the right-click menu: open the chain, the auto-open option, the audio rows (`AudioInputs.For` the live use, shown by `AudioInputs.Label`; a saved input the device lacks stays, marked) and the MIDI rows (one tick in each group, built from the connected devices at each opening), the audio device settings.
- `LiveInputMidi.cs`: the MIDI keyboard: one client of the engine client's shared `MidiInputHub` while on with a MIDI choice; each channel message goes to the stand-in's slot with `AudioEngineClient.Write` (no song channel); held notes are reset on off, a device change or a device gone; a two-second poll opens a device that appears.
- `LiveInputFeatureModule.cs`: the command and hotkey row, the Preferences group, one controller per window and the adapter from the main window.
- `LiveInputHint.cs`: the line in the live FX window while live input is off, with its Turn on button.
- `LiveInputSettingsRows.cs`: the Preferences row for the restore choice (a `SettingsCatalog` partial).

## How the sound gets there

The engine already monitors a record-armed track's input through that track's chain. While live input is on, the arm adds a stand-in `TrackModel` (audio, armed, monitored, no clips) to the tracks the engine sync wants, whichever song syncs. No engine command is new. A record-armed MIDI track or Keyboard mode listening to the same device sounds as well (each is its own client of the hub). With no audio input ticked the stand-in is not armed in the engine (as a MIDI-armed track) and only the keyboard plays its chain's instrument. The stand-in is in no `SongProject`, so it is not in the track list, the mixer, the saved file, renders or undo. A tab switch finds the same track object in the same slot with the same chain key, so nothing is unloaded or loaded. Its level is its own volume under the master (no song mute, solo or group applies), it goes to no group bus, it keeps its slot when a song closes, and a plug-in edit in its chain names no song. Turning off disarms at once, reads the plug-in states into the chain, saves the settings, then lets the stand-in leave the engine.

## Hooks outside this folder (the whole footprint)

- `AudioEngineClient.cs`: the property holding the arm; a plug-in edit in the live slot reports no owner.
- `AudioEngineClient.Sync.cs`: five one-call touches: add the stand-in to the wanted tracks, take its own level, send it to no group bus, keep its slot when a song closes, a constant owner id so a tab switch sends nothing.
- `AudioRouting.cs`: the arm gets the master volume and pan before the sync, and the live track joins the availability refresh. `EngineSyncController.cs`: one condition so a crashed plug-in in the live chain is dropped at once.
- `PluginSettings.cs`: one property for the settings above.
- `FeatureRegistry.cs`: one line, the module.
- `ArrangementPanel.cs`: three lines: the button as the toolbar's first child.
- `CompactColumnLayout.cs`: one line: the button's place in the compact column.
- `FxChainWindow.cs` and `FxChainWindow.Sections.cs`: an app-wide chain title and note on the host; such a window has that title, the note and no track options.
- `WindowProbes.CaptureExtras.cs`: the capture step that lights the button. `TrackRowMenus.cs`: the Add track row that replaces the toolbar button.

Removing the feature: delete this folder and the hooks above, the live input test file in the full suite and its registration, the command's row in `TOOLS_AND_HOTKEYS.md`; put the compact column counts in the compact mode test back and re-record the settings snapshot.

Tests: `TestLiveInput` (MIDI keyboard, arm, one slot across songs, off everywhere, own level, clean song, menu, input choice, restore choices, command), `TestCompactMode` (the button's place in the compact column).
